using System.Collections.Generic;
using UnityEngine;

namespace Veil.UI
{
    /// <summary>
    /// Vector-ish icons rasterised at startup with signed-distance shapes (no image assets needed).
    /// </summary>
    public static class Icons
    {
        public static Sprite Dash, Pulse, Decoy, Blaster, Tower, Vault, Reactor, Market, Ruins, Key, Core, Energy, Health, Players, Clock, Shield, Speed, Target, Arrow, Skull, Trophy, Eye,
            Mic, MicOff, Speaker, SpeakerOff, Crown, Check, Gear, Copy, Plus;

        public static void Init()
        {
            if (Dash != null) return;
            Dash = Make(c => { c.Line(0.2f, 0.25f, 0.45f, 0.5f, 0.07f); c.Line(0.45f, 0.5f, 0.2f, 0.75f, 0.07f); c.Line(0.45f, 0.25f, 0.7f, 0.5f, 0.07f); c.Line(0.7f, 0.5f, 0.45f, 0.75f, 0.07f); c.Line(0.08f, 0.5f, 0.3f, 0.5f, 0.04f); });
            Pulse = Make(c => { c.Circle(0.5f, 0.5f, 0.09f); c.Ring(0.5f, 0.5f, 0.22f, 0.05f); c.Ring(0.5f, 0.5f, 0.36f, 0.04f); });
            Decoy = Make(c => { Person(c, 0.62f, 1f); Person(c, 0.36f, 0.45f); });
            Blaster = Make(c => { c.Ring(0.5f, 0.5f, 0.25f, 0.05f); c.Line(0.5f, 0.1f, 0.5f, 0.3f, 0.05f); c.Line(0.5f, 0.7f, 0.5f, 0.9f, 0.05f); c.Line(0.1f, 0.5f, 0.3f, 0.5f, 0.05f); c.Line(0.7f, 0.5f, 0.9f, 0.5f, 0.05f); c.Circle(0.5f, 0.5f, 0.05f); });
            Tower = Make(c => { c.Poly(new[] { new Vector2(0.5f, 0.92f), new Vector2(0.64f, 0.7f), new Vector2(0.36f, 0.7f) }); c.Box(0.5f, 0.42f, 0.11f, 0.28f); c.Box(0.5f, 0.12f, 0.28f, 0.05f); c.Ring(0.5f, 0.5f, 0.42f, 0.035f, 0.35f); });
            Vault = Make(c => { c.Poly(new[] { new Vector2(0.5f, 0.92f), new Vector2(0.86f, 0.75f), new Vector2(0.8f, 0.35f), new Vector2(0.5f, 0.08f), new Vector2(0.2f, 0.35f), new Vector2(0.14f, 0.75f) }); c.Erase(e => { e.Circle(0.5f, 0.58f, 0.1f); e.Poly(new[] { new Vector2(0.5f, 0.6f), new Vector2(0.58f, 0.3f), new Vector2(0.42f, 0.3f) }); }); });
            Reactor = Make(c => { c.Poly(new[] { new Vector2(0.56f, 0.92f), new Vector2(0.24f, 0.46f), new Vector2(0.48f, 0.46f), new Vector2(0.42f, 0.08f), new Vector2(0.78f, 0.56f), new Vector2(0.54f, 0.56f) }); });
            Market = Make(c => { c.Poly(new[] { new Vector2(0.18f, 0.62f), new Vector2(0.82f, 0.62f), new Vector2(0.74f, 0.12f), new Vector2(0.26f, 0.12f) }); c.Ring(0.5f, 0.66f, 0.17f, 0.05f, 0.5f); });
            Ruins = Make(c => { c.Box(0.3f, 0.4f, 0.09f, 0.3f); c.Box(0.7f, 0.3f, 0.09f, 0.2f); c.Box(0.5f, 0.1f, 0.36f, 0.05f); c.Poly(new[] { new Vector2(0.2f, 0.72f), new Vector2(0.42f, 0.72f), new Vector2(0.36f, 0.82f), new Vector2(0.24f, 0.78f) }); });
            Key = Make(c => { c.Ring(0.32f, 0.62f, 0.16f, 0.07f); c.Line(0.44f, 0.5f, 0.82f, 0.22f, 0.06f); c.Line(0.66f, 0.34f, 0.74f, 0.44f, 0.05f); c.Line(0.76f, 0.26f, 0.84f, 0.36f, 0.05f); });
            Core = Make(c => { c.Poly(new[] { new Vector2(0.5f, 0.92f), new Vector2(0.78f, 0.55f), new Vector2(0.5f, 0.08f), new Vector2(0.22f, 0.55f) }); c.Erase(e => e.Poly(new[] { new Vector2(0.5f, 0.8f), new Vector2(0.62f, 0.55f), new Vector2(0.5f, 0.55f) })); });
            Energy = Make(c => { c.Poly(new[] { new Vector2(0.58f, 0.94f), new Vector2(0.22f, 0.46f), new Vector2(0.47f, 0.46f), new Vector2(0.4f, 0.06f), new Vector2(0.8f, 0.56f), new Vector2(0.54f, 0.56f) }); });
            Health = Make(c => { c.Box(0.5f, 0.5f, 0.12f, 0.34f); c.Box(0.5f, 0.5f, 0.34f, 0.12f); });
            Players = Make(c => { Person(c, 0.64f, 0.8f); Person(c, 0.38f, 1f); });
            Clock = Make(c => { c.Ring(0.5f, 0.5f, 0.38f, 0.07f); c.Line(0.5f, 0.5f, 0.5f, 0.74f, 0.05f); c.Line(0.5f, 0.5f, 0.68f, 0.42f, 0.05f); });
            Shield = Make(c => c.Poly(new[] { new Vector2(0.5f, 0.92f), new Vector2(0.84f, 0.78f), new Vector2(0.78f, 0.36f), new Vector2(0.5f, 0.08f), new Vector2(0.22f, 0.36f), new Vector2(0.16f, 0.78f) }));
            Speed = Make(c => { c.Line(0.2f, 0.3f, 0.5f, 0.5f, 0.07f); c.Line(0.5f, 0.5f, 0.2f, 0.7f, 0.07f); c.Line(0.5f, 0.3f, 0.8f, 0.5f, 0.07f); c.Line(0.8f, 0.5f, 0.5f, 0.7f, 0.07f); });
            Target = Make(c => { c.Ring(0.5f, 0.5f, 0.36f, 0.06f); c.Ring(0.5f, 0.5f, 0.18f, 0.06f); c.Circle(0.5f, 0.5f, 0.06f); });
            Arrow = Make(c => c.Poly(new[] { new Vector2(0.5f, 0.95f), new Vector2(0.82f, 0.12f), new Vector2(0.5f, 0.3f), new Vector2(0.18f, 0.12f) }));
            Skull = Make(c => { c.Circle(0.5f, 0.56f, 0.3f); c.Box(0.5f, 0.24f, 0.16f, 0.1f); c.Erase(e => { e.Circle(0.39f, 0.56f, 0.08f); e.Circle(0.61f, 0.56f, 0.08f); }); });
            Trophy = Make(c => { c.Poly(new[] { new Vector2(0.25f, 0.85f), new Vector2(0.75f, 0.85f), new Vector2(0.66f, 0.45f), new Vector2(0.34f, 0.45f) }); c.Box(0.5f, 0.3f, 0.05f, 0.12f); c.Box(0.5f, 0.14f, 0.2f, 0.05f); c.Ring(0.25f, 0.7f, 0.1f, 0.04f); c.Ring(0.75f, 0.7f, 0.1f, 0.04f); });
            Eye = Make(c => { c.Poly(Ellipse(0.5f, 0.5f, 0.4f, 0.24f, 24)); c.Erase(e => e.Circle(0.5f, 0.5f, 0.15f)); c.Circle(0.5f, 0.5f, 0.07f); });
            Mic = Make(MicShape);
            MicOff = Make(c => { MicShape(c); c.Erase(e => e.Line(0.16f, 0.14f, 0.84f, 0.9f, 0.14f)); c.Line(0.18f, 0.16f, 0.82f, 0.88f, 0.07f); });
            Speaker = Make(c => { c.Ring(0.46f, 0.5f, 0.2f, 0.06f); c.Ring(0.46f, 0.5f, 0.34f, 0.06f); c.Erase(e => e.Box(0.3f, 0.5f, 0.3f, 0.5f)); SpeakerCone(c); });
            SpeakerOff = Make(c => { SpeakerCone(c); c.Line(0.62f, 0.36f, 0.88f, 0.64f, 0.07f); c.Line(0.62f, 0.64f, 0.88f, 0.36f, 0.07f); });
            Crown = Make(c => c.Poly(new[] { new Vector2(0.14f, 0.24f), new Vector2(0.86f, 0.24f), new Vector2(0.92f, 0.74f), new Vector2(0.68f, 0.5f), new Vector2(0.5f, 0.84f), new Vector2(0.32f, 0.5f), new Vector2(0.08f, 0.74f) }));
            Check = Make(c => { c.Line(0.2f, 0.52f, 0.42f, 0.28f, 0.11f); c.Line(0.42f, 0.28f, 0.82f, 0.74f, 0.11f); });
            Gear = Make(c =>
            {
                c.Circle(0.5f, 0.5f, 0.28f);
                for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4; c.Line(0.5f + Mathf.Cos(a) * 0.2f, 0.5f + Mathf.Sin(a) * 0.2f, 0.5f + Mathf.Cos(a) * 0.4f, 0.5f + Mathf.Sin(a) * 0.4f, 0.13f); }
                c.Erase(e => e.Circle(0.5f, 0.5f, 0.12f));
            });
            Copy = Make(c =>
            {
                c.Box(0.58f, 0.58f, 0.25f, 0.25f); c.Erase(e => e.Box(0.58f, 0.58f, 0.18f, 0.18f));
                c.Erase(e => e.Box(0.4f, 0.4f, 0.3f, 0.3f));
                c.Box(0.4f, 0.4f, 0.25f, 0.25f); c.Erase(e => e.Box(0.4f, 0.4f, 0.18f, 0.18f));
            });
            Plus = Make(c => { c.Box(0.5f, 0.5f, 0.07f, 0.34f); c.Box(0.5f, 0.5f, 0.34f, 0.07f); });
        }

