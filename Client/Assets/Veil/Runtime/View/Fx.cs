using System.Collections.Generic;
using UnityEngine;

namespace Veil.View
{
    /// <summary>
    /// Lightweight pooled VFX: pulse waves, puffs, sparks, flashes and floating shards.
    /// Bright, readable effects (GDD §15) with bloom doing the heavy lifting.
    /// </summary>
    public sealed class Fx : MonoBehaviour
    {
        private sealed class Item
        {
            public GameObject Go;
            public MeshRenderer R;
            public Vector3 Vel;
            public float Life, MaxLife, Gravity, Drag;
            public Vector3 Scale0, Scale1;
            public Color Color;
            public bool FaceCamera, Spin;
            public float FadeIn;
        }

        public static Fx I { get; private set; }

        private readonly List<Item> _active = new List<Item>();
        private readonly Stack<Item> _pool = new Stack<Item>();
        private MaterialPropertyBlock _mpb;
        private Material _add, _alpha;
        private static readonly int ColorId = Shader.PropertyToID("_BaseColor");

        public static Fx Create(Transform parent)
        {
            var go = new GameObject("Fx");
            go.transform.SetParent(parent, false);
            I = go.AddComponent<Fx>();
            I._mpb = new MaterialPropertyBlock();
            I._add = MaterialLib.Unlit(Color.white, MaterialLib.Blend.Additive, null, 10);
            I._alpha = MaterialLib.Unlit(Color.white, MaterialLib.Blend.Alpha, null, 5);
            return I;
        }

        private Item Spawn(Mesh mesh, bool additive, Vector3 pos, Quaternion rot, Vector3 s0, Vector3 s1, Color c, float life)
        {
            var it = _pool.Count > 0 ? _pool.Pop() : null;
            if (it == null)
            {
                var go = Build.Part(transform, mesh, _add, pos, s0, rot, "fx", false);
                it = new Item { Go = go, R = go.GetComponent<MeshRenderer>() };
            }
            it.Go.SetActive(true);
            it.Go.GetComponent<MeshFilter>().sharedMesh = mesh;
            it.R.sharedMaterial = additive ? _add : _alpha;
            it.Go.transform.SetPositionAndRotation(pos, rot);
            it.Go.transform.localScale = s0;
            it.Scale0 = s0; it.Scale1 = s1; it.Color = c; it.Life = 0; it.MaxLife = life;
            it.Vel = Vector3.zero; it.Gravity = 0; it.Drag = 0; it.FaceCamera = false; it.Spin = false; it.FadeIn = 0;
            _active.Add(it);
            return it;
        }

        // ------------------------------------------------------------------ effects

        public void PulseWave(Vector3 pos, float radius, Color c)
        {
            var ring = MeshGen.Ring(0.46f, 0.5f, 72);
            Spawn(ring, true, pos + Vector3.up * 0.25f, Quaternion.identity, Vector3.one * 0.5f, Vector3.one * radius * 2f, c * 1.5f, 0.7f);
            Spawn(ring, true, pos + Vector3.up * 0.9f, Quaternion.identity, Vector3.one * 0.5f, Vector3.one * radius * 1.7f, c, 0.9f);
            Spawn(MeshGen.Disc(48), true, pos + Vector3.up * 0.2f, Quaternion.identity, Vector3.one, Vector3.one * radius * 2f, new Color(c.r, c.g, c.b, 0.25f), 0.6f);
            Spawn(MeshGen.Sphere, true, pos + Vector3.up * 1f, Quaternion.identity, Vector3.one * 0.5f, Vector3.one * 3f, c, 0.35f);
        }

        public void Puff(Vector3 pos, Color c, int count = 12, float size = 0.5f)
        {
            for (int i = 0; i < count; i++)
            {
                var dir = Random.onUnitSphere; dir.y = Mathf.Abs(dir.y) * 0.8f;
                var it = Spawn(MeshGen.SphereLow, false, pos + dir * 0.3f + Vector3.up * 0.8f, Quaternion.identity, Vector3.one * size * Random.Range(0.7f, 1.2f), Vector3.zero, c, Random.Range(0.4f, 0.7f));
                it.Vel = dir * Random.Range(1.5f, 3.5f);
                it.Drag = 3f;
            }
        }

