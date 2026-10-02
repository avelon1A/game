using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Veil.Match;
using Veil.Sim;
using Veil.View;

namespace Veil.UI
{
    /// <summary>Compass strip at the top of the HUD: headings + markers for your objective, the extraction and hack nodes.</summary>
    public sealed class CompassBar
    {
        private const float Width = 620f, Span = 180f;   // degrees shown across the bar
        private readonly RectTransform _root, _strip;
        private readonly List<Text> _ticks = new List<Text>();
        private readonly List<float> _tickYaw = new List<float>();
        private readonly List<Text> _marks = new List<Text>();

        public CompassBar(Transform parent)
        {
            // phones: the vitals box sits at the top centre, so the compass goes just under it
            _root = UIKit.At(parent, "Compass", new Vector2(0.5f, 1), new Vector2(0, Application.isMobilePlatform ? -126 : -14), new Vector2(Width, 46));
            _root.pivot = new Vector2(0.5f, 1);
            UIKit.Image(_root, UIKit.Rounded, new Color(0.04f, 0.05f, 0.12f, 0.55f));
            _strip = UIKit.At(_root, "Strip", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Width, 46));
            _strip.gameObject.AddComponent<RectMask2D>();
            string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            for (int d = 0; d < 360; d += 15)
            {
                bool major = d % 45 == 0;
                var t = UIKit.LabelAt(_strip, major ? names[d / 45] : (d % 30 == 0 ? d.ToString() : "·"), major ? 20 : 12,
                                      major ? (d == 0 ? Theme.Yellow : Theme.Text) : Theme.TextDim, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44, 24), TextAnchor.MiddleCenter, major ? UIKit.TitleFont : UIKit.BoldFont);
                _ticks.Add(t); _tickYaw.Add(d);
            }
            var centre = UIKit.At(_root, "Centre", new Vector2(0.5f, 0), new Vector2(0, -2), new Vector2(3, 12));
            UIKit.Image(centre, UIKit.Square, Theme.Yellow);
        }

        private float X(float bearing, float cam) => MathUtil.DeltaAngle(cam, bearing) / (Span * 0.5f) * (Width * 0.5f);

        private void Mark(int i, bool on, float bearing, float cam, string label, Color c)
        {
            while (_marks.Count <= i)
            {
                var t = UIKit.LabelAt(_strip, "", 14, Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(70, 20), TextAnchor.MiddleCenter, UIKit.BoldFont);
                t.supportRichText = true; UIKit.Outline(t, new Color(0, 0, 0, 0.8f), 1.5f);
                _marks.Add(t);
            }
            var m = _marks[i];
            float x = X(bearing, cam);
            m.gameObject.SetActive(on);
            if (!on) return;
            x = Mathf.Clamp(x, -Width * 0.5f + 30, Width * 0.5f - 30);
            m.rectTransform.anchoredPosition = new Vector2(x, -12);
            m.text = label; m.color = c;
        }

        public void Update(Snapshot s, PlayerState me, float cameraYaw)
        {
            for (int i = 0; i < _ticks.Count; i++)
            {
                float x = X(_tickYaw[i], cameraYaw);
                bool on = Mathf.Abs(x) < Width * 0.5f - 10;
                _ticks[i].gameObject.SetActive(on);
                if (on) _ticks[i].rectTransform.anchoredPosition = new Vector2(x, 8);
            }
            int k = 0;
            if (GameConfig.ExtractionMode && s != null)
            {
                bool site = s.Stage < 4 && s.Task != ChainTask.Collect && s.Nodes.Count == 0;
                Mark(k++, site, (s.Site - me.Pos).Yaw, cameraYaw, $"● {Vec2.Dist(me.Pos, s.Site):0}m", new Color(1f, 0.82f, 0.3f));
                Mark(k++, s.Stage == 0 && s.Nodes.Count == 0, (-me.Pos).Yaw, cameraYaw, $"● {me.Pos.Length:0}m", new Color(0.8f, 0.55f, 1f));
                Mark(k++, s.ExtractRevealed, (s.ExtractPos - me.Pos).Yaw, cameraYaw, $"● {Vec2.Dist(me.Pos, s.ExtractPos):0}m", s.ExtractController == -1 ? Color.white : new Color(1f, 0.4f, 0.4f));
                for (int i = 0; i < GameConfig.HackNodes; i++)
                {
                    bool on = i < s.Nodes.Count;
                    Mark(k++, on, on ? (s.Nodes[i].Pos - me.Pos).Yaw : 0, cameraYaw, on ? "●" : "", on ? HackPanel.KindColor(s.Nodes[i].Kind) : Color.white);
                }
            }
            for (; k < _marks.Count; k++) _marks[k].gameObject.SetActive(false);
        }
    }

    /// <summary>Full-screen island map (M key / tap the minimap): regions, roads, your squad, objective, extraction, legend.</summary>
    public sealed class MapScreen
    {
        private readonly RectTransform _root, _map;
        private readonly float _size, _scale;
        private readonly ClientMatch _m;
        private readonly List<Image> _dots = new List<Image>();
        private readonly Image _self;
        private int _used;
        public bool Open { get; private set; }

        public MapScreen(Transform parent, ClientMatch m)
        {
            _m = m;
            var map = m.Map;
            _root = UIKit.Fill(parent, "MapScreen");
            UIKit.Image(_root, UIKit.Square, new Color(0.02f, 0.03f, 0.08f, 0.92f), true);
            _size = 860f;
            _scale = _size / (map.Half * 2);
            _map = UIKit.At(_root, "Map", new Vector2(0.5f, 0.5f), new Vector2(-120, -10), new Vector2(_size, _size));
            var raw = _map.gameObject.AddComponent<RawImage>();
            raw.texture = Minimap.MapTexture(map, 512);
            raw.raycastTarget = false;
            // grid letters / numbers
            for (int i = 0; i < 8; i++)
            {
                var a = UIKit.LabelAt(_map, ((char)('A' + i)).ToString(), 14, Theme.TextDim, new Vector2(0, 1), new Vector2((i + 0.5f) * _size / 8, 14), new Vector2(30, 20), TextAnchor.MiddleCenter, UIKit.BoldFont);
                var n = UIKit.LabelAt(_map, (i + 1).ToString(), 14, Theme.TextDim, new Vector2(0, 1), new Vector2(-14, -(i + 0.5f) * _size / 8), new Vector2(30, 20), TextAnchor.MiddleCenter, UIKit.BoldFont);
                var lineV = UIKit.At(_map, "GridV", new Vector2(0, 0.5f), new Vector2(i * _size / 8, 0), new Vector2(1, _size));
                UIKit.Image(lineV, UIKit.Square, new Color(1, 1, 1, 0.07f));
                var lineH = UIKit.At(_map, "GridH", new Vector2(0.5f, 1), new Vector2(0, -i * _size / 8), new Vector2(_size, 1));
                UIKit.Image(lineH, UIKit.Square, new Color(1, 1, 1, 0.07f));
            }
            // region names
            foreach (var r in map.Regions)
            {
                var t = UIKit.LabelAt(_map, r.Name, r.Biome == Biome.City ? 22 : 18, Color.white, new Vector2(0.5f, 0.5f), W(r.Center) + new Vector2(0, r.Biome == Biome.City ? 40 : 0), new Vector2(200, 28), TextAnchor.MiddleCenter, UIKit.TitleFont);
                UIKit.Outline(t, new Color(0, 0, 0, 0.85f), 2);
            }
            var selfRt = UIKit.At(_map, "Self", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26, 26));
            _self = UIKit.Image(selfRt, Icons.Arrow, Theme.Yellow);

            // header + legend
            var title = UIKit.LabelAt(_root, "RILO ISLAND", 34, Color.white, new Vector2(0, 1), new Vector2(40, -30), new Vector2(400, 44), TextAnchor.MiddleLeft, UIKit.TitleFont);
            title.rectTransform.pivot = new Vector2(0, 1);
            var hint = UIKit.LabelAt(_root, "M / tap to close", 16, Theme.TextDim, new Vector2(0, 1), new Vector2(42, -74), new Vector2(400, 24), TextAnchor.MiddleLeft, UIKit.BoldFont);
            hint.rectTransform.pivot = new Vector2(0, 1);
            var legend = UIKit.LabelAt(_root,
                "<color=#ffd84a>●</color> You\n<color=#7dff9a>●</color> Squadmate\n<color=#ff5a6a>●</color> Enemy (spotted)\n<color=#ffd23f>●</color> Your objective / enemy terminal to raid\n<color=#c08cff>●</color> Central terminal (+bonus)\n<color=#ffffff>●</color> Extraction\n<color=#ff4d6d>●</color><color=#38d6ff>●</color><color=#b06bff>●</color> Hack nodes\n<color=#e8d7a8>●</color> Roads   <color=#b88a52>●</color> Bridges",
                18, Theme.Text, new Vector2(1, 0.5f), new Vector2(-40, 0), new Vector2(300, 260), TextAnchor.MiddleLeft, UIKit.BoldFont);
            legend.rectTransform.pivot = new Vector2(1, 0.5f);
            legend.supportRichText = true; legend.lineSpacing = 1.4f;

            var close = _root.gameObject.AddComponent<Button>();
            close.onClick.AddListener(() => Show(false));
            Show(false);
        }

        private Vector2 W(Vec2 p) => new Vector2(p.X, p.Y) * _scale;

        public void Show(bool v) { Open = v; _root.gameObject.SetActive(v); }
        public void Toggle() => Show(!Open);

        private Image Dot(Sprite s, Color c, Vec2 at, float size)
        {
            Image d;
            if (_used < _dots.Count) { d = _dots[_used]; d.gameObject.SetActive(true); }
            else { var rt = UIKit.At(_map, "Dot", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * 12); d = UIKit.Image(rt, s, c); _dots.Add(d); }
            _used++;
            d.sprite = s; d.color = c;
            d.rectTransform.sizeDelta = Vector2.one * size;
            d.rectTransform.anchoredPosition = W(at);
            return d;
        }

        public void Update(MatchView view)
        {
            if (!Open) return;
            var s = _m.Latest; var me = _m.Predicted;
            if (s == null) return;
            _used = 0;
            _self.rectTransform.anchoredPosition = W(me.Pos);
            _self.rectTransform.localRotation = Quaternion.Euler(0, 0, -me.Yaw);
            foreach (var a in s.Avatars)
            {
                if (a.AvatarId >= 1000) continue;
                bool ally = (a.Flags & AvatarFlags.Ally) != 0;
                if (!ally && a.Vis != Visibility.Full) continue;
                Dot(UIKit.Circle, ally ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.35f, 0.4f), a.Pos, 14);
            }
            if (GameConfig.ExtractionMode)
            {
                if (s.Stage < 4 && s.Task != ChainTask.Collect) Dot(Icons.Target, new Color(1f, 0.82f, 0.25f), s.Site, 30);
                if (s.Stage == 0) Dot(Icons.Target, new Color(0.8f, 0.55f, 1f), Vec2.Zero, 34);
                if (s.ExtractRevealed) Dot(Icons.Trophy, Color.white, s.ExtractPos, 34);
                foreach (var n in s.Nodes) Dot(UIKit.Diamond, HackPanel.KindColor(n.Kind), n.Pos, 16);
            }
            for (int i = _used; i < _dots.Count; i++) _dots[i].gameObject.SetActive(false);
        }
    }
}
