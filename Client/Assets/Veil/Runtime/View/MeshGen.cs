using System.Collections.Generic;
using UnityEngine;

namespace Veil.View
{
    /// <summary>
    /// Procedural, readable meshes (so they can be combined). Everything in VEIL's art is built
    /// from these shapes: low-poly for the world, smooth for the chibi characters.
    /// </summary>
    public static class MeshGen
    {
        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        private static Mesh Cached(string key, System.Func<Mesh> make)
        {
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = make();
            m.name = key;
            Cache[key] = m;
            return m;
        }

        // ------------------------------------------------------------------ basic shapes

        /// <summary>Unit cube centred at origin.</summary>
        public static Mesh Box => Cached("box", () =>
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var d in dirs)
            {
                Vector3 up = Mathf.Abs(d.y) > 0.5f ? Vector3.forward : Vector3.up;
                Vector3 right = Vector3.Cross(up, d);
                int b = v.Count;
                v.Add((d - right - up) * 0.5f); v.Add((d - right + up) * 0.5f); v.Add((d + right + up) * 0.5f); v.Add((d + right - up) * 0.5f);
                for (int i = 0; i < 4; i++) n.Add(d);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0));
                t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
            }
            return Make(v, n, uv, t);
        });

        /// <summary>Smooth UV sphere, diameter 1.</summary>
        public static Mesh Sphere => Cached("sphere", () => UvSphere(20, 14));
        public static Mesh SphereLow => Cached("sphereLow", () => UvSphere(10, 7));

        private static Mesh UvSphere(int lon, int lat)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int y = 0; y <= lat; y++)
            {
                float a = Mathf.PI * y / lat;
                for (int x = 0; x <= lon; x++)
                {
                    float b = 2 * Mathf.PI * x / lon;
                    var p = new Vector3(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Cos(a), Mathf.Sin(a) * Mathf.Sin(b));
                    v.Add(p * 0.5f); n.Add(p); uv.Add(new Vector2(x / (float)lon, 1 - y / (float)lat));
                }
            }
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int i0 = y * (lon + 1) + x, i1 = i0 + 1, i2 = i0 + lon + 1, i3 = i2 + 1;
                    t.Add(i0); t.Add(i1); t.Add(i2);
                    t.Add(i1); t.Add(i3); t.Add(i2);
                }
            return Make(v, n, uv, t);
        }

        /// <summary>Capsule along Y: total height 1, diameter = <paramref name="diameterRatio"/>.</summary>
        public static Mesh Capsule => Cached("capsule", () => MakeCapsule(0.5f));

        private static Mesh MakeCapsule(float diameter)
        {
            int lon = 16, latHalf = 6;
            float r = diameter * 0.5f;
            float half = Mathf.Max(0, 0.5f - r);
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            int rows = 0;
            for (int y = 0; y <= latHalf * 2 + 1; y++)
            {
                bool top = y <= latHalf;
                float a = top ? Mathf.PI * 0.5f * y / latHalf : Mathf.PI * 0.5f + Mathf.PI * 0.5f * (y - latHalf - 1) / latHalf;
                float cy = top ? half : -half;
                for (int x = 0; x <= lon; x++)
                {
                    float b = 2 * Mathf.PI * x / lon;
                    var d = new Vector3(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Cos(a), Mathf.Sin(a) * Mathf.Sin(b));
                    v.Add(d * r + new Vector3(0, cy, 0)); n.Add(d); uv.Add(new Vector2(x / (float)lon, y / (float)(latHalf * 2 + 1)));
                }
                rows++;
            }
            for (int y = 0; y < rows - 1; y++)
                for (int x = 0; x < lon; x++)
                {
                    int i0 = y * (lon + 1) + x, i1 = i0 + 1, i2 = i0 + lon + 1, i3 = i2 + 1;
                    t.Add(i0); t.Add(i1); t.Add(i2);
                    t.Add(i1); t.Add(i3); t.Add(i2);
                }
            return Make(v, n, uv, t);
        }

        /// <summary>Cylinder along Y, height 1, diameter 1. Flat = faceted low-poly look.</summary>
        public static Mesh Cylinder(int sides = 12, bool flat = false) => Cached($"cyl{sides}{flat}", () => MakeCylinder(sides, flat, 0.5f, 0.5f));

        /// <summary>Tapered cylinder / cone (top radius factor 0 = cone).</summary>
        public static Mesh Cone(int sides = 10, float topRadius = 0f) => Cached($"cone{sides}_{topRadius:0.00}", () => MakeCylinder(sides, true, 0.5f, topRadius * 0.5f));

        private static Mesh MakeCylinder(int sides, bool flat, float rBottom, float rTop)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            float slope = rBottom - rTop;
            for (int i = 0; i < sides; i++)
            {
                float a0 = 2 * Mathf.PI * i / sides, a1 = 2 * Mathf.PI * (i + 1) / sides;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Vector3 b0 = d0 * rBottom + Vector3.down * 0.5f, b1 = d1 * rBottom + Vector3.down * 0.5f;
                Vector3 t0 = d0 * rTop + Vector3.up * 0.5f, t1 = d1 * rTop + Vector3.up * 0.5f;
                int s = v.Count;
                v.Add(b0); v.Add(t0); v.Add(t1); v.Add(b1);
                if (flat)
                {
                    Vector3 fn = Vector3.Cross(t0 - b0, b1 - b0).normalized;
                    if (Vector3.Dot(fn, d0 + d1) < 0) fn = -fn;
                    for (int k = 0; k < 4; k++) n.Add(fn);
                }
                else
                {
                    Vector3 n0 = (d0 + Vector3.up * slope).normalized, n1 = (d1 + Vector3.up * slope).normalized;
                    n.Add(n0); n.Add(n0); n.Add(n1); n.Add(n1);
                }
                uv.Add(new Vector2(i / (float)sides, 0)); uv.Add(new Vector2(i / (float)sides, 1)); uv.Add(new Vector2((i + 1) / (float)sides, 1)); uv.Add(new Vector2((i + 1) / (float)sides, 0));
                t.Add(s); t.Add(s + 1); t.Add(s + 2); t.Add(s); t.Add(s + 2); t.Add(s + 3);

                // caps
                if (rTop > 0.001f)
                {
                    int c = v.Count;
                    v.Add(Vector3.up * 0.5f); v.Add(t1); v.Add(t0);
                    n.Add(Vector3.up); n.Add(Vector3.up); n.Add(Vector3.up);
                    uv.Add(new Vector2(0.5f, 0.5f)); uv.Add(new Vector2(0.5f + d1.x * 0.5f, 0.5f + d1.z * 0.5f)); uv.Add(new Vector2(0.5f + d0.x * 0.5f, 0.5f + d0.z * 0.5f));
                    t.Add(c); t.Add(c + 1); t.Add(c + 2);
                }
                int cb = v.Count;
                v.Add(Vector3.down * 0.5f); v.Add(b0); v.Add(b1);
                n.Add(Vector3.down); n.Add(Vector3.down); n.Add(Vector3.down);
                uv.Add(new Vector2(0.5f, 0.5f)); uv.Add(new Vector2(0.5f + d0.x * 0.5f, 0.5f + d0.z * 0.5f)); uv.Add(new Vector2(0.5f + d1.x * 0.5f, 0.5f + d1.z * 0.5f));
                t.Add(cb); t.Add(cb + 1); t.Add(cb + 2);
            }
            // winding fix: Unity is clockwise front faces when viewed from outside
            FlipIfInward(v, n, t);
            return Make(v, n, uv, t);
        }

        /// <summary>Faceted low-poly icosphere, optionally jittered (rocks, bushes, foliage).</summary>
        public static Mesh Icosphere(int subdiv, float jitter, int seed)
        {
            string key = $"ico{subdiv}_{jitter:0.00}_{seed}";
            return Cached(key, () =>
            {
                float t = (1f + Mathf.Sqrt(5f)) / 2f;
                var verts = new List<Vector3>
                {
                    new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                    new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                    new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
                };
                var faces = new List<int>
                {
                    0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                    3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
                };
                for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
                for (int s = 0; s < subdiv; s++)
                {
                    var mid = new Dictionary<long, int>();
                    var nf = new List<int>();
                    int Mid(int a, int b)
                    {
                        long k = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                        if (mid.TryGetValue(k, out int idx)) return idx;
                        verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                        mid[k] = verts.Count - 1;
                        return verts.Count - 1;
                    }
                    for (int f = 0; f < faces.Count; f += 3)
                    {
                        int a = faces[f], b = faces[f + 1], c = faces[f + 2];
                        int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                        nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                    }
                    faces = nf;
                }
                var rnd = new System.Random(seed);
                for (int i = 0; i < verts.Count; i++)
                    verts[i] *= 1f + ((float)rnd.NextDouble() * 2 - 1) * jitter;

                // flat shading: unshare vertices
                var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
                for (int f = 0; f < faces.Count; f += 3)
                {
                    Vector3 a = verts[faces[f]] * 0.5f, b = verts[faces[f + 1]] * 0.5f, c = verts[faces[f + 2]] * 0.5f;
                    Vector3 fn = Vector3.Cross(b - a, c - a).normalized;
                    int i0 = v.Count;
                    v.Add(a); v.Add(b); v.Add(c);
                    n.Add(fn); n.Add(fn); n.Add(fn);
                    uv.Add(Vector2.zero); uv.Add(Vector2.right); uv.Add(Vector2.up);
                    tri.Add(i0); tri.Add(i0 + 1); tri.Add(i0 + 2);
                }
                FlipIfInward(v, n, tri);
                return Make(v, n, uv, tri);
            });
        }

        public static Mesh Octahedron => Cached("octa", () =>
        {
            Vector3[] p = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            int[] f = { 0, 4, 3, 0, 3, 5, 0, 5, 2, 0, 2, 4, 1, 3, 4, 1, 5, 3, 1, 2, 5, 1, 4, 2 };
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i < f.Length; i += 3)
            {
                Vector3 a = p[f[i]] * 0.5f, b = p[f[i + 1]] * 0.5f, c = p[f[i + 2]] * 0.5f;
                Vector3 fn = Vector3.Cross(b - a, c - a).normalized;
                int s = v.Count;
                v.Add(a); v.Add(b); v.Add(c); n.Add(fn); n.Add(fn); n.Add(fn);
                uv.Add(Vector2.zero); uv.Add(Vector2.right); uv.Add(Vector2.up);
                t.Add(s); t.Add(s + 1); t.Add(s + 2);
            }
            FlipIfInward(v, n, t);
            return Make(v, n, uv, t);
        });

        /// <summary>Flat annulus in XZ. UV.x runs around, UV.y goes inner(0) → outer(1).</summary>
        public static Mesh Ring(float inner, float outer, int segments = 64) => Cached($"ring{inner:0.000}_{outer:0.000}_{segments}", () =>
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = 2 * Mathf.PI * i / segments;
                var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                v.Add(d * inner); v.Add(d * outer);
                n.Add(Vector3.up); n.Add(Vector3.up);
                uv.Add(new Vector2(i / (float)segments, 0)); uv.Add(new Vector2(i / (float)segments, 1));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                t.Add(a); t.Add(a + 1); t.Add(a + 3);
                t.Add(a); t.Add(a + 3); t.Add(a + 2);
            }
            return Make(v, n, uv, t);
        });

        /// <summary>Flat disc in XZ, radius 0.5, UV mapped to the unit square.</summary>
        public static Mesh Disc(int segments = 48) => Cached($"disc{segments}", () =>
        {
            var v = new List<Vector3> { Vector3.zero }; var n = new List<Vector3> { Vector3.up }; var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) }; var t = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = 2 * Mathf.PI * i / segments;
                var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * 0.5f;
                v.Add(d); n.Add(Vector3.up); uv.Add(new Vector2(0.5f + d.x, 0.5f + d.z));
                if (i > 0) { t.Add(0); t.Add(i); t.Add(i + 1); }
            }
            return Make(v, n, uv, t);
        });

        /// <summary>Quad in XZ (1x1), facing up.</summary>
        public static Mesh GroundQuad => Cached("gquad", () =>
        {
            var v = new List<Vector3> { new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f) };
            var n = new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            var uv = new List<Vector2> { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            return Make(v, n, uv, new List<int> { 0, 1, 2, 0, 2, 3 });
        });

        /// <summary>Camera-facing quad in XY (1x1) for billboards.</summary>
        public static Mesh Quad => Cached("quad", () =>
        {
            var v = new List<Vector3> { new Vector3(-0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(0.5f, -0.5f, 0) };
            var n = new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            var uv = new List<Vector2> { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            return Make(v, n, uv, new List<int> { 0, 1, 2, 0, 2, 3 });
        });

        /// <summary>Gable roof prism: base 1x1 (XZ), ridge along X at height 1.</summary>
        public static Mesh Roof => Cached("roof", () =>
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            Vector3 a = new Vector3(-0.5f, 0, -0.5f), b = new Vector3(0.5f, 0, -0.5f), c = new Vector3(0.5f, 0, 0.5f), d = new Vector3(-0.5f, 0, 0.5f);
            Vector3 r0 = new Vector3(-0.5f, 1, 0), r1 = new Vector3(0.5f, 1, 0);
            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
            {
                Vector3 fn = Vector3.Cross(p1 - p0, p2 - p0).normalized; int s = v.Count;
                v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3); for (int i = 0; i < 4; i++) n.Add(fn);
                uv.Add(Vector2.zero); uv.Add(Vector2.up); uv.Add(Vector2.one); uv.Add(Vector2.right);
                t.Add(s); t.Add(s + 1); t.Add(s + 2); t.Add(s); t.Add(s + 2); t.Add(s + 3);
            }
            void Tri(Vector3 p0, Vector3 p1, Vector3 p2)
            {
                Vector3 fn = Vector3.Cross(p1 - p0, p2 - p0).normalized; int s = v.Count;
                v.Add(p0); v.Add(p1); v.Add(p2); for (int i = 0; i < 3; i++) n.Add(fn);
                uv.Add(Vector2.zero); uv.Add(Vector2.up); uv.Add(Vector2.one);
                t.Add(s); t.Add(s + 1); t.Add(s + 2);
            }
            Quad(a, r0, r1, b);
            Quad(c, r1, r0, d);
            Tri(b, r1, c);
            Tri(d, r0, a);
            FlipIfInward(v, n, t);
            return Make(v, n, uv, t);
        });

        /// <summary>Torus in XZ plane, major radius 0.5, minor radius = thickness.</summary>
        public static Mesh Torus(float thickness = 0.1f, int seg = 24, int side = 8) => Cached($"torus{thickness:0.000}", () =>
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = 2 * Mathf.PI * i / seg;
                Vector3 c = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.5f;
                for (int j = 0; j <= side; j++)
                {
                    float b = 2 * Mathf.PI * j / side;
                    Vector3 dir = c.normalized * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    v.Add(c + dir * thickness); n.Add(dir); uv.Add(new Vector2(i / (float)seg, j / (float)side));
                }
            }
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < side; j++)
                {
                    int a0 = i * (side + 1) + j, a1 = a0 + 1, b0 = a0 + side + 1, b1 = b0 + 1;
                    t.Add(a0); t.Add(b0); t.Add(a1);
                    t.Add(a1); t.Add(b0); t.Add(b1);
                }
            FlipIfInward(v, n, t);
            return Make(v, n, uv, t);
        });

        // ------------------------------------------------------------------ character shapes

        /// <summary>
        /// Rounded box (superellipsoid) of size 1x1x1. roundness 0 = cube-ish, 1 = sphere.
        /// Used for jackets, gloves, pouches and chunky sneakers.
        /// </summary>
        public static Mesh RoundBox(float roundness = 0.35f) => Cached($"rbox{roundness:0.00}", () =>
        {
            float e = Mathf.Clamp(roundness, 0.08f, 1f);
            int lon = 24, lat = 16;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            float P(float c, float k) => Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), k);
            for (int y = 0; y <= lat; y++)
            {
                float a = Mathf.PI * y / lat;
                for (int x = 0; x <= lon; x++)
                {
                    float b = 2 * Mathf.PI * x / lon;
                    float sa = Mathf.Sin(a), ca = Mathf.Cos(a), sb = Mathf.Sin(b), cb = Mathf.Cos(b);
                    var p = new Vector3(P(sa, e) * P(cb, e), P(ca, e), P(sa, e) * P(sb, e)) * 0.5f;
                    var nn = new Vector3(P(sa, 2 - e) * P(cb, 2 - e), P(ca, 2 - e), P(sa, 2 - e) * P(sb, 2 - e));
                    if (nn.sqrMagnitude < 1e-6f) nn = new Vector3(0, Mathf.Sign(ca), 0);
                    v.Add(p); n.Add(nn.normalized); uv.Add(new Vector2(x / (float)lon, y / (float)lat));
                }
            }
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int i0 = y * (lon + 1) + x, i1 = i0 + 1, i2 = i0 + lon + 1, i3 = i2 + 1;
                    t.Add(i0); t.Add(i1); t.Add(i2);
                    t.Add(i1); t.Add(i3); t.Add(i2);
                }
            return Make(v, n, uv, t);
        });

        /// <summary>
        /// Anime hair strand: a tapered, flattened blade growing along +Y that curls toward +Z.
        /// Length 1 (scale it), base width 1, <paramref name="curl"/> = how far the tip bends.
        /// </summary>
        public static Mesh Strand(float curl = 0.35f, float flat = 0.55f) => Cached($"strand{curl:0.00}_{flat:0.00}", () =>
        {
            int rings = 9, sides = 7;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            Vector3 Center(float s) => new Vector3(0, s, curl * s * s);
            for (int r = 0; r <= rings; r++)
            {
                float s = r / (float)rings;
                Vector3 c = Center(s);
                Vector3 tangent = (Center(Mathf.Min(1, s + 0.01f)) - Center(Mathf.Max(0, s - 0.01f))).normalized;
                Vector3 side = Vector3.right;
                Vector3 fwd = Vector3.Cross(side, tangent).normalized;
                float w = 0.5f * Mathf.Pow(1 - s, 0.85f) + 0.004f;
                for (int k = 0; k <= sides; k++)
                {
                    float a = 2 * Mathf.PI * k / sides;
                    Vector3 dir = side * Mathf.Cos(a) + fwd * Mathf.Sin(a) * flat;
                    v.Add(c + dir * w);
                    n.Add((side * Mathf.Cos(a) + fwd * Mathf.Sin(a) / Mathf.Max(0.2f, flat)).normalized);
                    uv.Add(new Vector2(k / (float)sides, s));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int k = 0; k < sides; k++)
                {
                    int i0 = r * (sides + 1) + k, i1 = i0 + 1, i2 = i0 + sides + 1, i3 = i2 + 1;
                    t.Add(i0); t.Add(i2); t.Add(i1);
                    t.Add(i1); t.Add(i2); t.Add(i3);
                }
            return Make(v, n, uv, t);
        });

        // ------------------------------------------------------------------ helpers

        /// <summary>Ensures triangles wind clockwise when seen along -normal (Unity front face).</summary>
        private static void FlipIfInward(List<Vector3> v, List<Vector3> n, List<int> t)
        {
            for (int i = 0; i < t.Count; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 fn = Vector3.Cross(b - a, c - a);
                Vector3 avgN = n[t[i]] + n[t[i + 1]] + n[t[i + 2]];
                if (Vector3.Dot(fn, avgN) < 0) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; }
            }
        }

        private static Mesh Make(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t)
        {
            FlipIfInward(v, n, t);
            var m = new Mesh();
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
