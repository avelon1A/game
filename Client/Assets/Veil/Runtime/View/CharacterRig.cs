using System.Collections.Generic;
using System;
using UnityEngine;
using Veil.Sim;

namespace Veil.View
{
    /// <summary>What the rig should do this frame.</summary>
    public struct RigState
    {
        public Vector3 Velocity;
        public float VerticalVelocity;
        public bool Grounded;
        public bool Dashing;
        public bool Aiming;
        public bool Sprinting;
        public bool Dead;
        public bool Victory;
        public bool Idle;   // lobby / portrait pose
        public bool Downed;     // knocked down: sit / crawl
        public bool Reviving;   // kneeling over a downed squadmate
    }

    /// <summary>
    /// Procedural chibi character modelled on the VEIL reference lineup (Vanguard, Pixie, Shade,
    /// Nova, Bolt): oversized head with anime eyes, strand hair, layered costume, chunky sneakers,
    /// and a jointed skeleton (hips → spine → chest → neck/head, shoulders → elbows → hands,
    /// thighs → knees → feet) driven by a distance-matched procedural gait.
    /// </summary>
    public sealed class CharacterRig : MonoBehaviour
    {
        public Appearance Look { get; private set; }

        /// <summary>Raised when a foot plants (world position, 0..1 sprint amount).</summary>
        public Action<Vector3, float> Footstep;

        private Transform _body, _hips, _spine, _chest, _neck, _head;
        private Transform _shoulderL, _shoulderR, _elbowL, _elbowR, _handL, _handR;
        private Transform _thighL, _thighR, _kneeL, _kneeR, _footL, _footR;
        private Transform _eyeL, _eyeR, _gun, _blasterTip;
        private bool _shadows = true;

        // animation state
        private float _phase, _time, _squash, _squashVel, _crouch;
        private float _fireKick, _aimT, _aimW, _castT, _hitT, _deathT, _victoryT;
        private float _lean, _roll, _lastYaw, _turnRate, _blinkT = 2f, _blink, _lookT = 4f, _lookYaw, _lookTarget;
        private Vector3 _lastVel;
        private bool _wasGrounded = true;
        private float _lastFoot;

        private const float HipY = 0.62f;
        public Transform Head => _head;
        public Transform BlasterTip => _blasterTip;
        public float DeathProgress => _deathT;
        public bool IsAiming => _aimT > 0;

        public static CharacterRig Create(Transform parent, Appearance look, bool shadows = true, string name = "Character")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<CharacterRig>();
            rig._shadows = shadows;
            rig.Rebuild(look);
            return rig;
        }

        public void Rebuild(Appearance look)
        {
            Look = look;
            if (_body != null) Destroy(_body.gameObject);
            _anim = null;
            if (UseModels && TryBuildModel()) return;
            BuildBody();
        }

        // ================================================================== AI-generated model path

        /// <summary>When true, characters use AI-generated prefabs from Resources/Characters if present.</summary>
        public static bool UseModels = true;
        public static readonly string[] ModelNames = { "vanguard", "volt", "lyra", "nova", "sol" };
        /// <summary>Each hero's blaster (Kenney Blaster Kit, CC0) in Resources/Weapons, same order as ModelNames.</summary>
        public const string SniperModel = "blaster-e";   // long-barrel Kenney blaster
        public static readonly string[] WeaponNames = { "blaster-m", "blaster-a", "blaster-n", "blaster-j", "blaster-g" };
        private static Material _weaponMat;

        private static readonly Dictionary<string, Material> _meshyMats = new Dictionary<string, Material>();

        private static Material MeshyWeaponMaterial(string name)
        {
            if (_meshyMats.TryGetValue(name, out var m)) return m;
            m = new Material(Shader.Find("Veil/Toon")) { name = name, enableInstancing = true };
            m.SetTexture("_BaseMap", Resources.Load<Texture2D>("Weapons/" + name + "_tex"));
            m.SetColor("_BaseColor", Color.white);
            m.SetColor("_ShadeColor", new Color(0.72f, 0.72f, 0.88f));
            m.SetColor("_RimColor", new Color(1, 1, 1, 0.2f));
            m.SetFloat("_Ramp", 0.35f);
            m.SetFloat("_ArtKeep", 0.6f);
            m.SetFloat("_ShadowStrength", 0.6f);
            m.SetColor("_EmissionColor", Color.black);
            _meshyMats[name] = m;
            return m;
        }

        private static Material WeaponMaterial()
        {
            if (_weaponMat != null) return _weaponMat;
            _weaponMat = new Material(Shader.Find("Veil/Toon")) { name = "Blaster", enableInstancing = true };
            _weaponMat.SetTexture("_BaseMap", Resources.Load<Texture2D>("Weapons/colormap"));
            _weaponMat.SetColor("_BaseColor", Color.white);
            _weaponMat.SetColor("_ShadeColor", new Color(0.78f, 0.78f, 0.95f));
            _weaponMat.SetColor("_RimColor", new Color(1, 1, 1, 0.15f));
            _weaponMat.SetFloat("_Ramp", 0.35f);
            _weaponMat.SetFloat("_ArtKeep", 0.35f);
            _weaponMat.SetFloat("_ShadowStrength", 0.6f);
            _weaponMat.SetColor("_EmissionColor", Color.black);
            return _weaponMat;
        }
        public bool IsModel => _anim != null;

        private Animator _anim;
        private Transform _boneRUpper, _boneRLower, _boneLUpper, _boneLLower, _handLB;
        // fists: a quick jab with alternating hands (procedural, on top of the animator)
        private const float PunchTime = 0.28f;
        private float _punchT;
        private bool _punchLeft;
        private bool _fists;
        private float _punchHold;        // keeps the animator punch combo running between quick punches
        private int _hasPunchAnim = -1;  // controller has a "Punching" state (RILO animation pack)
        // gun on the back while running (not shooting); _slingW 0 = in the hands, 1 = slung
        private float _moveSpeed, _slingW;
        private bool Fists => _fists;
        public bool FistsMode => _fists;

        /// <summary>In a match: bare hands (gun hidden, jabs) or the chosen gun.</summary>
        public void SetFists(bool on)
        {
            if (_fists == on) return;
            _fists = on;   // the gun is not hidden: it swings onto the back (LateUpdate holster)
            if (_anim) _anim.SetBool("Aiming", false);
        }
        private float _dist, _deadT;

        // heroes with lobby_act clips (CharacterBuilder) do one of them every few seconds while idling in menus
        private float _lobbyActT = 4f;
        private int _lobbyActN = -1;
        private void LobbyActs(bool lobby, float dt)
        {
            if (_lobbyActN < 0) { _lobbyActN = 0; foreach (var p in _anim.parameters) if (p.name == "LobbyActs") _lobbyActN = p.defaultInt; }
            int n = _lobbyActN;
            if (n == 0) return;
            if (!lobby) { _lobbyActT = 4f; _anim.SetInteger("LobbyAct", 0); return; }
            _lobbyActT -= dt;
            if (_lobbyActT < -0.3f) { _anim.SetInteger("LobbyAct", 0); _lobbyActT = UnityEngine.Random.Range(6f, 11f); }
            else if (_lobbyActT < 0f && _anim.GetInteger("LobbyAct") == 0) _anim.SetInteger("LobbyAct", UnityEngine.Random.Range(1, n + 1));
        }

        public static bool HasModel(int outfit) => Resources.Load<GameObject>("Characters/" + ModelNames[outfit % ModelNames.Length]) != null;

