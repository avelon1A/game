using System.IO;
using UnityEditor;
using UnityEngine;

namespace Veil.EditorTools
{
    /// <summary>Temporary: renders every hero in its lobby idle (pose debugging).</summary>
    public static class PoseShots
    {
        public static void Run()
        {
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.6f, 0.7f, 0.85f);
            var light = new GameObject("light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(40, 160, 0);
            var rt = new RenderTexture(400, 520, 24);
            cam.targetTexture = rt;
            foreach (var hero in new[] { "vanguard", "volt", "lyra", "nova", "sol" })
            {
                var prefab = Resources.Load<GameObject>("Characters/" + hero);
                if (prefab == null) continue;
                var go = Object.Instantiate(prefab);
                var an = go.GetComponentInChildren<Animator>();
                an.SetBool("Lobby", true); an.SetBool("Grounded", true);
                for (int i = 0; i < 30; i++) an.Update(0.05f);
                cam.transform.position = new Vector3(0.6f, 1.2f, 3.2f); cam.transform.LookAt(new Vector3(0, 0.9f, 0));
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(400, 520, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 400, 520), 0, 0); tex.Apply();
                File.WriteAllBytes($"/private/tmp/claude-501/poses/{hero}.png", tex.EncodeToPNG());
                Object.DestroyImmediate(go);
            }
            Debug.Log("[VEIL] pose shots done");
        }
    }
}