        public void Sparks(Vector3 pos, Color c, int count = 8, float speed = 6f)
        {
            for (int i = 0; i < count; i++)
            {
                var dir = Random.onUnitSphere;
                var it = Spawn(MeshGen.Box, true, pos, Quaternion.LookRotation(dir), new Vector3(0.05f, 0.05f, 0.35f), new Vector3(0.01f, 0.01f, 0.05f), c * 2f, Random.Range(0.2f, 0.35f));
                it.Vel = dir * speed * Random.Range(0.6f, 1.2f);
                it.Gravity = 6f;
            }
        }

        public void Flash(Vector3 pos, Color c, float size = 0.6f, float life = 0.08f)
        {
            Spawn(MeshGen.Sphere, true, pos, Quaternion.identity, Vector3.one * size, Vector3.one * size * 1.8f, c * 2f, life);
        }

        public void Shards(Vector3 pos, Color c, int count = 10)
        {
            for (int i = 0; i < count; i++)
            {
                var dir = Random.onUnitSphere; dir.y = Mathf.Abs(dir.y) + 0.3f;
                var it = Spawn(MeshGen.Octahedron, true, pos + Vector3.up * 0.5f, Random.rotation, Vector3.one * 0.25f, Vector3.zero, c * 1.6f, Random.Range(0.6f, 1f));
                it.Vel = dir.normalized * Random.Range(3f, 6f);
                it.Gravity = 9f;
                it.Spin = true;
            }
        }

        public void Column(Vector3 pos, Color c, float height = 8f, float life = 1.2f)
        {
            Spawn(MeshGen.Cylinder(16), true, pos + Vector3.up * height * 0.5f, Quaternion.identity, new Vector3(1.6f, height, 1.6f), new Vector3(0.1f, height * 1.3f, 0.1f), c, life);
            Spawn(MeshGen.Ring(0.3f, 0.5f, 48), true, pos + Vector3.up * 0.1f, Quaternion.identity, Vector3.one, Vector3.one * 7f, c, life * 0.7f);
        }

        /// <summary>Small dust kick from a footstep or landing.</summary>
        public void Dust(Vector3 pos, float amount = 1f)
        {
            int n = Mathf.RoundToInt(3 + amount * 5);
            for (int i = 0; i < n; i++)
            {
                var dir = new Vector3(Random.Range(-1f, 1f), Random.Range(0.2f, 0.8f), Random.Range(-1f, 1f));
                var it = Spawn(MeshGen.SphereLow, false, new Vector3(pos.x, 0.08f, pos.z) + dir * 0.1f, Quaternion.identity,
                    Vector3.one * Random.Range(0.12f, 0.22f) * (0.7f + amount * 0.5f), Vector3.one * 0.35f * (0.7f + amount * 0.5f), new Color(0.92f, 0.88f, 0.8f, 0.55f), Random.Range(0.35f, 0.55f));
                it.Vel = dir * Random.Range(0.6f, 1.6f) * (0.6f + amount);
                it.Drag = 4f;
            }
        }

        public void Trail(Vector3 pos, Color c)
        {
            var it = Spawn(MeshGen.SphereLow, true, pos, Quaternion.identity, Vector3.one * 0.35f, Vector3.zero, c, 0.3f);
        }

        // ------------------------------------------------------------------ update

        private void Update()
        {
            float dt = Time.deltaTime;
            var cam = Camera.main;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var it = _active[i];
                it.Life += dt;
                float t = it.Life / it.MaxLife;
                if (t >= 1f)
                {
                    it.Go.SetActive(false);
                    _active.RemoveAt(i);
                    _pool.Push(it);
                    continue;
                }
                var tr = it.Go.transform;
                it.Vel += Vector3.down * it.Gravity * dt;
                it.Vel *= 1f / (1f + it.Drag * dt);
                tr.position += it.Vel * dt;
                float e = 1 - (1 - t) * (1 - t);
                tr.localScale = Vector3.LerpUnclamped(it.Scale0, it.Scale1, e);
                if (it.Spin) tr.Rotate(400 * dt, 300 * dt, 0);
                if (it.Vel.sqrMagnitude > 0.01f && !it.Spin && it.Scale0.z > it.Scale0.x * 2) tr.rotation = Quaternion.LookRotation(it.Vel);
                var c = it.Color;
                c.a *= 1 - t;
                _mpb.SetColor(ColorId, c);
                it.R.SetPropertyBlock(_mpb);
            }
        }
    }
}
