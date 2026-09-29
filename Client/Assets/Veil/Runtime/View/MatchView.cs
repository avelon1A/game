using System.Collections.Generic;
using UnityEngine;
using Veil.Audio;
using Veil.Match;
using Veil.Sim;
using EventType = Veil.Sim.EventType;

namespace Veil.View
{
    /// <summary>A rendered character (a real player or a decoy — the view can't tell either).</summary>
    public sealed class AvatarView
    {
        public int AvatarId, OwnerId;
        public CharacterRig Rig;
        public byte Vis;
        public float Health01 = 1;
        public bool IsLocal, IsMyDecoy, Stealthed, Shield, Alive = true;
        public float LastSeen;
        public float Fade = 0;
        public byte FireSeq, CastSeq, HitSeq, JumpSeq;
        public Vector3 Pos, Vel;
        public float Yaw, VisualYaw;
        public bool Grounded = true, Dashing, Sprinting;
        public float VH;
        public Transform ShieldBubble;
        public TrailRenderer Trail;
    }

    public sealed class MatchView
    {
        public readonly ClientMatch Match;
        public readonly Transform Root;
        public readonly Dictionary<int, AvatarView> Avatars = new Dictionary<int, AvatarView>();
        public AvatarView Local { get; private set; }

        private readonly WorldBuilder _world;
        private readonly CameraRig _cam;
        private readonly Dictionary<int, GameObject> _pickups = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _projectiles = new Dictionary<int, GameObject>();
        private readonly List<ZoneVisual> _zones = new List<ZoneVisual>();
        private readonly List<int> _scratch = new List<int>();
        private Transform _circle;
        private Material _circleMat;
        private Transform _aimMarker;
        /// <summary>World point the local player's next shot will hit (wall, avatar or max range).</summary>
        public Vector3 ShotImpact { get; private set; }
        public bool HasShotImpact { get; private set; }
        public bool ShotHitsAvatar { get; private set; }

        /// <summary>Raised for HUD: world events already filtered to what we may know.</summary>
        public event System.Action<SimEvent> OnEvent;

        public MatchView(ClientMatch match, WorldBuilder world, CameraRig cam, Transform parent)
        {
            Match = match;
            _world = world;
            _cam = cam;
            Root = Build.Node(parent, "MatchView", Vector3.zero);
            if (Fx.I == null) Fx.Create(parent);

            var entry = match.Entry(match.LocalId);
            Local = CreateAvatar(match.LocalId, match.LocalId, entry != null ? entry.Look : default);
            Local.IsLocal = true;
            Local.Vis = Visibility.Full;
            Local.Fade = 1;

            for (int i = 0; i < match.Map.Zones.Count; i++) _zones.Add(new ZoneVisual(Root, match.Map.Zones[i]));

            var tube = BuildTube();
            var grad = CircleTexture();
            _circleMat = MaterialLib.UnlitInstance(new Color(1f, 0.3f, 0.55f, 0.55f), MaterialLib.Blend.Additive, grad);
            _circleMat.SetVector("_Scroll", new Vector4(0.02f, 0, 0, 0));
            var cgo = Build.Part(Root, tube, _circleMat, Vector3.zero, new Vector3(1, 30, 1), null, "CollapseWall", false);
            _circle = cgo.transform;
            _circle.gameObject.SetActive(false);

            var marker = Build.Part(Root, MeshGen.Ring(0.35f, 0.5f, 32), MaterialLib.Unlit(new Color(1, 1, 1, 0.35f), MaterialLib.Blend.Additive), Vector3.zero, Vector3.one * 0.7f, null, "AimMarker", false);
            _aimMarker = marker.transform;

            match.Event += HandleEvent;
            foreach (var kv in match.Pickups) EnsurePickup(kv.Value);
        }

        public void Dispose()
        {
            Match.Event -= HandleEvent;
            Object.Destroy(Root.gameObject);
        }