        private static Vector2[] Ellipse(float cx, float cy, float rx, float ry, int n)
        {
            var p = new Vector2[n];
            for (int i = 0; i < n; i++) { float a = i / (float)n * Mathf.PI * 2; p[i] = new Vector2(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry); }
            return p;
        }

        private static void MicShape(Canvas c)
        {
            c.Circle(0.5f, 0.74f, 0.13f); c.Box(0.5f, 0.6f, 0.13f, 0.14f); c.Circle(0.5f, 0.47f, 0.13f);
            c.Ring(0.5f, 0.52f, 0.25f, 0.06f, 1f);
            c.Line(0.5f, 0.27f, 0.5f, 0.13f, 0.06f); c.Line(0.34f, 0.12f, 0.66f, 0.12f, 0.06f);
        }

        private static void SpeakerCone(Canvas c) =>
            c.Poly(new[] { new Vector2(0.12f, 0.38f), new Vector2(0.3f, 0.38f), new Vector2(0.52f, 0.18f), new Vector2(0.52f, 0.82f), new Vector2(0.3f, 0.62f), new Vector2(0.12f, 0.62f) });

        private static void Person(Canvas c, float x, float s)
        {
            c.Circle(x, 0.7f, 0.12f * s);
            c.Poly(new[] { new Vector2(x - 0.2f * s, 0.12f), new Vector2(x + 0.2f * s, 0.12f), new Vector2(x + 0.14f * s, 0.5f), new Vector2(x - 0.14f * s, 0.5f) });
        }

