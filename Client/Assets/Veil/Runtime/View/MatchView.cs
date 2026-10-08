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
        public bool Grounded = true, Dashing, Sprinting, Downed, Reviving;
        public float ReviveProg;
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

            if (GameConfig.ExtractionMode) BuildChainMarkers();

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
                if (av.IsLocal) Sfx.Play(Sfx.Step, 0.07f + sprint * 0.04f, 1f + Random.Range(-0.08f, 0.08f));
                else Sfx.PlayAt(Sfx.Step, pos, 0.07f);
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
            UpdateSpectate();
            if (GameConfig.ExtractionMode) UpdateChainMarkers(snap);
            UpdateRoute(snap, dt);
            UpdateProjectiles(snap);
            UpdatePickups(dt);
            for (int i = 0; i < _zones.Count && i < snap.Zones.Length; i++) _zones[i].Update(snap.Zones[i], Match, dt);

            // collapse wall
            bool collapsing = snap.Circle < GameConfig.CircleStartRadius - 0.5f;
            _circle.gameObject.SetActive(collapsing);
            if (collapsing) _circle.localScale = new Vector3(snap.Circle * 2, 30, snap.Circle * 2);
        }

        // ------------------------------------------------------------------ extraction mode markers

        private Transform _site, _siteRing, _extract, _extractFill;
        private Renderer _extractRing, _extractBeam;
        private Transform _heli, _heliRotor, _heliTail;
        private float _heliH = 32f, _heliYaw;
        private bool _heliCalled;
        private Quaternion _rotorBase = Quaternion.identity;
        private Transform _heliBeacon;   // an eligible squad reached the zone: the helicopter has come down and stays down

        /// <summary>The escape helicopter: circles high over the zone, comes down when an eligible squad holds it, lifts off with the winners.</summary>
        private void BuildHelicopter()
        {
            // RILO rescue helicopter, built from primitives (nose = +Z): rounded navy cabin with a yellow belly band,
            // glass bubble, tapered tail boom with fin + stabiliser, open side door, curved skids, 4-blade rotor, nav lights
            _heli = Build.Node(Root, "Helicopter", Vector3.zero);
            var navy = MaterialLib.Toon(Palette.Hex("#22305e"), 0.45f, 0.7f);
            var navyD = MaterialLib.Toon(Palette.Hex("#172042"), 0.35f, 0.6f);
            var yellow = MaterialLib.Toon(Palette.Hex("#ffc93a"), 0.35f, 0.6f);
            var glass = MaterialLib.Toon(Palette.Hex("#8fe3ff"), 0.95f, 1f);
            var metal = MaterialLib.Toon(Palette.Hex("#3a3f4f"), 0.6f, 0.8f);
            var dark = MaterialLib.Toon(Palette.Hex("#121319"), 0.3f, 0.4f);
            var inside = MaterialLib.Toon(Palette.Hex("#0b0d16"), 0.1f, 0.2f);
            Quaternion along = Quaternion.Euler(90, 0, 0);   // cylinders/cones point along Y → lay them along Z

            // cabin: three overlapping rounded volumes give a fat, toy-like body
            Build.Part(_heli, MeshGen.Sphere, navy, new Vector3(0, 1.75f, 0.2f), new Vector3(2.5f, 2.2f, 4.2f), null, "Cabin");
            Build.Part(_heli, MeshGen.Sphere, navy, new Vector3(0, 1.6f, 1.55f), new Vector3(2.1f, 1.8f, 2.4f), null, "Nose");
            Build.Part(_heli, MeshGen.Sphere, navy, new Vector3(0, 2.35f, -0.3f), new Vector3(1.9f, 1.4f, 2.6f), null, "Engine");
            Build.Part(_heli, MeshGen.Sphere, yellow, new Vector3(0, 1.25f, 0.25f), new Vector3(2.56f, 1.0f, 4.0f), null, "Belly");
            Build.Part(_heli, MeshGen.Box, yellow, new Vector3(0, 1.9f, 0.1f), new Vector3(2.52f, 0.28f, 2.6f), null, "Stripe");
            // glass: front bubble + side windows
            Build.Part(_heli, MeshGen.Sphere, glass, new Vector3(0, 2.0f, 1.85f), new Vector3(1.75f, 1.25f, 1.8f), null, "Bubble");
            foreach (float x in new[] { -1f, 1f })
            {
                Build.Part(_heli, MeshGen.RoundBox(0.4f), glass, new Vector3(x * 1.2f, 2.15f, 0.5f), new Vector3(0.06f, 0.55f, 0.75f), null, "Window");
                Build.Part(_heli, MeshGen.RoundBox(0.4f), glass, new Vector3(x * 1.15f, 2.2f, -0.6f), new Vector3(0.06f, 0.45f, 0.55f), null, "Window");
            }
            // open side door (right side, where the squad boards)
            Build.Part(_heli, MeshGen.Box, inside, new Vector3(1.12f, 1.65f, -0.1f), new Vector3(0.12f, 1.25f, 1.25f), null, "Door");
            Build.Part(_heli, MeshGen.Box, yellow, new Vector3(1.2f, 1.65f, -0.82f), new Vector3(0.1f, 1.3f, 0.12f), null, "DoorFrame");
            Build.Part(_heli, MeshGen.Box, yellow, new Vector3(1.2f, 1.65f, 0.62f), new Vector3(0.1f, 1.3f, 0.12f), null, "DoorFrame");
            // RILO plate on the nose
            Build.Part(_heli, MeshGen.Box, yellow, new Vector3(0, 1.55f, 2.75f), new Vector3(0.9f, 0.32f, 0.06f), null, "Plate");

            // tail: tapered boom, fin, stabiliser, tail rotor guard
            Build.Part(_heli, MeshGen.Cone(16, 0.45f), navy, new Vector3(0, 2.15f, -3.6f), new Vector3(1.0f, 4.6f, 0.9f), along * Quaternion.Euler(180, 0, 0), "Boom");
            Build.Part(_heli, MeshGen.Box, yellow, new Vector3(0, 2.2f, -3.3f), new Vector3(0.62f, 0.14f, 2.4f), null, "BoomStripe");
            Build.Part(_heli, MeshGen.RoundBox(0.3f), navyD, new Vector3(0, 3.0f, -5.7f), new Vector3(0.18f, 1.6f, 0.9f), Quaternion.Euler(-18, 0, 0), "Fin");
            Build.Part(_heli, MeshGen.RoundBox(0.3f), yellow, new Vector3(0, 3.55f, -5.95f), new Vector3(0.2f, 0.4f, 0.7f), Quaternion.Euler(-18, 0, 0), "FinTip");
            Build.Part(_heli, MeshGen.RoundBox(0.3f), navyD, new Vector3(0, 2.15f, -5.0f), new Vector3(1.9f, 0.12f, 0.55f), null, "Stabiliser");
            _heliTail = Build.Node(_heli, "TailRotor", new Vector3(0.22f, 2.75f, -5.75f));
            for (int b = 0; b < 2; b++)
                Build.Part(_heliTail, MeshGen.RoundBox(0.4f), dark, Vector3.zero, new Vector3(0.05f, 1.5f, 0.14f), Quaternion.Euler(b * 90f, 0, 0), "Blade", false);
            Build.Part(_heliTail, MeshGen.Cylinder(10), metal, Vector3.zero, new Vector3(0.18f, 0.08f, 0.18f), Quaternion.Euler(0, 0, 90), "Hub", false);

            // skids: two rails with curled-up fronts on four struts
            foreach (float x in new[] { -1.05f, 1.05f })
            {
                Build.Part(_heli, MeshGen.Cylinder(10), metal, new Vector3(x, 0.1f, 0.2f), new Vector3(0.14f, 3.2f, 0.14f), along, "Skid");
                Build.Part(_heli, MeshGen.Cylinder(10), metal, new Vector3(x, 0.25f, 1.92f), new Vector3(0.14f, 0.38f, 0.14f), Quaternion.Euler(55, 0, 0), "SkidTip");
                foreach (float z in new[] { -0.8f, 1.0f })
                    Build.Part(_heli, MeshGen.Cylinder(8), metal, new Vector3(x * 0.85f, 0.55f, z), new Vector3(0.1f, 0.5f, 0.1f), Quaternion.Euler(0, 0, x > 0 ? -18 : 18), "Strut");
            }

            // main rotor: mast, hub, four tapered blades with yellow tips
            Build.Part(_heli, MeshGen.Cylinder(12), metal, new Vector3(0, 3.05f, -0.2f), new Vector3(0.32f, 0.35f, 0.32f), null, "Mast");
            _heliRotor = Build.Node(_heli, "Rotor", new Vector3(0, 3.3f, -0.2f));
            Build.Part(_heliRotor, MeshGen.Cylinder(12), metal, Vector3.zero, new Vector3(0.55f, 0.16f, 0.55f), null, "Hub", false);
            for (int b = 0; b < 4; b++)
            {
                var q = Quaternion.Euler(0, b * 90f, 0);
                Build.Part(_heliRotor, MeshGen.RoundBox(0.25f), dark, q * new Vector3(0, 0.02f, 2.6f), new Vector3(0.38f, 0.05f, 4.9f), q, "Blade", false);
                Build.Part(_heliRotor, MeshGen.RoundBox(0.25f), yellow, q * new Vector3(0, 0.03f, 4.85f), new Vector3(0.4f, 0.06f, 0.4f), q, "Tip", false);
            }
            _rotorBase = Quaternion.identity;

            // lights: red beacon under the belly, green / red nav lights on the sides, white on the tail
            _heliBeacon = Build.Part(_heli, MeshGen.Sphere, MaterialLib.Glow(new Color(1f, 0.25f, 0.25f), 4f), new Vector3(0, 0.62f, -0.2f), Vector3.one * 0.26f, null, "Beacon", false).transform;
            Build.Part(_heli, MeshGen.Sphere, MaterialLib.Glow(new Color(0.3f, 1f, 0.45f), 3f), new Vector3(1.28f, 1.75f, 1.2f), Vector3.one * 0.16f, null, "NavR", false);
            Build.Part(_heli, MeshGen.Sphere, MaterialLib.Glow(new Color(1f, 0.3f, 0.3f), 3f), new Vector3(-1.28f, 1.75f, 1.2f), Vector3.one * 0.16f, null, "NavL", false);
            Build.Part(_heli, MeshGen.Sphere, MaterialLib.Glow(Color.white, 3f), new Vector3(0, 3.9f, -6.1f), Vector3.one * 0.14f, null, "TailLight", false);
            _heli.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ route guide

        /// <summary>Your path (resampled every ~3 m) to your own "go here" marker, else your current objective / the helicopter.</summary>
        public readonly List<Vec2> Route = new List<Vec2>();
        private Vec2? _myMark;
        private float _myMarkT, _routeT;
        private Vec2 _routeTarget;
        private readonly List<Vec2> _pathTmp = new List<Vec2>();
        private readonly List<Transform> _routeDots = new List<Transform>();

        private void UpdateRoute(Snapshot s, float dt)
        {
            var me = Match.Predicted;
            if (_myMark.HasValue) { _myMarkT -= dt; if (_myMarkT <= 0 || Vec2.Dist(me.Pos, _myMark.Value) < 5f) _myMark = null; }
            Vec2? target = _myMark;
            if (!target.HasValue && GameConfig.ExtractionMode)
            {
                if (s.Stage < 4 && s.Task != ChainTask.Collect) target = s.Site;
                else if (s.Stage >= 4 && s.ExtractRevealed) target = s.ExtractPos;
            }
            bool on = me.Alive && target.HasValue && Vec2.Dist(me.Pos, target.Value) > 8f && EscapeT < 0;
            if (!on) Route.Clear();
            else
            {
                _routeT -= dt;
                if (_routeT <= 0 || Vec2.DistSq(_routeTarget, target.Value) > 1f)
                {
                    _routeT = 0.7f; _routeTarget = target.Value;
                    _pathTmp.Clear();
                    Route.Clear();
                    if (Match.Map.Nav.FindPath(me.Pos, target.Value, _pathTmp, 60000))
                    {
                        // resample the polyline every 3 m
                        Vec2 prev = me.Pos; float carry = 0;
                        foreach (var q in _pathTmp)
                        {
                            float seg = Vec2.Dist(prev, q);
                            for (float d = 3f - carry; d <= seg; d += 3f) Route.Add(prev + (q - prev) * (d / Mathf.Max(seg, 1e-3f)));
                            carry = (carry + seg) % 3f;
                            prev = q;
                        }
                    }
                }
            }
            // ground: glowing dashes for the first ~70 m
            int n = Mathf.Min(Route.Count, 24);
            while (_routeDots.Count < n)
            {
                var d = Build.Part(Root, MeshGen.Disc(12), MaterialLib.Unlit(new Color(1f, 0.85f, 0.3f, 0.55f), MaterialLib.Blend.Additive), Vector3.zero, new Vector3(0.45f, 1, 0.45f), null, "RouteDot", false).transform;
                _routeDots.Add(d);
            }
            for (int i = 0; i < _routeDots.Count; i++)
            {
                bool show = i < n && i > 0;
                if (_routeDots[i].gameObject.activeSelf != show) _routeDots[i].gameObject.SetActive(show);
                if (!show) continue;
                var p = Route[i];
                float pulse = 0.35f + 0.25f * Mathf.Sin(Time.time * 4f - i * 0.6f);
                _routeDots[i].position = new Vector3(p.X, 0.09f, p.Y);
                _routeDots[i].localScale = new Vector3(0.45f + pulse * 0.3f, 1, 0.45f + pulse * 0.3f);
            }
        }

        // ------------------------------------------------------------------ escape cinematic

        public const float EscapeLength = 10.5f;
        public float EscapeT { get; private set; } = -1f;
        public int EscapeSquad { get; private set; } = -1;
        private Vector3 _escPos, _escDir;
        private readonly List<AvatarView> _boarders = new List<AvatarView>();
        private readonly List<Vector3> _boardFrom = new List<Vector3>();
        private readonly List<bool> _aboard = new List<bool>();

        /// <summary>The winners walk to the helicopter, board, and it flies them off the island.</summary>
        public void StartEscape(int squad, Vec2 at)
        {
            if (EscapeT >= 0 || squad < 0) return;
            EscapeSquad = squad;
            EscapeT = 0f;
            _escPos = new Vector3(at.X, 0, at.Y);
            var outward = new Vector3(at.X, 0, at.Y);
            _escDir = outward.sqrMagnitude > 1f ? outward.normalized : Vector3.forward;
            _heliH = Mathf.Min(_heliH, 6f);
            _boarders.Clear(); _boardFrom.Clear(); _aboard.Clear();
            foreach (var av in Avatars.Values)
                if (av.AvatarId < 1000 && av.Alive && Match.SquadOf(av.OwnerId) == squad && Vector3.Distance(av.Pos, _escPos) < 30f) { _boarders.Add(av); _boardFrom.Add(av.Pos); }
            if (Local.Alive && Match.LocalSquad == squad && Vector3.Distance(Local.Pos, _escPos) < 30f) { _boarders.Add(Local); _boardFrom.Add(Local.Pos); }
            Sfx.Play(Sfx.Objective, 1f);
        }

        private float _dustT;
        private Vector3 _letGo;   // shot 4: where the camera stops and lets the helicopter fly off

        private void UpdateEscape(float dt)
        {
            EscapeT += dt;
            float t = EscapeT;
            // helicopter: on the ground while the squad boards (0–3 s), lifts and hovers (3–4.6 s), then climbs out to sea banking
            float lift = Mathf.Clamp01((t - 3.0f) / 1.6f);
            float climb = Mathf.SmoothStep(0, 1, lift) * 6f + Mathf.Max(0, t - 4.6f) * Mathf.Max(0, t - 4.6f) * 2.2f;
            float fly = Mathf.Max(0, t - 4.4f);
            var side = Vector3.Cross(Vector3.up, _escDir);
            Vector3 heliPos = _escPos + Vector3.up * (0.05f + climb) + _escDir * (fly * fly * 3.2f) + side * Mathf.Sin(fly * 0.6f) * fly * 1.5f;
            _heli.gameObject.SetActive(true);
            _heli.position = heliPos;
            float bank = Mathf.Clamp(fly * 9f, 0, 22f) * Mathf.Cos(fly * 0.6f);
            _heli.rotation = Quaternion.LookRotation(_escDir) * Quaternion.Euler(Mathf.Clamp(fly * 7f, 0, 16f), 0, -bank);
            _heliRotor.localRotation = Quaternion.AngleAxis(Time.time * 1700f, Vector3.up) * _rotorBase;
            _heliTail.localRotation = Quaternion.Euler(Time.time * 2200f, 0, 0);
            if (_heliBeacon) _heliBeacon.localScale = Vector3.one * (Mathf.Repeat(Time.time, 1f) < 0.15f ? 0.34f : 0.12f);
            _heliH = climb;
            // rotor wash: dust rings blowing out while it is low
            _dustT -= dt;
            if (climb < 8f && _dustT <= 0) { _dustT = 0.12f; Fx.I.Puff(_escPos + new Vector3(Random.Range(-3f, 3f), 0.2f, Random.Range(-3f, 3f)), new Color(0.85f, 0.8f, 0.7f, 0.6f), 6, 0.9f); }
            if (t > 3.0f && t - dt <= 3.0f) { Fx.I.Dust(_escPos, 3f); _cam.Shake(0.35f); }
            // winners jog to the door (right side of the cabin) and vanish inside, one after another
            Vector3 door = heliPos + _heli.rotation * new Vector3(1.6f, 0, 0.0f);
            for (int i = 0; i < _boarders.Count; i++)
            {
                var av = _boarders[i];
                if (av.Rig == null) continue;
                float start = 0.35f * i, k = Mathf.Clamp01((t - start) / 1.8f);
                bool aboard = k >= 1f;
                while (_aboard.Count <= i) _aboard.Add(false);
                // one small puff at the door, then hidden for good (the local view re-shows your hero every frame)
                if (aboard && !_aboard[i]) { _aboard[i] = true; Fx.I.Puff(door + Vector3.up, new Color(1, 1, 1, 0.7f), 8, 0.4f); }
                if (aboard) { if (av.Rig.gameObject.activeSelf) av.Rig.gameObject.SetActive(false); continue; }
                var p = Vector3.Lerp(_boardFrom[i], door, k * k * (3 - 2 * k));
                av.Rig.transform.position = p;
                var dir = door - _boardFrom[i]; dir.y = 0;
                if (dir.sqrMagnitude > 0.01f) av.Rig.transform.rotation = Quaternion.LookRotation(dir);
                av.Rig.Animate(new RigState { Grounded = true, Velocity = dir.normalized * 6f, Sprinting = true }, dt);
            }
            _extract.gameObject.SetActive(false);   // no zone ring / beam in the cinematic
        }

        /// <summary>Escape cinematic, four shots: low run-up behind the squad · door close-up as it lifts · high drone shot climbing
        /// over the island · locked camera watching it bank away over the sea.</summary>
        public void EscapeCamera(CameraRig cam, float dt)
        {
            float t = EscapeT;
            var side = Vector3.Cross(Vector3.up, _escDir);
            Vector3 heli = _heli.position;
            if (t < 2.4f)
            {
                // 1 · low, behind the squad, slowly pushing towards the waiting helicopter
                var from = _escPos - _escDir * (16f - t * 2.2f) - side * 3f + Vector3.up * (1.1f + t * 0.15f);
                if (t < dt * 1.5f) cam.Snap(from, _escPos + Vector3.up * 2f); else cam.Shot(from, _escPos + Vector3.up * 2f, dt, 6f);
                cam.SetFov(48f);
            }
            else if (t < 4.8f)
            {
                // 2 · side close-up on the open door as the last one boards and it lifts
                var from = _escPos + side * 7.5f + _escDir * 1.5f + Vector3.up * 1.6f;
                if (t - dt < 2.4f) cam.Snap(from, heli + Vector3.up * 1.8f); else cam.Shot(from, heli + Vector3.up * 1.8f, dt, 5f);
                cam.SetFov(52f);
            }
            else if (t < 7.2f)
            {
                // 3 · drone shot: high and behind, the island below as it climbs away
                var from = heli - _escDir * 16f + side * 6f + Vector3.up * 14f;
                if (t - dt < 4.8f) cam.Snap(from, heli); else cam.Shot(from, heli + _escDir * 6f, dt, 3f);
                cam.SetFov(60f);
                _letGo = heli - _escDir * 4f + side * 10f + Vector3.up * 4f;
            }
            else
            {
                // 4 · the camera stops and lets it go: banking out over the sea towards the horizon
                cam.Shot(_letGo, heli, dt, 2.5f);
                cam.SetFov(Mathf.Lerp(60f, 42f, Mathf.Clamp01((t - 7.2f) / 2.5f)));
            }
        }

        private void UpdateHelicopter(Snapshot s, float dt)
        {
            if (EscapeT >= 0) { UpdateEscape(dt); return; }
            bool show = s.ExtractRevealed || s.ExtractSeen;
            _heli.gameObject.SetActive(show);
            if (!show) return;
            int c = s.ExtractController;
            bool boarding = c >= 0 && s.SquadStage[c] >= 4 && !s.ExtractContested;
            bool escaped = s.Winner >= 0;
            // once it has come down it stays down (no bouncing when players enter / leave / fight in the zone):
            // circling high → descends for the first eligible squad → landed until the escape
            if (boarding) _heliCalled = true;
            float target = escaped ? 120f : _heliCalled ? 0.05f : 30f;
            _heliH = Mathf.MoveTowards(_heliH, target, dt * (escaped ? 14f : 7f));
            _heliYaw += dt * (_heliH > 12f ? 14f : 0f);
            Vector3 centre = new Vector3(s.ExtractPos.X, 0, s.ExtractPos.Y);
            float orbit = Mathf.Clamp01((_heliH - 6f) / 20f) * 14f;   // circles while high, settles straight down when landing
            var q = Quaternion.Euler(0, _heliYaw, 0);
            _heli.position = centre + q * new Vector3(orbit, 0, 0) + Vector3.up * (_heliH + Mathf.Sin(Time.time * 1.7f) * 0.15f);
            _heli.rotation = q * Quaternion.Euler(orbit > 1f ? 8f : 0f, 0, 0);
            _heliRotor.localRotation = Quaternion.AngleAxis(Time.time * 1400f, Vector3.up) * _rotorBase;
            _heliTail.localRotation = Quaternion.Euler(Time.time * 2000f, 0, 0);
            if (_heliBeacon) _heliBeacon.localScale = Vector3.one * (Mathf.Repeat(Time.time, 1f) < 0.15f ? 0.34f : 0.12f);
        }
        private Material _xWhite, _xGreen, _xRed, _xGold;

        private void BuildChainMarkers()
        {
            var gold = new Color(1f, 0.82f, 0.28f);
            _xGold = MaterialLib.Unlit(new Color(gold.r, gold.g, gold.b, 0.55f), MaterialLib.Blend.Additive);
            _xWhite = MaterialLib.Unlit(new Color(0.85f, 0.9f, 1f, 0.5f), MaterialLib.Blend.Additive);
            _xGreen = MaterialLib.Unlit(new Color(0.35f, 1f, 0.5f, 0.55f), MaterialLib.Blend.Additive);
            _xRed = MaterialLib.Unlit(new Color(1f, 0.3f, 0.3f, 0.6f), MaterialLib.Blend.Additive);

            // own objective: gold ring on the ground + tall light beam (visible across the map)
            _site = Build.Node(Root, "ChainSite", Vector3.zero);
            _siteRing = Build.Part(_site, MeshGen.Ring(0.9f, 1f, 64), _xGold, new Vector3(0, 0.06f, 0), Vector3.one, null, "Ring", false).transform;
            Build.Part(_site, MeshGen.Cylinder(16), MaterialLib.Unlit(new Color(gold.r, gold.g, gold.b, 0.18f), MaterialLib.Blend.Additive), new Vector3(0, 31.5f, 0), new Vector3(0.3f, 60f, 0.3f), null, "Beam", false);
            Build.Part(_site, MeshGen.Octahedron, MaterialLib.Glow(gold, 3f), new Vector3(0, 4.2f, 0), new Vector3(0.45f, 0.7f, 0.45f), null, "Gem", false);
            _site.gameObject.SetActive(false);

            // extraction: big ring, beam and a fill disc that grows with your squad's progress
            _extract = Build.Node(Root, "Extraction", Vector3.zero);
            _extractRing = Build.Part(_extract, MeshGen.Ring(0.94f, 1f, 96), _xWhite, new Vector3(0, 0.07f, 0), Vector3.one * GameConfig.ExtractRadius, null, "Ring", false).GetComponent<Renderer>();
            _extractBeam = Build.Part(_extract, MeshGen.Cylinder(20), _xWhite, new Vector3(0, 40f, 0), new Vector3(1.4f, 80f, 1.4f), null, "Beam", false).GetComponent<Renderer>();
            _extractFill = Build.Part(_extract, MeshGen.Disc(64), _xGreen, new Vector3(0, 0.05f, 0), Vector3.zero, null, "Fill", false).transform;
            BuildHelicopter();
            _extract.gameObject.SetActive(false);
        }

        private readonly List<Transform> _nodes = new List<Transform>();
        private Material[] _nodeGlow, _nodeLine;

        /// <summary>Stabilization nodes: red crystal = Destroy (shoot), cyan ring = Stabilize (stand), purple pillar = Override (hold).
        /// The ground disc fills with the node's progress.</summary>
        private void UpdateNodes(Snapshot s)
        {
            if (_nodeGlow == null)
            {
                _nodeGlow = new Material[3]; _nodeLine = new Material[3];
                for (int k = 0; k < 3; k++)
                {
                    var c = Veil.UI.HackPanel.KindColor((NodeKind)k);
                    _nodeGlow[k] = MaterialLib.Glow(c, 4f);
                    _nodeLine[k] = MaterialLib.Unlit(new Color(c.r, c.g, c.b, 0.55f), MaterialLib.Blend.Additive);
                }
            }
            while (_nodes.Count < s.Nodes.Count)
            {
                var n = Build.Node(Root, "HackNode", Vector3.zero);
                Build.Part(n, MeshGen.Octahedron, _nodeGlow[0], new Vector3(0, 1.3f, 0), new Vector3(0.7f, 1f, 0.7f), null, "Core", false);
                Build.Part(n, MeshGen.Ring(0.9f, 1f, 48), _nodeLine[0], new Vector3(0, 0.06f, 0), Vector3.one * GameConfig.NodeStandRadius, null, "Ring", false);
                Build.Part(n, MeshGen.Cylinder(8), _nodeLine[0], new Vector3(0, 7f, 0), new Vector3(0.14f, 14f, 0.14f), null, "Beam", false);
                Build.Part(n, MeshGen.Disc(48), _nodeLine[0], new Vector3(0, 0.05f, 0), Vector3.zero, null, "Fill", false);
                _nodes.Add(n);
            }
            for (int i = 0; i < _nodes.Count; i++)
            {
                bool on = i < s.Nodes.Count;
                _nodes[i].gameObject.SetActive(on);
                if (!on) continue;
                var nd = s.Nodes[i];
                int k = (int)nd.Kind;
                var t = _nodes[i];
                t.position = new Vector3(nd.Pos.X, 0, nd.Pos.Y);
                var core = t.GetChild(0);
                core.GetComponent<Renderer>().sharedMaterial = _nodeGlow[k];
                for (int c = 1; c < 4; c++) t.GetChild(c).GetComponent<Renderer>().sharedMaterial = _nodeLine[k];
                float bob = Mathf.Sin(Time.time * 4f + i) * 0.15f;
                if (nd.Kind == NodeKind.Override) { core.localScale = new Vector3(0.45f, 2.2f, 0.45f); core.localPosition = new Vector3(0, 1.2f, 0); core.localRotation = Quaternion.Euler(0, Time.time * 60f, 0); }
                else { core.localScale = nd.Kind == NodeKind.Destroy ? new Vector3(0.7f, 1f, 0.7f) * (1f - nd.Prog * 0.4f) : new Vector3(0.6f, 0.6f, 0.6f); core.localPosition = new Vector3(0, 1.3f + bob, 0); core.localRotation = Quaternion.Euler(0, Time.time * 160f + i * 40f, 0); }
                t.GetChild(1).gameObject.SetActive(nd.Kind != NodeKind.Destroy);   // stand ring only where standing matters
                float f = nd.Kind == NodeKind.Destroy ? 0 : 2 * GameConfig.NodeStandRadius * nd.Prog;
                t.GetChild(3).localScale = new Vector3(f, 1, f);
            }
        }

        private Transform _centreMark;

        private void UpdateChainMarkers(Snapshot s)
        {
            UpdateNodes(s);
            if (_centreMark == null)
            {
                _centreMark = Build.Node(Root, "CentreTerminalMark", Vector3.zero);
                var violet = MaterialLib.Unlit(new Color(0.75f, 0.5f, 1f, 0.5f), MaterialLib.Blend.Additive);
                Build.Part(_centreMark, MeshGen.Ring(0.92f, 1f, 64), violet, new Vector3(0, 0.07f, 0), Vector3.one * GameConfig.HackRadius, null, "Ring", false);
            }
            _centreMark.gameObject.SetActive(s.Stage == 0);
            bool showSite = s.Stage < 4 && s.Task != ChainTask.Collect;
            _site.gameObject.SetActive(showSite);
            if (showSite)
            {
                float r = s.Task == ChainTask.Hack ? GameConfig.HomeHackRadius : s.Task == ChainTask.Capture ? GameConfig.CaptureRadius : GameConfig.VaultRadius;
                _site.position = new Vector3(s.Site.X, 0, s.Site.Y);
                _siteRing.localScale = Vector3.one * r * (1f + Mathf.Sin(Time.time * 3f) * 0.03f);
                _site.GetChild(2).localRotation = Quaternion.Euler(0, Time.time * 90f, 0);
            }
            UpdateHelicopter(s, Time.deltaTime);
            if (EscapeT >= 0) return;   // the escape cinematic owns the helicopter and the zone
            // squads without the Vault can see the helicopter up close, but get no beam or zone (they can't use it)
            _extract.gameObject.SetActive(s.ExtractRevealed);
            if (s.ExtractRevealed)
            {
                _extract.position = new Vector3(s.ExtractPos.X, 0, s.ExtractPos.Y);
                var m = s.ExtractContested ? _xRed : s.ExtractController == Match.LocalSquad ? _xGreen : s.ExtractController >= 0 ? _xRed : _xWhite;
                _extractRing.sharedMaterial = m; _extractBeam.sharedMaterial = m;
                // locked: thin beam; final phase: thick pulsing beam + throbbing ring so everyone sees someone's about to win
                float beam = s.ExtractLockT > 0 ? 0.6f : s.ExtractFinal ? 2.6f + Mathf.Sin(Time.time * 6.3f) * 0.9f : 1.4f;
                _extractBeam.transform.localScale = new Vector3(beam, 80f, beam);
                _extractRing.transform.localScale = Vector3.one * GameConfig.ExtractRadius * (s.ExtractFinal ? 1f + 0.04f * Mathf.Sin(Time.time * 12f) : 1f);
                if (s.ExtractFinal) { _extractRing.sharedMaterial = _xRed; _extractBeam.sharedMaterial = _xRed; }
                float p = s.SquadExtract[Match.LocalSquad];
                _extractFill.localScale = new Vector3(2 * GameConfig.ExtractRadius * p, 1, 2 * GameConfig.ExtractRadius * p);
            }
        }

        // ------------------------------------------------------------------ spectating (while eliminated)

        public int SpectateId { get; private set; } = -1;
        public string SpectateName => SpectateId >= 0 ? Match.NameOf(SpectateId) : null;
        public Vector3? SpectatePos => SpectateId >= 0 && Avatars.TryGetValue(SpectateId, out var a) ? a.Pos : (Vector3?)null;

        private readonly List<int> _mates = new List<int>();

        private void Mates()
        {
            _mates.Clear();
            foreach (var kv in Avatars)
                if (!kv.Value.IsLocal && kv.Key < 1000 && kv.Value.Alive && Match.IsAlly(kv.Value.OwnerId)) _mates.Add(kv.Key);
            _mates.Sort();
        }

        private void UpdateSpectate()
        {
            if (Local == null || Local.Alive) { SpectateId = -1; return; }
            Mates();
            if (!_mates.Contains(SpectateId)) SpectateId = _mates.Count > 0 ? _mates[0] : -1;
        }

        /// <summary>Switch the spectated squadmate: index 0..3 = 1..4 keys, -1 = next.</summary>
        public void SwitchSpectate(int index)
        {
            if (Local == null || Local.Alive) return;
            Mates();
            if (_mates.Count == 0) { SpectateId = -1; return; }
            if (index >= 0) { if (index < _mates.Count) SpectateId = _mates[index]; return; }
            int at = _mates.IndexOf(SpectateId);
            SpectateId = _mates[(at + 1) % _mates.Count];
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
            av.Downed = p.Alive && p.Downed;
            av.Reviving = snap != null && snap.Self.Reviving >= 0;
            if (snap != null) { av.Rig.SetFists(snap.Self.Fists); av.Rig.SetSpecial(snap.Self.Special > 0 ? snap.Self.Special - GameConfig.Shotgun + 1 : 0); }
            av.ReviveProg = snap != null ? snap.Self.ReviveProg : 0;
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
            HasShotImpact = p.Alive && !p.Downed && !Match.Driver.Autopilot;
            if (HasShotImpact)
            {
                var dir = Vec2.FromYaw(Match.AimYaw);
                var start = new Vec2(pos.x, pos.z) + dir * 0.6f;
                float hitR = GameConfig.HitRadius + GameConfig.ProjectileRadius, d = 0;
                ShotHitsAvatar = false;
                float range = GameConfig.Current(p.Look.Weapon, Local != null && Local.Rig.FistsMode).Range;
                for (; d < range && !ShotHitsAvatar; d += 0.4f)
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
                d = Mathf.Min(d, range);
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
                    av.Downed = src.Downed;
                    av.Reviving = src.Reviving;
                    av.Rig.SetFists(src.Fists);
                    av.Rig.SetSpecial(src.Special);
                    av.ReviveProg = src.ReviveProg;
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
                int weapon = av.Rig.FistsMode ? GameConfig.FistsWeapon : av.Rig.Look.Weapon;
                bool seen = av.Fade > 0.1f || local;
                if (weapon == GameConfig.FistsWeapon)
                {
                    // punch: whoosh + a small burst at the fist's reach
                    if (seen)
                    {
                        var f = av.Pos + Quaternion.Euler(0, av.Yaw, 0) * new Vector3(0, GameConfig.ProjectileHeight + 0.35f, 1.1f);
                        Fx.I.Flash(f, Color.white, 0.35f, 0.06f);
                        Sfx.PlayAt(Sfx.Dash, av.Pos + Vector3.up, local ? 0.5f : 0.35f, 1.7f);
                    }
                }
                else if (weapon == 1)
                {
                    // sniper: big muzzle flash, heavy lingering beam, deep crack, camera kick
                    var c = Palette.AccentColors[av.Rig.Look.Color % 8];
                    if (av.Rig.BlasterTip) { Fx.I.Flash(av.Rig.BlasterTip.position, Color.white, 1.1f, 0.1f); Fx.I.Flash(av.Rig.BlasterTip.position, c, 1.6f, 0.14f); }
                    if (seen) Tracer(av, local);
                    if (seen) { Sfx.PlayAt(Sfx.Shoot, av.Pos + Vector3.up, local ? 0.95f : 0.75f, 0.55f); Sfx.PlayAt(Sfx.Hit, av.Pos + Vector3.up, local ? 0.4f : 0.3f, 0.5f); }
                    if (local) _cam.Shake(0.45f);
                }
                else if (av.Rig.Special == 1)
                {
                    // shotgun: wide flash, a fan of short streaks, a heavy boom
                    if (av.Rig.BlasterTip) Fx.I.Flash(av.Rig.BlasterTip.position, new Color(1f, 0.75f, 0.35f), 1.0f, 0.1f);
                    if (seen) for (int i = 0; i < 5; i++) Tracer(av, false, (i / 4f - 0.5f) * GameConfig.ShotgunSpread);
                    if (seen) { Sfx.PlayAt(Sfx.Shoot, av.Pos + Vector3.up, local ? 0.9f : 0.7f, 0.6f); Sfx.PlayAt(Sfx.Hit, av.Pos + Vector3.up, local ? 0.5f : 0.35f, 0.7f); }
                    if (local) _cam.Shake(0.35f);
                }
                else
                {
                    bool smg = av.Rig.Special == 2;
                    if (av.Rig.BlasterTip) Fx.I.Flash(av.Rig.BlasterTip.position, Palette.AccentColors[av.Rig.Look.Color % 8], smg ? 0.3f : 0.4f);
                    if (seen) Tracer(av, local);
                    if (seen) Sfx.PlayAt(Sfx.Shoot, av.Pos + Vector3.up, local ? (smg ? 0.4f : 0.55f) : 0.4f, smg ? 1.35f : 1f);
                }
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

        // instant hit-scan streak from the gun to where the shot lands (local: the predicted impact point)
        private void Tracer(AvatarView av, bool local, float yawOffset = 0f)
        {
            Vector3 from = av.Rig.BlasterTip ? av.Rig.BlasterTip.position : av.Pos + Vector3.up * (GameConfig.ProjectileHeight + 0.1f);
            Vector3 to;
            if (local && HasShotImpact) to = ShotImpact;
            else
            {
                var dir = Vec2.FromYaw(av.Yaw + yawOffset);
                var start = new Vec2(av.Pos.x, av.Pos.z) + dir * 0.6f;
                float d = 0;
                float range = GameConfig.Current(av.Rig.Look.Weapon, av.Rig.FistsMode, av.Rig.Special > 0 ? av.Rig.Special + GameConfig.Shotgun - 1 : 0).Range;
                for (; d < range; d += 0.4f)
                    if (Match.Map.BlocksShotAt(start + dir * d, GameConfig.ProjectileHeight, GameConfig.ProjectileRadius)) break;
                var end = start + dir * Mathf.Min(d, range);
                to = new Vector3(end.X, GameConfig.ProjectileHeight + 0.1f, end.Y);
            }
            var c = Palette.AccentColors[av.Rig.Look.Color % 8];
            if (av.Rig.Look.Weapon == 1 && !av.Rig.FistsMode) { SniperBeam(from, to, c); return; }
            var go = new GameObject("Tracer");
            go.transform.SetParent(Root, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.positionCount = 2;
            lr.SetPosition(0, from); lr.SetPosition(1, to);
            lr.widthMultiplier = 0.07f;
            lr.sharedMaterial = MaterialLib.Unlit(Color.white, MaterialLib.Blend.Additive);
            lr.startColor = new Color(c.r, c.g, c.b, 0.25f); lr.endColor = c;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Fx.I.Flash(to, c, 0.25f);
            Object.Destroy(go, 0.06f);
        }

        /// <summary>Sniper shot: white-hot core + coloured glow that linger and thin out, sparks and a burst where it lands.</summary>
        private void SniperBeam(Vector3 from, Vector3 to, Color c)
        {
            var go = new GameObject("SniperBeam");
            go.transform.SetParent(Root, false);
            LineRenderer Line(float width, Color a, Color b)
            {
                var g = new GameObject("L"); g.transform.SetParent(go.transform, false);
                var lr = g.AddComponent<LineRenderer>();
                lr.useWorldSpace = true; lr.positionCount = 2;
                lr.SetPosition(0, from); lr.SetPosition(1, to);
                lr.widthMultiplier = width;
                lr.sharedMaterial = MaterialLib.Unlit(Color.white, MaterialLib.Blend.Additive);
                lr.startColor = a; lr.endColor = b;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.numCapVertices = 4;
                return lr;
            }
            var glow = Line(0.32f, new Color(c.r, c.g, c.b, 0.35f), new Color(c.r, c.g, c.b, 0.9f));
            var core = Line(0.09f, new Color(1, 1, 1, 0.6f), Color.white);
            go.AddComponent<BeamFade>().Init(new[] { glow, core }, 0.45f);
            Fx.I.Flash(to, Color.white, 0.8f, 0.1f);
            Fx.I.Flash(to, c, 1.4f, 0.18f);
            Fx.I.Sparks(to, c, 14, 9f);
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
                Sprinting = av.Sprinting, Dead = !av.Alive, Victory = false, Downed = av.Downed, Reviving = av.Reviving,
            }, dt);
        }

        private readonly Dictionary<int, Vec2> _grenadeStart = new Dictionary<int, Vec2>();

        private void UpdateProjectiles(Snapshot snap)
        {
            float ahead = Mathf.Clamp(Match.ServerNow - snap.Time, 0, 0.15f);
            _scratch.Clear();
            foreach (var kv in _projectiles) _scratch.Add(kv.Key);
            foreach (var p in snap.Projectiles)
            {
                if (p.Kind == 2) continue;   // punches: no visible bolt
                if (p.Kind == 1)
                {
                    // grenade: a glowing ball on an arc from where it was first seen
                    if (!_projectiles.TryGetValue(p.Id, out var g))
                    {
                        g = Build.Part(Root, MeshGen.Sphere, MaterialLib.Toon(Palette.Hex("#2b2f3d"), 0.4f), Vector3.zero, Vector3.one * 0.32f, null, "Grenade", false);
                        Build.Part(g.transform, MeshGen.Sphere, MaterialLib.Glow(new Color(1f, 0.45f, 0.2f), 3f), new Vector3(0, 0.45f, 0), Vector3.one * 0.4f, null, "Light", false);
                        _projectiles[p.Id] = g;
                        _grenadeStart[p.Id] = p.Pos;
                    }
                    _scratch.Remove(p.Id);
                    var gp = p.Pos + p.Vel * ahead;
                    float f = Mathf.Clamp01(Vec2.Dist(gp, _grenadeStart[p.Id]) / GameConfig.GrenadeRange);
                    g.transform.position = new Vector3(gp.X, 1.2f + Mathf.Sin(f * Mathf.PI) * 3.2f - f * 0.9f, gp.Y);
                    g.transform.Rotate(400f * Time.deltaTime, 0, 0);
                    continue;
                }
                if (!_projectiles.TryGetValue(p.Id, out var go))
                {
                    var owner = Match.Entry(p.Owner);
                    var c = Palette.AccentColors[(owner != null ? owner.Look.Color : 0) % 8];
                    go = Build.Part(Root, MeshGen.Sphere, MaterialLib.Glow(c, 4f), Vector3.zero, new Vector3(0.22f, 0.22f, 0.8f), null, "Bolt", false);
                    var tr = go.AddComponent<TrailRenderer>();
                    tr.time = 0.07f; tr.widthMultiplier = 0.16f;   // fast bolts: short streak
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
                _grenadeStart.Remove(id);
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
                case PickupType.Shotgun:
                case PickupType.Smg:
                case PickupType.Grenades:
                {
                    // loot: the weapon (or a grenade pair) spinning over a glowing orange base, with a short beacon
                    var orange = new Color(1f, 0.6f, 0.2f);
                    if (pk.Type == PickupType.Grenades)
                    {
                        var shell = MaterialLib.Toon(Palette.Hex("#3a4031"), 0.4f);
                        Build.Part(body, MeshGen.Sphere, shell, new Vector3(-0.17f, 0, 0), Vector3.one * 0.3f, null, "G1");
                        Build.Part(body, MeshGen.Sphere, shell, new Vector3(0.17f, 0, 0), Vector3.one * 0.3f, null, "G2");
                        Build.Part(body, MeshGen.Cylinder(8), MaterialLib.Glow(orange, 3f), new Vector3(-0.17f, 0.17f, 0), new Vector3(0.08f, 0.08f, 0.08f), null, "Pin1", false);
                        Build.Part(body, MeshGen.Cylinder(8), MaterialLib.Glow(orange, 3f), new Vector3(0.17f, 0.17f, 0), new Vector3(0.08f, 0.08f, 0.08f), null, "Pin2", false);
                    }
                    else
                    {
                        var prefab = Resources.Load<GameObject>("Weapons/" + (pk.Type == PickupType.Shotgun ? "blaster-g" : "blaster-j"));
                        if (prefab != null)
                        {
                            var gun = Object.Instantiate(prefab, body, false);
                            gun.transform.localRotation = Quaternion.Euler(0, 0, 90);
                            gun.transform.localScale = Vector3.one * 1.6f;
                            foreach (var r in gun.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        }
                    }
                    Build.Part(go.transform, MeshGen.Ring(0.35f, 0.5f, 32), MaterialLib.Unlit(new Color(1f, 0.6f, 0.2f, 0.7f), MaterialLib.Blend.Additive), Vector3.up * 0.05f, Vector3.one * 1.8f, null, "Base", false);
                    Build.Part(go.transform, MeshGen.Cylinder(12), MaterialLib.Unlit(new Color(1f, 0.6f, 0.2f, 0.12f), MaterialLib.Blend.Additive), Vector3.up * 2.5f, new Vector3(0.4f, 5f, 0.4f), null, "Beam", false);
                    break;
                }
                case PickupType.Tag:
                {
                    // a fallen player's tag: their squad colour, only bright for their own squad
                    bool ally = Match.IsAlly(pk.Spot);
                    var c = ally ? new Color(0.4f, 1f, 0.55f) : new Color(1f, 0.4f, 0.45f);
                    Build.Part(body, MeshGen.Box, MaterialLib.Toon(Palette.Hex("#c9ccd8"), 0.7f, 1f), Vector3.zero, new Vector3(0.36f, 0.5f, 0.05f), null, "Plate");
                    Build.Part(body, MeshGen.Box, MaterialLib.Glow(c, 3f), new Vector3(0, 0, 0.03f), new Vector3(0.22f, 0.08f, 0.02f), null, "Bar", false);
                    Build.Part(body, MeshGen.Box, MaterialLib.Glow(c, 3f), new Vector3(0, 0, 0.03f), new Vector3(0.08f, 0.22f, 0.02f), null, "Bar2", false);
                    Build.Part(go.transform, MeshGen.Ring(0.42f, 0.5f, 32), MaterialLib.Unlit(new Color(c.r, c.g, c.b, 0.7f), MaterialLib.Blend.Additive), Vector3.up * 0.05f, Vector3.one * GameConfig.TagRadius * 2f, null, "Base", false);
                    if (ally) Build.Part(go.transform, MeshGen.Cylinder(12), MaterialLib.Unlit(new Color(c.r, c.g, c.b, 0.15f), MaterialLib.Blend.Additive), Vector3.up * 5f, new Vector3(0.4f, 10f, 0.4f), null, "Beam", false);
                    break;
                }
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
                case EventType.Ping:
                    if (me && e.B == (int)PingKind.Go) { _myMark = e.Pos; _myMarkT = e.Value == -2 ? 45f : 12f; }
                    break;
                case EventType.MatchEnded:
                    if (GameConfig.ExtractionMode && e.A >= 0 && e.Pos.LengthSq > 0.01f) StartEscape(e.A, e.Pos);
                    break;
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
                        var c = e.Value == (int)PickupType.Orb ? Palette.Energy : e.Value == (int)PickupType.Core ? Palette.Core : MatchSim.IsLoot((PickupType)e.Value) ? new Color(1f, 0.6f, 0.2f) : Palette.Key;
                        Fx.I.Flash(pos + Vector3.up * 0.8f, c, 0.8f, 0.15f);
                        if (e.Value != (int)PickupType.Orb) Fx.I.Shards(pos, c, 8);
                        if (me) Sfx.Play(e.Value == (int)PickupType.Orb ? Sfx.Orb : e.Value == (int)PickupType.Core ? Sfx.Core : MatchSim.IsLoot((PickupType)e.Value) ? Sfx.Buy : Sfx.Key, 0.6f, 1f + Random.Range(0, 0.1f));
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
                    Sfx.PlayAt(Sfx.Dash, pos, me ? 0.7f : 0.5f, e.B == 1 ? 0.6f : 1f);
                    if (e.B == 1) Fx.I.Column(pos, Palette.Hex("#5fd8ff"), 6f, 0.6f);   // jump pad launch
                    break;
                case EventType.Explosion:
                {
                    Fx.I.Flash(pos + Vector3.up * 0.8f, new Color(1f, 0.8f, 0.4f), 3.2f, 0.18f);
                    Fx.I.Flash(pos + Vector3.up * 0.8f, new Color(1f, 0.35f, 0.15f), 5f, 0.3f);
                    Fx.I.PulseWave(pos, GameConfig.GrenadeRadius, new Color(1f, 0.5f, 0.2f));
                    Fx.I.Shards(pos, new Color(1f, 0.6f, 0.25f), 18);
                    Fx.I.Puff(pos, new Color(0.35f, 0.33f, 0.36f, 0.9f), 18, 1.3f);
                    float d = Vector3.Distance(pos, Local.Pos);
                    if (d < 22f) _cam.Shake(Mathf.Lerp(1.3f, 0.2f, d / 22f));
                    break;
                }
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
            Color owner = z.Squad >= 0 ? (z.Squad == m.LocalSquad ? Palette.Health : Palette.SquadColor(z.Squad)) : zc;
            float pulse = z.Contested ? 0.5f + Mathf.Sin(Time.time * 12f) * 0.4f : 0.85f;
            _ring.SetColor("_BaseColor", new Color(owner.r, owner.g, owner.b, pulse));
            _fill.SetColor("_BaseColor", new Color(owner.r, owner.g, owner.b, z.Squad >= 0 ? 0.16f : 0.07f));

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

    /// <summary>Thins and fades a set of beam lines, then removes them.</summary>
    public sealed class BeamFade : MonoBehaviour
    {
        private LineRenderer[] _lines;
        private float[] _w;
        private float _life, _t;

        public void Init(LineRenderer[] lines, float life)
        {
            _lines = lines; _life = life;
            _w = new float[lines.Length];
            for (int i = 0; i < lines.Length; i++) _w[i] = lines[i].widthMultiplier;
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float k = 1f - Mathf.Clamp01(_t / _life);
            for (int i = 0; i < _lines.Length; i++) _lines[i].widthMultiplier = _w[i] * k * k;
            if (_t >= _life) Destroy(gameObject);
        }
    }
}