        private AvatarView CreateAvatar(int avatarId, int ownerId, Appearance look)
        {
            var rig = CharacterRig.Create(Root, look, true, "Avatar" + avatarId);
            var av = new AvatarView { AvatarId = avatarId, OwnerId = ownerId, Rig = rig };
            var trailGo = new GameObject("Trail");
            trailGo.transform.SetParent(rig.transform, false);
            trailGo.transform.localPosition = new Vector3(0, 0.9f, 0);
            var tr = trailGo.AddComponent<TrailRenderer>();
            tr.time = 0.25f;
            tr.minVertexDistance = 0.1f;
            tr.widthMultiplier = 0.8f;
            tr.widthCurve = AnimationCurve.Linear(0, 1, 1, 0);
            tr.sharedMaterial = MaterialLib.Unlit(Color.white, MaterialLib.Blend.Additive);
            var acc = Palette.AccentColors[look.Color % Palette.AccentColors.Length];
            tr.startColor = new Color(acc.r, acc.g, acc.b, 0.7f);
            tr.endColor = new Color(acc.r, acc.g, acc.b, 0f);
            tr.emitting = false;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            av.Trail = tr;
            rig.Footstep = (pos, sprint) =>
            {
                if (av.Fade < 0.5f) return;
                Fx.I.Dust(pos, 0.15f + sprint * 0.45f);
                if (av.IsLocal) Sfx.Play(Sfx.Step, 0.18f + sprint * 0.12f, 1f + Random.Range(-0.1f, 0.1f));
                else Sfx.PlayAt(Sfx.Step, pos, 0.2f);
            };
            var bubble = Build.Part(rig.transform, MeshGen.Sphere, MaterialLib.Unlit(new Color(0.4f, 0.8f, 1f, 0.18f), MaterialLib.Blend.Additive), new Vector3(0, 1f, 0), Vector3.one * 2.3f, null, "Shield", false);
            av.ShieldBubble = bubble.transform;
            bubble.SetActive(false);
            Avatars[avatarId] = av;
            return av;
        }

        // ------------------------------------------------------------------ per frame

        public void Update(float dt)
        {
            var snap = Match.Latest;
            if (snap == null) return;

            UpdateLocal(dt, snap);
            UpdateRemotes(dt, snap);
            UpdateProjectiles(snap);
            UpdatePickups(dt);
            for (int i = 0; i < _zones.Count && i < snap.Zones.Length; i++) _zones[i].Update(snap.Zones[i], Match, dt);

            // collapse wall
            bool collapsing = snap.Circle < GameConfig.CircleStartRadius - 0.5f;
            _circle.gameObject.SetActive(collapsing);
            if (collapsing) _circle.localScale = new Vector3(snap.Circle * 2, 30, snap.Circle * 2);
        }

        private void UpdateLocal(float dt, Snapshot snap)
        {
            var p = Match.Predicted;
            var av = Local;
            Vector3 pos = Vector3.Lerp(Match.PrevPredictedPos, Match.PredictedPos, Match.Alpha) + Match.CorrectionOffset;
            if (Match.Driver.Autopilot) pos = Match.PredictedPos;
            av.Pos = pos;
            av.Vel = new Vector3(p.Vel.X, 0, p.Vel.Y);
            av.Alive = p.Alive;
            av.Health01 = p.HealthFrac;
            av.Grounded = p.Grounded;
            av.VH = p.VH;
            av.Dashing = p.DashT > 0;
            av.Sprinting = p.Sprinting;
            av.Shield = p.Shield > 0;
            av.Yaw = p.Yaw;
            av.Stealthed = p.ZoneId == Match.Map.Zone(ZoneType.Ruins).Id;
            TriggerSeqs(av, p.FireSeq, p.CastSeq, p.HitSeq, p.JumpSeq, true);
            DriveRig(av, dt, true);

            // where the next shot will actually land (mirrors MatchSim projectile stepping); the HUD crosshair sits here
            HasShotImpact = p.Alive && !Match.Driver.Autopilot;
            if (HasShotImpact)
            {
                var dir = Vec2.FromYaw(Match.AimYaw);
                var start = new Vec2(pos.x, pos.z) + dir * 0.6f;
                float hitR = GameConfig.HitRadius + GameConfig.ProjectileRadius, d = 0;
                ShotHitsAvatar = false;
                for (; d < GameConfig.ProjectileRange && !ShotHitsAvatar; d += 0.4f)
                {
                    var sp = start + dir * d;
                    if (Match.Map.BlocksShotAt(sp, GameConfig.ProjectileHeight, GameConfig.ProjectileRadius)) break;
                    foreach (var kv in Avatars)
                    {
                        var o = kv.Value;
                        if (o.IsLocal || o.IsMyDecoy || !o.Alive || o.Fade < 0.3f) continue;
                        float dx = o.Pos.x - sp.X, dz = o.Pos.z - sp.Y;
                        if (dx * dx + dz * dz <= hitR * hitR) { ShotHitsAvatar = true; break; }
                    }
                    if (ShotHitsAvatar) break;
                }
                d = Mathf.Min(d, GameConfig.ProjectileRange);
                var end = start + dir * d;
                ShotImpact = new Vector3(end.X, GameConfig.ProjectileHeight + 0.1f, end.Y);
                _aimMarker.gameObject.SetActive(true);
                _aimMarker.position = new Vector3(end.X, 0.08f, end.Y);
            }
            else _aimMarker.gameObject.SetActive(false);
        }

