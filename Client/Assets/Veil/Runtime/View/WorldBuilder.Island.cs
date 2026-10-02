using System.Collections.Generic;
using UnityEngine;
using Veil.Sim;

namespace Veil.View
{
    /// <summary>Rilo island visuals: biome terrain with river / sea dips, ocean, and the island's building kit.</summary>
    public sealed partial class WorldBuilder
    {
        private const float WaterLevel = -0.35f;

        private static bool Grassy(Biome b) => b == Biome.Forest || b == Biome.Hydro || b == Biome.Outpost || b == Biome.Ruins;

        public static Color BiomeColor(Biome b) => b switch
        {
            Biome.City => Palette.Hex("#c4c6d6"),
            Biome.Snow => Palette.Hex("#eef3fa"),
            Biome.Dock => Palette.Hex("#a3a9b6"),
            Biome.Ruins => Palette.Hex("#a9b866"),
            Biome.Beach => Palette.Hex("#f1dc9e"),
            Biome.Forest => Palette.Hex("#3f9e45"),
            Biome.Canyon => Palette.Hex("#d98b52"),
            Biome.Hydro => Palette.Hex("#79c264"),
            Biome.Outpost => Palette.Hex("#9cc65a"),
            _ => Palette.Hex("#2d86a8"),
        };

        // ------------------------------------------------------------------ terrain

        /// <summary>Signed distance to the nearest water obstacle (negative inside water).</summary>
        private float WaterSd(Vec2 p)
        {
            float best = 99f;
            foreach (int oi in _map.Query(p))
            {
                var o = _map.Obstacles[oi];
                if (o.Kind != ObstacleKind.Water) continue;
                best = Mathf.Min(best, o.SignedDistance(p));
            }
            return best;
        }

        private float TerrainHeight(Vec2 p)
        {
            if (Mathf.Abs(p.X) > _map.Half - 0.5f || Mathf.Abs(p.Y) > _map.Half - 0.5f) return -2.4f;
            float sd = WaterSd(p);
            if (sd < 0) return Mathf.Lerp(-0.5f, -2.4f, Mathf.Clamp01(-sd / 3f));
            float coast = IslandMap.CoastRadius(p.Yaw) - p.Length;
            float h = sd < 1.2f ? Mathf.Lerp(-0.45f, 0f, sd / 1.2f) : 0f;
            if (coast < 3f) h = Mathf.Min(h, Mathf.Lerp(-0.6f, 0f, Mathf.Clamp01(coast / 3f)));
            return h;
        }

        public string DebugGround(Vec2 p) { float h = TerrainHeight(p); return $"h={h:0.00} sd={WaterSd(p):0.0} biome={GroundBiome(p, h)}"; }

        private Biome GroundBiome(Vec2 p, float h)
        {
            var b = IslandMap.BiomeAt(p);
            if (h < WaterLevel + 0.05f) return Biome.Sea;
            float coast = IslandMap.CoastRadius(p.Yaw) - p.Length;
            if (b != Biome.Sea && b != Biome.City && b != Biome.Snow && b != Biome.Dock && coast < 7f) return Biome.Beach;
            return b;
        }

