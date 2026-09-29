using System.IO;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Veil.EditorTools
{
    /// <summary>
    /// One-click (or batch-mode) project setup: base materials in Resources, the boot scene,
    /// build settings and player settings. Safe to run repeatedly.
    ///   Unity -batchmode -projectPath Client -executeMethod Veil.EditorTools.ProjectSetup.Setup -quit
    /// </summary>
    public static class ProjectSetup
    {
        private const string MatDir = "Assets/Veil/Resources/Materials";
        private const string ScenePath = "Assets/Veil/Scenes/Main.unity";

        [MenuItem("VEIL/Setup Project")]
        public static void Setup()
        {
            Directory.CreateDirectory(MatDir);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            AssetDatabase.Refresh();

            EnsureMaterial("Toon", "Veil/Toon", "Universal Render Pipeline/Lit");
            EnsureMaterial("Unlit", "Veil/Unlit", "Universal Render Pipeline/Unlit");
            EnsureMaterial("Water", "Veil/Water", "Universal Render Pipeline/Lit");
            EnsureMaterial("Sky", "Veil/Sky", "Skybox/Procedural");

            CharacterBuilder.BuildAll();
            CreateScene();
            ConfigurePlayer();
            AssetDatabase.SaveAssets();
            Debug.Log("[VEIL] Project setup complete");
        }

        private static void EnsureMaterial(string name, string shaderName, string fallback)
        {
            string path = $"{MatDir}/{name}.mat";
            var shader = Shader.Find(shaderName);
            if (shader == null || ShaderUtil.ShaderHasError(shader))
            {
                Debug.LogWarning($"[VEIL] Shader {shaderName} missing or has errors — using {fallback}");
                shader = Shader.Find(fallback);
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else mat.shader = shader;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
        }

        private static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<UniversalAdditionalCameraData>();
            cam.transform.position = new Vector3(0, 30, -70);
            cam.transform.LookAt(Vector3.zero);
            var lightGo = new GameObject("Sun");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(48, -38, 0);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "VEIL Studio";
            PlayerSettings.productName = "Rilo";   // app name on the home screen / dock (bundle id stays com.veilstudio.veil)
            PlayerSettings.bundleVersion = "0.1." + Veil.App.BootConfig.Build;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "com.veilstudio.veil");
            ApplyIcons();
        }

        private const string IconDir = "Assets/Veil/Icons/";

        /// <summary>App icon everywhere; on Android also adaptive (blurred art background + rounded art foreground) and round icons.</summary>
        private static void ApplyIcons()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconDir + "app_icon.png");
            if (icon == null) { Debug.LogWarning("[VEIL] app icon missing"); return; }
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            var bg = AssetDatabase.LoadAssetAtPath<Texture2D>(IconDir + "app_icon_bg.png");
            var fg = AssetDatabase.LoadAssetAtPath<Texture2D>(IconDir + "app_icon_fg.png");
            var android = NamedBuildTarget.Android;
            var adaptive = PlayerSettings.GetPlatformIcons(android, AndroidPlatformIconKind.Adaptive);
            foreach (var pi in adaptive) pi.SetTextures(bg, fg);
            PlayerSettings.SetPlatformIcons(android, AndroidPlatformIconKind.Adaptive, adaptive);
            foreach (var kind in new[] { AndroidPlatformIconKind.Round, AndroidPlatformIconKind.Legacy })
            {
                var icons = PlayerSettings.GetPlatformIcons(android, kind);
                foreach (var pi in icons) pi.SetTexture(icon);
                PlayerSettings.SetPlatformIcons(android, kind, icons);
            }
        }
    }

    public static class BuildScript
    {
        private static readonly string[] Scenes = { "Assets/Veil/Scenes/Main.unity" };

        private static void Run(BuildTarget target, BuildTargetGroup group, string outPath)
        {
            ProjectSetup.Setup();
            // squad voice chat: required on macOS / iOS before the Microphone API may be used
            PlayerSettings.iOS.microphoneUsageDescription = "Rilo uses the microphone for squad voice chat (push-to-talk).";
            if (EditorUserBuildSettings.activeBuildTarget != target) EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = Scenes, locationPathName = outPath, target = target, options = BuildOptions.None });
            Debug.Log($"[VEIL] Build result: {report.summary.result}, size {report.summary.totalSize / (1024 * 1024)} MB, errors {report.summary.totalErrors} → {outPath}");
            if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        private static string Out(string rel) => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Builds/" + rel));

        [MenuItem("VEIL/Build macOS Player")]
        public static void BuildMac() => Run(BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone, Out("Mac/Rilo.app"));

        private static void MobileCommon()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.useAnimatedAutorotation = true;
            PlayerSettings.iOS.microphoneUsageDescription = "Rilo uses the microphone for squad voice chat (push-to-talk).";
        }

        [MenuItem("VEIL/Build Android APK")]
        public static void BuildAndroid()
        {
            MobileCommon();
            var nt = UnityEditor.Build.NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(nt, "com.veilstudio.veil");
            PlayerSettings.SetScriptingBackend(nt, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.bundleVersionCode = Veil.App.BootConfig.Build;   // bump BootConfig.Build per released APK
            EditorUserBuildSettings.buildAppBundle = false;
            UseReleaseKeystore();
            Run(BuildTarget.Android, BuildTargetGroup.Android, Out("Android/Rilo.apk"));
        }

        /// <summary>
        /// Signs with the Rilo release key (~/.android/rilo-release.keystore + .properties, never in git). Google sign-in is
        /// bound to this key's SHA-1, so every APK must use it. Falls back to the debug key if the files are missing.
        /// </summary>
        private static void UseReleaseKeystore()
        {
            string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            string props = Path.Combine(home, ".android/rilo-release.properties");
            if (!File.Exists(props)) { Debug.LogWarning("[VEIL] no release keystore: debug-signed (Google sign-in will not work)"); PlayerSettings.Android.useCustomKeystore = false; return; }
            string ks = "", alias = "rilo", pw = "";
            foreach (var line in File.ReadAllLines(props))
            {
                int i = line.IndexOf('=');
                if (i <= 0) continue;
                string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                if (k == "keystore") ks = v; else if (k == "alias") alias = v; else if (k == "password") pw = v;
            }
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = ks;
            PlayerSettings.Android.keystorePass = pw;
            PlayerSettings.Android.keyaliasName = alias;
            PlayerSettings.Android.keyaliasPass = pw;
        }

        /// <summary>Xcode project for a real iPhone (open Builds/iOS/Unity-iPhone.xcodeproj, pick your team, Run).</summary>
        [MenuItem("VEIL/Build iOS (device Xcode project)")]
        public static void BuildIOS() => BuildIOSInternal(false);

        /// <summary>Xcode project targeting the iOS Simulator (used for automated testing).</summary>
        [MenuItem("VEIL/Build iOS (Simulator)")]
        public static void BuildIOSSimulator() => BuildIOSInternal(true);

        private static void BuildIOSInternal(bool simulator)
        {
            MobileCommon();
            var nt = UnityEditor.Build.NamedBuildTarget.iOS;
            PlayerSettings.SetApplicationIdentifier(nt, "com.veilstudio.veil");
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.iOS.sdkVersion = simulator ? iOSSdkVersion.SimulatorSDK : iOSSdkVersion.DeviceSDK;
            if (simulator) PlayerSettings.iOS.simulatorSdkArchitecture = AppleMobileArchitectureSimulator.ARM64;
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            PlayerSettings.iOS.requiresFullScreen = true;
            Run(BuildTarget.iOS, BuildTargetGroup.iOS, Out(simulator ? "iOS-Simulator" : "iOS"));
        }
    }

#if UNITY_IOS
    /// <summary>iOS needs a local-network usage string to reach a LAN game server.</summary>
    public static class IOSPostBuild
    {
        [UnityEditor.Callbacks.PostProcessBuild]
        public static void OnPostBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            string plistPath = Path.Combine(path, "Info.plist");
            var plist = new UnityEditor.iOS.Xcode.PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetString("NSLocalNetworkUsageDescription", "VEIL connects to game servers on your local network for multiplayer matches.");
            plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
            plist.WriteToFile(plistPath);
        }
    }
#endif
}