        private void UpdateRemotes(float dt, Snapshot snap)
        {
            float rt = Match.RenderTime;
            // create / refresh from the latest snapshot
            foreach (var a in snap.Avatars)
            {
                if (!Avatars.TryGetValue(a.AvatarId, out var av))
                {
                    var e = Match.Entry(a.OwnerId);
                    av = CreateAvatar(a.AvatarId, a.OwnerId, e != null ? e.Look : Appearance.Preset(a.OwnerId));
                    av.Pos = new Vector3(a.Pos.X, a.H, a.Pos.Y);
                    av.VisualYaw = a.Yaw;
                    av.Rig.transform.position = av.Pos;
                }
                av.LastSeen = Time.time;
                av.Vis = a.Vis;
            }

            _scratch.Clear();
            foreach (var kv in Avatars)
            {
                var av = kv.Value;
                if (av.IsLocal) continue;
                bool inLatest = false;
                foreach (var a in snap.Avatars) if (a.AvatarId == av.AvatarId) { inLatest = true; break; }

                if (inLatest && Match.SampleAvatar(av.AvatarId, rt, out var s0, out var s1, out float t))
                {
                    Vector3 p0 = new Vector3(s0.Pos.X, s0.H, s0.Pos.Y);
                    Vector3 target = p0;
                    float yaw = s0.Yaw;
                    if (s1 != null)
                    {
                        target = Vector3.Lerp(p0, new Vector3(s1.Pos.X, s1.H, s1.Pos.Y), t);
                        yaw = Mathf.LerpAngle(s0.Yaw, s1.Yaw, t);
                    }
                    var src = s1 ?? s0;
                    if ((target - av.Pos).sqrMagnitude > 25f) av.Pos = target; // teleport (respawn)
                    else av.Pos = Vector3.Lerp(av.Pos, target, 1 - Mathf.Exp(-25f * dt));
                    av.Vel = new Vector3(src.Vel.X, 0, src.Vel.Y);
                    av.Yaw = yaw;
                    av.Health01 = src.Health01;
                    av.Grounded = (src.Flags & AvatarFlags.Grounded) != 0;
                    av.Dashing = (src.Flags & AvatarFlags.Dashing) != 0;
                    av.Sprinting = (src.Flags & AvatarFlags.Sprinting) != 0;
                    av.IsMyDecoy = (src.Flags & AvatarFlags.MyDecoy) != 0;
                    av.Stealthed = (src.Flags & AvatarFlags.Stealthed) != 0;
                    av.Shield = (src.Flags & AvatarFlags.Shield) != 0;
                    av.VH = s1 != null ? (s1.H - s0.H) / 0.066f : 0;
                    av.Alive = true;
                    TriggerSeqs(av, src.FireSeq, src.CastSeq, src.HitSeq, src.JumpSeq, false);
                    av.Fade = Mathf.MoveTowards(av.Fade, av.Vis == Visibility.Full ? 1 : 0, dt * 5f);
                }
                else
                {
                    av.Fade = Mathf.MoveTowards(av.Fade, 0, dt * (av.Alive ? 4f : 0.9f));
                    if (av.Fade <= 0 && Time.time - av.LastSeen > 0.5f) _scratch.Add(av.AvatarId);
                }
                DriveRig(av, dt, false);
            }
            foreach (int id in _scratch)
            {
                Object.Destroy(Avatars[id].Rig.gameObject);
                Avatars.Remove(id);
            }
        }