        private bool TryBuildModel()
        {
            var prefab = Resources.Load<GameObject>("Characters/" + ModelNames[Look.Outfit % ModelNames.Length]);
            if (prefab == null) return false;
            _body = Build.Node(transform, "Body", Vector3.zero);
            var inst = Instantiate(prefab, _body, false);
            inst.name = "Model";
            _anim = inst.GetComponentInChildren<Animator>();
            if (_anim == null) { Destroy(_body.gameObject); _anim = null; return false; }
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = _shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            bool human = _anim.avatar != null && _anim.avatar.isHuman;
            Transform Bone(HumanBodyBones hb, string generic) => human ? _anim.GetBoneTransform(hb) : FindBone(inst.transform, generic);
            // our Blender rigs use "UpperArm.R"; Quaternius / UE-style rigs (Ranger) use "upperarm_r"
            _head = Bone(HumanBodyBones.Head, "Head") ?? FindBone(inst.transform, "head");
            _chest = Bone(HumanBodyBones.Chest, "Chest") ?? FindBone(inst.transform, "spine_03");
            _boneRUpper = Bone(HumanBodyBones.RightUpperArm, "UpperArm.R") ?? FindBone(inst.transform, "upperarm_r");
            _boneRLower = Bone(HumanBodyBones.RightLowerArm, "LowerArm.R") ?? FindBone(inst.transform, "lowerarm_r");
            _handR = Bone(HumanBodyBones.RightHand, "Hand.R") ?? FindBone(inst.transform, "hand_r");
            _boneLUpper = Bone(HumanBodyBones.LeftUpperArm, "UpperArm.L") ?? FindBone(inst.transform, "upperarm_l");
            _boneLLower = Bone(HumanBodyBones.LeftLowerArm, "LowerArm.L") ?? FindBone(inst.transform, "lowerarm_l");
            _handLB = Bone(HumanBodyBones.LeftHand, "Hand.L") ?? FindBone(inst.transform, "hand_l");
            _footL = Bone(HumanBodyBones.LeftFoot, "Foot.L") ?? FindBone(inst.transform, "foot_l");
            _footR = Bone(HumanBodyBones.RightFoot, "Foot.R") ?? FindBone(inst.transform, "foot_r");

            // blaster lives outside the bone hierarchy (world-scaled), follows the right hand
            Color accent = Palette.AccentColors[Look.Color % Palette.AccentColors.Length];
            var glow = MaterialLib.Glow(accent, 2.2f);
            var dark = M(Palette.Hex("#1f1d26"), 0.25f, 0.4f);
            var rbox = MeshGen.RoundBox(0.4f);
            _gun = Build.Node(_body, "Blaster", Vector3.zero);
            // Meshy guns (meshy_rifle / meshy_sniper, own texture) when present, else the Kenney blasters
            // gun skin (Appearance.Accessory, bought in the store): 1 = plasma rifle, 2 = dragon sniper
            string meshyName = Look.Weapon == 1 ? (Look.Accessory == 2 ? "meshy_dragon_sniper" : "meshy_sniper")
                                                : (Look.Accessory == 1 ? "meshy_plasma_rifle" : "meshy_rifle");
            if (Resources.Load<GameObject>("Weapons/" + meshyName) == null) meshyName = Look.Weapon == 1 ? "meshy_sniper" : "meshy_rifle";
            var meshy = Resources.Load<GameObject>("Weapons/" + meshyName);
            var weapon = meshy ?? Resources.Load<GameObject>("Weapons/" + (Look.Weapon == 1 ? SniperModel : WeaponNames[Look.Outfit % WeaponNames.Length]));
            if (weapon != null)
            {
                // Kenney Blaster Kit gun (CC0), re-centred on import: grip at the pivot, barrel along +Y (Editor/WeaponImport.cs)
                var w = Instantiate(weapon, _gun, false);
                w.transform.localPosition = Vector3.zero; w.transform.localRotation = Quaternion.identity; w.transform.localScale = Vector3.one;
                foreach (var r in w.GetComponentsInChildren<Renderer>())
                {
                    r.sharedMaterial = meshy != null ? MeshyWeaponMaterial(meshyName) : WeaponMaterial();
                    r.shadowCastingMode = _shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                _blasterTip = w.transform.Find("Muzzle");
                if (Look.Weapon == 1 && meshy == null)   // the Meshy sniper has its own scope
                {
                    // sniper scope on top of the gun (barrel = +Y, top = -Z): tube, two mounts, glowing lenses
                    var scopeMat = M(Palette.Hex("#22212b"), 0.3f, 0.6f);
                    var lens = MaterialLib.Glow(Palette.Hex("#5fd8ff"), 2.5f);
                    float top = -0.16f;
                    P(_gun, MeshGen.Cylinder(12), scopeMat, new Vector3(0, 0.13f, top), new Vector3(0.065f, 0.15f, 0.065f));
                    P(_gun, MeshGen.Cylinder(12), scopeMat, new Vector3(0, 0.27f, top), new Vector3(0.085f, 0.035f, 0.085f));
                    P(_gun, MeshGen.Cylinder(12), scopeMat, new Vector3(0, -0.01f, top), new Vector3(0.08f, 0.03f, 0.08f));
                    P(_gun, MeshGen.Cylinder(12), lens, new Vector3(0, 0.29f, top), new Vector3(0.07f, 0.006f, 0.07f));
                    P(_gun, MeshGen.Cylinder(12), lens, new Vector3(0, -0.027f, top), new Vector3(0.065f, 0.006f, 0.065f));
                    P(_gun, MeshGen.Box, scopeMat, new Vector3(0, 0.06f, top + 0.05f), new Vector3(0.03f, 0.03f, 0.06f));
                    P(_gun, MeshGen.Box, scopeMat, new Vector3(0, 0.2f, top + 0.05f), new Vector3(0.03f, 0.03f, 0.06f));
                }
            }
            else
            {
                P(_gun, rbox, dark, new Vector3(0, 0.1f, 0.02f), new Vector3(0.1f, 0.3f, 0.13f));
                P(_gun, rbox, M(accent * 0.9f, 0.3f), new Vector3(0, 0.04f, -0.07f), new Vector3(0.07f, 0.1f, 0.1f));
                P(_gun, MeshGen.Cylinder(10), glow, new Vector3(0, 0.27f, 0.02f), new Vector3(0.065f, 0.05f, 0.065f));
                P(_gun, MeshGen.Torus(0.25f), glow, new Vector3(0, 0.15f, 0.02f), new Vector3(0.14f, 0.2f, 0.16f));
            }
            if (_blasterTip == null) _blasterTip = Build.Node(_gun, "Tip", new Vector3(0, 0.3f, 0.02f));
            return true;
        }

        private static Transform FindBone(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name || t.name.EndsWith(name) || t.name.Replace("_", ".") == name) return t;
            return null;
        }

        private void AnimateModel(RigState s, float dt)
        {
            Vector3 hv = new Vector3(s.Velocity.x, 0, s.Velocity.z);
            float speed = hv.magnitude;
            _moveSpeed = s.Dead ? 0 : speed;
            _anim.SetFloat("Speed", s.Dead ? 0 : speed, 0.07f, dt);
            _anim.SetFloat("VSpeed", s.VerticalVelocity);
            _anim.SetBool("Grounded", s.Grounded || s.Dead);
            _anim.SetBool("Dashing", s.Dashing);
            _aimT -= dt;
            bool aiming = (s.Aiming || _aimT > 0) && !s.Dead && !s.Downed && !s.Reviving;
            _aimW = Mathf.MoveTowards(_aimW, aiming ? 1f : 0f, dt * 9f);
            _fireKick = Mathf.MoveTowards(_fireKick, 0, dt * 8f);
            _anim.SetBool("Aiming", aiming && !Fists);   // fists: no rifle pose, the jab is procedural
            _punchT = Mathf.Max(0, _punchT - dt);
            _punchHold = Mathf.Max(0, _punchHold - dt);
            if (_hasPunchAnim < 0) { _hasPunchAnim = 0; foreach (var p in _anim.parameters) if (p.name == "Punching") _hasPunchAnim = 1; }
            if (_hasPunchAnim == 1) _anim.SetBool("Punching", Fists && _punchHold > 0 && !s.Dead);
            _anim.SetBool("Dead", s.Dead);
            _anim.SetBool("Downed", s.Downed && !s.Dead);
            _anim.SetBool("Reviving", s.Reviving && !s.Downed && !s.Dead);
            _anim.SetBool("Victory", s.Victory);
            _anim.SetBool("Lobby", s.Idle && !s.Victory);
            LobbyActs(s.Idle && !s.Victory, dt);

            // footsteps from distance travelled
            if (s.Grounded && speed > 0.5f && !s.Dead)
            {
                _dist += speed * dt;
                float half = (Mathf.Lerp(1.15f, 1.75f, Mathf.Clamp01(speed / GameConfig.RunSpeed)) + (speed > GameConfig.RunSpeed ? 0.45f : 0)) * 0.5f;
                if (_dist >= half)
                {
                    _dist = 0;
                    var f = (_phase += 1) % 2 == 0 ? _footL : _footR;
                    Footstep?.Invoke(f != null ? f.position : transform.position, Mathf.Clamp01((speed - GameConfig.RunSpeed) / 2.6f));
                }
            }

            // juice on top of the animation: landing squash, turn lean, hit flinch, death fade
            if (s.Grounded && !_wasGrounded) TriggerLand(Mathf.Clamp01(-s.VerticalVelocity / 9f) + 0.35f);
            if (!s.Grounded && _wasGrounded && s.VerticalVelocity > 1) TriggerJump();
            _wasGrounded = s.Grounded;
            _squashVel += (-_squash * 170f - _squashVel * 15f) * dt;
            _squash += _squashVel * dt;
            float yaw = transform.eulerAngles.y;
            _turnRate = Mathf.Lerp(_turnRate, Mathf.Clamp(Mathf.DeltaAngle(_lastYaw, yaw) / dt, -720, 720), 1 - Mathf.Exp(-10f * dt));
            _lastYaw = yaw;
            _roll = Mathf.Lerp(_roll, Mathf.Clamp(-_turnRate * 0.03f, -12f, 12f) * Mathf.Clamp01(speed / GameConfig.RunSpeed), 1 - Mathf.Exp(-9f * dt));
            float hitTilt = 0;
            if (_hitT > 0) { _hitT -= dt; hitTilt = -Mathf.Sin(Mathf.Clamp01(_hitT / 0.3f) * Mathf.PI) * 10f; }
            float sq = Mathf.Clamp(_squash, -0.18f, 0.18f);
            if (s.Dead) { _deadT += dt; _deathT = _deadT; } else { _deadT = 0; _deathT = 0; }
            float shrink = Mathf.Clamp01((_deadT - 1.4f) / 0.4f);
            _body.localScale = new Vector3(1 - sq * 0.5f, 1 + sq, 1 - sq * 0.5f) * (1 - shrink);
            _body.localRotation = Quaternion.Euler(hitTilt, 0, _roll);
        }