        private static Sprite Make(System.Action<Canvas> draw, int size = 96)
        {
            var c = new Canvas(size);
            draw(c);
            return c.ToSprite();
        }

        /// <summary>Tiny coverage rasteriser working in 0..1 space.</summary>
        public sealed class Canvas
        {
            private readonly int _n;
            private readonly float[] _a;
            private bool _erase;

            public Canvas(int n) { _n = n; _a = new float[n * n]; }

            private void Plot(System.Func<Vector2, float> sdf)
            {
                float px = 1f / _n;
                for (int y = 0; y < _n; y++)
                    for (int x = 0; x < _n; x++)
                    {
                        var p = new Vector2((x + 0.5f) / _n, (y + 0.5f) / _n);
                        float cov = Mathf.Clamp01(0.5f - sdf(p) / px);
                        int i = y * _n + x;
                        _a[i] = _erase ? Mathf.Min(_a[i], 1 - cov) : Mathf.Max(_a[i], cov);
                    }
            }

            public void Circle(float cx, float cy, float r) => Plot(p => Vector2.Distance(p, new Vector2(cx, cy)) - r);

            public void Ring(float cx, float cy, float r, float w, float gapTop = 0)
            {
                Plot(p =>
                {
                    float d = Mathf.Abs(Vector2.Distance(p, new Vector2(cx, cy)) - r) - w * 0.5f;
                    if (gapTop > 0 && p.y > cy + r * (1 - gapTop) && Mathf.Abs(p.x - cx) < r * gapTop) return 1;
                    return d;
                });
            }

            public void Line(float x0, float y0, float x1, float y1, float w)
            {
                var a = new Vector2(x0, y0); var b = new Vector2(x1, y1);
                Plot(p =>
                {
                    var ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                    return Vector2.Distance(p, a + ab * t) - w * 0.5f;
                });
            }

            public void Box(float cx, float cy, float hx, float hy)
            {
                Plot(p =>
                {
                    float dx = Mathf.Abs(p.x - cx) - hx, dy = Mathf.Abs(p.y - cy) - hy;
                    return Mathf.Max(dx, dy);
                });
            }

            public void Poly(Vector2[] pts)
            {
                Plot(p =>
                {
                    float d = float.MaxValue;
                    bool inside = false;
                    for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
                    {
                        Vector2 a = pts[j], b = pts[i];
                        var ab = b - a;
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                        d = Mathf.Min(d, Vector2.Distance(p, a + ab * t));
                        if ((b.y > p.y) != (a.y > p.y) && p.x < (a.x - b.x) * (p.y - b.y) / (a.y - b.y) + b.x) inside = !inside;
                    }
                    return inside ? -d : d;
                });
            }

            public void Erase(System.Action<Canvas> draw)
            {
                _erase = true;
                draw(this);
                _erase = false;
            }

            public Sprite ToSprite()
            {
                var t = new Texture2D(_n, _n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var px = new Color[_n * _n];
                for (int i = 0; i < px.Length; i++) px[i] = new Color(1, 1, 1, _a[i]);
                t.SetPixels(px);
                t.Apply();
                return Sprite.Create(t, new Rect(0, 0, _n, _n), new Vector2(0.5f, 0.5f), 100);
            }
        }
    }
}