        private void TriggerSeqs(AvatarView av, byte fire, byte cast, byte hit, byte jump, bool local)
        {
            if (fire != av.FireSeq)
            {
                av.FireSeq = fire;
                av.Rig.TriggerFire();
                if (av.Rig.BlasterTip) Fx.I.Flash(av.Rig.BlasterTip.position, Palette.AccentColors[av.Rig.Look.Color % 8], 0.4f);
                if (av.Fade > 0.1f || local) Sfx.PlayAt(Sfx.Shoot, av.Pos + Vector3.up, local ? 0.55f : 0.45f);
            }
            if (cast != av.CastSeq) { av.CastSeq = cast; av.Rig.TriggerCast(); }
            if (hit != av.HitSeq)
            {
                av.HitSeq = hit;
                av.Rig.TriggerHit();
                if (local) { _cam.Shake(0.6f); Sfx.Play(Sfx.HitMe, 0.7f); }
            }
            if (jump != av.JumpSeq)
            {
                av.JumpSeq = jump;
                if (local) Sfx.Play(Sfx.Jump, 0.4f);
            }
        }

        private void DriveRig(AvatarView av, float dt, bool local)
        {
            var tr = av.Rig.transform;
            bool visible = av.Fade > 0.02f && (av.Alive || av.Rig.DeathProgress < 1.5f);
            if (tr.gameObject.activeSelf != visible) tr.gameObject.SetActive(visible);
            if (!visible) return;
            tr.position = av.Pos;
            // face aim while shooting, otherwise movement direction
            float moveYaw = av.Vel.sqrMagnitude > 0.5f ? Mathf.Atan2(av.Vel.x, av.Vel.z) * Mathf.Rad2Deg : av.VisualYaw;
            float want = av.Rig.IsAiming || av.Vel.sqrMagnitude < 0.5f && local ? av.Yaw : moveYaw;
            if (!local && av.Vel.sqrMagnitude < 0.5f) want = av.Yaw;
            av.VisualYaw = Mathf.LerpAngle(av.VisualYaw, want, 1 - Mathf.Exp((local ? -18f : -12f) * dt));
            tr.rotation = Quaternion.Euler(0, av.VisualYaw, 0);
            float scale = Mathf.Lerp(0.6f, 1f, av.Fade);
            tr.localScale = Vector3.one * scale;
            av.ShieldBubble.gameObject.SetActive(av.Shield && av.Alive);
            av.Trail.emitting = av.Dashing;
            av.Rig.Animate(new RigState
            {
                Velocity = av.Vel, Grounded = av.Grounded, VerticalVelocity = av.VH, Dashing = av.Dashing,
                Sprinting = av.Sprinting, Dead = !av.Alive, Victory = false,
            }, dt);
        }

        private void UpdateProjectiles(Snapshot snap)
        {
            float ahead = Mathf.Clamp(Match.ServerNow - snap.Time, 0, 0.15f);
            _scratch.Clear();
            foreach (var kv in _projectiles) _scratch.Add(kv.Key);
            foreach (var p in snap.Projectiles)
            {
                if (!_projectiles.TryGetValue(p.Id, out var go))
                {
                    var owner = Match.Entry(p.Owner);
                    var c = Palette.AccentColors[(owner != null ? owner.Look.Color : 0) % 8];
                    go = Build.Part(Root, MeshGen.Sphere, MaterialLib.Glow(c, 4f), Vector3.zero, new Vector3(0.22f, 0.22f, 0.8f), null, "Bolt", false);
                    var tr = go.AddComponent<TrailRenderer>();
                    tr.time = 0.12f; tr.widthMultiplier = 0.18f;
                    tr.sharedMaterial = MaterialLib.Unlit(Color.white, MaterialLib.Blend.Additive);
                    tr.startColor = c; tr.endColor = new Color(c.r, c.g, c.b, 0);
                    tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _projectiles[p.Id] = go;
                }
                _scratch.Remove(p.Id);
                var pos = p.Pos + p.Vel * ahead;
                go.transform.position = new Vector3(pos.X, GameConfig.ProjectileHeight + 0.1f, pos.Y);
                if (p.Vel.LengthSq > 0.1f) go.transform.rotation = Quaternion.LookRotation(new Vector3(p.Vel.X, 0, p.Vel.Y));
            }
            foreach (int id in _scratch)
            {
                Object.Destroy(_projectiles[id]);
                _projectiles.Remove(id);
            }
        }