        private const float HolsterTime = 0.55f;   // seconds to swing the gun from the hands onto the back (and back)

        private void LateUpdate()
        {
            if (_anim == null) return;
            Vector3 fwd = transform.forward;
            if (Fists && _punchT > 0)
            {
                // jab: the arm shoots out along the aim direction and comes back (sin curve)
                float w = Mathf.Sin(Mathf.PI * (1f - _punchT / PunchTime));
                var up = _punchLeft ? _boneLUpper : _boneRUpper;
                var lo = _punchLeft ? _boneLLower : _boneRLower;
                var hand = _punchLeft ? _handLB : _handR;
                if (up && lo && hand)
                {
                    Vector3 dir = (fwd + Vector3.up * 0.08f).normalized;
                    var q = Quaternion.FromToRotation(hand.position - up.position, dir);
                    up.rotation = Quaternion.Slerp(Quaternion.identity, q, w) * up.rotation;
                    var q2 = Quaternion.FromToRotation(hand.position - lo.position, dir);
                    lo.rotation = Quaternion.Slerp(Quaternion.identity, q2, w) * lo.rotation;
                }
            }
            // aim override: point the right arm (and gun) straight down the aim direction
            float aimW = Fists ? 0f : _aimW;
            if (aimW > 0.01f && _boneRUpper && _boneRLower && _handR)
            {
                var q = Quaternion.FromToRotation(_handR.position - _boneRUpper.position, fwd);
                _boneRUpper.rotation = Quaternion.Slerp(Quaternion.identity, q, aimW) * _boneRUpper.rotation;
                var q2 = Quaternion.FromToRotation(_handR.position - _boneRLower.position, fwd);
                _boneRLower.rotation = Quaternion.Slerp(Quaternion.identity, q2, aimW) * _boneRLower.rotation;
            }
            if (!_gun || !_handR) return;

            // holster: on the back while running without shooting, and always while fighting with fists
            bool sling = Fists || (_moveSpeed > 1.2f && _aimW < 0.05f);
            _slingW = Mathf.MoveTowards(_slingW, sling ? 1f : 0f, Time.deltaTime / HolsterTime);
            float t = Mathf.SmoothStep(0f, 1f, _slingW);

            var relaxed = transform.rotation * Quaternion.Euler(150, 0, 0);
            var aimed = transform.rotation * Quaternion.Euler(90 - _fireKick * 12f, 0, 0);
            var handPos = _handR.position + fwd * 0.04f;
            var handRot = Quaternion.Slerp(relaxed, aimed, aimW);
            var gunPos = handPos; var gunRot = handRot;
            if (t > 0.001f && _chest)
            {
                Vector3 right = transform.right, upv = transform.up;
                Vector3 barrel = (upv * 0.82f + right * 0.57f).normalized;           // ~35° off vertical, barrel over the right shoulder
                var backRot = Quaternion.LookRotation(fwd, barrel);                 // gun +Y = barrel, -Z = top → top faces away from the back
                var backPos = _chest.position - fwd * 0.2f - right * 0.14f - upv * 0.22f;
                // the gun travels on an arc up past the right shoulder (quadratic bezier), not straight through the body
                var over = _chest.position + upv * 0.38f + right * 0.24f - fwd * 0.05f;
                gunPos = (1 - t) * (1 - t) * handPos + 2 * (1 - t) * t * over + t * t * backPos;
                gunRot = Quaternion.Slerp(handRot, backRot, t);

                // mid-swing the right hand carries the gun: the arm reaches for the grip, then lets go on the back
                float carry = Mathf.Sin(Mathf.PI * t);
                if (carry > 0.01f && _boneRUpper && _boneRLower)
                {
                    var q = Quaternion.FromToRotation(_handR.position - _boneRUpper.position, gunPos - _boneRUpper.position);
                    _boneRUpper.rotation = Quaternion.Slerp(Quaternion.identity, q, carry) * _boneRUpper.rotation;
                    var q2 = Quaternion.FromToRotation(_handR.position - _boneRLower.position, gunPos - _boneRLower.position);
                    _boneRLower.rotation = Quaternion.Slerp(Quaternion.identity, q2, carry) * _boneRLower.rotation;
                }
            }
            _gun.SetPositionAndRotation(gunPos, gunRot);

            // shooting: the big gun is held with BOTH hands — left hand reaches the foregrip
            if (aimW > 0.01f && _boneLUpper && _boneLLower && _handLB)
            {
                float reach = Look.Weapon == 1 ? 0.42f : 0.27f;                    // sniper foregrip sits further out
                Vector3 grip = _gun.position + _gun.up * reach;
                var q = Quaternion.FromToRotation(_handLB.position - _boneLUpper.position, grip - _boneLUpper.position);
                _boneLUpper.rotation = Quaternion.Slerp(Quaternion.identity, q, aimW) * _boneLUpper.rotation;
                var q2 = Quaternion.FromToRotation(_handLB.position - _boneLLower.position, grip - _boneLLower.position);
                _boneLLower.rotation = Quaternion.Slerp(Quaternion.identity, q2, aimW) * _boneLLower.rotation;
            }
        }

        // ================================================================== construction

        private GameObject P(Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 scale, Quaternion? rot = null)
            => Build.Part(parent, mesh, mat, pos, scale, rot, null, _shadows);

        private static Material M(Color c, float rim = 0.3f, float gloss = 0f) => MaterialLib.Toon(c, rim, gloss);