        private void BuildIslandGround()
        {
            const float cell = 2f;
            float half = _map.Half;
            int n = (int)(half * 2 / cell) + 1;
            var heights = new float[n, n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    heights[x, y] = TerrainHeight(new Vec2(-half + x * cell, -half + y * cell));

            // one mesh per biome (the toon material is per colour)
            var verts = new Dictionary<Biome, List<Vector3>>();
            var tris = new Dictionary<Biome, List<int>>();
            for (int y = 0; y < n - 1; y++)
                for (int x = 0; x < n - 1; x++)
                {
                    var mid = new Vec2(-half + (x + 0.5f) * cell, -half + (y + 0.5f) * cell);
                    float hm = (heights[x, y] + heights[x + 1, y] + heights[x, y + 1] + heights[x + 1, y + 1]) * 0.25f;
                    var b = GroundBiome(mid, hm);
                    if (b == Biome.Sea) continue;     // open water: only the sea plane
                    if (!verts.TryGetValue(b, out var v)) { v = verts[b] = new List<Vector3>(); tris[b] = new List<int>(); }
                    var t = tris[b];
                    int i0 = v.Count;
                    v.Add(new Vector3(-half + x * cell, heights[x, y], -half + y * cell));
                    v.Add(new Vector3(-half + (x + 1) * cell, heights[x + 1, y], -half + y * cell));
                    v.Add(new Vector3(-half + x * cell, heights[x, y + 1], -half + (y + 1) * cell));
                    v.Add(new Vector3(-half + (x + 1) * cell, heights[x + 1, y + 1], -half + (y + 1) * cell));
                    t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1);
                    t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0 + 3);
                }
            var terrain = Build.Node(Root, "Terrain", Vector3.zero);
            foreach (var kv in verts)
            {
                var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, name = "Terrain_" + kv.Key };
                mesh.SetVertices(kv.Value);
                mesh.SetTriangles(tris[kv.Key], 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                var col = BiomeColor(kv.Key);
                var go = Build.Part(terrain, mesh, MaterialLib.Toon(col, 0f), Vector3.zero, Vector3.one, null, "Terrain_" + kv.Key, false);
                go.GetComponent<MeshRenderer>().receiveShadows = true;
            }

            // ground patches for colour variation inside grassy regions
            for (int i = 0; i < 260; i++)
            {
                var p = new Vec2(R(-half + 10, half - 10), R(-half + 10, half - 10));
                var b = IslandMap.BiomeAt(p);
                if (b == Biome.Sea || b == Biome.City || TerrainHeight(p) < -0.05f) continue;
                var c = BiomeColor(b);
                c = Color.Lerp(c, i % 2 == 0 ? Color.white : Color.black, b == Biome.Snow ? 0.04f : 0.12f);
                float s = R(6, 18);
                Add(MeshGen.Disc(20), MaterialLib.Toon(c, 0f), Build.V(p, 0.02f + i * 0.00004f), new Vector3(s, 1, s * R(0.6f, 1f)), Quaternion.Euler(0, R(0, 180), 0), false);
            }
        }

        private void BuildIslandSurroundings()
        {
            // the sea: one plane at water level (rivers, moat and coast are terrain dips under it)
            var sea = Build.Part(Root, MeshGen.GroundQuad, MaterialLib.Water(), new Vector3(0, WaterLevel, 0), new Vector3(1600, 1, 1600), null, "Sea", false);
            // small islets on the horizon
            var rock = MaterialLib.Toon(Palette.Cliff, 0.15f);
            var grass = MaterialLib.Toon(Palette.GrassDark, 0.1f);
            for (int i = 0; i < 18; i++)
            {
                float a = i / 18f * 360f + R(-6, 6);
                var p = Quaternion.Euler(0, a, 0) * Vector3.forward * R(250, 420);
                float s = R(14, 34);
                Add(MeshGen.Icosphere(1, 0.25f, i % 9), rock, p + Vector3.up * (-s * 0.45f), new Vector3(s, s * 1.1f, s), Quaternion.Euler(0, R(0, 360), 0), false);
                Add(MeshGen.Icosphere(1, 0.2f, (i + 4) % 9), grass, p + Vector3.up * (s * 0.12f), new Vector3(s * 0.85f, s * 0.2f, s * 0.85f), null, false);
            }
            // waterfall at the hydro dam
            var fallTex = WaterfallTexture();
            var fallMat = MaterialLib.Unlit(new Color(0.75f, 0.95f, 1f, 0.85f), MaterialLib.Blend.Alpha, fallTex);
            fallMat.SetVector("_Scroll", new Vector4(0, 1.2f, 0, 0));
            var dam = Vec2.FromYaw(270f) * 170f;
            var go = Build.Part(Root, MeshGen.Quad, fallMat, Build.V(dam, 4.5f) + new Vector3(-1.4f, 0, 0), new Vector3(8f, 9f, 1f), Quaternion.Euler(0, 90, 0), "Waterfall", false);
            go.GetComponent<MeshRenderer>().sharedMaterial.mainTextureScale = new Vector2(1, 2);
        }

        // ------------------------------------------------------------------ props (Kenney CC0 models, Resources/Props)