        // ------------------------------------------------------------------ pickups

        private void EnsurePickup(Pickup pk)
        {
            if (_pickups.ContainsKey(pk.Id)) return;
            var go = new GameObject("Pickup" + pk.Id);
            go.transform.SetParent(Root, false);
            go.transform.position = new Vector3(pk.Pos.X, 0, pk.Pos.Y);
            var body = Build.Node(go.transform, "Body", Vector3.up * 0.9f);
            switch (pk.Type)
            {
                case PickupType.Orb:
                    Build.Part(body, MeshGen.SphereLow, MaterialLib.Glow(Palette.Energy, 3f), Vector3.zero, Vector3.one * 0.32f, null, "Orb", false);
                    Build.Part(body, MeshGen.Sphere, MaterialLib.Unlit(new Color(0.3f, 0.9f, 1f, 0.15f), MaterialLib.Blend.Additive), Vector3.zero, Vector3.one * 0.8f, null, "Halo", false);
                    body.localPosition = Vector3.up * 0.6f;
                    break;
                case PickupType.Core:
                    Build.Part(body, MeshGen.Octahedron, MaterialLib.Glow(Palette.Core, 3f), Vector3.zero, new Vector3(0.6f, 1f, 0.6f), null, "Core");
                    Build.Part(body, MeshGen.Octahedron, MaterialLib.Unlit(new Color(0.4f, 0.7f, 1f, 0.2f), MaterialLib.Blend.Additive), Vector3.zero, new Vector3(1.1f, 1.6f, 1.1f), null, "Halo", false);
                    Build.Part(go.transform, MeshGen.Ring(0.35f, 0.5f, 32), MaterialLib.Unlit(new Color(0.4f, 0.7f, 1f, 0.6f), MaterialLib.Blend.Additive), Vector3.up * 0.05f, Vector3.one * 1.6f, null, "Base", false);
                    Build.Part(go.transform, MeshGen.Cylinder(12), MaterialLib.Unlit(new Color(0.4f, 0.7f, 1f, 0.1f), MaterialLib.Blend.Additive), Vector3.up * 4f, new Vector3(0.5f, 8f, 0.5f), null, "Beam", false);
                    break;
                default:
                {
                    var gold = MaterialLib.Toon(Palette.Key, 0.6f, 1f);
                    Build.Part(body, MeshGen.Torus(0.12f), gold, new Vector3(0, 0.25f, 0), new Vector3(0.45f, 0.5f, 0.45f), Quaternion.Euler(90, 0, 0), "Ring");
                    Build.Part(body, MeshGen.Box, gold, new Vector3(0, -0.15f, 0), new Vector3(0.1f, 0.6f, 0.1f), null, "Shaft");
                    Build.Part(body, MeshGen.Box, gold, new Vector3(0.1f, -0.35f, 0), new Vector3(0.18f, 0.08f, 0.1f), null, "Tooth");
                    Build.Part(body, MeshGen.Box, gold, new Vector3(0.08f, -0.2f, 0), new Vector3(0.14f, 0.07f, 0.1f), null, "Tooth2");
                    Build.Part(body, MeshGen.Sphere, MaterialLib.Unlit(new Color(1f, 0.85f, 0.3f, 0.18f), MaterialLib.Blend.Additive), Vector3.zero, Vector3.one * 1.3f, null, "Halo", false);
                    Build.Part(go.transform, MeshGen.Cylinder(12), MaterialLib.Unlit(new Color(1f, 0.8f, 0.3f, 0.12f), MaterialLib.Blend.Additive), Vector3.up * 5f, new Vector3(0.4f, 10f, 0.4f), null, "Beam", false);
                    break;
                }
            }
            _pickups[pk.Id] = go;
        }

