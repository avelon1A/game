using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Veil.Match;
using Veil.Sim;
using EventType = Veil.Sim.EventType;
using Veil.View;

namespace Veil.UI
{
    /// <summary>In-match HUD (GDD §25 UI): timer, players, score, minimap, objectives, vitals, abilities, prompts.</summary>
    public sealed class Hud
    {
        public readonly RectTransform Root;
        private readonly ClientMatch _m;
        private readonly MatchView _view;
        private readonly Camera _cam;
        private readonly RectTransform _labels;

        private Text _players, _timer, _phase, _score, _banner, _bannerSub, _prompt, _respawn, _status, _keys, _cores, _hpText, _enText, _popup;
        private Image _phaseFill, _vignette, _revealEdge, _hitMarker, _crosshair, _portraitRing;
        private Bar _hp, _en, _promptBar;
        private Minimap _minimap;
        private readonly Ability[] _abilities = new Ability[4];
        private readonly ObjectiveCard[] _objectives = new ObjectiveCard[2];
        private readonly List<(Text t, float time)> _feed = new List<(Text, float)>();
        private RectTransform _feedRoot;
        private float _bannerT, _hitT, _damageT, _revealT, _popupT;
        private MatchPhase _lastPhase = (MatchPhase)255;
        private RectTransform _scoreboard;
        private Text _scoreboardText;
        private readonly Dictionary<int, Nameplate> _plates = new Dictionary<int, Nameplate>();
        private readonly List<ZoneMarker> _zoneMarkers = new List<ZoneMarker>();
        private readonly List<int> _scratch = new List<int>();
        private int _lastScore;
        private float _scoreShown;

        private sealed class Ability
        {
            public Image Icon, Cooldown, Back;
            public Text Key, Cost, Timer;
            public string Name;
            public int CostValue;
        }

        private sealed class ObjectiveCard
        {
            public RectTransform Root;
            public Image Icon, Check;
            public Text Title, Desc, Progress;
            public Bar Bar;
        }

        private sealed class Nameplate
        {
            public RectTransform Root;
            public Text Name;
            public Bar Hp;
            public Image Marker;
        }

        private sealed class ZoneMarker
        {
            public RectTransform Root;
            public Image Icon, Back;
            public Text Label;
        }

        private readonly bool _mobile;
        private bool _forceBoard;

        public void SetScoreboard(bool open) => _forceBoard = open;

        public Hud(Transform canvas, ClientMatch m, MatchView view, Camera cam, bool mobile = false)
        {
            _m = m;
            _view = view;
            _cam = cam;
            Root = UIKit.Fill(canvas, "HUD");
            _labels = UIKit.Fill(Root, "WorldLabels");
            BuildOverlays();
            BuildTopLeft();
            BuildTopRight();
            BuildObjectives();
            BuildVitals();
            BuildAbilities();
            BuildCenter();
            BuildScoreboard();
            foreach (var z in m.Map.Zones) _zoneMarkers.Add(MakeZoneMarker(z));
            view.OnEvent += HandleEvent;
            _mobile = mobile;
            if (mobile) ApplyMobileLayout();
        }

        public void Dispose()
        {
            _view.OnEvent -= HandleEvent;
            Object.Destroy(Root.gameObject);
        }

        /// <summary>Phone layout: thumbs own the bottom corners, so vitals go top-centre and objectives left-middle.</summary>
        private void ApplyMobileLayout()
        {
            Root.gameObject.AddComponent<SafeArea>();
            void Move(string name, Vector2 anchor, Vector2 pos, float scale = 1f)
            {
                var t = (RectTransform)Root.Find(name);
                if (t == null) return;
                t.anchorMin = t.anchorMax = t.pivot = anchor;
                t.anchoredPosition = pos;
                t.localScale = Vector3.one * scale;
            }
            Move("Vitals", new Vector2(0.5f, 1), new Vector2(0, -20), 0.85f);
            Move("Objective0", new Vector2(0, 0.5f), new Vector2(24, 110), 0.8f);
            Move("Objective1", new Vector2(0, 0.5f), new Vector2(24, 25), 0.8f);
            for (int i = 0; i < 4; i++) { var a = Root.Find("Ability" + i); if (a) a.gameObject.SetActive(false); }
            foreach (var t in Root.GetComponentsInChildren<Text>(true))
                if (t.text == "OBJECTIVES") { t.rectTransform.anchorMin = t.rectTransform.anchorMax = t.rectTransform.pivot = new Vector2(0, 0.5f); t.rectTransform.anchoredPosition = new Vector2(28, 165); t.alignment = TextAnchor.MiddleLeft; }
            var feed = (RectTransform)Root.Find("Feed");
            if (feed) feed.localScale = Vector3.one * 0.85f;
            var prompt = (RectTransform)Root.Find("Prompt");
            if (prompt) prompt.anchoredPosition = new Vector2(0, 330);
            _promptBar.Root.anchoredPosition = new Vector2(0, 300);
        }

        // ------------------------------------------------------------------ construction

        private Text Pill(Vector2 pos, float width, Sprite icon, string text, out RectTransform rt)
        {
            rt = UIKit.At(Root, "Pill", new Vector2(0, 1), pos, new Vector2(width, 48));
            UIKit.Image(rt, UIKit.Pill, Theme.Panel);
            var ic = UIKit.At(rt, "Icon", new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(26, 26));
            ic.pivot = new Vector2(0, 0.5f);
            UIKit.Image(ic, icon, Theme.Text);
            var t = UIKit.LabelAt(rt, text, 24, Theme.Text, new Vector2(0, 0.5f), new Vector2(48, 0), new Vector2(width - 56, 40), TextAnchor.MiddleLeft, UIKit.BoldFont);
            t.rectTransform.pivot = new Vector2(0, 0.5f);
            return t;
        }

