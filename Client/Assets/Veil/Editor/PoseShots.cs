using System.IO;
using UnityEngine;

namespace Veil.EditorTools
{
    /// <summary>Pose check: every hero standing (match idle and lobby), front + side, rendered to /tmp/claude-501/poses.
    ///   Unity -batchmode -quit -projectPath Client -executeMethod Veil.EditorTools.PoseShots.Run</summary>
    public static class PoseShots
    {
        public static void Run()
        {
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.7f, 0.82f);
            cam.fieldOfView = 30;
            var light = new GameObject("light").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(35, 150, 0);
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
            var rt = new RenderTexture(300, 420, 24);
            cam.targetTexture = rt;
            Directory.CreateDirectory("/private/tmp/claude-501/poses");
            foreach (var hero in new[] { "vanguard", "volt", "lyra", "nova", "sol" })
            {
                var prefab = Resources.Load<GameObject>("Characters/" + hero);
                if (prefab == null) continue;
                foreach (bool lobby in new[] { false, true })
                {
                    var go = Object.Instantiate(prefab);
                    var an = go.GetComponentInChildren<Animator>();
                    an.Rebind();
                    an.SetFloat("Speed", 0); an.SetBool("Grounded", true);
                    foreach (var p in an.parameters) if (p.name == "Lobby") an.SetBool("Lobby", lobby);
                    for (int i = 0; i < 40; i++) an.Update(0.05f);
                    Veil.View.CharacterRig.Upright(an, go.transform, 1f);
                    foreach (var (view, pos) in new[] { ("front", new Vector3(0, 1.0f, 4.6f)), ("side", new Vector3(4.6f, 1.0f, 0)) })
                    {
                        cam.transform.position = pos; cam.transform.LookAt(new Vector3(0, 0.9f, 0));
                        cam.Render();
                        RenderTexture.active = rt;
                        var tex = new Texture2D(300, 420, TextureFormat.RGB24, false);
                        tex.ReadPixels(new Rect(0, 0, 300, 420), 0, 0); tex.Apply();
                        File.WriteAllBytes($"/private/tmp/claude-501/poses/{hero}_{(lobby ? "lobby" : "match")}_{view}.png", tex.EncodeToPNG());
                    }
                    Object.DestroyImmediate(go);
                }
            }
            Debug.Log("[VEIL] pose shots done");
        }
    }
}
