using System.IO;
using UnityEditor.Android;
using UnityEngine;

namespace Veil.EditorTools
{
    /// <summary>
    /// Adds the Google sign-in libraries (Credential Manager + Google ID) to the generated Android project and
    /// turns on AndroidX. Done as a post-generate hook so it works without custom Gradle templates.
    /// </summary>
    public sealed class AndroidGradleDeps : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 0;

        private static readonly string[] Deps =
        {
            "androidx.credentials:credentials:1.3.0",
            "androidx.credentials:credentials-play-services-auth:1.3.0",
            "com.google.android.libraries.identity.googleid:googleid:1.1.1",
        };

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            var gradle = Path.Combine(unityLibraryPath, "build.gradle");
            var s = File.ReadAllText(gradle);
            if (!s.Contains(Deps[0]))
            {
                var lines = "";
                foreach (var d in Deps) lines += $"    implementation '{d}'\n";
                int i = s.IndexOf("dependencies {");
                if (i < 0) { Debug.LogError("[VEIL] unityLibrary/build.gradle has no dependencies block"); return; }
                i = s.IndexOf('\n', i) + 1;
                s = s.Insert(i, lines);
                File.WriteAllText(gradle, s);
            }
            var props = Path.Combine(Path.GetDirectoryName(unityLibraryPath) ?? unityLibraryPath, "gradle.properties");
            var p = File.Exists(props) ? File.ReadAllText(props) : "";
            if (!p.Contains("android.useAndroidX=true")) File.AppendAllText(props, "\nandroid.useAndroidX=true\n");
            Debug.Log("[VEIL] Android: Google sign-in dependencies added");
        }
    }
}