        private sealed class PropInfo { public GameObject Prefab; public Vector3 Size; }
        private readonly Dictionary<string, List<PropInfo>> _props = new Dictionary<string, List<PropInfo>>();

        private List<PropInfo> Props(string cat)
        {
            if (_props.TryGetValue(cat, out var l)) return l;
            l = _props[cat] = new List<PropInfo>();
            for (int i = 0; ; i++)
            {
                var go = Resources.Load<GameObject>($"Props/{cat}_{i}");
                if (go == null) break;
                var b = new Bounds(); bool first = true;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var mb = mf.sharedMesh.bounds;
                    var m = mf.transform.localToWorldMatrix;
                    for (int c = 0; c < 8; c++)
                    {
                        var p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                        if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
                    }
                }
                l.Add(new PropInfo { Prefab = go, Size = b.size });
            }
            return l;
        }

        /// <summary>Adds a prop to the static batches. scale is uniform; the prop's origin is its bottom centre.</summary>
        private void AddProp(PropInfo p, Vector3 pos, float yaw, float scale)
        {
            var place = Matrix4x4.TRS(pos, Quaternion.Euler(0, yaw, 0), Vector3.one * scale);
            foreach (var mf in p.Prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null) continue;
                var m = place * mf.transform.localToWorldMatrix;
                var mats = mr.sharedMaterials;
                for (int s = 0; s < mf.sharedMesh.subMeshCount && s < mats.Length; s++)
                    _batch.Add(mf.sharedMesh, mats[s], m, true, s);
            }
        }

        /// <summary>Real model for an obstacle, fitted to its collision shape. False = no prop, use the coded look.</summary>
        private bool TryProp(Obstacle o, Vector3 c)
        {
            string cat; int v = Mathf.Abs(o.Variant);
            switch (o.Kind)
            {
                case ObstacleKind.CityBlock: cat = o.Height > 18f ? "tower" : "city"; break;
                case ObstacleKind.Building: cat = "warehouse"; break;
                case ObstacleKind.Container: cat = "container"; break;
                case ObstacleKind.Hut: cat = "house"; break;
                case ObstacleKind.Palm: cat = "palm"; break;
                case ObstacleKind.Pine: cat = "pine"; break;
                case ObstacleKind.Tree: cat = "tree"; break;
                case ObstacleKind.Rock: cat = "rock"; break;
                case ObstacleKind.Mesa: cat = "mesa"; break;
                case ObstacleKind.Tank: cat = "tank"; break;
                case ObstacleKind.Watchtower: cat = "watchtower"; break;
                case ObstacleKind.Stall: cat = "stall"; break;
                case ObstacleKind.Crate: cat = "crate"; break;
                default: return false;
            }
            var list = Props(cat);
            if (list.Count == 0) return false;
            var p = list[v % list.Count];
            float yaw = o.Rot, scale;
            if (o.Shape == ShapeKind.Box)
            {
                // fit the footprint; long side of the model along the long side of the box
                bool boxLongY = o.Half.Y > o.Half.X, modelLongZ = p.Size.z > p.Size.x;
                if (boxLongY != modelLongZ) yaw += 90f;
                float bw = Mathf.Max(o.Half.X, o.Half.Y) * 2, bd = Mathf.Min(o.Half.X, o.Half.Y) * 2;
                float mw = Mathf.Max(p.Size.x, p.Size.z), md = Mathf.Min(p.Size.x, p.Size.z);
                scale = Mathf.Min(bw / Mathf.Max(mw, 0.01f), bd / Mathf.Max(md, 0.01f)) * 1.04f;
                if (o.Kind == ObstacleKind.CityBlock || o.Kind == ObstacleKind.Hut) yaw += (v % 4) * 90f;
            }
            else
            {
                bool byHeight = o.Kind == ObstacleKind.Tree || o.Kind == ObstacleKind.Palm || o.Kind == ObstacleKind.Pine || o.Kind == ObstacleKind.Watchtower || o.Kind == ObstacleKind.Mesa;
                scale = byHeight ? o.Height * (0.85f + (v % 7) * 0.05f) / Mathf.Max(p.Size.y, 0.01f)
                                 : o.Radius * 2.15f / Mathf.Max(Mathf.Max(p.Size.x, p.Size.z), 0.01f);
                yaw = v % 360;
            }
            if (o.Kind == ObstacleKind.Mesa)
            {
                // canyon spires: tall rock stretched to the collision radius, two smaller rocks at the foot
                float sy = o.Height / Mathf.Max(p.Size.y, 0.01f), sxz = o.Radius * 2.2f / Mathf.Max(Mathf.Max(p.Size.x, p.Size.z), 0.01f);
                var place = Matrix4x4.TRS(c, Quaternion.Euler(0, yaw, 0), new Vector3(sxz, sy, sxz));
                foreach (var mf in p.Prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>(); if (mr == null || mf.sharedMesh == null) continue;
                    var mats = mr.sharedMaterials;
                    for (int s = 0; s < mf.sharedMesh.subMeshCount && s < mats.Length; s++) _batch.Add(mf.sharedMesh, mats[s], place * mf.transform.localToWorldMatrix, true, s);
                }
                var small = list[(v + 3) % list.Count];
                AddProp(small, c + Quaternion.Euler(0, yaw, 0) * new Vector3(o.Radius * 0.9f, 0, 0), yaw + 70, o.Radius * 0.9f / Mathf.Max(small.Size.x, 0.01f));
                return true;
            }
            if (o.Kind == ObstacleKind.Container && o.Height > 3f)
            {
                AddProp(p, c, yaw, scale);
                AddProp(list[(v + 1) % list.Count], c + Vector3.up * p.Size.y * scale, yaw, scale);
                return true;
            }
            AddProp(p, c, yaw, scale);
            return true;
        }

        // ------------------------------------------------------------------ building kit

        private static readonly string[] Facades = { "#8b90b8", "#6e74a3", "#b7a6d6", "#5f6c8f", "#c9b48a", "#9bb3d1" };

        private void BuildCityBlock(Obstacle o, Vector3 c)
        {
            int v = o.Variant;
            var facade = MaterialLib.Toon(Palette.Hex(Facades[v % Facades.Length]), 0.25f, 0.2f);
            var trim = MaterialLib.Toon(Palette.Hex("#3e4266"), 0.2f);
            var window = MaterialLib.Glow(v % 3 == 0 ? Palette.Hex("#9fe8ff") : Palette.Hex("#ffd98a"), 1.2f);
            float w = o.Half.X * 2, d = o.Half.Y * 2, h = o.Height;
            Add(MeshGen.Box, trim, c + Vector3.up * 0.6f, new Vector3(w + 0.4f, 1.2f, d + 0.4f));
            Add(MeshGen.Box, facade, c + Vector3.up * (h / 2), new Vector3(w, h, d));
            // floors: trim bands + window strips
            for (float y = 3.2f; y < h - 1f; y += 3.2f)
            {
                Add(MeshGen.Box, trim, c + Vector3.up * y, new Vector3(w + 0.12f, 0.18f, d + 0.12f), null, false);
                Add(MeshGen.Box, window, c + Vector3.up * (y + 1.4f), new Vector3(w + 0.06f, 0.7f, d * 0.7f), null, false);
                Add(MeshGen.Box, window, c + Vector3.up * (y + 1.4f), new Vector3(w * 0.7f, 0.7f, d + 0.06f), null, false);
            }
            // rooftop
            Add(MeshGen.Box, trim, c + Vector3.up * (h + 0.2f), new Vector3(w + 0.3f, 0.4f, d + 0.3f));
            Add(MeshGen.Box, MaterialLib.Toon(Palette.Hex("#d6d8e4"), 0.2f), c + new Vector3(w * 0.2f, h + 0.9f, -d * 0.15f), new Vector3(1.6f, 1.2f, 1.2f));
            if (v % 4 == 0) Add(MeshGen.Cylinder(6, true), trim, c + new Vector3(-w * 0.25f, h + 2.4f, d * 0.2f), new Vector3(0.15f, 4f, 0.15f));
            if (v % 5 == 1) Add(MeshGen.Icosphere(1, 0.12f, v % 5), MaterialLib.Toon(Palette.Hex("#57b947"), 0.3f), c + new Vector3(-w * 0.2f, h + 0.9f, d * 0.2f), new Vector3(2f, 1.2f, 2f));
        }

        private static readonly string[] ContainerColors = { "#d9483b", "#3d7fd6", "#e88a2e", "#3fa66a", "#8a5ad6", "#e0c341" };

        private void BuildContainer(Obstacle o, Vector3 c, Quaternion rot)
        {
            int stacks = o.Height > 3f ? 2 : 1;
            for (int s = 0; s < stacks; s++)
            {
                var col = Palette.Hex(ContainerColors[(o.Variant + s * 3) % ContainerColors.Length]);
                var mat = MaterialLib.Toon(col, 0.25f, 0.2f);
                var dark = MaterialLib.Toon(col * 0.7f, 0.2f);
                Vector3 size = new Vector3(o.Half.X * 2, 2.5f, o.Half.Y * 2);
                var p = c + Vector3.up * (1.25f + s * 2.6f);
                Add(MeshGen.Box, mat, p, size, rot);
                for (int r = -4; r <= 4; r++)
                    Add(MeshGen.Box, dark, p + rot * new Vector3(0, 0, r * size.z / 9f), new Vector3(size.x + 0.08f, 2.3f, 0.12f), rot, false);
            }
        }

        private void BuildCrane(Obstacle o, Vector3 c)
        {
            var steel = MaterialLib.Toon(Palette.Hex("#e8a33a"), 0.3f, 0.3f);
            var dark = MaterialLib.Toon(Palette.Hex("#3b3752"), 0.2f);
            float h = o.Height;
            for (int i = 0; i < 4; i++)
            {
                var off = new Vector3((i % 2 == 0 ? -1 : 1) * 1.3f, h / 2, (i < 2 ? -1 : 1) * 1.3f);
                Add(MeshGen.Box, steel, c + off, new Vector3(0.45f, h, 0.45f));
            }
            for (float y = 3f; y < h; y += 4f) Add(MeshGen.Box, steel, c + Vector3.up * y, new Vector3(3f, 0.3f, 3f), null, false);
            var dir = new Vector3(c.x, 0, c.z).normalized;
            var boomRot = Quaternion.LookRotation(dir);
            Add(MeshGen.Box, steel, c + Vector3.up * h + dir * 8f, new Vector3(1.2f, 1.2f, 26f), boomRot);
            Add(MeshGen.Box, dark, c + Vector3.up * (h - 1.6f) - dir * 1.5f, new Vector3(2.4f, 2.2f, 2.4f), boomRot);
            Add(MeshGen.Box, dark, c + Vector3.up * h - dir * 5f, new Vector3(2.6f, 1.6f, 3f), boomRot);
            Add(MeshGen.Cylinder(6, true), dark, c + Vector3.up * (h - 5f) + dir * 16f, new Vector3(0.12f, 10f, 0.12f), null, false);
        }

        private void BuildPalm(Obstacle o, Vector3 c)
        {
            int v = o.Variant;
            var trunk = MaterialLib.Toon(Palette.Hex("#a77b4f"), 0.2f);
            var leaf = MaterialLib.Toon(Palette.Hex(v % 3 == 0 ? "#3fae4f" : "#4cc25a"), 0.35f);
            float lean = (v % 30) - 15f;
            var tilt = Quaternion.Euler(lean * 0.6f, v % 360, 0);
            Vector3 top = c;
            for (int i = 0; i < 5; i++)
            {
                var seg = tilt * Vector3.up * 1.3f;
                Add(MeshGen.Cylinder(6, true), trunk, top + seg * 0.5f, new Vector3(0.42f - i * 0.04f, 1.35f, 0.42f - i * 0.04f), tilt);
                top += seg;
            }
            for (int i = 0; i < 7; i++)
            {
                var r = Quaternion.Euler(0, i * 51f + v % 40, 0) * Quaternion.Euler(28, 0, 0);
                Add(MeshGen.Box, leaf, top + r * new Vector3(0, 0, 1.4f), new Vector3(0.7f, 0.08f, 3f), r);
            }
            Add(MeshGen.SphereLow, MaterialLib.Toon(Palette.Hex("#7a5a2e"), 0.2f), top + Vector3.down * 0.3f, Vector3.one * 0.6f);
        }

        private void BuildPine(Obstacle o, Vector3 c)
        {
            int v = o.Variant;
            var trunk = MaterialLib.Toon(Palette.Wood, 0.2f);
            var green = MaterialLib.Toon(Palette.Hex("#2f7a52"), 0.3f);
            var snow = MaterialLib.Toon(Palette.Hex("#f4f8ff"), 0.3f);
            float s = 0.9f + (v % 5) * 0.08f;
            Add(MeshGen.Cylinder(6, true), trunk, c + Vector3.up * 0.9f, new Vector3(0.45f, 1.8f, 0.45f));
            for (int i = 0; i < 3; i++)
            {
                var p = c + Vector3.up * (1.8f + i * 1.5f * s);
                Add(MeshGen.Cone(7), green, p, new Vector3((3.2f - i * 0.8f) * s, 2.4f * s, (3.2f - i * 0.8f) * s), Quaternion.Euler(0, v % 50 + i * 20, 0));
                Add(MeshGen.Cone(7), snow, p + Vector3.up * 0.6f * s, new Vector3((2.2f - i * 0.6f) * s, 1.3f * s, (2.2f - i * 0.6f) * s), Quaternion.Euler(0, v % 50 + i * 20, 0), false);
            }
        }

        private void BuildMesa(Obstacle o, Vector3 c)
        {
            int v = o.Variant;
            var rock = MaterialLib.Toon(Color.Lerp(Palette.Hex("#c96a3a"), Palette.Hex("#e09a5c"), (v % 5) / 5f), 0.15f);
            var band = MaterialLib.Toon(Palette.Hex("#a9542c"), 0.15f);
            var top = MaterialLib.Toon(Palette.Hex("#c2a15c"), 0.1f);
            float h = o.Height, r = o.Radius;
            Add(MeshGen.Icosphere(1, 0.2f, v % 7), rock, c + Vector3.up * (h * 0.35f), new Vector3(r * 2.3f, h * 0.9f, r * 2.2f), Quaternion.Euler(0, v % 360, 0));
            Add(MeshGen.Cylinder(9, true), rock, c + Vector3.up * (h * 0.62f), new Vector3(r * 1.8f, h * 0.6f, r * 1.7f), Quaternion.Euler(0, v % 90, 0));
            Add(MeshGen.Cylinder(9, true), band, c + Vector3.up * (h * 0.55f), new Vector3(r * 1.86f, 0.5f, r * 1.76f), Quaternion.Euler(0, v % 90, 0), false);
            Add(MeshGen.Cylinder(9, true), top, c + Vector3.up * (h * 0.93f), new Vector3(r * 1.7f, 0.4f, r * 1.6f), Quaternion.Euler(0, v % 90, 0));
        }

        private void BuildTank(Obstacle o, Vector3 c)
        {
            var white = MaterialLib.Toon(Palette.Hex("#e4e8ef"), 0.3f, 0.3f);
            var band = MaterialLib.Toon(Palette.Hex("#3d7fd6"), 0.3f);
            float h = o.Height, r = o.Radius;
            Add(MeshGen.Cylinder(16, true), white, c + Vector3.up * (h / 2), new Vector3(r * 2, h, r * 2));
            Add(MeshGen.Sphere, white, c + Vector3.up * h, new Vector3(r * 2, r * 0.8f, r * 2));
            Add(MeshGen.Cylinder(16, true), band, c + Vector3.up * (h * 0.7f), new Vector3(r * 2.04f, 0.5f, r * 2.04f), null, false);
            Add(MeshGen.Cylinder(16, true), band, c + Vector3.up * (h * 0.3f), new Vector3(r * 2.04f, 0.5f, r * 2.04f), null, false);
        }

        private void BuildWatchtower(Obstacle o, Vector3 c)
        {
            var wood = MaterialLib.Toon(Palette.Wood, 0.2f);
            var woodD = MaterialLib.Toon(Palette.WoodDark, 0.2f);
            var roof = MaterialLib.Toon(Palette.RoofRed, 0.3f);
            float h = o.Height;
            for (int i = 0; i < 4; i++)
                Add(MeshGen.Box, wood, c + new Vector3((i % 2 == 0 ? -1 : 1) * 1.2f, h * 0.4f, (i < 2 ? -1 : 1) * 1.2f), new Vector3(0.3f, h * 0.8f, 0.3f));
            Add(MeshGen.Box, woodD, c + Vector3.up * (h * 0.8f), new Vector3(3.4f, 0.3f, 3.4f));
            Add(MeshGen.Box, wood, c + Vector3.up * (h * 0.8f + 0.6f), new Vector3(3.4f, 0.9f, 3.4f));
            Add(MeshGen.Roof, roof, c + Vector3.up * (h * 0.8f + 2.2f), new Vector3(4f, 1.6f, 4f));
            Add(MeshGen.Box, wood, c + Vector3.up * (h * 0.8f + 1.6f), new Vector3(0.2f, 1.2f, 0.2f));
            for (float y = 1.5f; y < h * 0.75f; y += 2f) Add(MeshGen.Box, woodD, c + Vector3.up * y, new Vector3(2.6f, 0.15f, 0.15f), Quaternion.Euler(0, 45, 0), false);
        }

        /// <summary>The Rilo tower at the centre of the city: dark spire with purple light, crystal on top.</summary>
        private void BuildRiloTower(Vector3 c)
        {
            var dark = MaterialLib.Toon(Palette.Hex("#2d2a45"), 0.3f, 0.3f);
            var mid = MaterialLib.Toon(Palette.Hex("#4a4470"), 0.3f, 0.3f);
            var glow = MaterialLib.Glow(Palette.Hex("#b46bff"), 2.6f);
            var stoneL = MaterialLib.Toon(Palette.StoneLight, 0.3f);
            Add(MeshGen.Cylinder(12, true), stoneL, c + Vector3.up * 0.6f, new Vector3(7.2f, 1.2f, 7.2f));
            Add(MeshGen.Box, dark, c + Vector3.up * 14f, new Vector3(5f, 28f, 5f), Quaternion.Euler(0, 45, 0));
            Add(MeshGen.Box, mid, c + Vector3.up * 34f, new Vector3(3.8f, 12f, 3.8f), Quaternion.Euler(0, 45, 0));
            for (int i = 0; i < 4; i++)
            {
                var rot = Quaternion.Euler(0, i * 90, 0);
                Add(MeshGen.Box, glow, c + rot * new Vector3(2.55f, 18f, 2.55f) * 0.71f + Vector3.up * 0f, new Vector3(0.25f, 36f, 0.25f), rot * Quaternion.Euler(0, 45, 0), false);
                Add(MeshGen.Box, glow, c + rot * new Vector3(0, 22f, 2.5f), new Vector3(1.2f, 6f, 0.1f), rot * Quaternion.Euler(0, 45, 0), false);
            }
            for (int i = 0; i < 6; i++)
                Add(MeshGen.Box, glow, c + Vector3.up * (6f + i * 5f), new Vector3(5.2f, 0.25f, 5.2f), Quaternion.Euler(0, 45, 0), false);
            var crystal = Build.Part(Root, MeshGen.Octahedron, MaterialLib.Glow(Palette.Hex("#c48bff"), 3.5f), c + Vector3.up * 44f, new Vector3(2.6f, 4f, 2.6f), null, "TowerCrystal", false);
            TowerCrystal = crystal.transform;
            Build.Part(crystal.transform, MeshGen.Octahedron, MaterialLib.Unlit(new Color(0.7f, 0.5f, 1f, 0.25f), MaterialLib.Blend.Additive), Vector3.zero, Vector3.one * 1.6f, null, "Halo", false);
            Build.Part(Root, MeshGen.Cylinder(16), MaterialLib.Unlit(new Color(0.75f, 0.5f, 1f, 0.06f), MaterialLib.Blend.Additive), c + Vector3.up * 75f, new Vector3(0.9f, 60f, 0.9f), null, "TowerBeam", false);
        }
    }
}