        private void UpdatePickups(float dt)
        {
            float t = Time.time;
            foreach (var kv in Match.Pickups) EnsurePickup(kv.Value);
            _scratch.Clear();
            foreach (var kv in _pickups)
            {
                if (!Match.Pickups.ContainsKey(kv.Key)) { _scratch.Add(kv.Key); continue; }
                var body = kv.Value.transform.GetChild(0);
                float phase = kv.Key * 0.37f;
                body.localPosition = new Vector3(0, body.localPosition.y > 0.7f ? 0.9f + Mathf.Sin(t * 2f + phase) * 0.15f : 0.6f + Mathf.Sin(t * 3f + phase) * 0.1f, 0);
                body.localRotation = Quaternion.Euler(0, t * 90f + phase * 50f, 0);
            }
            foreach (int id in _scratch)
            {
                Object.Destroy(_pickups[id]);
                _pickups.Remove(id);
            }
        }

        // ------------------------------------------------------------------ events

        private void HandleEvent(SimEvent e)
        {
            Vector3 pos = new Vector3(e.Pos.X, 0, e.Pos.Y);
            bool me = e.A == Match.LocalId;
            switch (e.Type)
            {
                case EventType.PulseCast:
                    Fx.I.PulseWave(pos, GameConfig.PulseRadius, Palette.Energy);
                    Sfx.PlayAt(Sfx.Pulse, pos, me ? 0.9f : 0.6f);
                    break;
                case EventType.DecoySpawn:
                    Fx.I.Puff(pos, new Color(0.7f, 0.6f, 1f, 0.8f), 14, 0.6f);
                    Sfx.PlayAt(Sfx.Decoy, pos, 0.7f);
                    break;
                case EventType.DecoyPop:
                    Fx.I.Puff(pos, new Color(0.8f, 0.7f, 1f, 0.9f), 16, 0.5f);
                    Fx.I.Shards(pos, Palette.VeilLight, 8);
                    Sfx.PlayAt(Sfx.Decoy, pos, 0.6f, 0.7f);
                    break;
                case EventType.Hit:
                    Fx.I.Sparks(pos + Vector3.up * 1f, e.B < 0 ? new Color(1f, 0.9f, 0.6f) : new Color(1f, 0.5f, 0.4f), e.B < 0 ? 5 : 10);
                    if (e.B >= 0) Sfx.PlayAt(Sfx.Hit, pos, me ? 0.8f : 0.5f);
                    break;
                case EventType.Eliminated:
                {
                    if (Avatars.TryGetValue(e.B, out var av)) { av.Alive = false; }
                    if (e.B == Match.LocalId) { _cam.Shake(1.2f); Local.Alive = false; }
                    Fx.I.Shards(pos, Palette.Danger, 14);
                    Fx.I.Puff(pos, new Color(1f, 1f, 1f, 0.8f), 10, 0.7f);
                    Sfx.PlayAt(Sfx.Eliminate, pos, 0.8f);
                    break;
                }
                case EventType.Respawned:
                    Fx.I.Column(pos, Palette.VeilLight, 8, 1.0f);
                    if (me) Local.Rig.ResetPose();
                    else if (Avatars.TryGetValue(e.A, out var av2)) av2.Rig.ResetPose();
                    break;
                case EventType.PickupCollected:
                    if (e.A >= 0)
                    {
                        var c = e.Value == (int)PickupType.Orb ? Palette.Energy : e.Value == (int)PickupType.Core ? Palette.Core : Palette.Key;
                        Fx.I.Flash(pos + Vector3.up * 0.8f, c, 0.8f, 0.15f);
                        if (e.Value != (int)PickupType.Orb) Fx.I.Shards(pos, c, 8);
                        if (me) Sfx.Play(e.Value == (int)PickupType.Orb ? Sfx.Orb : e.Value == (int)PickupType.Core ? Sfx.Core : Sfx.Key, 0.6f, 1f + Random.Range(0, 0.1f));
                    }
                    break;
                case EventType.ZoneCaptured:
                    Fx.I.Column(pos, Palette.ZoneColor(Match.Map.Zones[e.B].Type), 14f, 1.6f);
                    Sfx.PlayAt(Sfx.Capture, pos, me ? 1f : 0.6f);
                    if (me) Sfx.Play(Sfx.Capture, 0.6f);
                    break;
                case EventType.VaultOpened:
                    _world.Root.GetComponent<WorldAnimator>()?.PlayVaultOpen();
                    Fx.I.Column(pos, Palette.Vault, 16f, 2f);
                    Fx.I.Shards(pos, Palette.Gold, 20);
                    Sfx.PlayAt(Sfx.Vault, pos, 1f);
                    break;
                case EventType.DashStart:
                    Sfx.PlayAt(Sfx.Dash, pos, me ? 0.7f : 0.5f);
                    break;
                case EventType.ShieldBreak:
                    Fx.I.Shards(pos + Vector3.up, Palette.Energy, 12);
                    break;
                case EventType.Purchase:
                    Sfx.Play(Sfx.Buy, 0.7f);
                    Fx.I.Column(pos, Palette.Market, 4f, 0.6f);
                    break;
                case EventType.ObjectiveComplete:
                    Sfx.Play(Sfx.Objective, 0.9f);
                    Fx.I.Column(pos, Palette.Gold, 10f, 1.4f);
                    break;
                case EventType.Revealed:
                    if (e.B == Match.LocalId) Sfx.Play(Sfx.Reveal, 0.7f);
                    break;
                case EventType.Land:
                    if (me) Sfx.Play(Sfx.Land, 0.35f);
                    Fx.I.Dust(pos, 1.2f);
                    break;
            }
            OnEvent?.Invoke(e);
        }

