using UnityEditor;
using UnityEngine;

namespace Veil.EditorTools
{
    /// <summary>
    /// Island props (Kenney CC0, converted by Tools/ai3d/blender/build_props.py) in Resources/Props:
    /// static meshes, no animation, materials switched to the VEIL toon shader so they match the rest of the world.
    /// </summary>
    public sealed class PropImport : AssetPostprocessor
    {
        private bool IsProp => assetPath.StartsWith("Assets/Veil/Resources/Props/");

        public override uint GetVersion() => 2;

        private void OnPreprocessModel()
        {
            if (!IsProp) return;
            var mi = (ModelImporter)assetImporter;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importCameras = false; mi.importLights = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.isReadable = true;        // WorldBuilder combines them into static batches at runtime
            mi.meshCompression = ModelImporterMeshCompression.Medium;
        }

        private void OnPreprocessTexture()
        {
            if (!IsProp) return;
            var ti = (TextureImporter)assetImporter;
            if (System.IO.Path.GetFileName(assetPath).StartsWith("big_"))
            {
                // textured Meshy landmarks: smooth, mipmapped, phone-sized
                ti.filterMode = FilterMode.Bilinear;
                ti.mipmapEnabled = true;
                ti.maxTextureSize = 1024;
                ti.textureCompression = TextureImporterCompression.Compressed;
                return;
            }
            ti.filterMode = FilterMode.Point;       // Kenney colour palettes: keep the flat colours crisp
            ti.mipmapEnabled = false;
        }

        private void OnPostprocessMaterial(Material m)
        {
            if (!IsProp) return;
            var toon = Shader.Find("Veil/Toon");
            if (toon == null) return;
            Texture tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            Color col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
            m.shader = toon;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", col);
            m.SetColor("_ShadeColor", new Color(0.78f, 0.78f, 0.95f));
            m.SetFloat("_Ramp", 0.35f);
            m.SetFloat("_ArtKeep", 0f);
            m.enableInstancing = true;
        }
    }
}