        private void BuildTopLeft()
        {
            _players = Pill(new Vector2(24, -24), 220, Icons.Players, "15 Players", out _);
            _timer = Pill(new Vector2(254, -24), 128, Icons.Clock, "15:00", out _);
            var phaseRt = UIKit.At(Root, "Phase", new Vector2(0, 1), new Vector2(24, -82), new Vector2(358, 44));
            UIKit.Image(phaseRt, UIKit.RoundedSmall, new Color(0.07f, 0.08f, 0.16f, 0.7f));
            _phase = UIKit.LabelAt(phaseRt, "EXPLORATION", 18, Theme.PurpleLight, new Vector2(0, 1), new Vector2(14, -4), new Vector2(300, 24), TextAnchor.UpperLeft, UIKit.BoldFont);
            _phase.rectTransform.pivot = new Vector2(0, 1);
            var fillRt = UIKit.Rect(phaseRt, "Fill", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 8), new Vector2(-28, 6));
            fillRt.anchoredPosition = new Vector2(14, 8);
            UIKit.Image(fillRt, UIKit.Pill, new Color(1, 1, 1, 0.12f));
            var inner = UIKit.Rect(fillRt, "Inner", Vector2.zero, new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            _phaseFill = UIKit.Image(inner, UIKit.Pill, Theme.Purple);
            _score = UIKit.LabelAt(Root, "SCORE 0", 26, Theme.Gold, new Vector2(0, 1), new Vector2(26, -134), new Vector2(360, 34), TextAnchor.UpperLeft, UIKit.TitleFont);
            _score.rectTransform.pivot = new Vector2(0, 1);
            UIKit.Shadow(_score);
            _status = UIKit.LabelAt(Root, "", 18, Theme.TextDim, new Vector2(0, 1), new Vector2(26, -172), new Vector2(420, 30), TextAnchor.UpperLeft, UIKit.BoldFont);
            _status.rectTransform.pivot = new Vector2(0, 1);
            UIKit.Shadow(_status);
        }

        private void BuildTopRight()
        {
            _minimap = new Minimap(Root, _m.Map, new Vector2(1, 1), new Vector2(-24, -24), 250);
            ((RectTransform)Root.Find("Minimap")).pivot = new Vector2(1, 1);
            _feedRoot = UIKit.At(Root, "Feed", new Vector2(1, 1), new Vector2(-24, -300), new Vector2(420, 200));
            _feedRoot.pivot = new Vector2(1, 1);
        }

        private void BuildObjectives()
        {
            var header = UIKit.LabelAt(Root, "OBJECTIVES", 18, Theme.PurpleLight, new Vector2(1, 0.5f), new Vector2(-24, -52), new Vector2(360, 26), TextAnchor.MiddleRight, UIKit.BoldFont);
            header.rectTransform.pivot = new Vector2(1, 0.5f);
            UIKit.Shadow(header);
            for (int i = 0; i < 2; i++)
            {
                var c = new ObjectiveCard();
                c.Root = UIKit.At(Root, "Objective" + i, new Vector2(1, 0.5f), new Vector2(-24, -114 - i * 102), new Vector2(380, 94));
                c.Root.pivot = new Vector2(1, 0.5f);
                UIKit.Image(c.Root, UIKit.Rounded, Theme.Panel);
                var iconBack = UIKit.At(c.Root, "IconBack", new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(58, 58));
                iconBack.pivot = new Vector2(0, 0.5f);
                UIKit.Image(iconBack, UIKit.Circle, i == 0 ? new Color(0.62f, 0.38f, 1f, 0.35f) : new Color(1, 1, 1, 0.12f));
                var ic = UIKit.At(iconBack, "Icon", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34, 34));
                c.Icon = UIKit.Image(ic, Icons.Tower, Color.white);
                c.Title = UIKit.LabelAt(c.Root, "", 20, Theme.Text, new Vector2(0, 1), new Vector2(82, -10), new Vector2(260, 24), TextAnchor.UpperLeft, UIKit.BoldFont);
                c.Title.rectTransform.pivot = new Vector2(0, 1);
                c.Desc = UIKit.LabelAt(c.Root, "", 15, Theme.TextDim, new Vector2(0, 1), new Vector2(82, -36), new Vector2(265, 22), TextAnchor.UpperLeft);
                c.Desc.rectTransform.pivot = new Vector2(0, 1);
                c.Bar = new Bar(c.Root, new Vector2(0, 0), new Vector2(82, 14), new Vector2(200, 12), i == 0 ? Theme.Purple : Theme.Cyan, new Color(1, 1, 1, 0.1f));
                c.Bar.Root.pivot = new Vector2(0, 0);
                c.Progress = UIKit.LabelAt(c.Root, "", 15, Theme.Text, new Vector2(0, 0), new Vector2(292, 10), new Vector2(80, 20), TextAnchor.LowerLeft, UIKit.BoldFont);
                c.Progress.rectTransform.pivot = new Vector2(0, 0);
                var chk = UIKit.At(c.Root, "Check", new Vector2(1, 1), new Vector2(-10, -10), new Vector2(26, 26));
                chk.pivot = new Vector2(1, 1);
                c.Check = UIKit.Image(chk, UIKit.Circle, Theme.Green);
                var tag = UIKit.LabelAt(c.Root, i == 0 ? "PRIMARY +500" : "SECONDARY +250", 12, i == 0 ? Theme.PurpleLight : Theme.Cyan, new Vector2(1, 1), new Vector2(-44, -14), new Vector2(140, 16), TextAnchor.UpperRight, UIKit.BoldFont);
                tag.rectTransform.pivot = new Vector2(1, 1);
                _objectives[i] = c;
            }
        }