        // ------------------------------------------------------------------ helpers

        private static Mesh BuildTube()
        {
            int sides = 96;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i <= sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2;
                var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                v.Add(d * 0.5f); v.Add(d * 0.5f + Vector3.up);
                n.Add(-d); n.Add(-d);
                uv.Add(new Vector2(i / (float)sides * 24, 0)); uv.Add(new Vector2(i / (float)sides * 24, 1));
            }
            for (int i = 0; i < sides; i++)
            {
                int k = i * 2;
                t.Add(k); t.Add(k + 1); t.Add(k + 3); t.Add(k); t.Add(k + 3); t.Add(k + 2);
            }
            var m = new Mesh();
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        private static Texture2D CircleTexture()
        {
            var tex = new Texture2D(32, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 32; x++)
                {
                    float fade = Mathf.Pow(1 - y / 63f, 1.6f);
                    float stripe = ((x + y) / 6) % 2 == 0 ? 1f : 0.55f;
                    tex.SetPixel(x, y, new Color(1, 1, 1, fade * stripe));
                }
            tex.Apply();
            return tex;
        }
    }

    /// <summary>Ground ring, capture arc and ownership tint for one zone.</summary>
    public sealed class ZoneVisual
    {
        private readonly ZoneDef _def;
        private readonly Transform _root;
        private readonly Material _ring, _fill, _arcMat;
        private readonly Mesh _arc;
        private readonly Transform _spin;
        private float _shown = -1;