        private void BuildBody()
        {
            var o = Palette.Outfits[Look.Outfit % Palette.Outfits.Length];
            Color accent = Palette.AccentColors[Look.Color % Palette.AccentColors.Length];
            Color hair = Palette.HairColors[Look.HairColor % Palette.HairColors.Length];
            bool hood = Look.Hair == 4;

            var jacket = M(o.Jacket, 0.35f, 0.15f);
            var panel = M(o.Panel, 0.3f);
            var inner = M(o.Inner, 0.25f);
            var collar = M(o.Collar, 0.3f);
            var pants = M(o.Pants, 0.28f);
            var cuff = M(o.Cuff, 0.3f);
            var glove = M(o.Glove, 0.35f, 0.35f);
            var gloveAcc = M(o.GloveAccent, 0.3f);
            var shoe = M(o.Shoe, 0.3f, 0.3f);
            var shoeAcc = M(o.ShoeAccent, 0.3f);
            var sole = M(o.Sole, 0.2f);
            var belt = M(o.Belt, 0.25f);
            var buckle = M(o.Buckle, 0.3f, 0.6f);
            var sock = M(o.Sock, 0.25f);
            var skin = M(Palette.Skin, 0.22f);
            var glow = MaterialLib.Glow(accent, 2.2f);
            var dark = M(Palette.Hex("#1f1d26"), 0.25f, 0.4f);
            var rbox = MeshGen.RoundBox(0.4f);
            var rboxSoft = MeshGen.RoundBox(0.6f);

            _body = Build.Node(transform, "Body", Vector3.zero);
            _hips = Build.Node(_body, "Hips", new Vector3(0, HipY, 0));

            // ---------------- hips: shorts/pants block, belt, pouches ----------------
            P(_hips, rboxSoft, pants, new Vector3(0, -0.03f, 0), new Vector3(0.5f, 0.26f, 0.38f));
            P(_hips, MeshGen.Cylinder(18), belt, new Vector3(0, 0.09f, 0), new Vector3(0.53f, 0.075f, 0.41f));
            P(_hips, rbox, buckle, new Vector3(0, 0.09f, 0.205f), new Vector3(0.13f, 0.085f, 0.04f));
            for (int s = -1; s <= 1; s += 2)
                P(_hips, rbox, o.Cargo ? M(o.Belt * 0.85f, 0.25f) : belt, new Vector3(0.25f * s, 0.01f, 0.05f), new Vector3(0.1f, 0.14f, 0.13f));

            // ---------------- spine / chest ----------------
            _spine = Build.Node(_hips, "Spine", new Vector3(0, 0.1f, 0));
            _chest = Build.Node(_spine, "Chest", new Vector3(0, 0.02f, 0));
            float jacketH = o.CropTop ? 0.34f : 0.5f;
            float jacketY = o.CropTop ? 0.34f : 0.25f;
            if (o.CropTop)
            {
                P(_chest, rboxSoft, skin, new Vector3(0, 0.1f, 0), new Vector3(0.44f, 0.22f, 0.34f));
                P(_chest, rboxSoft, inner, new Vector3(0, 0.28f, 0.02f), new Vector3(0.46f, 0.2f, 0.36f));
            }
            P(_chest, rbox, jacket, new Vector3(0, jacketY, 0), new Vector3(0.62f, jacketH, 0.44f));
            // open front showing the inner shirt
            P(_chest, rbox, inner, new Vector3(0, jacketY + 0.02f, 0.2f), new Vector3(0.22f, jacketH * 0.86f, 0.07f));
            P(_chest, MeshGen.Box, M(o.Panel * 0.8f), new Vector3(0, jacketY + 0.02f, 0.238f), new Vector3(0.012f, jacketH * 0.8f, 0.01f));
            // front panels + shoulder panels
            for (int s = -1; s <= 1; s += 2)
            {
                P(_chest, rbox, panel, new Vector3(0.16f * s, jacketY + 0.03f, 0.205f), new Vector3(0.09f, jacketH * 0.8f, 0.05f));
                P(_chest, rboxSoft, panel, new Vector3(0.25f * s, 0.46f, 0), new Vector3(0.2f, 0.12f, 0.38f));
            }
            // chest emblem
            P(_chest, MeshGen.Cylinder(16), glow, new Vector3(-0.2f, jacketY + 0.1f, 0.225f), new Vector3(0.07f, 0.02f, 0.07f), Quaternion.Euler(90, 0, 0));
            // collar / hood bunched behind the neck / scarf
            P(_chest, MeshGen.Torus(0.24f), collar, new Vector3(0, 0.52f, -0.01f), new Vector3(0.44f, 1.3f, 0.4f));
            if (o.HoodDown) P(_chest, rboxSoft, collar, new Vector3(0, 0.5f, -0.2f), new Vector3(0.42f, 0.2f, 0.18f), Quaternion.Euler(-15, 0, 0));
            if (o.Scarf)
            {
                var scarf = M(Palette.AccentColors[Look.Color % 8], 0.3f);
                P(_chest, MeshGen.Torus(0.3f), scarf, new Vector3(0, 0.55f, 0.01f), new Vector3(0.44f, 1.8f, 0.42f));
                P(_chest, MeshGen.Strand(0.15f, 0.3f), scarf, new Vector3(0.1f, 0.52f, 0.2f), new Vector3(0.14f, 0.28f, 0.14f), Quaternion.Euler(180, 0, -10));
            }
            if (o.ShoulderPad)
            {
                P(_chest, MeshGen.Cylinder(18), dark, new Vector3(-0.37f, 0.44f, 0), new Vector3(0.28f, 0.08f, 0.28f), Quaternion.Euler(0, 0, 90));
                P(_chest, MeshGen.Torus(0.12f), glow, new Vector3(-0.415f, 0.44f, 0), new Vector3(0.2f, 1f, 0.2f), Quaternion.Euler(0, 0, 90));
            }

            // ---------------- arms ----------------
            _shoulderL = Build.Node(_chest, "ShoulderL", new Vector3(-0.35f, 0.42f, 0));
            _shoulderR = Build.Node(_chest, "ShoulderR", new Vector3(0.35f, 0.42f, 0));
            BuildArm(_shoulderL, -1, out _elbowL, out _handL, jacket, cuff, glove, gloveAcc, glow, o.ArmRings);
            BuildArm(_shoulderR, 1, out _elbowR, out _handR, jacket, cuff, glove, gloveAcc, glow, o.ArmRings);

            // blaster in the right hand
            _gun = Build.Node(_handR, "Blaster", new Vector3(0, -0.07f, 0.03f), Quaternion.Euler(90, 0, 0));
            P(_gun, rbox, dark, new Vector3(0, 0.1f, 0.02f), new Vector3(0.1f, 0.3f, 0.13f));
            P(_gun, rbox, M(accent * 0.9f, 0.3f), new Vector3(0, 0.04f, -0.07f), new Vector3(0.07f, 0.1f, 0.1f));
            P(_gun, MeshGen.Cylinder(10), glow, new Vector3(0, 0.27f, 0.02f), new Vector3(0.065f, 0.05f, 0.065f));
            P(_gun, MeshGen.Torus(0.25f), glow, new Vector3(0, 0.15f, 0.02f), new Vector3(0.14f, 0.2f, 0.16f));
            _blasterTip = Build.Node(_gun, "Tip", new Vector3(0, 0.3f, 0.02f));

            // ---------------- legs ----------------
            _thighL = Build.Node(_hips, "ThighL", new Vector3(-0.13f, -0.05f, 0));
            _thighR = Build.Node(_hips, "ThighR", new Vector3(0.13f, -0.05f, 0));
            BuildLeg(_thighL, -1, out _kneeL, out _footL, o, pants, cuff, sock, shoe, shoeAcc, sole, dark, glow);
            BuildLeg(_thighR, 1, out _kneeR, out _footR, o, pants, cuff, sock, shoe, shoeAcc, sole, dark, glow);

            // ---------------- head ----------------
            _neck = Build.Node(_chest, "Neck", new Vector3(0, 0.56f, 0));
            _head = Build.Node(_neck, "Head", Vector3.zero);
            Vector3 hc = new Vector3(0, 0.37f, 0.02f);
            if (!hood)
            {
                P(_neck, MeshGen.Cylinder(12), skin, new Vector3(0, 0.03f, 0), new Vector3(0.16f, 0.1f, 0.16f));
                P(_head, MeshGen.Sphere, skin, hc, new Vector3(0.8f, 0.78f, 0.76f));
                P(_head, MeshGen.Sphere, skin, hc + new Vector3(0, -0.13f, 0.06f), new Vector3(0.64f, 0.48f, 0.58f));
                BuildFace(hc, hair, accent, skin);
            }
            BuildHair(hc, hair, accent);
            BuildAccessory(hc, accent, dark, glow);
        }

        private void BuildArm(Transform shoulder, int s, out Transform elbow, out Transform hand, Material jacket, Material cuff, Material glove, Material gloveAcc, Material glow, bool rings)
        {
            P(shoulder, MeshGen.Sphere, jacket, Vector3.zero, Vector3.one * 0.24f);
            P(shoulder, MeshGen.Capsule, jacket, new Vector3(0, -0.13f, 0), new Vector3(0.44f, 0.3f, 0.44f));
            if (rings) P(shoulder, MeshGen.Torus(0.16f), glow, new Vector3(0, -0.12f, 0), new Vector3(0.24f, 0.8f, 0.24f));
            elbow = Build.Node(shoulder, "Elbow", new Vector3(0, -0.26f, 0));
            P(elbow, MeshGen.Capsule, jacket, new Vector3(0, -0.09f, 0), new Vector3(0.42f, 0.24f, 0.42f));
            P(elbow, MeshGen.Torus(0.3f), cuff, new Vector3(0, -0.19f, 0), new Vector3(0.2f, 0.8f, 0.2f));
            hand = Build.Node(elbow, "Hand", new Vector3(0, -0.23f, 0));
            var rb = MeshGen.RoundBox(0.55f);
            P(hand, MeshGen.Cylinder(12), glove, new Vector3(0, 0.02f, 0), new Vector3(0.19f, 0.07f, 0.19f));
            P(hand, rb, glove, new Vector3(0, -0.06f, 0.01f), new Vector3(0.17f, 0.17f, 0.18f));
            P(hand, rb, gloveAcc, new Vector3(0.075f * s, -0.05f, 0.01f), new Vector3(0.04f, 0.1f, 0.12f));
            P(hand, MeshGen.Box, gloveAcc, new Vector3(0, -0.03f, 0.1f), new Vector3(0.12f, 0.035f, 0.02f));
        }

