using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Veil.EditorTools
{
    /// <summary>
    /// Import rules for the generated characters (Tools/ai3d → Blender) in Assets/Veil/Characters/&lt;name&gt;/:
    ///   &lt;name&gt;.fbx          skinned mesh + armature + all actions (idle, walk, run, sprint, jump, fall, dash, shoot, hit, death, victory)
    ///   &lt;name&gt;_albedo.png   baked texture (concept art projection)
    /// </summary>
    public sealed class CharacterImport : AssetPostprocessor
    {
        private static readonly string[] Looping = { "lobby", "idle", "walk", "run", "sprint", "fall", "shoot", "victory", "fixing_kneeling" };

        /// <summary>
        /// Heroes imported as Unity humanoids so the Universal Animation Library (Quaternius, CC0, Characters/_anim/ual.fbx)
        /// locomotion retargets onto them. The Ranger already shares the library's rig and stays generic.
        /// </summary>
        public static readonly string[] HumanoidHeroes = { "vanguard", "volt", "lyra", "nova", "sol" };   // e.g. future Meshy-rigged heroes; the Quaternius heroes share the library rig
        public const string LibraryFbx = "Assets/Veil/Characters/_anim/ual.fbx";

        public override uint GetVersion() => 4;   // bump → Unity re-imports every character with these rules

        private bool IsCharacter => assetPath.StartsWith("Assets/Veil/Characters/");
        private bool IsLibrary => assetPath == LibraryFbx;
        private bool IsHumanoid => IsLibrary || HumanoidHeroes.Contains(Path.GetFileNameWithoutExtension(assetPath));

        private void OnPreprocessModel()
        {
            if (!IsCharacter) return;
            var mi = (ModelImporter)assetImporter;
            mi.animationType = IsHumanoid ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
            mi.resampleCurves = true;
        }

        private void OnPreprocessTexture()
        {
            if (!IsCharacter) return;
            var ti = (TextureImporter)assetImporter;
            ti.sRGBTexture = true;
            ti.maxTextureSize = 4096;
            ti.mipmapEnabled = true;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.anisoLevel = 8;
            ti.mipMapBias = -0.35f;              // stay crisp at gameplay distance
            ti.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
            foreach (var plat in new[] { "Android", "iPhone" })
                ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                {
                    name = plat, overridden = true, maxTextureSize = 4096, format = TextureImporterFormat.ASTC_6x6,
                    compressionQuality = 80,
                });
        }

        public static string ActionOf(string clipName)
        {
            string n = clipName;
            int bar = n.LastIndexOf('|');
            if (bar >= 0) n = n.Substring(bar + 1);
            return n.ToLowerInvariant();
        }

        private void OnPreprocessAnimation()
        {
            if (!IsCharacter) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                string action = ActionOf(c.takeName);
                c.name = action;
                c.loopTime = Looping.Contains(action) || action.EndsWith("_loop");
                c.loopPose = false;
                if (IsHumanoid)
                {
                    // in-place clips: bake the root into the pose so characters never drift, turn or sink
                    // library clips are authored facing the other way: orient by the body so retargeted heroes face forward
                    c.lockRootRotation = true; c.keepOriginalOrientation = !IsLibrary;
                    c.lockRootHeightY = true; c.keepOriginalPositionY = true;
                    c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
                }
            }
            mi.clipAnimations = clips;
        }
    }

    public static class CharacterBuilder
    {
        private const string Root = "Assets/Veil/Characters";
        private const string PrefabDir = "Assets/Veil/Resources/Characters";

        [MenuItem("VEIL/Build Character Prefabs")]
        public static void BuildAll()
        {
            if (!AssetDatabase.IsValidFolder(Root)) { Debug.Log("[VEIL] No generated characters found (Assets/Veil/Characters)"); return; }
            Directory.CreateDirectory(PrefabDir);
            AssetDatabase.Refresh();
            foreach (var dir in Directory.GetDirectories(Root))
            {
                string name = Path.GetFileName(dir);
                string fbx = $"{Root}/{name}/{name}.fbx";
                if (!File.Exists(fbx)) continue;
                try { BuildOne(name, fbx); }
                catch (System.Exception e) { Debug.LogError($"[VEIL] Character {name} failed: {e}"); }
            }
            AssetDatabase.SaveAssets();
        }

        private static Dictionary<string, AnimationClip> ClipsOf(string fbx) =>
            AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .GroupBy(c => CharacterImport.ActionOf(c.name)).ToDictionary(g => g.Key, g => g.First());

        /// <summary>
        /// The heroes were rigged in an A-pose (arms down). Unity's humanoid retargeting needs a T-pose reference, so
        /// rotate the arms straight out to the sides in the avatar's skeleton description (like "Enforce T-Pose").
        /// </summary>
        private static void EnsureTPose(string fbx)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
            if (mi == null || mi.animationType != ModelImporterAnimationType.Human || mi.userData == "tpose-v3") return;
            var hd = mi.humanDescription;
            if (hd.human == null || hd.human.Length == 0) { Debug.LogWarning($"[VEIL] {fbx}: humanoid mapping failed"); return; }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var go = Object.Instantiate(model);
            var all = go.GetComponentsInChildren<Transform>(true);
            var map = hd.human.ToDictionary(h => h.humanName, h => h.boneName);
            Transform T(string human) => map.TryGetValue(human, out var b) ? all.FirstOrDefault(t => t.name == b) : null;
            void Align(Transform bone, Vector3 cur, Vector3 want) => bone.rotation = Quaternion.FromToRotation(cur, want) * bone.rotation;
            // canonical T-pose: spine/neck/head straight up, legs straight down, arms straight out (Unity's humanoid reference)
            var up = go.transform.up;
            void Chain(string a, string b, Vector3 dir) { var ta = T(a); var tb = T(b); if (ta != null && tb != null) Align(ta, tb.position - ta.position, dir); }
            Chain("Hips", "Spine", up); Chain("Spine", "Chest", up); Chain("Chest", "UpperChest", up);
            Chain(T("UpperChest") != null ? "UpperChest" : "Chest", "Neck", up); Chain("Neck", "Head", up);
            foreach (var side in new[] { "Left", "Right" })
            {
                Chain(side + "UpperLeg", side + "LowerLeg", -up);
                Chain(side + "LowerLeg", side + "Foot", -up);
                var ua = T(side + "UpperArm"); var la = T(side + "LowerArm"); var hand = T(side + "Hand");
                if (ua == null || la == null) continue;
                float sx = Mathf.Sign(go.transform.InverseTransformPoint(ua.position).x - go.transform.InverseTransformPoint(T("Hips").position).x);
                var want = go.transform.TransformDirection(new Vector3(sx, 0, 0));
                Align(ua, la.position - ua.position, want);
                if (hand != null) Align(la, hand.position - la.position, want);
            }
            var byName = all.GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var sk = hd.skeleton;
            for (int i = 0; i < sk.Length; i++)
                if (byName.TryGetValue(sk[i].name, out var t) && t != go.transform) { sk[i].rotation = t.localRotation; sk[i].position = t.localPosition; }
            hd.skeleton = sk;
            mi.humanDescription = hd;
            mi.userData = "tpose-v3";
            Object.DestroyImmediate(go);
            mi.SaveAndReimport();
            Debug.Log($"[VEIL] {fbx}: humanoid T-pose reference set");
        }

        // game locomotion clip -> Universal Animation Library clip (retargeted onto humanoid heroes)
        private static readonly (string game, string lib)[] LibraryLocomotion =
        {
            ("idle", "idle_loop"), ("walk", "walk_loop"), ("run", "jog_fwd_loop"), ("sprint", "sprint_loop"),
            ("jump", "jump_start"), ("fall", "jump_loop"), ("dash", "roll"),
        };

        private static void BuildOne(string name, string fbx)
        {
            bool humanoid = CharacterImport.HumanoidHeroes.Contains(name);
            if (humanoid) EnsureTPose(fbx);
            var clips = ClipsOf(fbx);
            // the hero's own Meshy idle (made for this exact rig) beats a retargeted library idle in matches too
            if (!clips.ContainsKey("idle") && clips.TryGetValue("lobby", out var ownIdle)) clips["idle"] = ownIdle;
            if (humanoid && File.Exists(CharacterImport.LibraryFbx))
            {
                var lib = ClipsOf(CharacterImport.LibraryFbx);
                // the hero's own clips win (e.g. Meshy walk / run); the library fills whatever is missing
                foreach (var (game, libName) in LibraryLocomotion)
                    if (!clips.ContainsKey(game) && lib.TryGetValue(libName, out var lc)) clips[game] = lc;
                foreach (var (game, libName) in new[] { ("shoot", "pistol_aim_neutral"), ("hit", "hit_chest"), ("death", "death01"), ("victory", "dance_loop"),
                                                         ("downed", "sitting_idle_loop"), ("crawl", "crouch_fwd_loop"), ("revive", "fixing_kneeling") })
                    if (!clips.ContainsKey(game) && lib.TryGetValue(libName, out var lc2)) clips[game] = lc2;
            }
            AnimationClip C(params string[] names) { foreach (var n in names) if (clips.TryGetValue(n, out var c)) return c; return null; }

            // ---- material: VEIL toon shader with the baked concept-art texture ----
            string matPath = $"{Root}/{name}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(Shader.Find("Veil/Toon")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = Shader.Find("Veil/Toon");
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/{name}/{name}_albedo.png"));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetColor("_ShadeColor", new Color(0.78f, 0.78f, 0.95f));
            mat.SetColor("_RimColor", new Color(1, 1, 1, 0.12f));
            mat.SetFloat("_Ramp", 0.35f);
            mat.SetFloat("_Gloss", 0f);
            // finished textured models (Meshy / hand-made) carry plain albedo → let the toon lighting shade them;
            // projected concept art already has painted lighting → keep most of it
            bool textured = File.Exists($"{Root}/{name}/{name}_textured.txt");
            mat.SetFloat("_ArtKeep", textured ? 0.35f : 0.85f);
            mat.SetFloat("_Cull", textured ? 0f : 2f);   // textured models are thin shells: double-sided hides seams
            mat.SetFloat("_ShadowStrength", textured ? 0.6f : 0.45f);   // soft self-shadows so faces stay readable
            var emis = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/{name}/{name}_emission.png");
            mat.SetTexture("_EmissionMap", emis);
            float e = textured ? 1.6f : 2.2f;
            mat.SetColor("_EmissionColor", emis != null ? new Color(e, e, e) : Color.black);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);

            // ---- animator controller ----
            string ctrlPath = $"{Root}/{name}/{name}.controller";
            AssetDatabase.DeleteAsset(ctrlPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            foreach (var (p, t) in new[] { ("Speed", AnimatorControllerParameterType.Float), ("VSpeed", AnimatorControllerParameterType.Float),
                                           ("Grounded", AnimatorControllerParameterType.Bool), ("Dashing", AnimatorControllerParameterType.Bool),
                                           ("Aiming", AnimatorControllerParameterType.Bool), ("Dead", AnimatorControllerParameterType.Bool),
                                           ("Victory", AnimatorControllerParameterType.Bool), ("Lobby", AnimatorControllerParameterType.Bool), ("Downed", AnimatorControllerParameterType.Bool), ("Reviving", AnimatorControllerParameterType.Bool), ("LobbyAct", AnimatorControllerParameterType.Int), ("LobbyActs", AnimatorControllerParameterType.Int), ("Hit", AnimatorControllerParameterType.Trigger) })
                ctrl.AddParameter(p, t);

            var idle = C("idle", "walk"); var walk = C("walk"); var run = C("run", "walk"); var sprint = C("sprint", "run");
            var jump = C("jump"); var fall = C("fall", "jump"); var dash = C("dash", "sprint");
            var shoot = C("shoot"); var hit = C("hit"); var death = C("death"); var victory = C("victory", "idle");

            var sm = ctrl.layers[0].stateMachine;
            var loco = ctrl.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            if (idle) tree.AddChild(idle, 0f);
            if (walk) tree.AddChild(walk, 2.2f);
            if (run) tree.AddChild(run, 6.2f);
            if (sprint) tree.AddChild(sprint, 8.8f);
            sm.defaultState = loco;

            AnimatorStateTransition Tr(AnimatorState from, AnimatorState to, float dur)
            {
                var t = from.AddTransition(to); t.hasExitTime = false; t.duration = dur; return t;
            }
            AnimatorStateTransition Any(AnimatorState to, float dur)
            {
                var t = sm.AddAnyStateTransition(to); t.hasExitTime = false; t.duration = dur; t.canTransitionToSelf = false; return t;
            }

            if (jump || fall)
            {
                var air = sm.AddState("Air");
                var airTree = new BlendTree { name = "AirTree", blendParameter = "VSpeed", useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(airTree, ctrl);
                if (fall) airTree.AddChild(fall, -6f);
                if (jump) airTree.AddChild(jump, 6f);
                air.motion = airTree;
                Tr(loco, air, 0.08f).AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
                Tr(air, loco, 0.06f).AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            }
            if (dash)
            {
                var d = sm.AddState("Dash"); d.motion = dash;
                Any(d, 0.04f).AddCondition(AnimatorConditionMode.If, 0, "Dashing");
                Tr(d, loco, 0.1f).AddCondition(AnimatorConditionMode.IfNot, 0, "Dashing");
            }
            if (death)
            {
                var d = sm.AddState("Death"); d.motion = death;
                Any(d, 0.08f).AddCondition(AnimatorConditionMode.If, 0, "Dead");
                Tr(d, loco, 0.1f).AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            }
            // downed: sit / crawl on the ground; reviving a squadmate: kneel
            var downed = C("downed"); var crawl = C("crawl", "downed"); var revive = C("revive");
            if (downed)
            {
                var dn = sm.AddState("Downed");
                var dTree = new BlendTree { name = "DownedTree", blendParameter = "Speed", useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(dTree, ctrl);
                dTree.AddChild(downed, 0f);
                dTree.AddChild(crawl, 1.2f);
                dn.motion = dTree;
                Any(dn, 0.2f).AddCondition(AnimatorConditionMode.If, 0, "Downed");
                Tr(dn, loco, 0.3f).AddCondition(AnimatorConditionMode.IfNot, 0, "Downed");
            }
            if (revive)
            {
                var rv = sm.AddState("Reviving"); rv.motion = revive;
                Any(rv, 0.2f).AddCondition(AnimatorConditionMode.If, 0, "Reviving");
                Tr(rv, loco, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0, "Reviving");
            }
            var lobbyClip = C("lobby");
            if (!lobbyClip && C("lobby_act1")) lobbyClip = C("idle");   // lobby actions without an own lobby idle: use the idle
            if (lobbyClip)
            {
                // menus / podium / portraits: the concept-art hero stance
                var l = sm.AddState("Lobby"); l.motion = lobbyClip;
                Any(l, 0.25f).AddCondition(AnimatorConditionMode.If, 0, "Lobby");
                Tr(l, loco, 0.25f).AddCondition(AnimatorConditionMode.IfNot, 0, "Lobby");
                // lobby_act1..N: one-shot idle actions CharacterRig plays now and then (LobbyAct = index)
                int n = 0;
                for (var act = C("lobby_act" + (n + 1)); act; act = C("lobby_act" + (n + 1)))
                {
                    n++;
                    var a = sm.AddState("LobbyAct" + n); a.motion = act;
                    Tr(l, a, 0.25f).AddCondition(AnimatorConditionMode.Equals, n, "LobbyAct");
                    var back = a.AddTransition(l); back.hasExitTime = true; back.exitTime = 0.92f; back.duration = 0.25f;
                    Tr(a, loco, 0.25f).AddCondition(AnimatorConditionMode.IfNot, 0, "Lobby");
                }
                var ps = ctrl.parameters;
                foreach (var p in ps) if (p.name == "LobbyActs") p.defaultInt = n;
                ctrl.parameters = ps;
            }
            if (victory)
            {
                var v = sm.AddState("Victory"); v.motion = victory;
                Any(v, 0.2f).AddCondition(AnimatorConditionMode.If, 0, "Victory");
                Tr(v, loco, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0, "Victory");
            }

            // ---- prefab first (the upper-body mask needs the transform hierarchy) ----
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var root = new GameObject(name);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.SetParent(root.transform, false);

            // upper-body layer (shoot / hit over locomotion)
            var mask = new AvatarMask { name = name + "_UpperBody" };
            if (humanoid)
                foreach (AvatarMaskBodyPart part in System.Enum.GetValues(typeof(AvatarMaskBodyPart)))
                    if (part != AvatarMaskBodyPart.LastBodyPart)
                        mask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head ||
                            part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                            part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers);
            else mask.AddTransformPath(inst.transform, true);
            for (int i = 0; i < mask.transformCount; i++)
            {
                string p = mask.GetTransformPath(i);
                string leaf = p.Contains("/") ? p.Substring(p.LastIndexOf('/') + 1) : p;
                string lo = leaf.ToLowerInvariant();
                // our Blender rigs (Chest, UpperArm.R…) and UE-style rigs such as the Ranger's (spine_03, clavicle_r, upperarm_r, hand_r, fingers…)
                bool upper = lo.StartsWith("chest") || lo.StartsWith("shoulder") || lo.StartsWith("upperarm") || lo.StartsWith("lowerarm") ||
                             lo.StartsWith("hand") || lo.StartsWith("neck") || lo.StartsWith("head") || lo == "spine_03" || lo.StartsWith("clavicle") ||
                             lo.StartsWith("thumb") || lo.StartsWith("index") || lo.StartsWith("middle") || lo.StartsWith("ring") || lo.StartsWith("pinky");
                mask.SetTransformActive(i, upper);
            }
            string maskPath = $"{Root}/{name}/{name}_UpperBody.mask";
            AssetDatabase.DeleteAsset(maskPath);
            AssetDatabase.CreateAsset(mask, maskPath);
            ctrl.AddLayer("UpperBody");
            var layers = ctrl.layers;
            layers[1].avatarMask = mask;
            layers[1].defaultWeight = 1f;
            ctrl.layers = layers;
            var usm = ctrl.layers[1].stateMachine;
            var empty = usm.AddState("Empty");
            usm.defaultState = empty;
            if (shoot)
            {
                var s = usm.AddState("Shoot"); s.motion = shoot;
                var t = empty.AddTransition(s); t.hasExitTime = false; t.duration = 0.06f; t.AddCondition(AnimatorConditionMode.If, 0, "Aiming");
                var b = s.AddTransition(empty); b.hasExitTime = false; b.duration = 0.18f; b.AddCondition(AnimatorConditionMode.IfNot, 0, "Aiming");
            }
            if (hit)
            {
                var h = usm.AddState("Hit"); h.motion = hit; h.speed = 1.3f;
                var t = usm.AddAnyStateTransition(h); t.hasExitTime = false; t.duration = 0.04f; t.AddCondition(AnimatorConditionMode.If, 0, "Hit");
                var b = h.AddTransition(empty); b.hasExitTime = true; b.exitTime = 0.85f; b.duration = 0.12f;
            }

            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterials = Enumerable.Repeat(mat, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();
                if (r is SkinnedMeshRenderer smr) { smr.updateWhenOffscreen = true; smr.quality = SkinQuality.Bone4; }
            }
            var anim = inst.GetComponent<Animator>() ?? inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.avatar = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().FirstOrDefault();
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            string prefabPath = $"{PrefabDir}/{name}.prefab";
            bool hasAvatar = anim.avatar != null;
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[VEIL] Character prefab {prefabPath}: clips [{string.Join(",", clips.Keys)}], avatar {(hasAvatar ? "ok" : "missing")}");
        }
    }
}