        private void BuildVitals()
        {
            var root = UIKit.At(Root, "Vitals", new Vector2(0, 0), new Vector2(24, 24), new Vector2(430, 120));
            root.pivot = new Vector2(0, 0);
            UIKit.Image(root, UIKit.Rounded, Theme.Panel);
            var pr = UIKit.At(root, "Portrait", new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(96, 96));
            pr.pivot = new Vector2(0, 0.5f);
            _portraitRing = UIKit.Image(pr, UIKit.Circle, new Color(0.25f, 0.2f, 0.45f, 1));
            var entry = _m.Entry(_m.LocalId);
            if (entry != null)
            {
                var rawRt = UIKit.Fill(pr, "Face", 4);
                var raw = rawRt.gameObject.AddComponent<RawImage>();
                raw.texture = PortraitStudio.Get(entry.Look);
                raw.raycastTarget = false;
            }
            var ring = UIKit.Fill(pr, "Ring", -2);
            UIKit.Image(ring, UIKit.Ring, Theme.Purple);

            var hpIcon = UIKit.At(root, "HpIcon", new Vector2(0, 1), new Vector2(122, -22), new Vector2(22, 22));
            UIKit.Image(hpIcon, Icons.Health, Palette.Health);
            _hp = new Bar(root, new Vector2(0, 1), new Vector2(150, -22), new Vector2(210, 20), Palette.Health, new Color(0, 0, 0, 0.45f));
            _hp.Root.pivot = new Vector2(0, 0.5f);
            _hpText = UIKit.LabelAt(root, "100", 20, Theme.Text, new Vector2(0, 1), new Vector2(370, -22), new Vector2(60, 24), TextAnchor.MiddleLeft, UIKit.BoldFont);
            _hpText.rectTransform.pivot = new Vector2(0, 0.5f);

            var enIcon = UIKit.At(root, "EnIcon", new Vector2(0, 1), new Vector2(122, -54), new Vector2(22, 22));
            UIKit.Image(enIcon, Icons.Energy, Palette.Energy);
            _en = new Bar(root, new Vector2(0, 1), new Vector2(150, -54), new Vector2(210, 20), Palette.Energy, new Color(0, 0, 0, 0.45f));
            _en.Root.pivot = new Vector2(0, 0.5f);
            _enText = UIKit.LabelAt(root, "50", 20, Theme.Text, new Vector2(0, 1), new Vector2(370, -54), new Vector2(60, 24), TextAnchor.MiddleLeft, UIKit.BoldFont);
            _enText.rectTransform.pivot = new Vector2(0, 0.5f);

            var keyIcon = UIKit.At(root, "KeyIcon", new Vector2(0, 0), new Vector2(122, 26), new Vector2(24, 24));
            UIKit.Image(keyIcon, Icons.Key, Palette.Key);
            _keys = UIKit.LabelAt(root, "0/3", 20, Theme.Text, new Vector2(0, 0), new Vector2(150, 26), new Vector2(80, 24), TextAnchor.MiddleLeft, UIKit.BoldFont);
            _keys.rectTransform.pivot = new Vector2(0, 0.5f);
            var coreIcon = UIKit.At(root, "CoreIcon", new Vector2(0, 0), new Vector2(220, 26), new Vector2(24, 24));
            UIKit.Image(coreIcon, Icons.Core, Palette.Core);
            _cores = UIKit.LabelAt(root, "0", 20, Theme.Text, new Vector2(0, 0), new Vector2(248, 26), new Vector2(80, 24), TextAnchor.MiddleLeft, UIKit.BoldFont);
            _cores.rectTransform.pivot = new Vector2(0, 0.5f);
        }

