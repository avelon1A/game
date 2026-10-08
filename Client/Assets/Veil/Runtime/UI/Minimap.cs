using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Veil.Match;
using Veil.Sim;
using Veil.View;

namespace Veil.UI
{
    /// <summary>
    /// Circular, camera-aligned minimap. Shows only what the player knows: visible players,
    /// pings (Tower sight / Reactor / gunfire), zones, keys/cores and the collapse circle.
    /// </summary>
    public sealed class Minimap
    {
        private readonly RectTransform _root, _content;
        private readonly float _size;
        private readonly float _scale;
        private readonly List<Image> _dots = new List<Image>();
        private readonly List<Image> _zoneIcons = new List<Image>();
        private readonly List<Image> _zoneRings = new List<Image>();
        private readonly Image _self, _circle, _view;
        private readonly Text _north;
        private int _used;

        public static Texture2D MapTexture(MapData map, int res)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            float half = map.Half;
            var px = new Color[res * res];
            Color grass = Palette.Grass * 0.75f, path = Palette.Path * 0.85f, asphalt = Palette.Hex("#4a4d5e");
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    var p = new Vec2((x + 0.5f) / res * half * 2 - half, (y + 0.5f) / res * half * 2 - half);
                    Color c = grass;
                    if (map.Island)
                    {
                        var b = IslandMap.BiomeAt(p);
                        c = b == Biome.Sea ? Palette.Hex("#2a7fb0") : WorldBuilder.BiomeColor(b) * 0.88f;
                    }
                    foreach (var d in map.Decals)
                    {
                        if (d.Kind == 2 && Vec2.Dist(p, d.Center) < d.Radius) c = Palette.Stone * 0.8f;
                        else if (d.Kind == 6) { if (Vec2.Dist(p, d.Center) < d.Radius + 1.5f) c = Palette.Hex("#5fd8ff"); }   // jump pad
                        else if (d.Kind != 2)
                        {
                            var l = Vec2.InverseRotateYaw(p - d.Center, d.Rot);
                            if (Mathf.Abs(l.X) < d.Half.X && Mathf.Abs(l.Y) < d.Half.Y) c = d.Kind == 1 ? Palette.Wood : d.Kind >= 4 ? asphalt : path;
                        }
                    }
                    foreach (int oi in map.Query(p))
                    {
                        var o = map.Obstacles[oi];
                        if (o.SignedDistance(p) > 0) continue;
                        if (o.Kind == ObstacleKind.Water) c = Palette.Hex("#3aa0e0");
                        else if (map.Island && (o.Kind == ObstacleKind.Tree || o.Kind == ObstacleKind.Palm || o.Kind == ObstacleKind.Pine)) c *= 0.8f;
                        else if (o.Kind == ObstacleKind.CityBlock) c = Palette.Hex("#6e7290");
                        else if (o.Kind == ObstacleKind.Tree) c = Palette.GrassDark * 0.7f;
                        else if (o.Kind == ObstacleKind.Cliff) c = Palette.CliffDark * 0.8f;
                        else c = o.Height > 2 ? Palette.StoneDark * 0.8f : Palette.Stone * 0.9f;
                    }
                    c.a = 1;
                    px[y * res + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        public Minimap(Transform parent, MapData map, Vector2 anchor, Vector2 pos, float size)
        {
            _size = size;
            _scale = size / (map.Half * 2) * (map.Island ? 3.2f : 1.55f); // zoomed in: shows ~100-125m
            _root = UIKit.At(parent, "Minimap", anchor, pos, new Vector2(size + 16, size + 16));
            UIKit.Image(_root, UIKit.Circle, new Color(0.05f, 0.06f, 0.14f, 0.9f));
            var ring = UIKit.Fill(_root, "Ring");
            UIKit.Image(ring, UIKit.Ring, new Color(0.6f, 0.5f, 1f, 0.8f));

            var maskRt = UIKit.At(_root, "Mask", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            var maskImg = UIKit.Image(maskRt, UIKit.Circle, Color.white);
            maskRt.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            maskImg.color = new Color(0.1f, 0.12f, 0.2f, 1);

            _content = UIKit.At(maskRt, "Content", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var mapRt = UIKit.At(_content, "Map", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(map.Half * 2 * _scale, map.Half * 2 * _scale));
            var raw = mapRt.gameObject.AddComponent<RawImage>();
            raw.texture = MapTexture(map, map.Island ? 384 : 192);
            raw.color = new Color(1, 1, 1, 0.95f);
            raw.raycastTarget = false;

            foreach (var z in map.Zones)
            {
                var zr = UIKit.At(_content, "ZoneRing", new Vector2(0.5f, 0.5f), W(z.Center), Vector2.one * z.Radius * 2 * _scale);
                _zoneRings.Add(UIKit.Image(zr, UIKit.Ring, Palette.ZoneColor(z.Type)));
                var zi = UIKit.At(_content, "ZoneIcon", new Vector2(0.5f, 0.5f), W(z.Center), new Vector2(22, 22));
                _zoneIcons.Add(UIKit.Image(zi, ZoneIcon(z.Type), Color.white));
            }

            var cr = UIKit.At(_content, "Circle", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * 10);
            _circle = UIKit.Image(cr, UIKit.Ring, new Color(1f, 0.3f, 0.5f, 0.9f));

            var viewRt = UIKit.At(maskRt, "View", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.9f, size * 0.9f));
            viewRt.pivot = new Vector2(0.5f, 0.5f);
            _view = UIKit.Image(viewRt, UIKit.Glow, new Color(1, 1, 1, 0.06f));

            var selfRt = UIKit.At(maskRt, "Self", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20));
            _self = UIKit.Image(selfRt, Icons.Arrow, Theme.Yellow);

            _north = UIKit.LabelAt(_root, "N", 18, Theme.Text, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30, 30), TextAnchor.MiddleCenter, UIKit.TitleFont);
            UIKit.Shadow(_north);
        }