        private void BuildLeg(Transform thigh, int s, out Transform knee, out Transform foot, Palette.Outfit o, Material pants, Material cuff, Material sock, Material shoe, Material shoeAcc, Material sole, Material dark, Material glow)
        {
            var rb = MeshGen.RoundBox(0.4f);
            var rbSoft = MeshGen.RoundBox(0.55f);
            P(thigh, MeshGen.Capsule, pants, new Vector3(0, -0.11f, 0), new Vector3(0.5f, 0.3f, 0.5f));
            if (o.Cargo) P(thigh, rb, M(o.Pants * 0.8f, 0.25f), new Vector3(0.13f * s, -0.12f, 0.02f), new Vector3(0.06f, 0.12f, 0.14f));
            knee = Build.Node(thigh, "Knee", new Vector3(0, -0.24f, 0));
            if (o.Shorts)
            {
                P(knee, MeshGen.Torus(0.3f), cuff, new Vector3(0, 0.03f, 0), new Vector3(0.26f, 0.6f, 0.26f));
                P(knee, MeshGen.Capsule, sock, new Vector3(0, -0.1f, 0), new Vector3(0.36f, 0.24f, 0.36f));
                if (o.KneePads)
                {
                    P(knee, rbSoft, dark, new Vector3(0, -0.03f, 0.075f), new Vector3(0.19f, 0.15f, 0.1f));
                    P(knee, MeshGen.Box, glow, new Vector3(0, -0.03f, 0.13f), new Vector3(0.1f, 0.02f, 0.01f));
                }
            }
            else
            {
                P(knee, MeshGen.Capsule, pants, new Vector3(0, -0.1f, 0), new Vector3(0.48f, 0.26f, 0.48f));
                P(knee, MeshGen.Torus(0.3f), cuff, new Vector3(0, -0.2f, 0), new Vector3(0.23f, 0.6f, 0.23f));
                if (o.Cargo) P(knee, rb, M(o.Pants * 0.85f, 0.25f), new Vector3(0, -0.02f, 0.1f), new Vector3(0.14f, 0.08f, 0.05f));
            }
            // chunky sneaker
            foot = Build.Node(knee, "Foot", new Vector3(0, -0.2f, 0));
            P(foot, rbSoft, shoe, new Vector3(0, -0.035f, 0.05f), new Vector3(0.23f, 0.15f, 0.31f));
            P(foot, rbSoft, shoeAcc, new Vector3(0, -0.055f, 0.17f), new Vector3(0.2f, 0.1f, 0.13f));
            P(foot, rb, shoeAcc, new Vector3(0, -0.03f, 0.01f), new Vector3(0.24f, 0.05f, 0.2f));
            P(foot, rb, sole, new Vector3(0, -0.1f, 0.05f), new Vector3(0.26f, 0.07f, 0.38f));
            P(foot, MeshGen.Torus(0.25f), shoeAcc, new Vector3(0, 0.035f, -0.02f), new Vector3(0.2f, 0.6f, 0.2f));
            P(foot, MeshGen.Box, sole, new Vector3(0, 0.03f, 0.1f), new Vector3(0.09f, 0.025f, 0.12f), Quaternion.Euler(-20, 0, 0));
        }

        private static Vector3 Face(Vector3 hc, float x, float y, float lift = 0)
        {
            float z = 0.38f * Mathf.Sqrt(Mathf.Max(0.05f, 1 - (x / 0.4f) * (x / 0.4f) - (y / 0.39f) * (y / 0.39f)));
            return hc + new Vector3(x, y, z + lift);
        }

        private void BuildFace(Vector3 hc, Color hair, Color accent, Material skin)
        {
            var outline = M(Palette.Hex("#1b1321"), 0.05f);
            bool whiteHair = hair.r > 0.85f && hair.g > 0.85f;
            Color irisCol = whiteHair ? accent : Color.Lerp(hair, Color.black, 0.2f);
            if (Look.HairColor == 0) irisCol = Palette.Hex("#7a4a28");
            var iris = M(irisCol, 0.1f, 0.2f);
            var pupil = M(Color.Lerp(irisCol, Color.black, 0.7f), 0.05f);
            var white = M(Color.white, 0.05f);
            var brow = M(Color.Lerp(hair, Color.black, 0.45f), 0.1f);

            for (int s = -1; s <= 1; s += 2)
            {
                var eye = Build.Node(_head, s < 0 ? "EyeL" : "EyeR", Face(hc, 0.145f * s, -0.035f), Quaternion.Euler(0, 20 * s, 0));
                if (s < 0) _eyeL = eye; else _eyeR = eye;
                P(eye, MeshGen.Sphere, outline, Vector3.zero, new Vector3(0.15f, 0.2f, 0.045f));
                P(eye, MeshGen.Sphere, iris, new Vector3(0, -0.012f, 0.01f), new Vector3(0.12f, 0.165f, 0.04f));
                P(eye, MeshGen.Sphere, pupil, new Vector3(0, -0.02f, 0.016f), new Vector3(0.068f, 0.1f, 0.03f));
                P(eye, MeshGen.SphereLow, white, new Vector3(-0.025f * s, 0.035f, 0.025f), Vector3.one * 0.05f);
                P(eye, MeshGen.SphereLow, white, new Vector3(0.028f * s, -0.055f, 0.023f), Vector3.one * 0.022f);
                P(eye, MeshGen.Box, outline, new Vector3(0.005f * s, 0.098f, 0.005f), new Vector3(0.17f, 0.035f, 0.035f), Quaternion.Euler(0, 0, 9 * s));
                // determined brows: inner end low, outer end high
                P(_head, MeshGen.RoundBox(0.5f), brow, Face(hc, 0.14f * s, 0.12f, 0.01f), new Vector3(0.15f, 0.04f, 0.035f), Quaternion.Euler(0, 20 * s, 17 * s));
                P(_head, MeshGen.SphereLow, M(Palette.Hex("#ffb0a8"), 0.05f), Face(hc, 0.22f * s, -0.14f, -0.01f), new Vector3(0.08f, 0.035f, 0.02f), Quaternion.Euler(0, 32 * s, 0));
                P(_head, MeshGen.Sphere, skin, hc + new Vector3(0.39f * s, -0.04f, 0), new Vector3(0.09f, 0.13f, 0.07f));
            }
            P(_head, MeshGen.SphereLow, M(Palette.Skin * 0.9f, 0.1f), Face(hc, 0, -0.12f, 0.005f), new Vector3(0.035f, 0.03f, 0.03f));
            if (Look.Accessory != 4)
                P(_head, MeshGen.RoundBox(0.6f), M(Palette.Hex("#6a2a32"), 0.05f), Face(hc, 0.015f, -0.2f, -0.005f), new Vector3(0.075f, 0.02f, 0.02f), Quaternion.Euler(0, 0, 8));
        }

        /// <summary>A hair strand growing from inside the scalp towards dir, curling towards curlDir.</summary>
        private void Strand(Vector3 hc, Material mat, Vector3 origin, Vector3 dir, Vector3 curlDir, float len, float width, float curl = 0.35f)
        {
            dir.Normalize();
            Vector3 fwd = Vector3.ProjectOnPlane(curlDir, dir);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(Vector3.back, dir);
            var rot = Quaternion.LookRotation(fwd.normalized, dir);
            P(_head, MeshGen.Strand(curl), mat, hc + origin, new Vector3(width, len, width), rot);
        }

