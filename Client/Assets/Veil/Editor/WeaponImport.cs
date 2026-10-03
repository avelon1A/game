using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Veil.EditorTools
{
    /// <summary>
    /// Kenney Blaster Kit (CC0) guns in Resources/Weapons: re-centres every blaster so the grip sits at the pivot
    /// (the hand), the barrel points along local +Y and the top of the gun along local -Z — the same convention as the
    /// old procedural blaster in CharacterRig — and adds a "Muzzle" child where shots start.
    /// </summary>
    public sealed class WeaponImport : AssetPostprocessor
    {
        private const float Scale = 0.8f;    // Kenney units → metres next to 2 m tall heroes with big hands

        private bool IsWeapon => assetPath.StartsWith("Assets/Veil/Resources/Weapons/");

        public override uint GetVersion() => 3;

        private void OnPreprocessModel()
        {
            if (!IsWeapon) return;
            var mi = (ModelImporter)assetImporter;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;   // CharacterRig gives it the toon material
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importCameras = false; mi.importLights = false;
        }

        private void OnPreprocessTexture()
        {
            if (!IsWeapon) return;
            var ti = (TextureImporter)assetImporter;
            if (assetPath.Contains("meshy_"))
            {
                // painted Meshy texture: smooth, mipmapped, phone-sized
                ti.filterMode = FilterMode.Bilinear; ti.mipmapEnabled = true; ti.maxTextureSize = 1024;
                ti.textureCompression = TextureImporterCompression.Compressed;
                return;
            }
            ti.filterMode = FilterMode.Point;          // palette texture: keep the flat colours crisp
            ti.mipmapEnabled = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
        }

        private void OnPostprocessModel(GameObject g)
        {
            if (!IsWeapon) return;
            // all vertices in the root's space
            var pts = new List<Vector3>();
            foreach (var mf in g.GetComponentsInChildren<MeshFilter>(true))
                foreach (var v in mf.sharedMesh.vertices) pts.Add(g.transform.InverseTransformPoint(mf.transform.TransformPoint(v)));
            if (pts.Count == 0) return;
            var b = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) b.Encapsulate(p);
            // barrel = the longest horizontal axis; grip = the lowest part; muzzle = the far end from the grip
            bool alongX = b.size.x >= b.size.z;
            float Along(Vector3 p) => alongX ? p.x : p.z;
            var low = pts.Where(p => p.y < b.min.y + b.size.y * 0.25f).ToList();
            var grip = low.Aggregate(Vector3.zero, (a, p) => a + p) / Mathf.Max(1, low.Count);
            float dir = Mathf.Sign(Along(b.center) - Along(grip));
            if (dir == 0) dir = 1;
            var barrel = (alongX ? Vector3.right : Vector3.forward) * dir;
            var top = pts.Where(p => p.y > b.center.y).ToList();
            float muzzleY = top.Count > 0 ? top.Average(p => p.y) * 0.5f + b.center.y * 0.5f : b.center.y;
            var muzzle = new Vector3(alongX ? (dir > 0 ? b.max.x : b.min.x) : b.center.x, muzzleY, alongX ? b.center.z : (dir > 0 ? b.max.z : b.min.z));
            var gripPoint = new Vector3(grip.x, b.min.y + b.size.y * 0.3f, grip.z);
            // Meshy guns carry exact "Grip" / "MuzzlePt" markers (Tools/ai3d/blender/build_meshy_weapon.py): use them
            Transform gripT = null, muzT = null;
            foreach (var t in g.GetComponentsInChildren<Transform>(true)) { if (t.name == "Grip") gripT = t; if (t.name == "MuzzlePt") muzT = t; }
            if (gripT != null && muzT != null)
            {
                gripPoint = g.transform.InverseTransformPoint(gripT.position);
                muzzle = g.transform.InverseTransformPoint(muzT.position);
                var d = muzzle - gripPoint; d.y = 0;
                barrel = d.normalized;
            }

            // model (barrel, up) → rig convention (+Y, -Z)
            var q = Quaternion.LookRotation(Vector3.up, Vector3.back) * Quaternion.Inverse(Quaternion.LookRotation(barrel, Vector3.up));

            // wrap everything under "Model" so the root keeps an identity transform
            var model = new GameObject("Model").transform;
            var kids = new List<Transform>();
            foreach (Transform c in g.transform) kids.Add(c);
            model.SetParent(g.transform, false);
            foreach (var c in kids) c.SetParent(model, false);
            var rootMf = g.GetComponent<MeshFilter>();
            if (rootMf != null)
            {
                var body = new GameObject(g.name + "_body").transform;
                body.SetParent(model, false);
                body.gameObject.AddComponent<MeshFilter>().sharedMesh = rootMf.sharedMesh;
                body.gameObject.AddComponent<MeshRenderer>();
                Object.DestroyImmediate(g.GetComponent<MeshRenderer>());
                Object.DestroyImmediate(rootMf);
            }
            model.localRotation = q;
            model.localScale = Vector3.one * Scale;
            model.localPosition = -(q * gripPoint) * Scale;
            var tip = new GameObject("Muzzle").transform;
            tip.SetParent(g.transform, false);
            tip.localPosition = q * (muzzle - gripPoint) * Scale;
        }
    }
}
