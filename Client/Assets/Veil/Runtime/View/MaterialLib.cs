using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Veil.View
{
    /// <summary>
    /// All materials are clones of four base materials kept in Resources (so their shaders ship
    /// in the build). Coloured variants are cached so identical colours share one material.
    /// </summary>
    public static class MaterialLib
    {
        private static Material _toon, _unlit, _water, _sky;
        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        public static void Init()
        {
            if (_toon != null) return;
            _toon = Load("Materials/Toon", "Veil/Toon");
            _unlit = Load("Materials/Unlit", "Veil/Unlit");
            _water = Load("Materials/Water", "Veil/Water");
            _sky = Load("Materials/Sky", "Veil/Sky");
        }

        private static Material Load(string path, string shader)
        {
            var m = Resources.Load<Material>(path);
            if (m != null) return m;
            var s = Shader.Find(shader);
            if (s == null)
            {
                Debug.LogWarning($"[VEIL] Missing shader {shader}, falling back to URP Lit");
                s = Shader.Find("Universal Render Pipeline/Lit");
            }
            return new Material(s);
        }

        private static string Key(string kind, Color c, float a = 0, float b = 0) =>
            $"{kind}{Mathf.RoundToInt(c.r * 255)},{Mathf.RoundToInt(c.g * 255)},{Mathf.RoundToInt(c.b * 255)},{Mathf.RoundToInt(c.a * 255)}|{a:0.00}|{b:0.00}";

        /// <summary>Cel-shaded opaque material.</summary>
        public static Material Toon(Color c, float rim = 0.3f, float gloss = 0f)
        {
            Init();
            string k = Key("t", c, rim, gloss);
            if (Cache.TryGetValue(k, out var m)) return m;
            m = new Material(_toon) { name = k };
            m.SetColor("_BaseColor", c);
            m.SetColor("_RimColor", new Color(1, 1, 1, rim));
            m.SetFloat("_Gloss", gloss);
            m.enableInstancing = true;
            Cache[k] = m;
            return m;
        }

        /// <summary>Toon material with HDR emission (glows with bloom).</summary>
        public static Material Glow(Color c, float intensity = 2f)
        {
            Init();
            string k = Key("g", c, intensity);
            if (Cache.TryGetValue(k, out var m)) return m;
            m = new Material(_toon) { name = k };
            m.SetColor("_BaseColor", c * 0.6f);
            m.SetColor("_EmissionColor", c * intensity);
            m.SetColor("_RimColor", new Color(1, 1, 1, 0.2f));
            Cache[k] = m;
            return m;
        }

        public enum Blend { Alpha, Additive, Opaque }

        /// <summary>Unlit material; additive for glows/VFX, alpha for UI-like world overlays.</summary>
        public static Material Unlit(Color c, Blend blend = Blend.Alpha, Texture tex = null, int queueOffset = 0, bool zwrite = false)
        {
            Init();
            string k = Key("u" + (int)blend + (tex != null ? tex.GetInstanceID().ToString() : "") + queueOffset + zwrite, c);
            if (Cache.TryGetValue(k, out var m)) return m;
            m = new Material(_unlit) { name = k };
            m.SetColor("_BaseColor", c);
            if (tex != null) m.SetTexture("_MainTex", tex);
            switch (blend)
            {
                case Blend.Additive:
                    m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    m.SetFloat("_DstBlend", (float)BlendMode.One);
                    m.SetFloat("_ZWrite", 0);
                    m.renderQueue = (int)RenderQueue.Transparent + queueOffset;
                    break;
                case Blend.Alpha:
                    m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    m.SetFloat("_ZWrite", zwrite ? 1 : 0);
                    m.renderQueue = (int)RenderQueue.Transparent + queueOffset;
                    break;
                default:
                    m.SetFloat("_SrcBlend", (float)BlendMode.One);
                    m.SetFloat("_DstBlend", (float)BlendMode.Zero);
                    m.SetFloat("_ZWrite", 1);
                    m.renderQueue = (int)RenderQueue.Geometry + queueOffset;
                    break;
            }
            Cache[k] = m;
            return m;
        }

        /// <summary>A fresh (uncached) unlit material you can animate independently.</summary>
        public static Material UnlitInstance(Color c, Blend blend, Texture tex = null)
        {
            var m = new Material(Unlit(c, blend, tex));
            return m;
        }

        public static Material Water()
        {
            Init();
            return _water;
        }

        public static Material Sky()
        {
            Init();
            return _sky;
        }
    }

    /// <summary>Colour palettes shared by the world, characters and UI.</summary>
    public static class Palette
    {
        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        // world
        public static readonly Color Grass = Hex("#6cc04a");
        public static readonly Color GrassDark = Hex("#4f9e3a");
        public static readonly Color GrassLight = Hex("#8fd660");
        public static readonly Color Path = Hex("#d9c9a3");
        public static readonly Color PathDark = Hex("#bfae88");
        public static readonly Color Stone = Hex("#aeb0c8");
        public static readonly Color StoneDark = Hex("#7f7f9e");
        public static readonly Color StoneLight = Hex("#d3d4e6");
        public static readonly Color Wood = Hex("#9a6a44");
        public static readonly Color WoodDark = Hex("#6e4a30");
        public static readonly Color RoofBlue = Hex("#4a6fd6");
        public static readonly Color RoofRed = Hex("#d9534f");
        public static readonly Color RoofPurple = Hex("#8a5cd6");
        public static readonly Color Cliff = Hex("#a59a8c");
        public static readonly Color CliffDark = Hex("#7d7266");

        // zones
        public static readonly Color Tower = Hex("#38c8ff");
        public static readonly Color Vault = Hex("#ffc93a");
        public static readonly Color Reactor = Hex("#b35cff");
        public static readonly Color Market = Hex("#5c7dff");
        public static readonly Color Ruins = Hex("#43d17a");

        // gameplay
        public static readonly Color Energy = Hex("#3ee6ff");
        public static readonly Color Health = Hex("#6bff6b");
        public static readonly Color Danger = Hex("#ff4b5c");
        public static readonly Color Key = Hex("#ffd24a");
        public static readonly Color Core = Hex("#4aa8ff");
        public static readonly Color VeilPurple = Hex("#9b5cff");
        public static readonly Color VeilLight = Hex("#c7a6ff");
        public static readonly Color Gold = Hex("#ffcf3f");

        public static Color ZoneColor(Veil.Sim.ZoneType t)
        {
            switch (t)
            {
                case Veil.Sim.ZoneType.Tower: return Tower;
                case Veil.Sim.ZoneType.Vault: return Vault;
                case Veil.Sim.ZoneType.Reactor: return Reactor;
                case Veil.Sim.ZoneType.Market: return Market;
                default: return Ruins;
            }
        }

        // characters
        public static readonly Color Skin = Hex("#ffd9bd");
        public static readonly Color[] HairColors =
        {
            Hex("#6e4127"), Hex("#ff5fae"), Hex("#f1ece4"), Hex("#5cc23a"), Hex("#4f8bff"), Hex("#2a2733"), Hex("#ff8a2a"), Hex("#a36bff"),
        };
        public static readonly Color[] AccentColors =
        {
            Hex("#ff7a1f"), Hex("#ff4fa0"), Hex("#2ee6ff"), Hex("#a66bff"), Hex("#ffd21f"), Hex("#4dff88"), Hex("#ff4b4b"), Hex("#4f7bff"),
        };

        /// <summary>Per-player identity colour for minimap/zone ownership.</summary>
        /// <summary>Enemy squad colours (your own squad is always green).</summary>
        public static Color SquadColor(int squad) => squad switch
        {
            0 => new Color(1f, 0.55f, 0.2f),
            1 => new Color(1f, 0.35f, 0.7f),
            2 => new Color(0.3f, 0.75f, 1f),
            _ => new Color(0.75f, 0.45f, 1f),
        };

        public static Color PlayerColor(int id)
        {
            float h = (id * 0.618034f) % 1f;
            return Color.HSVToRGB(h, 0.7f, 1f);
        }

        /// <summary>Full costume description, modelled on the five VEIL reference characters.</summary>
        public struct Outfit
        {
            public string Name;
            public Color Jacket, Panel, Inner, Collar, Pants, Cuff, Glove, GloveAccent, Shoe, ShoeAccent, Sole, Belt, Buckle, Sock;
            public bool Shorts, KneePads, CropTop, HoodDown, ShoulderPad, ArmRings, Scarf, Cargo;
        }

        public static readonly Outfit[] Outfits =
        {
            new Outfit // brown spiky hero: orange/white jacket, dark cargo shorts
            {
                Name = "Vanguard", Jacket = Hex("#ff7a1f"), Panel = Hex("#f4f1ea"), Inner = Hex("#26262e"), Collar = Hex("#f4f1ea"),
                Pants = Hex("#34302e"), Cuff = Hex("#ff7a1f"), Glove = Hex("#24242b"), GloveAccent = Hex("#8a5a3a"),
                Shoe = Hex("#2a2a31"), ShoeAccent = Hex("#ff7a1f"), Sole = Hex("#f4f4f6"), Belt = Hex("#e9e3d6"), Buckle = Hex("#2a2a31"), Sock = Hex("#f4f4f6"),
                Shorts = true, Cargo = true,
            },
            new Outfit // pink ponytail: black/white cropped jacket, shorts, knee pads
            {
                Name = "Pixie", Jacket = Hex("#25242c"), Panel = Hex("#f4f2f7"), Inner = Hex("#f4f2f7"), Collar = Hex("#25242c"),
                Pants = Hex("#25242c"), Cuff = Hex("#ff4fa0"), Glove = Hex("#25242c"), GloveAccent = Hex("#ff4fa0"),
                Shoe = Hex("#ff4fa0"), ShoeAccent = Hex("#25242c"), Sole = Hex("#f4f2f7"), Belt = Hex("#25242c"), Buckle = Hex("#ff4fa0"), Sock = Hex("#25242c"),
                Shorts = true, KneePads = true, CropTop = true, HoodDown = true,
            },
            new Outfit // hooded shade: black tactical gear, cyan accents, scarf, shoulder disc
            {
                Name = "Shade", Jacket = Hex("#1d1e25"), Panel = Hex("#3a3c48"), Inner = Hex("#15161c"), Collar = Hex("#2ee6ff"),
                Pants = Hex("#1d1e25"), Cuff = Hex("#e4e6ee"), Glove = Hex("#15161c"), GloveAccent = Hex("#2ee6ff"),
                Shoe = Hex("#1b1c22"), ShoeAccent = Hex("#2ee6ff"), Sole = Hex("#2b2d36"), Belt = Hex("#e4e6ee"), Buckle = Hex("#2b2d36"), Sock = Hex("#1d1e25"),
                ShoulderPad = true, Scarf = true, Cargo = true,
            },
            new Outfit // white-haired nova: purple/white jacket with ring emblems, purple cargo pants
            {
                Name = "Nova", Jacket = Hex("#8f5cf7"), Panel = Hex("#efeaf8"), Inner = Hex("#24222c"), Collar = Hex("#8f5cf7"),
                Pants = Hex("#9b6cf7"), Cuff = Hex("#24222c"), Glove = Hex("#24222c"), GloveAccent = Hex("#a66bff"),
                Shoe = Hex("#24222c"), ShoeAccent = Hex("#a66bff"), Sole = Hex("#efeaf8"), Belt = Hex("#24222c"), Buckle = Hex("#ffcf3f"), Sock = Hex("#24222c"),
                ArmRings = true, HoodDown = true, Cargo = true,
            },
            new Outfit // green crest bolt: yellow jacket with black hood, black cargo pants
            {
                Name = "Bolt", Jacket = Hex("#ffc21f"), Panel = Hex("#ffc21f"), Inner = Hex("#1f1f25"), Collar = Hex("#1f1f25"),
                Pants = Hex("#1f1f25"), Cuff = Hex("#ffc21f"), Glove = Hex("#1f1f25"), GloveAccent = Hex("#ffc21f"),
                Shoe = Hex("#ffc21f"), ShoeAccent = Hex("#1f1f25"), Sole = Hex("#1f1f25"), Belt = Hex("#ffc21f"), Buckle = Hex("#1f1f25"), Sock = Hex("#ffc21f"),
                HoodDown = true, Cargo = true,
            },
        };

        public static readonly string[] HairNames = { "Spiky", "Ponytail", "Sleek", "Crest", "Hood" };
        public static readonly string[] AccessoryNames = { "None", "Goggles", "Headset", "Lenses", "Mask" };
    }
}
