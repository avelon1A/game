using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Veil.View
{
    /// <summary>Tiny helpers for assembling procedural objects.</summary>
    public static class Build
    {
        public static GameObject Part(Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 scale, Quaternion? rot = null, string name = null, bool shadows = true)
        {
            var go = new GameObject(name ?? mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        public static Transform Node(Transform parent, string name, Vector3 pos, Quaternion? rot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            return go.transform;
        }

        public static Vector3 V(Veil.Sim.Vec2 v, float y = 0) => new Vector3(v.X, y, v.Y);
        public static Veil.Sim.Vec2 V2(Vector3 v) => new Veil.Sim.Vec2(v.x, v.z);
    }

    /// <summary>
    /// Collects static geometry and bakes it into a few combined meshes per material and
    /// spatial cell — the whole arena renders in a handful of draw calls.
    /// </summary>
    public sealed class StaticBatcher
    {
        private struct Item { public Mesh Mesh; public Matrix4x4 M; public bool Shadows; }
        private readonly Dictionary<(Material, int, bool), List<Item>> _items = new Dictionary<(Material, int, bool), List<Item>>();
        private readonly float _cell;

        public StaticBatcher(float cellSize = 40f) { _cell = cellSize; }

        public void Add(Mesh mesh, Material mat, Vector3 pos, Vector3 scale, Quaternion rot, bool shadows = true)
        {
            int cx = Mathf.FloorToInt(pos.x / _cell), cz = Mathf.FloorToInt(pos.z / _cell);
            int cell = (cx + 100) * 1000 + (cz + 100);
            var key = (mat, cell, shadows);
            if (!_items.TryGetValue(key, out var list)) _items[key] = list = new List<Item>();
            list.Add(new Item { Mesh = mesh, M = Matrix4x4.TRS(pos, rot, scale), Shadows = shadows });
        }

        public void Add(Mesh mesh, Material mat, Matrix4x4 m, bool shadows = true)
        {
            Vector3 pos = m.GetColumn(3);
            int cx = Mathf.FloorToInt(pos.x / _cell), cz = Mathf.FloorToInt(pos.z / _cell);
            int cell = (cx + 100) * 1000 + (cz + 100);
            var key = (mat, cell, shadows);
            if (!_items.TryGetValue(key, out var list)) _items[key] = list = new List<Item>();
            list.Add(new Item { Mesh = mesh, M = m, Shadows = shadows });
        }

        public void Bake(Transform parent)
        {
            foreach (var kv in _items)
            {
                var list = kv.Value;
                int start = 0;
                while (start < list.Count)
                {
                    var combine = new List<CombineInstance>();
                    int verts = 0;
                    int i = start;
                    for (; i < list.Count; i++)
                    {
                        int vc = list[i].Mesh.vertexCount;
                        if (verts + vc > 60000 && combine.Count > 0) break;
                        combine.Add(new CombineInstance { mesh = list[i].Mesh, transform = list[i].M });
                        verts += vc;
                    }
                    start = i;
                    var mesh = new Mesh { name = "batch_" + kv.Key.Item1.name };
                    mesh.CombineMeshes(combine.ToArray(), true, true);
                    mesh.RecalculateBounds();
                    var go = new GameObject(mesh.name);
                    go.transform.SetParent(parent, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = kv.Key.Item1;
                    r.shadowCastingMode = kv.Key.Item3 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    r.lightProbeUsage = LightProbeUsage.Off;
                    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    go.isStatic = true;
                }
            }
            _items.Clear();
        }
    }
}