        private void BuildHair(Vector3 hc, Color hairCol, Color accent)
        {
            var hair = M(hairCol, 0.4f, 0.15f);
            var hairD = M(Color.Lerp(hairCol, Color.black, 0.18f), 0.35f, 0.1f);
            Vector3 back = Vector3.back, down = Vector3.down;
            switch (Look.Hair)
            {
                case 0: // Vanguard: big swept-back anime spikes + bangs
                {
                    P(_head, MeshGen.Sphere, hair, hc + new Vector3(0, 0.08f, -0.05f), new Vector3(0.86f, 0.8f, 0.84f));
                    Vector3[] d =
                    {
                        new Vector3(0, 1, -0.2f), new Vector3(0.38f, 0.9f, -0.35f), new Vector3(-0.38f, 0.9f, -0.35f), new Vector3(0.72f, 0.55f, -0.45f),
                        new Vector3(-0.72f, 0.55f, -0.45f), new Vector3(0, 0.55f, -1f), new Vector3(0.45f, 0.25f, -0.9f), new Vector3(-0.45f, 0.25f, -0.9f),
                        new Vector3(0.95f, 0.15f, -0.2f), new Vector3(-0.95f, 0.15f, -0.2f), new Vector3(0.2f, 1f, 0.25f), new Vector3(-0.25f, 0.95f, 0.22f),
                        new Vector3(0, 0.05f, -1f),
                    };
                    float[] l = { 0.55f, 0.5f, 0.5f, 0.45f, 0.45f, 0.5f, 0.42f, 0.42f, 0.34f, 0.34f, 0.44f, 0.42f, 0.36f };
                    for (int i = 0; i < d.Length; i++)
                        Strand(hc, i % 3 == 0 ? hairD : hair, d[i].normalized * 0.28f + new Vector3(0, 0.06f, -0.04f), d[i], back + Vector3.up * 0.2f, l[i], 0.26f, 0.4f);
                    // bangs over the forehead
                    Strand(hc, hair, new Vector3(0.12f, 0.3f, 0.2f), new Vector3(0.35f, -0.35f, 1f), down, 0.3f, 0.2f, 0.5f);
                    Strand(hc, hairD, new Vector3(-0.02f, 0.3f, 0.22f), new Vector3(-0.05f, -0.45f, 1f), down, 0.33f, 0.21f, 0.5f);
                    Strand(hc, hair, new Vector3(-0.16f, 0.28f, 0.2f), new Vector3(-0.4f, -0.3f, 1f), down, 0.28f, 0.19f, 0.5f);
                    Strand(hc, hair, new Vector3(0.28f, 0.2f, 0.12f), new Vector3(0.6f, -0.3f, 0.7f), down, 0.26f, 0.17f, 0.5f);
                    Strand(hc, hair, new Vector3(-0.3f, 0.18f, 0.1f), new Vector3(-0.7f, -0.35f, 0.6f), down, 0.26f, 0.17f, 0.5f);
                    break;
                }
                case 1: // Pixie: high side ponytail, messy bangs, side locks
                {
                    P(_head, MeshGen.Sphere, hair, hc + new Vector3(0, 0.07f, -0.04f), new Vector3(0.87f, 0.82f, 0.86f));
                    for (int i = 0; i < 5; i++)
                    {
                        float x = -0.2f + i * 0.1f;
                        Strand(hc, i % 2 == 0 ? hair : hairD, new Vector3(x, 0.3f, 0.18f), new Vector3(x * 0.8f + 0.25f, -0.55f, 0.8f), down, 0.34f, 0.2f, 0.5f);
                    }
                    for (int s = -1; s <= 1; s += 2)
                        Strand(hc, hair, new Vector3(0.33f * s, 0.12f, 0.1f), new Vector3(0.2f * s, -1f, 0.15f), new Vector3(s, 0, 0), 0.46f, 0.17f, 0.3f);
                    Vector3 tie = new Vector3(0.24f, 0.32f, -0.22f);
                    P(_head, MeshGen.Torus(0.3f), MaterialLib.Glow(accent, 1.5f), hc + tie, new Vector3(0.14f, 0.3f, 0.14f), Quaternion.Euler(40, 0, -40));
                    Vector3[] t = { new Vector3(0.6f, 0.5f, -0.6f), new Vector3(0.85f, 0.15f, -0.45f), new Vector3(0.5f, 0.05f, -0.85f), new Vector3(0.9f, 0.55f, -0.1f), new Vector3(0.7f, -0.25f, -0.6f), new Vector3(0.3f, 0.35f, -0.95f) };
                    for (int i = 0; i < t.Length; i++)
                        Strand(hc, i % 2 == 0 ? hair : hairD, tie, t[i], down, 0.68f + (i % 3) * 0.08f, 0.28f, 0.65f);
                    break;
                }
                case 2: // Nova: sleek bangs, side locks, back ponytail with an accent streak
                {
                    P(_head, MeshGen.Sphere, hair, hc + new Vector3(0, 0.07f, -0.04f), new Vector3(0.87f, 0.82f, 0.87f));
                    for (int i = 0; i < 6; i++)
                    {
                        float x = -0.22f + i * 0.088f;
                        var mat = i == 4 ? M(accent, 0.4f) : (i % 2 == 0 ? hair : hairD);
                        Strand(hc, mat, new Vector3(x, 0.31f, 0.16f), new Vector3(x * 0.6f - 0.15f, -0.7f, 0.6f), down, 0.36f, 0.2f, 0.45f);
                    }
                    for (int s = -1; s <= 1; s += 2)
                        Strand(hc, hair, new Vector3(0.33f * s, 0.1f, 0.12f), new Vector3(0.12f * s, -1f, 0.1f), new Vector3(s, 0, 0), 0.56f, 0.19f, 0.25f);
                    Vector3 tie = new Vector3(-0.2f, 0.3f, -0.27f);
                    Vector3[] t = { new Vector3(-0.7f, 0.4f, -0.6f), new Vector3(-0.9f, 0.05f, -0.4f), new Vector3(-0.4f, 0.1f, -0.9f), new Vector3(-0.85f, 0.5f, 0.1f), new Vector3(-0.6f, -0.3f, -0.6f) };
                    for (int i = 0; i < t.Length; i++)
                        Strand(hc, i == 2 ? M(accent, 0.4f) : (i % 2 == 0 ? hair : hairD), tie, t[i], down, 0.62f, 0.27f, 0.6f);
                    break;
                }
                case 3: // Bolt: tall green crest
                {
                    P(_head, MeshGen.Sphere, hair, hc + new Vector3(0, 0.05f, -0.05f), new Vector3(0.84f, 0.76f, 0.84f));
                    Vector3[] d =
                    {
                        new Vector3(0, 1, 0.25f), new Vector3(0.3f, 1, 0.05f), new Vector3(-0.3f, 1, 0.05f), new Vector3(0, 1, -0.35f),
                        new Vector3(0.45f, 0.9f, -0.3f), new Vector3(-0.45f, 0.9f, -0.3f), new Vector3(0, 0.75f, -0.8f), new Vector3(0.65f, 0.6f, 0.1f),
                        new Vector3(-0.65f, 0.6f, 0.1f), new Vector3(0.3f, 0.55f, -0.8f), new Vector3(-0.3f, 0.55f, -0.8f),
                    };
                    for (int i = 0; i < d.Length; i++)
                        Strand(hc, i % 3 == 0 ? hairD : hair, d[i].normalized * 0.26f + new Vector3(0, 0.05f, -0.03f), d[i], back, 0.5f + (i < 4 ? 0.1f : 0), 0.26f, 0.3f);
                    break;
                }
                case 4: // Shade: dark hood, void face, glowing angled eyes
                {
                    var hoodMat = M(Palette.Outfits[Look.Outfit % Palette.Outfits.Length].Jacket, 0.4f, 0.1f);
                    P(_head, MeshGen.Sphere, M(Palette.Hex("#07070c"), 0f), hc, new Vector3(0.8f, 0.78f, 0.76f));
                    P(_head, MeshGen.Sphere, hoodMat, hc + new Vector3(0, 0.05f, -0.06f), new Vector3(1.0f, 0.98f, 1.02f));
                    P(_head, MeshGen.Sphere, M(Palette.Hex("#050508"), 0f), hc + new Vector3(0, -0.03f, 0.3f), new Vector3(0.66f, 0.62f, 0.3f));
                    P(_head, MeshGen.Torus(0.1f), hoodMat, hc + new Vector3(0, -0.02f, 0.38f), new Vector3(0.72f, 0.7f, 0.68f), Quaternion.Euler(90, 0, 0));
                    Strand(hc, hoodMat, new Vector3(0, 0.35f, 0.02f), new Vector3(0, 0.55f, 0.85f), down, 0.32f, 0.5f, 0.6f);
                    for (int s = -1; s <= 1; s += 2)
                        Strand(hc, MaterialLib.Glow(accent, 1.6f), new Vector3(0.36f * s, 0.18f, -0.05f), new Vector3(0.25f * s, 1f, -0.3f), new Vector3(s, 0, 0), 0.22f, 0.06f, 0.2f);
                    var eyeGlow = MaterialLib.Glow(accent, 4f);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var eye = Build.Node(_head, s < 0 ? "EyeL" : "EyeR", hc + new Vector3(0.12f * s, -0.01f, 0.45f), Quaternion.Euler(0, 0, -24 * s));
                        if (s < 0) _eyeL = eye; else _eyeR = eye;
                        P(eye, MeshGen.Sphere, eyeGlow, Vector3.zero, new Vector3(0.13f, 0.075f, 0.04f));
                        P(eye, MeshGen.Sphere, MaterialLib.Unlit(new Color(accent.r, accent.g, accent.b, 0.35f), MaterialLib.Blend.Additive), Vector3.zero, new Vector3(0.22f, 0.14f, 0.05f));
                    }
                    break;
                }
            }
        }

        private void Goggles(Vector3 hc, Vector3 center, float tilt, Material frame, Material lens, float size)
        {
            var rot = Quaternion.Euler(tilt, 0, 0);
            for (int s = -1; s <= 1; s += 2)
            {
                var pos = hc + center + new Vector3(0.12f * s, 0, 0);
                P(_head, MeshGen.Cylinder(16), frame, pos, new Vector3(size, 0.07f, size), rot);
                P(_head, MeshGen.Cylinder(16), lens, pos + rot * new Vector3(0, 0.04f, 0), new Vector3(size * 0.72f, 0.02f, size * 0.72f), rot);
            }
            P(_head, MeshGen.Box, frame, hc + center + rot * new Vector3(0, 0.02f, 0), new Vector3(0.06f, 0.03f, 0.05f), rot);
        }

        private void Headphones(Vector3 hc, Material dark, Material glow, float size)
        {
            P(_head, MeshGen.Torus(0.045f), dark, hc + new Vector3(0, 0.02f, 0.01f), new Vector3(0.94f, 0.94f, 0.94f), Quaternion.Euler(0, 0, 90));
            for (int s = -1; s <= 1; s += 2)
            {
                P(_head, MeshGen.Cylinder(18), dark, hc + new Vector3(0.42f * s, -0.03f, 0.02f), new Vector3(size, 0.13f, size), Quaternion.Euler(0, 0, 90));
                P(_head, MeshGen.Torus(0.14f), glow, hc + new Vector3(0.49f * s, -0.03f, 0.02f), new Vector3(size * 0.75f, 1f, size * 0.75f), Quaternion.Euler(0, 0, 90));
                P(_head, MeshGen.Cylinder(16), dark, hc + new Vector3(0.5f * s, -0.03f, 0.02f), new Vector3(size * 0.4f, 0.02f, size * 0.4f), Quaternion.Euler(0, 0, 90));
            }
        }

        private void BuildAccessory(Vector3 hc, Color accent, Material dark, Material glow)
        {
            var lens = MaterialLib.Glow(accent, 1.4f);
            switch (Look.Accessory)
            {
                case 1: // goggles pushed up on the head + headphones (Pixie)
                    P(_head, MeshGen.Torus(0.05f), dark, hc + new Vector3(0, 0.2f, -0.02f), new Vector3(0.86f, 0.9f, 0.86f), Quaternion.Euler(-28, 0, 0));
                    Goggles(hc, new Vector3(0, 0.34f, 0.18f), -55, dark, lens, 0.2f);
                    Headphones(hc, dark, glow, 0.26f);
                    break;
                case 2: // headset: big glowing headphones + goggles band (Nova)
                    P(_head, MeshGen.Torus(0.05f), dark, hc + new Vector3(0, 0.24f, -0.02f), new Vector3(0.86f, 0.9f, 0.86f), Quaternion.Euler(-22, 0, 0));
                    Goggles(hc, new Vector3(0, 0.36f, 0.14f), -50, dark, lens, 0.18f);
                    Headphones(hc, dark, glow, 0.3f);
                    break;
                case 3: // round lenses on the forehead + earpieces (Bolt)
                {
                    var band = M(Palette.Hex("#2a2a30"), 0.3f, 0.3f);
                    P(_head, MeshGen.Torus(0.06f), band, hc + new Vector3(0, 0.15f, 0), new Vector3(0.84f, 1f, 0.84f), Quaternion.Euler(-8, 0, 0));
                    Goggles(hc, new Vector3(0, 0.18f, 0.33f), -80, band, MaterialLib.Glow(Palette.Hex("#8dff3a"), 1.6f), 0.21f);
                    for (int s = -1; s <= 1; s += 2)
                        P(_head, MeshGen.Cylinder(14), band, hc + new Vector3(0.41f * s, 0.02f, 0), new Vector3(0.2f, 0.1f, 0.2f), Quaternion.Euler(0, 0, 90));
                    break;
                }
                case 4: // mask
                    P(_head, MeshGen.Sphere, dark, hc + new Vector3(0, -0.15f, 0.28f), new Vector3(0.44f, 0.22f, 0.2f));
                    P(_head, MeshGen.Box, glow, hc + new Vector3(0, -0.15f, 0.385f), new Vector3(0.2f, 0.025f, 0.02f));
                    break;
            }
        }

        // ================================================================== triggers

        public void TriggerFire()
        {
            _fireKick = 1f; _aimT = 0.9f;
            if (Fists) { _punchHold = 0.6f; if (_hasPunchAnim != 1) { _punchT = PunchTime; _punchLeft = !_punchLeft; } }   // animated combo, or the procedural jab
        }
        public void TriggerCast() { _castT = 0.55f; }
        public void TriggerHit() { _hitT = 0.3f; if (_anim) _anim.SetTrigger("Hit"); }
        public void TriggerLand(float strength = 1f) { _squashVel -= 3.5f * strength; _crouch = Mathf.Max(_crouch, 0.6f * strength); }
        public void TriggerJump() { _squashVel += 3f; }

        public void ResetPose()
        {
            _deathT = 0; _victoryT = 0;
            if (_body) { _body.localRotation = Quaternion.identity; _body.localScale = Vector3.one; _body.localPosition = Vector3.zero; }
        }

        // ================================================================== animation

        private static Quaternion X(float a) => Quaternion.Euler(a, 0, 0);

        public void Animate(RigState s, float dt)
        {
            if (_body == null || dt <= 0) return;
            _time += dt;
            if (_anim != null) { AnimateModel(s, dt); return; }

            // ---------- death ----------
            if (s.Dead)
            {
                _deathT += dt;
                float f = Mathf.Clamp01(_deathT / 0.4f);
                float e = 1 - (1 - f) * (1 - f);
                _body.localRotation = Quaternion.Euler(-80f * e, 0, 12f * e);
                _body.localPosition = new Vector3(0, Mathf.Sin(f * Mathf.PI) * 0.3f, -0.3f * e);
                _shoulderL.localRotation = Quaternion.Euler(-40, 0, -70);
                _shoulderR.localRotation = Quaternion.Euler(-40, 0, 70);
                float shrink = Mathf.Clamp01((_deathT - 1.0f) / 0.4f);
                _body.localScale = Vector3.one * (1 - shrink);
                return;
            }
            if (_deathT > 0) ResetPose();

            Vector3 hv = new Vector3(s.Velocity.x, 0, s.Velocity.z);
            Vector3 local = transform.InverseTransformDirection(hv);
            float speed = hv.magnitude;
            float run = Mathf.Clamp01(speed / GameConfig.RunSpeed);
            float sprint = Mathf.Clamp01((speed - GameConfig.RunSpeed) / (GameConfig.SprintSpeed - GameConfig.RunSpeed));
            bool moving = speed > 0.35f && s.Grounded;

            // ---------- turn + acceleration lean ----------
            float yaw = transform.eulerAngles.y;
            float dYaw = Mathf.DeltaAngle(_lastYaw, yaw) / dt;
            _lastYaw = yaw;
            _turnRate = Mathf.Lerp(_turnRate, Mathf.Clamp(dYaw, -720f, 720f), 1 - Mathf.Exp(-10f * dt));
            Vector3 accel = (hv - _lastVel) / dt;
            _lastVel = hv;
            float fwdAccel = Vector3.Dot(accel, transform.forward);

            // ---------- landing / jumping springs ----------
            if (s.Grounded && !_wasGrounded) TriggerLand(Mathf.Clamp01(-s.VerticalVelocity / 9f) + 0.35f);
            if (!s.Grounded && _wasGrounded && s.VerticalVelocity > 1) TriggerJump();
            _wasGrounded = s.Grounded;
            _squashVel += (-_squash * 170f - _squashVel * 15f) * dt;
            _squash += _squashVel * dt;
            _crouch = Mathf.MoveTowards(_crouch, 0, dt * 3.5f);

            // ---------- gait (distance-matched: no foot sliding) ----------
            float stride = Mathf.Lerp(1.15f, 1.75f, run) + sprint * 0.45f;
            float prevPhase = _phase;
            if (moving) _phase += speed / stride * Mathf.PI * 2f * dt;
            else _phase = Mathf.Lerp(_phase, Mathf.Round(_phase / Mathf.PI) * Mathf.PI, 1 - Mathf.Exp(-8f * dt));
            if (moving && Mathf.Floor(prevPhase / Mathf.PI) != Mathf.Floor(_phase / Mathf.PI) && _time - _lastFoot > 0.12f)
            {
                _lastFoot = _time;
                var foot = Mathf.FloorToInt(_phase / Mathf.PI) % 2 == 0 ? _footR : _footL;
                Footstep?.Invoke(foot.position, sprint);
            }

            float sn = Mathf.Sin(_phase), cs = Mathf.Cos(_phase);
            float gait = moving ? Mathf.Clamp01(speed / 2.5f) : 0f;
            float thighAmp = Mathf.Lerp(26f, 42f, run) + sprint * 16f;
            float kneeAmp = Mathf.Lerp(40f, 75f, run) + sprint * 25f;
            float armAmp = Mathf.Lerp(22f, 38f, run) + sprint * 22f;

            // backpedal runs the cycle in reverse; strafing turns the legs
            float dirSign = local.z < -0.3f * speed ? -1f : 1f;
            float strafe = speed > 0.5f ? Mathf.Clamp(Mathf.Atan2(local.x, Mathf.Abs(local.z) + 0.01f) * Mathf.Rad2Deg, -50f, 50f) : 0f;

            float thighL = -sn * thighAmp * gait * dirSign, thighR = sn * thighAmp * gait * dirSign;
            float kneeL = (Mathf.Max(0, cs * dirSign) * kneeAmp + 8f) * gait, kneeR = (Mathf.Max(0, -cs * dirSign) * kneeAmp + 8f) * gait;
            float footL = -thighL * 0.4f - kneeL * 0.35f, footR = -thighR * 0.4f - kneeR * 0.35f;
            float armL = sn * armAmp * gait, armR = -sn * armAmp * gait;
            float elbowL = -(18f + 55f * run + 20f * sprint) * gait - 12f, elbowR = elbowL;
            float armOutL = -8f, armOutR = 8f;
            float bob = Mathf.Abs(cs) * (0.035f + 0.04f * run) * gait - 0.02f * gait;
            float hipTwist = sn * 9f * gait, chestTwist = -sn * 7f * gait;
            float targetLean = (7f * run + 10f * sprint) + Mathf.Clamp(fwdAccel * 0.6f, -10f, 12f);
            float targetRoll = Mathf.Clamp(-_turnRate * 0.035f, -14f, 14f) * run;
            float headPitch = 0;
            float thighOutL = 0, thighOutR = 0;

            // ---------- idle: breathing, stance, blinks, glances ----------
            if (!moving && s.Grounded)
            {
                float breathe = Mathf.Sin(_time * 2.1f);
                bob += breathe * 0.008f;
                armOutL = -10f - breathe * 1.5f;
                armOutR = 10f + breathe * 1.5f;
                elbowL = elbowR = -22f;
                armL = armR = 4f;
                thighOutL = -5f; thighOutR = 5f;
                kneeL = kneeR = 6f;
                footL = footR = -3f;
                headPitch = breathe * 1.5f;
                _lookT -= dt;
                if (_lookT <= 0) { _lookTarget = UnityEngine.Random.value < 0.4f ? 0 : UnityEngine.Random.Range(-22f, 22f); _lookT = UnityEngine.Random.Range(2.5f, 6f); }
                if (s.Idle) { thighOutL = -9f; thighOutR = 9f; armOutL = -16f; armOutR = 16f; }
            }
            else _lookTarget = 0;
            _lookYaw = Mathf.Lerp(_lookYaw, _lookTarget, 1 - Mathf.Exp(-4f * dt));

            // ---------- airborne ----------
            if (!s.Grounded)
            {
                bool rising = s.VerticalVelocity > 0.5f;
                thighL = rising ? -55f : -25f; kneeL = rising ? 85f : 40f;
                thighR = rising ? 15f : -5f; kneeR = rising ? 40f : 25f;
                footL = footR = 10f;
                armL = -30f; armR = -30f; elbowL = elbowR = -40f;
                armOutL = rising ? -55f : -75f; armOutR = rising ? 55f : 75f;
                targetLean = rising ? 6f : -4f;
                bob = 0;
            }

            // ---------- dash ----------
            if (s.Dashing)
            {
                targetLean = 38f;
                armL = armR = 70f; elbowL = elbowR = -10f; armOutL = -20f; armOutR = 20f;
                thighL = 35f; kneeL = 60f; thighR = -30f; kneeR = 20f;
            }

            // ---------- crouch after landing ----------
            float crouch = Mathf.Clamp01(_crouch);
            thighL -= 40f * crouch; thighR -= 40f * crouch;
            kneeL += 70f * crouch; kneeR += 70f * crouch;
            footL -= 30f * crouch; footR -= 30f * crouch;
            bob -= 0.14f * crouch;
            targetLean += 12f * crouch;

            // ---------- aim: right arm points the blaster ----------
            _aimT -= dt;
            bool aiming = s.Aiming || _aimT > 0;
            _aimW = Mathf.MoveTowards(_aimW, aiming ? 1f : 0f, dt * 9f);
            _fireKick = Mathf.MoveTowards(_fireKick, 0, dt * 8f);
            armR = Mathf.Lerp(armR, -86f + _fireKick * 16f, _aimW);
            elbowR = Mathf.Lerp(elbowR, -4f - _fireKick * 12f, _aimW);
            armOutR = Mathf.Lerp(armOutR, 4f, _aimW);
            if (!moving) { armL = Mathf.Lerp(armL, -40f, _aimW); elbowL = Mathf.Lerp(elbowL, -60f, _aimW); }
            chestTwist = Mathf.Lerp(chestTwist, -10f, _aimW * 0.6f);
            _gun.localRotation = Quaternion.Euler(Mathf.Lerp(90f, 180f, _aimW), 0, 0);

            // ---------- cast: left arm thrust up ----------
            if (_castT > 0)
            {
                _castT -= dt;
                float k = Mathf.Sin(Mathf.Clamp01(_castT / 0.55f) * Mathf.PI);
                armL = Mathf.Lerp(armL, -165f, k);
                elbowL = Mathf.Lerp(elbowL, -10f, k);
                armOutL = Mathf.Lerp(armOutL, -10f, k);
            }

            // ---------- hit flinch ----------
            float hitTilt = 0;
            if (_hitT > 0)
            {
                _hitT -= dt;
                hitTilt = -Mathf.Sin(Mathf.Clamp01(_hitT / 0.3f) * Mathf.PI) * 20f;
            }

            // ---------- victory ----------
            float bodyYaw = 0;
            if (s.Victory)
            {
                _victoryT += dt;
                float hop = Mathf.Abs(Mathf.Sin(_victoryT * 4.5f));
                bob = hop * 0.32f;
                armR = -170f + Mathf.Sin(_victoryT * 9f) * 12f; elbowR = -20f; armOutR = 10f;
                armL = 10f; elbowL = -90f; armOutL = -25f;
                thighL = -20f * hop; kneeL = 40f * hop; thighR = -10f * hop; kneeR = 30f * hop;
                bodyYaw = Mathf.Sin(_victoryT * 1.4f) * 18f;
                headPitch = -8f;
            }

            // ---------- blink ----------
            _blinkT -= dt;
            if (_blinkT <= 0) { _blink = 0.13f; _blinkT = UnityEngine.Random.Range(2.2f, 5f); }
            _blink -= dt;
            float eyeY = _blink > 0 ? 0.12f : 1f;
            float kb = 1 - Mathf.Exp(-40f * dt);
            if (_eyeL) _eyeL.localScale = new Vector3(1, Mathf.Lerp(_eyeL.localScale.y, eyeY, kb), 1);
            if (_eyeR) _eyeR.localScale = new Vector3(1, Mathf.Lerp(_eyeR.localScale.y, eyeY, kb), 1);

            // ---------- apply ----------
            float k1 = 1 - Mathf.Exp(-20f * dt);
            float k2 = 1 - Mathf.Exp(-9f * dt);
            _lean = Mathf.Lerp(_lean, targetLean, k2);
            _roll = Mathf.Lerp(_roll, targetRoll, k2);
            float sq = Mathf.Clamp(_squash, -0.22f, 0.22f);
            if (!s.Grounded) sq = Mathf.Max(sq, Mathf.Clamp(s.VerticalVelocity * 0.012f, -0.06f, 0.08f));
            _body.localScale = new Vector3(1 - sq * 0.5f, 1 + sq, 1 - sq * 0.5f);
            _body.localRotation = Quaternion.Euler(0, bodyYaw, _roll);
            _body.localPosition = Vector3.zero;
            _hips.localPosition = new Vector3(0, HipY + bob, 0);
            _hips.localRotation = Quaternion.Euler(_lean * 0.35f, hipTwist + strafe * 0.5f, 0);
            _spine.localRotation = Quaternion.Euler(_lean * 0.4f + hitTilt * 0.6f, 0, 0);
            _chest.localRotation = Quaternion.Slerp(_chest.localRotation, Quaternion.Euler(_lean * 0.25f + hitTilt * 0.4f, chestTwist - strafe * 0.35f, cs * 2.5f * gait), k1);
            _neck.localRotation = Quaternion.Euler(-_lean * 0.75f + headPitch - hitTilt * 0.5f, _lookYaw, -_roll * 0.4f);

            _shoulderL.localRotation = Quaternion.Slerp(_shoulderL.localRotation, Quaternion.Euler(armL, 0, armOutL), k1);
            _shoulderR.localRotation = Quaternion.Slerp(_shoulderR.localRotation, Quaternion.Euler(armR, 0, armOutR), Mathf.Min(1, k1 * 1.2f));
            _elbowL.localRotation = Quaternion.Slerp(_elbowL.localRotation, X(elbowL), k1);
            _elbowR.localRotation = Quaternion.Slerp(_elbowR.localRotation, X(elbowR), Mathf.Min(1, k1 * 1.2f));
            _thighL.localRotation = Quaternion.Slerp(_thighL.localRotation, Quaternion.Euler(thighL, strafe * 0.5f, thighOutL), k1);
            _thighR.localRotation = Quaternion.Slerp(_thighR.localRotation, Quaternion.Euler(thighR, strafe * 0.5f, thighOutR), k1);
            _kneeL.localRotation = Quaternion.Slerp(_kneeL.localRotation, X(kneeL), k1);
            _kneeR.localRotation = Quaternion.Slerp(_kneeR.localRotation, X(kneeR), k1);
            _footL.localRotation = Quaternion.Slerp(_footL.localRotation, X(footL), k1);
            _footR.localRotation = Quaternion.Slerp(_footR.localRotation, X(footR), k1);
        }
    }
}
