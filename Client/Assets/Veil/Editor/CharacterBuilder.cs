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
        private static readonly string[] Looping = { "lobby", "idle", "walk", "run", "sprint", "fall", "shoot", "victory" };

        private bool IsCharacter => assetPath.StartsWith("Assets/Veil/Characters/");

        private void OnPreprocessModel()
        {
            if (!IsCharacter) return;
            var mi = (ModelImporter)assetImporter;
            mi.animationType = ModelImporterAnimationType.Generic;
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
                c.loopTime = Looping.Contains(action);
                c.loopPose = false;
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

        private static void BuildOne(string name, string fbx)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .GroupBy(c => CharacterImport.ActionOf(c.name)).ToDictionary(g => g.Key, g => g.First());
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
            mat.SetFloat("_ArtKeep", 0.85f);          // the texture is painted concept art: keep it
            mat.SetFloat("_ShadowStrength", 0.45f);   // soft self-shadows so faces stay readable
            var emis = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/{name}/{name}_emission.png");
            mat.SetTexture("_EmissionMap", emis);
            mat.SetColor("_EmissionColor", emis != null ? new Color(2.2f, 2.2f, 2.2f) : Color.black);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);

            // ---- animator controller ----
            string ctrlPath = $"{Root}/{name}/{name}.controller";
            AssetDatabase.DeleteAsset(ctrlPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            foreach (var (p, t) in new[] { ("Speed", AnimatorControllerParameterType.Float), ("VSpeed", AnimatorControllerParameterType.Float),
                                           ("Grounded", AnimatorControllerParameterType.Bool), ("Dashing", AnimatorControllerParameterType.Bool),
                                           ("Aiming", AnimatorControllerParameterType.Bool), ("Dead", AnimatorControllerParameterType.Bool),
                                           ("Victory", AnimatorControllerParameterType.Bool), ("Lobby", AnimatorControllerParameterType.Bool), ("Hit", AnimatorControllerParameterType.Trigger) })
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
            var lobbyClip = C("lobby");
            if (lobbyClip)
            {
                // menus / podium / portraits: the concept-art hero stance
                var l = sm.AddState("Lobby"); l.motion = lobbyClip;
                Any(l, 0.25f).AddCondition(AnimatorConditionMode.If, 0, "Lobby");
                Tr(l, loco, 0.25f).AddCondition(AnimatorConditionMode.IfNot, 0, "Lobby");
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
            mask.AddTransformPath(inst.transform, true);
            for (int i = 0; i < mask.transformCount; i++)
            {
                string p = mask.GetTransformPath(i);
                string leaf = p.Contains("/") ? p.Substring(p.LastIndexOf('/') + 1) : p;
                bool upper = leaf.StartsWith("Chest") || leaf.StartsWith("Shoulder") || leaf.StartsWith("UpperArm") || leaf.StartsWith("LowerArm") ||
                             leaf.StartsWith("Hand") || leaf.StartsWith("Neck") || leaf.StartsWith("Head");
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