        public ZoneVisual(Transform parent, ZoneDef def)
        {
            _def = def;
            var c = Palette.ZoneColor(def.Type);
            _root = Build.Node(parent, "Zone_" + def.Name, Build.V(def.Center, 0.07f));
            _ring = MaterialLib.UnlitInstance(new Color(c.r, c.g, c.b, 0.85f), MaterialLib.Blend.Additive);
            _fill = MaterialLib.UnlitInstance(new Color(c.r, c.g, c.b, 0.1f), MaterialLib.Blend.Additive);
            _arcMat = MaterialLib.UnlitInstance(Color.white, MaterialLib.Blend.Additive);
            float r = def.Radius;
            Build.Part(_root, MeshGen.Ring(0.47f, 0.5f, 96), _ring, Vector3.zero, Vector3.one * r * 2, null, "Ring", false);
            Build.Part(_root, MeshGen.Disc(64), _fill, Vector3.up * -0.01f, Vector3.one * r * 2, null, "Fill", false);
            var spin = Build.Part(_root, DashedRing(), MaterialLib.Unlit(new Color(c.r, c.g, c.b, 0.5f), MaterialLib.Blend.Additive), Vector3.up * 0.01f, Vector3.one * (r * 2 - 1.2f), null, "Dashes", false);
            _spin = spin.transform;
            _arc = new Mesh();
            Build.Part(_root, _arc, _arcMat, Vector3.up * 0.02f, Vector3.one, null, "Arc", false);
        }

        public void Update(ZoneSnap z, ClientMatch m, float dt)
        {
            _spin.localRotation = Quaternion.Euler(0, Time.time * 12f, 0);
            Color zc = Palette.ZoneColor(_def.Type);
            Color owner = z.Controller >= 0 ? (z.Controller == m.LocalId ? Palette.Health : Palette.PlayerColor(z.Controller)) : zc;
            float pulse = z.Contested ? 0.5f + Mathf.Sin(Time.time * 12f) * 0.4f : 0.85f;
            _ring.SetColor("_BaseColor", new Color(owner.r, owner.g, owner.b, pulse));
            _fill.SetColor("_BaseColor", new Color(owner.r, owner.g, owner.b, z.Controller >= 0 ? 0.16f : 0.07f));

            float prog = _def.Type == ZoneType.Vault ? (m.Predicted.ZoneId == _def.Id ? m.Predicted.VaultChannel / GameConfig.VaultChannelTime : 0) : z.Progress;
            if (Mathf.Abs(prog - _shown) > 0.004f)
            {
                _shown = prog;
                BuildArc(prog);
                Color ac = z.Capturer == m.LocalId || _def.Type == ZoneType.Vault ? Palette.Gold : (z.Capturer >= 0 ? Palette.PlayerColor(z.Capturer) : zc);
                _arcMat.SetColor("_BaseColor", new Color(ac.r, ac.g, ac.b, 0.9f));
            }
        }

        private void BuildArc(float frac)
        {
            _arc.Clear();
            if (frac <= 0.001f) return;
            int seg = Mathf.Max(2, (int)(96 * frac));
            float r0 = _def.Radius - 0.9f, r1 = _def.Radius - 0.3f;
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            for (int i = 0; i <= seg; i++)
            {
                float a = frac * Mathf.PI * 2 * i / seg;
                var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                v.Add(d * r0); v.Add(d * r1); uv.Add(Vector2.zero); uv.Add(Vector2.one);
            }
            for (int i = 0; i < seg; i++)
            {
                int k = i * 2;
                t.Add(k); t.Add(k + 1); t.Add(k + 3); t.Add(k); t.Add(k + 3); t.Add(k + 2);
            }
            _arc.SetVertices(v); _arc.SetUVs(0, uv); _arc.SetTriangles(t, 0);
            _arc.RecalculateBounds();
        }

        private static Mesh _dashed;

        private static Mesh DashedRing()
        {
            if (_dashed != null) return _dashed;
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            int dashes = 36;
            for (int i = 0; i < dashes; i++)
            {
                float a0 = i / (float)dashes * Mathf.PI * 2, a1 = a0 + Mathf.PI * 2 / dashes * 0.5f;
                int b = v.Count;
                foreach (var a in new[] { a0, a1 })
                {
                    var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                    v.Add(d * 0.47f); v.Add(d * 0.5f); uv.Add(Vector2.zero); uv.Add(Vector2.one);
                }
                t.Add(b); t.Add(b + 1); t.Add(b + 3); t.Add(b); t.Add(b + 3); t.Add(b + 2);
            }
            _dashed = new Mesh();
            _dashed.SetVertices(v); _dashed.SetUVs(0, uv); _dashed.SetTriangles(t, 0);
            _dashed.RecalculateBounds();
            return _dashed;
        }
    }
}