        private void BuildAbilities()
        {
            string[] keys = { "LMB", "Q", "E", "R" };
            Sprite[] icons = { Icons.Blaster, Icons.Dash, Icons.Pulse, Icons.Decoy };
            string[] names = { "BLAST", "DASH", "PULSE", "DECOY" };
            Color[] cols = { Theme.Yellow, Theme.Cyan, Theme.Purple, Theme.PurpleLight };
            for (int i = 0; i < 4; i++)
            {
                float size = i == 0 ? 70 : 88;
                var rt = UIKit.At(Root, "Ability" + i, new Vector2(1, 0), new Vector2(-24 - (3 - i) * 104, 44), new Vector2(size, size));
                rt.pivot = new Vector2(1, 0);
                var a = new Ability();
                a.Back = UIKit.Image(rt, UIKit.Circle, new Color(0.1f, 0.1f, 0.24f, 0.92f));
                var ring = UIKit.Fill(rt, "Ring");
                UIKit.Image(ring, UIKit.Ring, cols[i]);
                var ic = UIKit.At(rt, "Icon", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.52f, size * 0.52f));
                a.Icon = UIKit.Image(ic, icons[i], cols[i]);
                var cd = UIKit.Fill(rt, "Cooldown", 3);
                a.Cooldown = UIKit.Image(cd, UIKit.Circle, new Color(0, 0, 0, 0.6f));
                a.Cooldown.type = Image.Type.Filled;
                a.Cooldown.fillMethod = Image.FillMethod.Radial360;
                a.Cooldown.fillOrigin = 2;
                a.Cooldown.fillClockwise = false;
                a.Timer = UIKit.LabelAt(rt, "", 26, Theme.Text, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size), TextAnchor.MiddleCenter, UIKit.TitleFont);
                UIKit.Shadow(a.Timer);
                var keyRt = UIKit.At(rt, "Key", new Vector2(0.5f, 0), new Vector2(0, -16), new Vector2(i == 0 ? 50 : 34, 28));
                UIKit.Image(keyRt, UIKit.RoundedSmall, new Color(0.9f, 0.92f, 1f, 0.95f));
                a.Key = UIKit.Label(keyRt, keys[i], 16, new Color(0.1f, 0.1f, 0.2f), TextAnchor.MiddleCenter, UIKit.BoldFont);
                a.Cost = UIKit.LabelAt(rt, "", 14, Theme.TextDim, new Vector2(0.5f, 1), new Vector2(0, 16), new Vector2(120, 18), TextAnchor.MiddleCenter, UIKit.BoldFont);
                a.Cost.supportRichText = true;
                a.Name = names[i];
                a.CostValue = i == 0 ? 0 : (int)(i == 1 ? GameConfig.DashCost : i == 2 ? GameConfig.PulseCost : GameConfig.DecoyCost);
                UIKit.Outline(a.Cost, new Color(0, 0, 0, 0.7f), 1.5f);
                _abilities[i] = a;
            }
        }

        private void BuildCenter()
        {
            var ch = UIKit.At(Root, "Crosshair", new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(34, 34));
            _crosshair = UIKit.Image(ch, Icons.Blaster, new Color(1, 1, 1, 0.85f));
            var hm = UIKit.At(Root, "HitMarker", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(46, 46));
            _hitMarker = UIKit.Image(hm, UIKit.Diamond, new Color(1, 0.3f, 0.3f, 0));
            hm.localRotation = Quaternion.Euler(0, 0, 45);

            var bannerRt = UIKit.At(Root, "Banner", new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(900, 90));
            _banner = UIKit.Label(bannerRt, "", 64, Color.white, TextAnchor.MiddleCenter, UIKit.TitleFont);
            UIKit.Outline(_banner, new Color(0.3f, 0.1f, 0.6f, 0.9f), 3);
            _bannerSub = UIKit.LabelAt(Root, "", 24, Theme.PurpleLight, new Vector2(0.5f, 1), new Vector2(0, -210), new Vector2(900, 34), TextAnchor.MiddleCenter, UIKit.BoldFont);
            UIKit.Shadow(_bannerSub);

            var promptRt = UIKit.At(Root, "Prompt", new Vector2(0.5f, 0), new Vector2(0, 200), new Vector2(760, 50));
            _prompt = UIKit.Label(promptRt, "", 22, Theme.Text, TextAnchor.MiddleCenter, UIKit.BoldFont);
            UIKit.Outline(_prompt, new Color(0, 0, 0, 0.7f), 2);
            _promptBar = new Bar(Root, new Vector2(0.5f, 0), new Vector2(0, 170), new Vector2(300, 14), Theme.Gold, new Color(0, 0, 0, 0.5f));

            var popupRt = UIKit.At(Root, "Popup", new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(700, 50));
            _popup = UIKit.Label(popupRt, "", 30, Theme.Gold, TextAnchor.MiddleCenter, UIKit.TitleFont);
            UIKit.Outline(_popup, new Color(0.2f, 0.1f, 0, 0.8f), 2);

            var respRt = UIKit.At(Root, "Respawn", new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(900, 120));
            _respawn = UIKit.Label(respRt, "", 48, Theme.Red, TextAnchor.MiddleCenter, UIKit.TitleFont);
            UIKit.Outline(_respawn, new Color(0, 0, 0, 0.8f), 3);
        }

        private void BuildOverlays()
        {
            var v = UIKit.Fill(Root, "DamageVignette");
            _vignette = UIKit.Image(v, VignetteSprite(), new Color(1, 0.1f, 0.15f, 0));
            var r = UIKit.Fill(Root, "RevealEdge");
            _revealEdge = UIKit.Image(r, VignetteSprite(), new Color(0.6f, 0.3f, 1f, 0));
        }

        private void BuildScoreboard()
        {
            _scoreboard = UIKit.At(Root, "Scoreboard", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640, 640));
            UIKit.Image(_scoreboard, UIKit.Rounded, new Color(0.05f, 0.06f, 0.14f, 0.94f));
            var title = UIKit.LabelAt(_scoreboard, "PLAYERS", 34, Theme.Text, new Vector2(0.5f, 1), new Vector2(0, -36), new Vector2(600, 40), TextAnchor.MiddleCenter, UIKit.TitleFont);
            _scoreboardText = UIKit.LabelAt(_scoreboard, "", 20, Theme.Text, new Vector2(0.5f, 1), new Vector2(0, -70), new Vector2(560, 560), TextAnchor.UpperLeft, UIKit.BodyFont);
            _scoreboardText.rectTransform.pivot = new Vector2(0.5f, 1);
            _scoreboardText.supportRichText = true;
            _scoreboard.gameObject.SetActive(false);
        }

        private ZoneMarker MakeZoneMarker(ZoneDef z)
        {
            var zm = new ZoneMarker();
            zm.Root = UIKit.At(_labels, "Zone_" + z.Name, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(170, 44));
            zm.Back = UIKit.Image(zm.Root, UIKit.Pill, new Color(0.06f, 0.07f, 0.15f, 0.75f));
            var ic = UIKit.At(zm.Root, "Icon", new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(34, 34));
            ic.pivot = new Vector2(0, 0.5f);
            UIKit.Image(ic, UIKit.Circle, Palette.ZoneColor(z.Type));
            var inner = UIKit.At(ic, "Glyph", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22, 22));
            zm.Icon = UIKit.Image(inner, Minimap.ZoneIcon(z.Type), new Color(0.06f, 0.06f, 0.12f));
            zm.Label = UIKit.LabelAt(zm.Root, z.Name.ToUpper(), 17, Theme.Text, new Vector2(0, 0.5f), new Vector2(46, 0), new Vector2(120, 40), TextAnchor.MiddleLeft, UIKit.BoldFont);
            zm.Label.rectTransform.pivot = new Vector2(0, 0.5f);
            zm.Label.supportRichText = true;
            return zm;
        }

        private Nameplate MakePlate()
        {
            var np = new Nameplate();
            np.Root = UIKit.At(_labels, "Plate", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160, 40));
            np.Name = UIKit.LabelAt(np.Root, "", 17, Theme.Text, new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(200, 22), TextAnchor.MiddleCenter, UIKit.BoldFont);
            UIKit.Outline(np.Name, new Color(0, 0, 0, 0.8f), 1.5f);
            np.Hp = new Bar(np.Root, new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(90, 9), Palette.Health, new Color(0, 0, 0, 0.6f));
            var mk = UIKit.At(np.Root, "Marker", new Vector2(0.5f, 1), new Vector2(0, 14), new Vector2(22, 22));
            np.Marker = UIKit.Image(mk, UIKit.Diamond, Theme.Purple);
            return np;
        }

        private static Sprite _vig;

        private static Sprite VignetteSprite()
        {
            if (_vig != null) return _vig;
            int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                    float d = Mathf.Sqrt(dx * dx * 0.8f + dy * dy * 0.8f);
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((d - 0.55f) / 0.6f)));
                }
            t.Apply();
            _vig = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
            return _vig;
        }

        // ------------------------------------------------------------------ events → feed / banners

        private void Feed(string text, Color c)
        {
            var t = UIKit.LabelAt(_feedRoot, text, 18, c, new Vector2(1, 1), Vector2.zero, new Vector2(420, 26), TextAnchor.MiddleRight, UIKit.BoldFont);
            t.rectTransform.pivot = new Vector2(1, 1);
            t.supportRichText = true;
            UIKit.Outline(t, new Color(0, 0, 0, 0.7f), 1.5f);
            _feed.Insert(0, (t, Time.time));
            while (_feed.Count > 6) { Object.Destroy(_feed[_feed.Count - 1].t.gameObject); _feed.RemoveAt(_feed.Count - 1); }
        }

        public void Banner(string title, string sub, float time = 3.2f)
        {
            _banner.text = title;
            _bannerSub.text = sub;
            _bannerT = time;
        }

        private void Popup(string text)
        {
            _popup.text = text;
            _popupT = 1.8f;
        }

        private string N(int id) => id == _m.LocalId ? "<color=#ffd84a>You</color>" : _m.NameOf(id);

        private void HandleEvent(SimEvent e)
        {
            switch (e.Type)
            {
                case EventType.Eliminated:
                    if (e.B == _m.LocalId) Feed(e.A >= 0 ? $"{N(e.A)} eliminated <color=#ff5a6a>You</color>" : "<color=#ff5a6a>You</color> were caught by the collapse", Theme.Text);
                    else if (e.A == _m.LocalId) { Feed($"You eliminated {N(e.B)}", Theme.Gold); Popup("+ELIMINATION"); _hitT = 0.4f; }
                    else Feed(e.A >= 0 ? $"{N(e.A)} eliminated {N(e.B)}" : $"{N(e.B)} fell to the collapse", Theme.TextDim);
                    break;
                case EventType.ZoneCaptured:
                    Feed($"{N(e.A)} captured the <color=#{ColorUtility.ToHtmlStringRGB(Palette.ZoneColor(_m.Map.Zones[e.B].Type))}>{_m.Map.Zones[e.B].Name}</color>", Theme.Text);
                    if (e.A == _m.LocalId) Popup($"{_m.Map.Zones[e.B].Name.ToUpper()} CAPTURED");
                    break;
                case EventType.VaultOpened:
                    Feed($"{N(e.A)} unlocked the <color=#ffc93a>Vault</color>!", Theme.Text);
                    if (e.A == _m.LocalId) Popup("VAULT UNLOCKED  +200");
                    break;
                case EventType.ObjectiveComplete:
                    Banner("OBJECTIVE COMPLETE", $"{ObjectiveState.Title((ObjectiveType)e.Value)}   +{(e.B == 1 ? GameConfig.PrimaryPoints : GameConfig.SecondaryPoints)}", 3f);
                    break;
                case EventType.AbilityPlay:
                    Popup(e.B == 99 ? $"FINAL TOWER  +{e.Value}" : e.B == 1 ? $"PULSE REVEAL  +{e.Value}" : $"DECOY FOOLED THEM  +{e.Value}");
                    break;
                case EventType.Hit:
                    if (e.A == _m.LocalId && e.B >= 0) _hitT = 0.25f;
                    if (e.B == _m.LocalId) _damageT = 0.6f;
                    break;
                case EventType.Revealed:
                    if (e.B == _m.LocalId) { _revealT = 2.5f; Feed($"{N(e.A)} <color=#b58cff>revealed</color> you with Pulse", Theme.Text); }
                    break;
                case EventType.Purchase:
                    Popup(e.B == 1 ? "SPEED BOOST" : e.B == 2 ? "SHIELD ONLINE" : "KEY PURCHASED");
                    break;
                case EventType.DecoyPop:
                    if (e.B == _m.LocalId) Feed("Your decoy vanished", Theme.TextDim);
                    break;
            }
        }

        private static string PhaseName(MatchPhase p)
        {
            switch (p)
            {
                case MatchPhase.Exploration: return "EXPLORATION";
                case MatchPhase.Competition: return "COMPETITION";
                case MatchPhase.Manipulation: return "MANIPULATION";
                case MatchPhase.Collapse: return "COLLAPSE";
                case MatchPhase.Final: return "FINAL MINUTE";
                default: return "MATCH OVER";
            }
        }

        private static string PhaseHint(MatchPhase p)
        {
            switch (p)
            {
                case MatchPhase.Exploration: return "Gather resources. Watch where everyone goes.";
                case MatchPhase.Competition: return "The Vault is open. Objectives start colliding.";
                case MatchPhase.Manipulation: return "Fake, bait, ambush. Read your opponents.";
                case MatchPhase.Collapse: return "The arena is closing in. Move to the center!";
                case MatchPhase.Final: return "Hold the Tower at 0:00 for +300. Tower points x2.";
                default: return "";
            }
        }

        // ------------------------------------------------------------------ update

        public void Update(float dt, float cameraYaw)
        {
            var s = _m.Latest;
            if (s == null) return;
            var me = _m.Predicted;

            // phase
            if (s.Phase != _lastPhase)
            {
                _lastPhase = s.Phase;
                Banner(PhaseName(s.Phase), PhaseHint(s.Phase), 4f);
                Audio.Sfx.Play(s.Phase == MatchPhase.Exploration ? Audio.Sfx.Start : Audio.Sfx.Beep, 0.8f);
            }
            float left = Mathf.Max(0, s.Duration - s.Time);
            _timer.text = $"{(int)left / 60}:{(int)left % 60:00}";
            _timer.color = left < 60 ? Color.Lerp(Theme.Red, Theme.Text, Mathf.PingPong(Time.time * 2, 1)) : Theme.Text;
            _players.text = $"{s.AliveCount}/{s.PlayerCount} Players";
            _phase.text = PhaseName(s.Phase);
            float ps = PhaseStart(s.Phase, s.Duration), pe = PhaseStart(s.Phase + 1, s.Duration);
            var pf = _phaseFill.rectTransform;
            pf.anchorMax = new Vector2(Mathf.Clamp01((s.Time - ps) / Mathf.Max(1, pe - ps)), 1);
            pf.offsetMin = Vector2.zero; pf.offsetMax = Vector2.zero;

            int score = me.Score.Total;
            _scoreShown = Mathf.MoveTowards(_scoreShown, score, Mathf.Max(20, Mathf.Abs(score - _scoreShown) * 4) * dt);
            _score.text = $"SCORE {Mathf.RoundToInt(_scoreShown):N0}";
            if (score > _lastScore + 1 && _lastScore > 0) _score.transform.localScale = Vector3.one * 1.15f;
            _lastScore = score;
            _score.transform.localScale = Vector3.Lerp(_score.transform.localScale, Vector3.one, 1 - Mathf.Exp(-8 * dt));

            // status line
            string status = "";
            if (me.ZoneId == _m.Map.Zone(ZoneType.Ruins).Id) status = "HIDDEN IN RUINS — others see you only up close";
            else if (me.ZoneId == _m.Map.Zone(ZoneType.Reactor).Id) status = "<color=#c78cff>EXPOSED</color> — the Reactor reveals you on every map";
            if (me.TowerSightT > 0) status = "<color=#5fd8ff>TOWER SIGHT</color> — you see every player";
            if (me.SpeedBuffT > 0) status += (status.Length > 0 ? "   " : "") + $"<color=#5fffb0>SPEED {me.SpeedBuffT:0}s</color>";
            _status.supportRichText = true;
            _status.text = status;

            // vitals
            _hp.Set(me.HealthFrac, dt);
            _hp.SetColor(me.HealthFrac < 0.3f ? Theme.Red : Palette.Health);
            _hpText.text = $"{Mathf.CeilToInt(me.Health)}{(me.Shield > 0 ? $" <color=#6fe8ff>+{Mathf.CeilToInt(me.Shield)}</color>" : "")}";
            _hpText.supportRichText = true;
            _en.Set(me.Energy / GameConfig.MaxEnergy, dt);
            _enText.text = Mathf.FloorToInt(me.Energy).ToString();
            _keys.text = $"{me.Keys}/{GameConfig.VaultKeys}";
            _keys.color = me.Keys >= GameConfig.VaultKeys ? Theme.Gold : Theme.Text;
            _cores.text = me.CoresCollected.ToString();

            // abilities
            SetAbility(_abilities[0], me.FireCd, GameConfig.FireCooldown, 0, me);
            SetAbility(_abilities[1], me.DashCd, GameConfig.DashCooldown, GameConfig.DashCost, me);
            SetAbility(_abilities[2], me.PulseCd, GameConfig.PulseCooldown, GameConfig.PulseCost, me);
            SetAbility(_abilities[3], me.DecoyCd, GameConfig.DecoyCooldown, GameConfig.DecoyCost, me);

            // objectives
            SetObjective(_objectives[0], me.Primary, me);
            SetObjective(_objectives[1], me.Secondary, me);

            // prompt + channel bar
            UpdatePrompt(me, s);

            // overlays
            _bannerT -= dt;
            float ba = Mathf.Clamp01(_bannerT * 2f);
            _banner.color = new Color(1, 1, 1, ba);
            _bannerSub.color = new Color(Theme.PurpleLight.r, Theme.PurpleLight.g, Theme.PurpleLight.b, ba);
            _banner.transform.localScale = Vector3.one * (1 + Mathf.Clamp01(_bannerT - 3.4f) * 0.8f);
            _popupT -= dt;
            _popup.color = new Color(Theme.Gold.r, Theme.Gold.g, Theme.Gold.b, Mathf.Clamp01(_popupT * 2));
            _popup.rectTransform.anchoredPosition = new Vector2(0, 120 + (1.8f - Mathf.Max(0, _popupT)) * 30);
            _hitT -= dt;
            _hitMarker.color = new Color(1, 0.35f, 0.35f, Mathf.Clamp01(_hitT * 4));
            _hitMarker.rectTransform.localScale = Vector3.one * (1 + Mathf.Clamp01(_hitT) * 0.6f);
            _damageT -= dt;
            float lowHp = me.Alive && me.HealthFrac < 0.3f ? 0.25f + Mathf.Sin(Time.time * 6) * 0.1f : 0;
            _vignette.color = new Color(1, 0.1f, 0.15f, Mathf.Max(Mathf.Clamp01(_damageT) * 0.7f, lowHp, s.Circle < me.Pos.Length ? 0.5f : 0));
            _revealT -= dt;
            _revealEdge.color = new Color(0.6f, 0.3f, 1f, Mathf.Clamp01(_revealT) * 0.6f);
            _crosshair.enabled = me.Alive && !_m.Driver.Autopilot;

            if (!me.Alive) _respawn.text = $"ELIMINATED\n<size=28><color=#ffffff>Respawning in {Mathf.Max(0, me.RespawnT):0.0}</color></size>";
            else _respawn.text = "";
            _respawn.supportRichText = true;

            // feed fade
            for (int i = _feed.Count - 1; i >= 0; i--)
            {
                var (t, time) = _feed[i];
                float age = Time.time - time;
                t.rectTransform.anchoredPosition = Vector2.Lerp(t.rectTransform.anchoredPosition, new Vector2(0, -i * 30), 1 - Mathf.Exp(-12 * dt));
                t.color = new Color(t.color.r, t.color.g, t.color.b, Mathf.Clamp01(7 - age));
                if (age > 7) { Object.Destroy(t.gameObject); _feed.RemoveAt(i); }
            }

            _minimap.Update(_m, _view, cameraYaw);
            UpdateWorldLabels(me, s);

            // scoreboard
            bool tab = _forceBoard || (Keyboard.current != null && Keyboard.current.tabKey.isPressed);
            _scoreboard.gameObject.SetActive(tab);
            if (tab) FillScoreboard(s);
        }

        private static float PhaseStart(MatchPhase p, float dur)
        {
            switch (p)
            {
                case MatchPhase.Exploration: return 0;
                case MatchPhase.Competition: return GameConfig.PhaseCompetition * dur;
                case MatchPhase.Manipulation: return GameConfig.PhaseManipulation * dur;
                case MatchPhase.Collapse: return GameConfig.PhaseCollapse * dur;
                case MatchPhase.Final: return GameConfig.PhaseFinal * dur;
                default: return dur;
            }
        }

        private void SetAbility(Ability a, float cd, float maxCd, float cost, PlayerState me)
        {
            bool affordable = me.Energy >= cost;
            a.Cooldown.fillAmount = cd > 0 ? cd / maxCd : (affordable ? 0 : 1);
            a.Timer.text = cd > 0.05f && maxCd > 1 ? Mathf.CeilToInt(cd).ToString() : "";
            a.Icon.color = new Color(a.Icon.color.r, a.Icon.color.g, a.Icon.color.b, cd <= 0 && affordable ? 1f : 0.45f);
            a.Cost.text = a.CostValue > 0 ? $"{a.Name} <color={(affordable ? "#3ee6ff" : "#ff5a6a")}>{a.CostValue}</color>" : a.Name;
        }

        private void SetObjective(ObjectiveCard c, ObjectiveState o, PlayerState me)
        {
            c.Title.text = ObjectiveState.Title(o.Type);
            c.Icon.sprite = ObjectiveIcon(o.Type);
            c.Desc.text = o.Describe(id => _m.NameOf(id));
            float frac = o.Type == ObjectiveType.VaultRaid && !o.Done ? Mathf.Min(me.Keys, GameConfig.VaultKeys) / (float)GameConfig.VaultKeys : o.Fraction;
            c.Bar.Set(o.Done ? 1 : frac, Time.deltaTime);
            string prog;
            switch (o.Type)
            {
                case ObjectiveType.TowerControl: prog = $"{Mathf.FloorToInt(o.Progress)}/{o.Target:0}s"; break;
                case ObjectiveType.VaultRaid: prog = o.Done ? "DONE" : $"{Mathf.Min(me.Keys, 3)}/3 keys"; break;
                case ObjectiveType.HighEnergy: prog = $"{Mathf.FloorToInt(me.Energy)}%"; break;
                default: prog = $"{Mathf.FloorToInt(o.Progress)}/{o.Target:0}"; break;
            }
            c.Progress.text = o.Done ? "DONE" : prog;
            c.Check.gameObject.SetActive(o.Done);
        }

        public static Sprite ObjectiveIcon(ObjectiveType t)
        {
            switch (t)
            {
                case ObjectiveType.TowerControl: return Icons.Tower;
                case ObjectiveType.CollectCores: return Icons.Core;
                case ObjectiveType.VaultRaid: return Icons.Key;
                case ObjectiveType.CaptureTwo: return Icons.Target;
                case ObjectiveType.HighEnergy: return Icons.Energy;
                default: return Icons.Skull;
            }
        }

        private void UpdatePrompt(PlayerState me, Snapshot s)
        {
            string text = "";
            float bar = -1;
            if (me.ZoneId >= 0 && me.Alive)
            {
                var def = _m.Map.Zones[me.ZoneId];
                var z = s.Zones[me.ZoneId];
                if (def.Type == ZoneType.Vault)
                {
                    if (z.Locked) text = "The Vault is sealed until the Competition phase";
                    else if (z.Cooldown > 0) text = $"Vault recharging… {z.Cooldown:0}s";
                    else if (me.Keys < GameConfig.VaultKeys) text = $"Bring {GameConfig.VaultKeys} keys to unlock the Vault  ({me.Keys}/{GameConfig.VaultKeys})";
                    else { text = "UNLOCKING VAULT… stay inside!"; bar = me.VaultChannel / GameConfig.VaultChannelTime; }
                }
                else if (def.Type == ZoneType.Market)
                {
                    bool mine = z.Controller == _m.LocalId;
                    float k = mine ? GameConfig.MarketDiscount : 1f;
                    text = $"MARKET  [1] Speed {GameConfig.MarketSpeedCost * k:0}   [2] Shield {GameConfig.MarketShieldCost * k:0}   [3] Key {GameConfig.MarketKeyCost * k:0}" + (mine ? "  (owner discount)" : "");
                }
                if (def.Capturable && z.Controller != _m.LocalId && text.Length == 0 || def.Capturable && z.Capturer == _m.LocalId && z.Progress > 0)
                {
                    if (z.Contested) text = $"{def.Name.ToUpper()} CONTESTED — drive them out!";
                    else if (z.Capturer == _m.LocalId) { text = $"CAPTURING {def.Name.ToUpper()}…"; bar = z.Progress; }
                }
                else if (def.Capturable && z.Controller == _m.LocalId && text.Length == 0) text = $"You control the {def.Name}";
            }
            _prompt.text = text;
            _promptBar.Root.gameObject.SetActive(bar >= 0);
            if (bar >= 0) _promptBar.Set(bar, 1);
        }

        private Vector2 _crossPos;

        /// <summary>Crosshair sits exactly where the next shot will land (projected onto the screen).</summary>
        public void PlaceCrosshair(float dt)
        {
            var rt = _crosshair.rectTransform;
            Vector2 target = Vector2.zero;
            if (_view.HasShotImpact)
            {
                Vector3 sp = _cam.WorldToScreenPoint(_view.ShotImpact);
                if (sp.z > 0) RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)Root, sp, null, out target);
            }
            _crossPos = Vector2.Lerp(_crossPos, target, 1 - Mathf.Exp(-30f * dt));
            rt.anchoredPosition = _crossPos;
            _hitMarker.rectTransform.anchoredPosition = _crossPos;
            _crosshair.color = _view.ShotHitsAvatar ? new Color(1f, 0.35f, 0.35f, 0.95f) : new Color(1, 1, 1, 0.85f);
        }

        private void UpdateWorldLabels(PlayerState me, Snapshot s)
        {
            var canvasRt = (RectTransform)Root;
            // nameplates
            _scratch.Clear();
            foreach (var kv in _plates) _scratch.Add(kv.Key);
            foreach (var kv in _view.Avatars)
            {
                var av = kv.Value;
                if (av.IsLocal || av.Fade < 0.3f || !av.Alive) continue;
                Vector3 world = av.Pos + Vector3.up * 2.65f;
                Vector3 sp = _cam.WorldToScreenPoint(world);
                if (sp.z < 0 || Vector3.Distance(av.Pos, _cam.transform.position) > 42) continue;
                if (!_plates.TryGetValue(av.AvatarId, out var np)) { np = MakePlate(); _plates[av.AvatarId] = np; }
                _scratch.Remove(av.AvatarId);
                np.Root.gameObject.SetActive(true);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, sp, null, out var lp);
                np.Root.anchoredPosition = lp;
                string name = _m.NameOf(av.OwnerId);
                np.Name.text = av.IsMyDecoy ? "Your Decoy" : name;
                np.Name.color = av.IsMyDecoy ? Theme.PurpleLight : Theme.Text;
                np.Hp.Set(av.Health01, Time.deltaTime);
                np.Hp.SetColor(av.Health01 < 0.3f ? Theme.Red : Palette.Health);
                bool nemesis = (me.Primary.Type == ObjectiveType.Nemesis && !me.Primary.Done && me.Primary.TargetPlayer == av.OwnerId) ||
                               (me.Secondary.Type == ObjectiveType.Nemesis && !me.Secondary.Done && me.Secondary.TargetPlayer == av.OwnerId);
                bool revealed = av.Vis == Visibility.Full && Vector3.Distance(av.Pos, new Vector3(me.Pos.X, 0, me.Pos.Y)) > GameConfig.VisionRadius;
                np.Marker.gameObject.SetActive(nemesis || revealed);
                np.Marker.color = nemesis ? Theme.Red : Theme.Purple;
                float dist = Vector3.Distance(av.Pos, _cam.transform.position);
                np.Root.localScale = Vector3.one * Mathf.Clamp(1.4f - dist / 45f, 0.6f, 1.1f);
            }
            foreach (int id in _scratch)
            {
                Object.Destroy(_plates[id].Root.gameObject);
                _plates.Remove(id);
            }

            // zone markers
            for (int i = 0; i < _zoneMarkers.Count; i++)
            {
                var def = _m.Map.Zones[i];
                var zm = _zoneMarkers[i];
                Vector3 world = Build.V(def.Center, def.Type == ZoneType.Tower ? 27f : 5.5f);
                Vector3 sp = _cam.WorldToScreenPoint(world);
                float dist = Vec2.Dist(def.Center, me.Pos);
                bool show = sp.z > 0 && dist > def.Radius * 0.8f;
                zm.Root.gameObject.SetActive(show);
                if (!show) continue;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, sp, null, out var lp);
                zm.Root.anchoredPosition = lp;
                var z = s.Zones[i];
                string owner = z.Controller < 0 ? "" : z.Controller == _m.LocalId ? " <color=#6bff6b>●</color>" : " <color=#ff6b6b>●</color>";
                zm.Label.text = $"{def.Name.ToUpper()}{owner} <size=13><color=#aab0d8>{dist:0}m</color></size>";
                zm.Root.localScale = Vector3.one * Mathf.Clamp(1.2f - dist / 160f, 0.65f, 1f);
            }
        }

        private void FillScoreboard(Snapshot s)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<color=#aab0d8>NAME                          STATUS</color>\n\n");
            foreach (var r in _m.Roster)
            {
                bool me = r.Id == _m.LocalId;
                bool seen = false;
                foreach (var a in s.Avatars) if (a.OwnerId == r.Id && a.Vis == Visibility.Full) seen = true;
                string status = me ? $"<color=#ffd84a>{_m.Predicted.Score.Total} pts</color>" : seen ? "<color=#ff8a8a>in sight</color>" : "<color=#6a6f90>unknown</color>";
                string name = me ? $"<color=#ffd84a>{r.Name}</color>" : r.Name;
                sb.Append($"{name,-30}{status}\n");
            }
            sb.Append("\n<color=#8a90b8>Other players' scores stay hidden until the match ends.</color>");
            _scoreboardText.text = sb.ToString();
        }
    }
}