        private Vector2 W(Vec2 p) => new Vector2(p.X, p.Y) * _scale;
        public RectTransform Root => _root;

        public static Sprite ZoneIcon(ZoneType t)
        {
            switch (t)
            {
                case ZoneType.Tower: return Icons.Tower;
                case ZoneType.Vault: return Icons.Vault;
                case ZoneType.Reactor: return Icons.Reactor;
                case ZoneType.Market: return Icons.Market;
                default: return Icons.Ruins;
            }
        }

        private Image Dot()
        {
            if (_used < _dots.Count) { var d = _dots[_used++]; d.gameObject.SetActive(true); return d; }
            var rt = UIKit.At(_content, "Dot", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12, 12));
            var img = UIKit.Image(rt, UIKit.Circle, Color.white);
            _dots.Add(img);
            _used++;
            return img;
        }

        /// <summary>Extra markers for this frame (pings, events, alerts) — filled by HudExtras before Update.</summary>
        public readonly List<(Vec2 pos, Sprite sprite, Color color, float size)> Blips = new List<(Vec2, Sprite, Color, float)>();

        public void Update(ClientMatch m, MatchView view, float cameraYaw)
        {
            var snap = m.Latest;
            if (snap == null) return;
            var me = m.Predicted;
            Vector2 center = W(me.Pos);
            _content.localRotation = Quaternion.Euler(0, 0, cameraYaw);
            _content.anchoredPosition = Quaternion.Euler(0, 0, cameraYaw) * -center;
            _self.rectTransform.localRotation = Quaternion.Euler(0, 0, cameraYaw - me.Yaw);
            _view.rectTransform.localRotation = Quaternion.identity;

            // north marker orbits the rim
            var n = Quaternion.Euler(0, 0, cameraYaw) * Vector2.up * (_size / 2 + 2);
            _north.rectTransform.anchoredPosition = n;

            for (int i = 0; i < _zoneIcons.Count && i < snap.Zones.Length; i++)
            {
                _zoneIcons[i].rectTransform.localRotation = Quaternion.Euler(0, 0, -cameraYaw);
                var z = snap.Zones[i];
                var c = z.Squad < 0 ? Palette.ZoneColor(m.Map.Zones[i].Type) : (z.Squad == m.LocalSquad ? Palette.Health : Palette.SquadColor(z.Squad));
                _zoneRings[i].color = new Color(c.r, c.g, c.b, z.Contested ? 0.5f + Mathf.Sin(Time.time * 10) * 0.4f : 0.9f);
            }

            bool collapsing = snap.Circle < GameConfig.CircleStartRadius - 0.5f;
            _circle.gameObject.SetActive(collapsing);
            if (collapsing) _circle.rectTransform.sizeDelta = Vector2.one * snap.Circle * 2 * _scale;

            _used = 0;
            foreach (var pk in m.Pickups.Values)
            {
                if (pk.Type == PickupType.Orb) continue;
                if (Vec2.Dist(pk.Pos, me.Pos) > 60) continue;
                bool tag = pk.Type == PickupType.Tag;
                if (tag && !m.IsAlly(pk.Spot)) continue;
                var d = Dot();
                d.sprite = tag ? Icons.Skull : MatchSim.IsLoot(pk.Type) ? Icons.Blaster : pk.Type == PickupType.Key ? Icons.Key : Icons.Core;
                d.color = tag ? new Color(0.4f, 1f, 0.55f) : MatchSim.IsLoot(pk.Type) ? new Color(1f, 0.75f, 0.3f) : pk.Type == PickupType.Key ? Palette.Key : Palette.Core;
                d.rectTransform.sizeDelta = new Vector2(14, 14);
                d.rectTransform.anchoredPosition = W(pk.Pos);
                d.rectTransform.localRotation = Quaternion.Euler(0, 0, -cameraYaw);
            }
            if (GameConfig.ExtractionMode)
            {
                if (snap.Stage < 4 && snap.Task != ChainTask.Collect)
                {
                    var d = Dot();
                    d.sprite = Icons.Target; d.color = new Color(1f, 0.85f, 0.3f, 0.75f + Mathf.Sin(Time.time * 5) * 0.25f);
                    d.rectTransform.sizeDelta = new Vector2(24, 24);
                    d.rectTransform.anchoredPosition = W(snap.Site);
                    d.rectTransform.localRotation = Quaternion.Euler(0, 0, -cameraYaw);
                }
                if (snap.Stage == 0)
                {
                    var d = Dot();
                    d.sprite = Icons.Target; d.color = new Color(0.8f, 0.55f, 1f, 0.9f);
                    d.rectTransform.sizeDelta = new Vector2(24, 24);
                    d.rectTransform.anchoredPosition = W(Vec2.Zero);
                    d.rectTransform.localRotation = Quaternion.Euler(0, 0, -cameraYaw);
                }
                foreach (var nd in snap.Nodes)
                {
                    var d = Dot();
                    d.sprite = UIKit.Circle; d.color = HackPanel.KindColor(nd.Kind);
                    d.rectTransform.sizeDelta = new Vector2(16, 16);
                    d.rectTransform.anchoredPosition = W(nd.Pos);
                }
                if (snap.ExtractRevealed)
                {
                    var d = Dot();
                    d.sprite = Icons.Trophy;
                    d.color = snap.ExtractContested ? new Color(1, 0.3f, 0.3f, 0.6f + Mathf.Sin(Time.time * 10) * 0.4f) : snap.ExtractController == m.LocalSquad ? Palette.Health : Color.white;
                    d.rectTransform.sizeDelta = new Vector2(26, 26);
                    d.rectTransform.anchoredPosition = W(snap.ExtractPos);
                    d.rectTransform.localRotation = Quaternion.Euler(0, 0, -cameraYaw);
                }
            }
            foreach (var a in snap.Avatars)
            {
                var d = Dot();
                bool full = a.Vis == Visibility.Full;
                bool mine = (a.Flags & AvatarFlags.MyDecoy) != 0;
                bool ally = (a.Flags & AvatarFlags.Ally) != 0;
                bool decoy = a.AvatarId >= 1000;
                d.sprite = full ? UIKit.Circle : UIKit.Ring;
                float pulse = full ? 1f : 0.6f + Mathf.Sin(Time.time * 8f) * 0.4f;
                d.color = mine || (ally && decoy) ? new Color(0.7f, 0.6f, 1f, 0.8f)
                    : ally ? new Color(0.35f, 1f, 0.5f, 1f)                       // squadmates: always visible, green
                    : (full ? new Color(1f, 0.35f, 0.4f, 1f) : new Color(1f, 0.6f, 0.2f, pulse));
                d.rectTransform.sizeDelta = ally && !decoy ? new Vector2(15, 15) : full ? new Vector2(12, 12) : new Vector2(18, 18);
                d.rectTransform.anchoredPosition = W(a.Pos);
                d.rectTransform.localRotation = Quaternion.identity;
            }
            foreach (var b in Blips)
            {
                var d = Dot();
                d.sprite = b.sprite; d.color = b.color;
                d.rectTransform.sizeDelta = new Vector2(b.size, b.size);
                d.rectTransform.anchoredPosition = W(b.pos);
                d.rectTransform.localRotation = Quaternion.Euler(0, 0, -cameraYaw);
            }
            for (int i = _used; i < _dots.Count; i++) _dots[i].gameObject.SetActive(false);
        }
    }

    /// <summary>Renders character portraits into render textures (for lobby and results).</summary>
    public sealed class PortraitStudio : MonoBehaviour
    {
        private static PortraitStudio _i;
        private readonly Dictionary<string, RenderTexture> _cache = new Dictionary<string, RenderTexture>();
        private readonly Queue<(Appearance look, RenderTexture rt)> _queue = new Queue<(Appearance, RenderTexture)>();
        private Camera _cam;
        private CharacterRig _rig;
        private int _state;

        private static string Key(Appearance a) => $"{a.Outfit}{a.Hair}{a.HairColor}{a.Accessory}{a.Color}";

        public static Texture Get(Appearance look)
        {
            if (_i == null)
            {
                var go = new GameObject("PortraitStudio");
                DontDestroyOnLoad(go);
                go.transform.position = new Vector3(0, -300, 0);
                _i = go.AddComponent<PortraitStudio>();
                var camGo = new GameObject("PortraitCam");
                camGo.transform.SetParent(go.transform, false);
                camGo.transform.localPosition = new Vector3(0.5f, 1.55f, 2.1f);
                camGo.transform.LookAt(go.transform.position + new Vector3(0, 1.45f, 0));
                _i._cam = camGo.AddComponent<Camera>();
                _i._cam.clearFlags = CameraClearFlags.SolidColor;
                _i._cam.backgroundColor = new Color(0, 0, 0, 0);
                _i._cam.fieldOfView = 30;
                _i._cam.nearClipPlane = 0.1f;
                _i._cam.farClipPlane = 6f;
                _i._cam.enabled = false;
            }
            string k = Key(look);
            if (_i._cache.TryGetValue(k, out var rt)) return rt;
            rt = new RenderTexture(160, 160, 16, RenderTextureFormat.ARGB32) { name = "Portrait" + k };
            rt.Create();
            _i._cache[k] = rt;
            _i._queue.Enqueue((look, rt));
            return rt;
        }

        private void LateUpdate()
        {
            if (_state == 1)
            {
                _cam.enabled = false;
                if (_rig) Destroy(_rig.gameObject);
                _rig = null;
                _state = 0;
                return;
            }
            if (_queue.Count == 0) return;
            var (look, rt) = _queue.Dequeue();
            _rig = CharacterRig.Create(transform, look, false, "PortraitRig");
            _rig.transform.localPosition = Vector3.zero;
            _rig.transform.localRotation = Quaternion.Euler(0, 15, 0);
            _rig.Animate(new RigState { Grounded = true, Idle = true }, 0.016f);
            _cam.targetTexture = rt;
            _cam.enabled = true;
            _state = 1;
        }
    }
}
