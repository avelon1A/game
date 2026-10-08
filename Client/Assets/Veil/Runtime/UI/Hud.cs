using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Veil.App;
using Veil.Match;
using Veil.Sim;
using EventType = Veil.Sim.EventType;
using Veil.View;
using Veil.Audio;

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
        private readonly ObjectiveCard[] _objectives = new ObjectiveCard[3];
        private readonly List<MateCard> _mates = new List<MateCard>();
        private Text _squadLabel;

        private sealed class MateCard
        {
            public RosterEntry Entry;
            public RectTransform Root;
            public Image Ring;
            public RawImage Face;
            public Bar Hp;
            public Text Name, State;
        }
        private readonly List<(Text t, float time)> _feed = new List<(Text, float)>();
        private RectTransform _feedRoot;
        private float _bannerT, _hitT, _damageT, _revealT, _popupT;
        private MatchPhase _lastPhase = (MatchPhase)255;
        private bool _overtimeShown;
        private HudExtras _extras;
        private readonly Transform canvasRoot;
        private RectTransform _cine, _barTop, _barBottom;
        private Text _cineTitle, _cineSub;

        /// <summary>Escape cinematic overlay: letterbox bars + the winner title (the HUD itself hides).</summary>
        private void BuildCinematic(Transform canvas)
        {
            _cine = UIKit.Fill(canvas, "Cinematic");
            _barTop = UIKit.Rect(_cine, "Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, Vector2.zero);
            UIKit.Image(_barTop, UIKit.Square, Color.black);
            _barBottom = UIKit.Rect(_cine, "Bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), Vector2.zero, Vector2.zero);
            UIKit.Image(_barBottom, UIKit.Square, Color.black);
            _cineTitle = UIKit.LabelAt(_cine, "", 86, Color.white, new Vector2(0.5f, 0), new Vector2(0, 150), new Vector2(1400, 110), TextAnchor.MiddleCenter, UIKit.TitleFont);
            _cineTitle.fontStyle = FontStyle.Italic;
            UIKit.Outline(_cineTitle, new Color(0.3f, 0.1f, 0.6f, 0.9f), 4);
            _cineSub = UIKit.LabelAt(_cine, "", 28, Theme.Yellow, new Vector2(0.5f, 0), new Vector2(0, 100), new Vector2(1200, 40), TextAnchor.MiddleCenter, UIKit.BoldFont);
            UIKit.Shadow(_cineSub);
            _cine.gameObject.SetActive(false);
        }

        private void UpdateCinematic()
        {
            float t = _view.EscapeT;
            bool on = t >= 0;
            if (_cine.gameObject.activeSelf != on) _cine.gameObject.SetActive(on);
            if (Root.gameObject.activeSelf == on) Root.gameObject.SetActive(!on);
            if (!on) return;
            if (_hackBtn != null && _hackBtn.gameObject.activeSelf) _hackBtn.gameObject.SetActive(false);
            if (_puzzle != null && _puzzle.Open) _puzzle.Close();
            float h = ((RectTransform)_cine).rect.height * 0.12f * Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / 0.8f));
            _barTop.sizeDelta = new Vector2(0, h);
            _barBottom.sizeDelta = new Vector2(0, h);
            bool mine = _view.EscapeSquad == _m.LocalSquad;
            float a = Mathf.Clamp01((t - 7.3f) / 0.8f);   // title over the last shot (the helicopter banking away)
            _cineTitle.text = mine ? "VICTORY" : $"SQUAD {(char)('A' + _view.EscapeSquad)} ESCAPED";
            _cineSub.text = mine ? "Your squad made it off Rilo Island" : "They made it off Rilo Island";
            _cineTitle.color = new Color(mine ? 1f : 1f, mine ? 0.85f : 1f, mine ? 0.3f : 1f, a);
            _cineSub.color = new Color(Theme.Yellow.r, Theme.Yellow.g, Theme.Yellow.b, Mathf.Clamp01((t - 7.9f) / 0.8f));
            _cineTitle.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, a);
        }
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
            public Text Title, Desc, Progress, Tag;
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
            canvasRoot = canvas;
            // phones: a quick tap on the minimap (under the camera look pad) opens the big map
            VirtualInput.Tap = pos =>
            {
                if (_minimap == null || _mapScreen == null || !RectTransformUtility.RectangleContainsScreenPoint(_minimap.Root, pos, null)) return false;
                _mapScreen.Toggle();
                return true;
            };
            Root = UIKit.Fill(canvas, "HUD");
            _labels = UIKit.Fill(Root, "WorldLabels");
            BuildOverlays();
            BuildTopLeft();
            BuildTopRight();
            BuildObjectives();
            if (GameConfig.ExtractionMode)
            {
                _hack = new HackPanel(Root, _m.LocalSquad) { HackerName = id => _m.NameOf(id) };
                // on the canvas, not under the HUD: phones put the touch controls (camera look pad) above the HUD,
                // which swallowed taps on HACK and on the puzzle wires
                _hackBtn = UIKit.Button(canvas, "HACK", new Vector2(0.5f, 0), new Vector2(0, 300), new Vector2(240, 80), UIKit.ButtonStyle.Primary, () => { _puzzle.Show(); _puzzle.Root.SetAsLastSibling(); }, 34);
                _puzzle = new CircuitPuzzle(canvas);
                _extractPanel = new ExtractPanel(Root, _m.LocalSquad, mobile);
            }
            _compass = new CompassBar(Root);
            // sniper scope overlay: dark vignette ring + fine cross lines
            _scope = UIKit.Fill(Root, "Scope");
            var ring = UIKit.At(_scope, "Ring", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2600, 2600));
            UIKit.Image(ring, UIKit.Ring, new Color(0, 0, 0, 0.88f));
            var inner = UIKit.At(_scope, "Inner", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 760));
            UIKit.Image(inner, UIKit.Ring, new Color(0, 0, 0, 0.9f));
            var h = UIKit.At(_scope, "H", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700, 2)); UIKit.Image(h, UIKit.Square, new Color(0, 0, 0, 0.7f));
            var v = UIKit.At(_scope, "V", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2, 700)); UIKit.Image(v, UIKit.Square, new Color(0, 0, 0, 0.7f));
            var dot = UIKit.At(_scope, "Dot", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8, 8)); UIKit.Image(dot, UIKit.Circle, new Color(1f, 0.25f, 0.3f, 0.95f));
            _scope.gameObject.SetActive(false);
            BuildSquad();
            BuildVitals();
            BuildAbilities();
            BuildCenter();
            BuildScoreboard();
            foreach (var z in m.Map.Zones) _zoneMarkers.Add(MakeZoneMarker(z));
            BuildCinematic(canvas);
            _extras = new HudExtras(Root, m, view, cam, _minimap, mobile, Feed, (a, b, t) => Banner(a, b, t), Popup);
            view.OnEvent += HandleEvent;
            view.OnEvent += _extras.HandleEvent;
            _mobile = mobile;
            if (mobile) ApplyMobileLayout();
        }

        public void Dispose()
        {
            _view.OnEvent -= HandleEvent;
            _view.OnEvent -= _extras.HandleEvent;
            VirtualInput.Tap = null;
            if (_hackBtn != null) Object.Destroy(_hackBtn.gameObject);
            if (_puzzle != null) Object.Destroy(_puzzle.Root.gameObject);
            _mapScreen?.Destroy();
            _extras.Dispose();
            Object.Destroy(_cine.gameObject);
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
            // left column, top to bottom: pills, phase, score, squad (3 mates), objectives — scaled so nothing overlaps
            Move("Squad", new Vector2(0, 1), new Vector2(24, -178), 0.72f);
            for (int i = 0; i < 3; i++) Move("Objective" + i, new Vector2(0, 1), new Vector2(24, -346 - i * 72), 0.72f);
            for (int i = 0; i < 4; i++) { var a = Root.Find("Ability" + i); if (a) a.gameObject.SetActive(false); }
            foreach (var t in Root.GetComponentsInChildren<Text>(true))
                if (t.text == "OBJECTIVES") { t.rectTransform.anchorMin = t.rectTransform.anchorMax = t.rectTransform.pivot = new Vector2(0, 0.5f); t.rectTransform.anchorMin = t.rectTransform.anchorMax = t.rectTransform.pivot = new Vector2(0, 1); t.rectTransform.anchoredPosition = new Vector2(28, -322); t.alignment = TextAnchor.MiddleLeft; }
            _status.rectTransform.anchoredPosition = new Vector2(190, -138);
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
            return UIKit.Fit(t, 12);
        }

        private void BuildTopLeft()
        {
            _players = Pill(new Vector2(24, -24), 220, Icons.Players, "15 Players", out _);
            _timer = Pill(new Vector2(254, -24), 128, Icons.Clock, "15:00", out _);
            var phaseRt = UIKit.At(Root, "Phase", new Vector2(0, 1), new Vector2(24, -82), new Vector2(358, 44));
            UIKit.Image(phaseRt, UIKit.RoundedSmall, new Color(0.07f, 0.08f, 0.16f, 0.7f));
            _phase = UIKit.LabelAt(phaseRt, "EXPLORATION", 18, Theme.PurpleLight, new Vector2(0, 1), new Vector2(14, -4), new Vector2(300, 24), TextAnchor.UpperLeft, UIKit.BoldFont);
            _phase.rectTransform.pivot = new Vector2(0, 1); UIKit.Fit(_phase);
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
            for (int i = 0; i < 3; i++)
            {
                var c = new ObjectiveCard();
                c.Root = UIKit.At(Root, "Objective" + i, new Vector2(1, 0.5f), new Vector2(-24, -114 - i * 102), new Vector2(380, 94));
                c.Root.pivot = new Vector2(1, 0.5f);
                UIKit.Image(c.Root, UIKit.Rounded, Theme.Panel);
                var iconBack = UIKit.At(c.Root, "IconBack", new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(58, 58));
                iconBack.pivot = new Vector2(0, 0.5f);
                UIKit.Image(iconBack, UIKit.Circle, i == 0 ? new Color(0.62f, 0.38f, 1f, 0.35f) : i == 2 ? new Color(0.4f, 1f, 0.45f, 0.22f) : new Color(1, 1, 1, 0.12f));
                var ic = UIKit.At(iconBack, "Icon", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34, 34));
                c.Icon = UIKit.Image(ic, Icons.Tower, Color.white);
                c.Title = UIKit.LabelAt(c.Root, "", 20, Theme.Text, new Vector2(0, 1), new Vector2(82, -10), new Vector2(260, 24), TextAnchor.UpperLeft, UIKit.BoldFont);
                c.Title.rectTransform.pivot = new Vector2(0, 1); c.Title.rectTransform.sizeDelta = new Vector2(250, 24); UIKit.Fit(c.Title);
                c.Desc = UIKit.LabelAt(c.Root, "", 15, Theme.TextDim, new Vector2(0, 1), new Vector2(82, -36), new Vector2(265, 22), TextAnchor.UpperLeft);
                c.Desc.rectTransform.pivot = new Vector2(0, 1); UIKit.Fit(c.Desc, 9);
                c.Bar = new Bar(c.Root, new Vector2(0, 0), new Vector2(82, 14), new Vector2(200, 12), i == 0 ? Theme.Purple : i == 2 ? Theme.Green : Theme.Cyan, new Color(1, 1, 1, 0.1f));
                c.Bar.Root.pivot = new Vector2(0, 0);
                c.Progress = UIKit.LabelAt(c.Root, "", 15, Theme.Text, new Vector2(0, 0), new Vector2(292, 10), new Vector2(80, 20), TextAnchor.LowerLeft, UIKit.BoldFont);
                c.Progress.rectTransform.pivot = new Vector2(0, 0); c.Progress.rectTransform.sizeDelta = new Vector2(80, 20); UIKit.Fit(c.Progress, 9);
                var chk = UIKit.At(c.Root, "Check", new Vector2(1, 1), new Vector2(-10, -10), new Vector2(26, 26));
                chk.pivot = new Vector2(1, 1);
                c.Check = UIKit.Image(chk, UIKit.Circle, Theme.Green);
                // tag sits under the progress bar so long titles never collide with it
                var tag = UIKit.LabelAt(c.Root, i == 0 ? "PRIMARY +500" : i == 2 ? $"SQUAD +{GameConfig.SquadObjectivePoints}" : "SECONDARY +250", 11, i == 0 ? Theme.PurpleLight : i == 2 ? Theme.Green : Theme.Cyan, new Vector2(0, 0), new Vector2(82, 0), new Vector2(200, 14), TextAnchor.LowerLeft, UIKit.BoldFont);
                tag.rectTransform.pivot = new Vector2(0, 0);
                c.Tag = tag;
                _objectives[i] = c;
            }
        }

        /// <summary>Squadmate cards under the top-left pills: portrait, name, HP, speaking ring, down state.</summary>
        private void BuildSquad()
        {
            var root = UIKit.At(Root, "Squad", new Vector2(0, 1), new Vector2(24, -210), new Vector2(270, 200));
            root.pivot = new Vector2(0, 1);
            _squadLabel = UIKit.LabelAt(root, $"SQUAD {(char)('A' + _m.LocalSquad)}", 16, Theme.Green, new Vector2(0, 1), new Vector2(2, 0), new Vector2(260, 22), TextAnchor.MiddleLeft, UIKit.BoldFont);
            _squadLabel.rectTransform.pivot = new Vector2(0, 1);
            UIKit.Shadow(_squadLabel);
            int i = 0;
            foreach (var r in _m.Squadmates())
            {
                var c = new MateCard { Entry = r };
                c.Root = UIKit.At(root, "Mate" + i, new Vector2(0, 1), new Vector2(0, -26 - i * 56), new Vector2(262, 50));
                c.Root.pivot = new Vector2(0, 1);
                UIKit.Image(c.Root, UIKit.RoundedSmall, new Color(0.07f, 0.08f, 0.16f, 0.72f));
                var ringRt = UIKit.At(c.Root, "Ring", new Vector2(0, 0.5f), new Vector2(3, 0), new Vector2(46, 46));
                ringRt.pivot = new Vector2(0, 0.5f);
                c.Ring = UIKit.Image(ringRt, UIKit.Ring, new Color(0.4f, 1f, 0.5f, 0));
                var face = UIKit.At(c.Root, "Face", new Vector2(0, 0.5f), new Vector2(7, 0), new Vector2(38, 38));
                face.pivot = new Vector2(0, 0.5f);
                UIKit.Image(face, UIKit.Circle, new Color(0.15f, 0.15f, 0.3f));
                var fr = UIKit.Fill(face, "Img", 2);
                c.Face = fr.gameObject.AddComponent<RawImage>();
                c.Face.texture = PortraitStudio.Get(r.Look);
                c.Face.raycastTarget = false;
                c.Name = UIKit.LabelAt(c.Root, r.Name, 16, Theme.Text, new Vector2(0, 1), new Vector2(54, -4), new Vector2(150, 20), TextAnchor.UpperLeft, UIKit.BoldFont);
                c.Name.rectTransform.pivot = new Vector2(0, 1); UIKit.Fit(c.Name);
                c.State = UIKit.LabelAt(c.Root, "", 13, Theme.TextDim, new Vector2(1, 1), new Vector2(-8, -5), new Vector2(90, 18), TextAnchor.UpperRight, UIKit.BoldFont);
                c.State.rectTransform.pivot = new Vector2(1, 1);
                c.State.supportRichText = true;
                c.Hp = new Bar(c.Root, new Vector2(0, 0), new Vector2(54, 10), new Vector2(196, 9), Palette.Health, new Color(1, 1, 1, 0.1f));
                c.Hp.Root.pivot = new Vector2(0, 0);
                _mates.Add(c);
                i++;
            }
        }

        private void UpdateSquad(Snapshot s, float dt)
        {
            var voice = GameApp.I != null ? GameApp.I.Voice : null;
            foreach (var c in _mates)
            {
                AvatarSnap a = null;
                foreach (var av in s.Avatars) if (av.AvatarId == c.Entry.Id) { a = av; break; }
                bool alive = a != null;   // allies are always in the snapshot while alive
                c.Hp.Set(alive ? a.Health01 : 0, dt);
                c.Face.color = alive ? Color.white : new Color(1, 1, 1, 0.3f);
                bool talking = voice != null && !string.IsNullOrEmpty(c.Entry.ProfileId) && voice.IsSpeaking(c.Entry.ProfileId);
                var rc = c.Ring.color;
                rc.a = Mathf.MoveTowards(rc.a, talking ? 1f : 0f, dt * 8f);
                c.Ring.color = rc;
                string st = !alive ? "<color=#ff7a8a>DOWN</color>" : c.Entry.IsBot ? "<color=#8a90b8>BOT</color>" : "";
                if (talking) st = "<color=#7dff9a>TALKING</color>";
                else if (voice != null && !string.IsNullOrEmpty(c.Entry.ProfileId) && voice.IsMuted(c.Entry.ProfileId)) st = "<color=#8a90b8>MUTED</color>";
                c.State.text = st;
            }
            if (voice != null && voice.LocalSpeaking) _squadLabel.text = $"SQUAD {(char)('A' + _m.LocalSquad)}  <color=#7dff9a>● YOU</color>";
            else _squadLabel.text = $"SQUAD {(char)('A' + _m.LocalSquad)}  <color=#aab0d8>{s.SquadTotal:N0} pts</color>";
            _squadLabel.supportRichText = true;
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

        private string SpectateHint => Application.isMobilePlatform ? "tap to switch" : "1-4 / click to switch";

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
                case EventType.Downed:
                    if (e.B == _m.LocalId) Feed(e.A >= 0 ? $"{N(e.A)} knocked <color=#ff5a6a>You</color> down" : "<color=#ff5a6a>You</color> are down", Theme.Text);
                    else if (e.A == _m.LocalId) { Feed($"You knocked down {N(e.B)}", Theme.Gold); Popup("KNOCKED DOWN"); _hitT = 0.4f; }
                    else if (_m.IsAlly(e.B)) Feed($"<color=#ff5a6a>{_m.NameOf(e.B)} is down</color> — revive them!", Theme.Text);
                    break;
                case EventType.Revived:
                    if (e.A == _m.LocalId) Popup($"REVIVED {_m.NameOf(e.B).ToUpper()}  +{GameConfig.RevivePoints}");
                    else if (e.B == _m.LocalId) Feed($"{N(e.A)} revived <color=#7dff9a>You</color>", Theme.Text);
                    else if (_m.IsAlly(e.B)) Feed($"{N(e.A)} revived {N(e.B)}", Theme.TextDim);
                    break;
                case EventType.StageComplete:
                {
                    string sq = $"Squad {(char)('A' + e.A)}";
                    string what = e.B < 3 ? $"objective {e.B + 1}/3" : "the <color=#ffc93a>Vault</color>";
                    if (e.A == _m.LocalSquad)
                    {
                        if (e.B == 0) Popup("ENEMY ACTIVITY DETECTED · nearby squads revealed");
                        Banner(e.B < 3 ? $"OBJECTIVE {e.B + 1} COMPLETE" : "VAULT OPENED", e.B < 2 ? $"Next: {TaskTitle(MatchSim.TaskOf(e.B + 1))}" : e.B == 2 ? "Next: open your squad's Vault" : "Get to the extraction!", 3f);
                        Sfx.Play(Sfx.Objective, 0.8f);
                    }
                    else Feed($"{sq} completed {what}", e.B >= 3 ? Theme.Red : Theme.TextDim);
                    break;
                }
                case EventType.HackGlitch:
                    Banner((e.Value == 1 ? "HOME " : "CENTRAL ") + (e.B <= 1 ? "TERMINAL INSTABILITY" : "SYSTEM FAILURE"),
                           e.B <= 1 ? "System requires stabilization — split up: destroy · stabilize · override" : "Nodes moved out past the bridges — split up!", 3.5f);
                    Sfx.Play(Sfx.HitMe, 0.6f);
                    break;
                case EventType.NodeDestroyed:
                {
                    string verb = (NodeKind)e.Value == NodeKind.Destroy ? "NODE DESTROYED" : (NodeKind)e.Value == NodeKind.Stabilize ? "NODE STABILIZED" : "NODE OVERRIDDEN";
                    if (e.B > 0) Popup($"{verb} · {e.B} LEFT");
                    else { Banner("TERMINAL STABILIZED", "Hack resumed", 2.2f); Sfx.Play(Sfx.Capture, 0.7f); }
                    break;
                }
                case EventType.HackActivity:
                    Feed($"<color=#ffd84a>TERMINAL ACTIVITY</color> · Squad {(char)('A' + e.A)} → {(e.B == 1 ? "home" : "CENTRE")} {e.Value}%", Theme.Text);
                    if (e.Value >= 50) Popup($"SQUAD {(char)('A' + e.A)} HACKING · {e.Value}%");
                    Sfx.Play(Sfx.Click, 0.5f);
                    break;
                case EventType.CenterBonus:
                    if (e.A == _m.LocalSquad) { Banner("CENTRAL TERMINAL HACKED", $"Bonus: all enemies revealed {GameConfig.CenterBonusReveal:0}s · +{GameConfig.CenterBonusEnergy:0} energy", 4f); Sfx.Play(Sfx.Objective, 0.9f); }
                    else Feed($"<color=#ffb057>Squad {(char)('A' + e.A)} hacked the CENTRAL terminal</color> — they can see you", Theme.Text);
                    break;
                case EventType.HackContested:
                    Popup(e.B == 1 ? "ENEMY TERMINAL CONTESTED" : "CENTRAL TERMINAL CONTESTED");
                    break;
                case EventType.ExtractRevealed:
                    Banner(e.A == _m.LocalSquad ? "HELICOPTER LOCATED" : "A SQUAD OPENED THEIR VAULT",
                        e.A == _m.LocalSquad ? $"Reach the extraction — hold it {GameConfig.ExtractTime:0} s to board and win"
                        : _m.Latest != null && _m.Latest.Stage >= 4 ? $"Squad {(char)('A' + e.A)} is racing to the helicopter — beat them there"
                        : $"Squad {(char)('A' + e.A)} knows where the helicopter is — open your Vault to find it", 4.5f);
                    Sfx.Play(Sfx.Capture, 0.9f);
                    break;
                case EventType.ExtractOpen:
                    Popup("EXTRACTION OPEN");
                    Sfx.Play(Sfx.Objective, 0.8f);
                    break;
                case EventType.ExtractAlert:
                    Feed(e.A == _m.LocalSquad ? $"<color=#7dff9a>Extracting · {e.Value}%</color>" : $"<color=#ff5a6a>Squad {(char)('A' + e.A)} extracting · {e.Value}%</color>", Theme.Text);
                    if (e.A != _m.LocalSquad && e.Value >= 50) Popup($"SQUAD {(char)('A' + e.A)} EXTRACTING · {e.Value}%");
                    Sfx.Play(Sfx.Click, 0.6f);
                    break;
                case EventType.ExtractFinal:
                    if (e.A == _m.LocalSquad) Banner("BOARDING", $"{GameConfig.ExtractTime * (1 - GameConfig.ExtractFinalAt):0} seconds — HOLD THE ZONE", 3f);
                    else Banner("BOARDING", $"Squad {(char)('A' + e.A)} is boarding the helicopter — STOP THEM!", 3f);
                    Sfx.Play(Sfx.Reveal, 1f);
                    break;
                case EventType.ExtractControl:
                    if (e.B == 1) Feed("<color=#ff5a6a>Extraction CONTESTED</color>", Theme.Text);
                    else if (e.A == _m.LocalSquad) { Feed("<color=#7dff9a>Your squad holds the extraction</color>", Theme.Text); }
                    else if (e.A >= 0) { Feed($"<color=#ff5a6a>Squad {(char)('A' + e.A)} took the extraction!</color>", Theme.Text); Popup($"SQUAD {(char)('A' + e.A)} IS EXTRACTING"); }
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
                    if (e.B == 2) { Banner("SQUAD OBJECTIVE COMPLETE", $"{ObjectiveState.Title((ObjectiveType)e.Value)}   +{GameConfig.SquadObjectivePoints}", 3f); break; }
                    Banner("OBJECTIVE COMPLETE", $"{ObjectiveState.Title((ObjectiveType)e.Value)}   +{(e.B == 1 ? GameConfig.PrimaryPoints : GameConfig.SecondaryPoints)}", 3f);
                    break;
                case EventType.AbilityPlay:
                    if (e.B == 3 || e.B == 4) break;   // squad combos: HudExtras
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
            UpdateCinematic();
            if (_view.EscapeT >= 0) return;
            var s = _m.Latest;
            if (s == null) return;
            var me = _m.Predicted;
            if (_mapScreen == null && _m.Map.Island)
            {
                _mapScreen = new MapScreen(canvasRoot, _m, _minimap);
                var tap = _minimap.Root.gameObject.AddComponent<Button>();
                tap.onClick.AddListener(() => _mapScreen.Toggle());
                _minimap.Root.GetComponent<Image>().raycastTarget = true;
            }
            if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame) _mapScreen?.Toggle();
            _mapScreen?.Update(_view);
            _compass.Update(s, me, cameraYaw);
            _extras.Update(dt, cameraYaw);
            if (_scope.gameObject.activeSelf != GameApp.Scoped) _scope.gameObject.SetActive(GameApp.Scoped);

            // phase
            if (s.Phase != _lastPhase)
            {
                _lastPhase = s.Phase;
                Banner(PhaseName(s.Phase), PhaseHint(s.Phase), 4f);
                Audio.Sfx.Play(s.Phase == MatchPhase.Exploration ? Audio.Sfx.Start : Audio.Sfx.Beep, 0.8f);
            }
            float left = Mathf.Max(0, s.Duration - s.Time);
            bool overtime = GameConfig.ExtractionMode && s.Time >= s.Duration;
            if (overtime && !_overtimeShown)
            {
                _overtimeShown = true;
                Banner("OVERTIME", "No time limit — open your Vault and extract to win", 5f);
                Audio.Sfx.Play(Audio.Sfx.Beep, 1f);
            }
            if (overtime)
            {
                _timer.text = "OVERTIME";
                _timer.color = Color.Lerp(Theme.Red, Theme.Gold, Mathf.PingPong(Time.time * 2, 1));
            }
            else
            {
                _timer.text = $"{(int)left / 60}:{(int)left % 60:00}";
                _timer.color = left < 60 ? Color.Lerp(Theme.Red, Theme.Text, Mathf.PingPong(Time.time * 2, 1)) : Theme.Text;
            }
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
            _abilities[0].Name = (s.Self.Fists ? "FISTS" : me.Look.Weapon == 1 ? "SNIPER" : "RIFLE") + (_mobile ? "" : "  X⇄");
            SetAbility(_abilities[0], me.FireCd, GameConfig.Current(me.Look.Weapon, s.Self.Fists).Cooldown, 0, me);
            SetAbility(_abilities[1], me.DashCd, GameConfig.DashCooldown, GameConfig.DashCost, me);
            SetAbility(_abilities[2], me.PulseCd, GameConfig.PulseCooldown, GameConfig.PulseCost, me);
            SetAbility(_abilities[3], me.DecoyCd, GameConfig.DecoyCooldown, GameConfig.DecoyCost, me);

            // objectives
            if (GameConfig.ExtractionMode)
            {
                SetChain(s, me);
                _hack?.Update(s, me, dt);
                _extractPanel?.Update(s, me);
                // keep banners clear of the extraction strip
                float by = _extractPanel != null && _extractPanel.Visible ? -(_extractPanel.Bottom + 60) : -150;
                ((RectTransform)_banner.transform.parent).anchoredPosition = new Vector2(0, by);
                _bannerSub.rectTransform.anchoredPosition = new Vector2(0, by - 60);
                // final phase: alarm every second + red pulsing screen edge for everyone
                if (s.ExtractFinal && s.Winner < 0)
                {
                    _sirenT -= dt;
                    if (_sirenT <= 0) { _sirenT = 1f; Sfx.Play(Sfx.Beep, 0.55f, s.ExtractController == _m.LocalSquad ? 1.3f : 0.8f); }
                }
                // HACK button: in a terminal ring, objective 1, and nobody from your squad is hacking that terminal yet
                bool homeT = Vec2.Dist(me.Pos, s.HomePos) <= Vec2.Dist(me.Pos, Vec2.Zero);
                bool inRing = homeT ? Vec2.Dist(me.Pos, s.HomePos) <= GameConfig.HomeHackRadius : me.Pos.Length <= GameConfig.HackRadius;
                int hk = homeT ? s.HomeHacker : s.CenterHacker;
                bool canHack = s.Stage == 0 && me.Alive && inRing && hk < 0 && !_puzzle.Open;
                if (_hackBtn.gameObject.activeSelf != canHack) { _hackBtn.gameObject.SetActive(canHack); if (canHack) _hackBtn.transform.SetAsLastSibling(); }
                if (canHack && Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame) { _puzzle.Show(); _puzzle.Root.SetAsLastSibling(); }
                if (_puzzle.Open && !_puzzleDebug && (!inRing || !me.Alive || s.Stage != 0)) _puzzle.Close();
                if (canHack) UIKit.ButtonLabel(_hackBtn).text = Application.isMobilePlatform ? "HACK" : "HACK  [F]";
                if (!_announced && s.Time > 1.5f) { _announced = true; Banner("OBJECTIVE 1/3 · HACK A TERMINAL", "Raid an ENEMY home terminal (20 s) or take the CENTRAL one (12 s + bonus) — you cannot hack your own", 5f); }
            }
            else
            {
                SetObjective(_objectives[0], me.Primary, me);
                SetObjective(_objectives[1], me.Secondary, me);
                SetObjective(_objectives[2], s.SquadObjective, me);
            }
            UpdateSquad(s, dt);

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
            if (s.ExtractFinal && s.Winner < 0) _vignette.color = new Color(1, 0.1f, 0.15f, Mathf.Max(_vignette.color.a, 0.18f + 0.22f * (0.5f + 0.5f * Mathf.Sin(Time.time * 6.3f))));
            _crosshair.enabled = me.Alive && !_m.Driver.Autopilot;

            if (!me.Alive) _respawn.text = $"ELIMINATED\n<size=28><color=#ffffff>Respawning in {Mathf.Max(0, me.RespawnT):0.0}</color></size>" +
                                           (_view.SpectateName != null ? $"\n<size=24><color=#7dff9a>Spectating {_view.SpectateName}</color>  <color=#aab0d8>· {SpectateHint}</color></size>" : "");
            else if (s.Self.Downed)
                _respawn.text = s.Self.ReviveProg > 0.01f
                    ? $"<color=#7dff9a>BEING REVIVED</color>\n<size=30><color=#ffffff>{Mathf.RoundToInt(s.Self.ReviveProg * 100)}%</color></size>"
                    : $"DOWNED\n<size=26><color=#ffffff>A squadmate can revive you · bleeding out in {Mathf.Max(0, s.Self.BleedT):0}s</color></size>";
            else if (s.Self.Reviving >= 0)
            {
                float rp = 0;
                foreach (var a in s.Avatars) if (a.OwnerId == s.Self.Reviving && a.AvatarId < 1000) rp = a.ReviveProg;
                _respawn.text = $"<color=#7dff9a>REVIVING {_m.NameOf(s.Self.Reviving).ToUpper()}</color>\n<size=30><color=#ffffff>{Mathf.RoundToInt(rp * 100)}%</color></size>";
            }
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

        // ------------------------------------------------------------------ extraction mode route

        public static string TaskTitle(ChainTask t) => t switch
        {
            ChainTask.Hack => "HACK TERMINAL",
            ChainTask.Capture => "CAPTURE ZONE",
            ChainTask.Collect => "COLLECT CORES",
            ChainTask.Vault => "SQUAD VAULT",
            _ => "EXTRACTION",
        };

        private static Sprite TaskIcon(ChainTask t) => t switch
        {
            ChainTask.Hack => Icons.Target,
            ChainTask.Capture => Icons.Tower,
            ChainTask.Collect => Icons.Core,
            ChainTask.Vault => Icons.Vault,
            _ => Icons.Trophy,
        };

        private static string StageName(int stage) => stage < 3 ? $"OBJ {stage + 1}/3" : stage == 3 ? "VAULT" : "EXTRACT";

        private void SetChain(Snapshot s, PlayerState me)
        {
            float dist = Vec2.Dist(me.Pos, s.Site);
            // card 0: the squad's current task
            var c = _objectives[0];
            var task = s.Task;
            if (s.Stage < 4)
            {
                c.Title.text = s.Stage < 3 ? $"OBJ {s.Stage + 1}/3 · {TaskTitle(task)}" : "OPEN YOUR VAULT";
                c.Desc.text = task switch
                {
                    ChainTask.Hack when s.Nodes.Count > 0 => $"<color=#ff5a8a>Instability: {s.Nodes.Count} node(s) to resolve</color>",
                    ChainTask.Hack => $"Enemy {Mathf.RoundToInt(s.HomeProg * 100)}% · {dist:0} m  |  Centre {Mathf.RoundToInt(s.CenterProg * 100)}% · {me.Pos.Length:0} m",
                    ChainTask.Capture => $"Hold your capture zone · {dist:0} m",
                    ChainTask.Collect => $"Pick up {GameConfig.CollectCores} energy cores as a squad",
                    _ => $"Channel at your squad's Vault · {dist:0} m",
                };
                c.Progress.text = task == ChainTask.Collect ? $"{Mathf.RoundToInt(s.StageProg * GameConfig.CollectCores)}/{GameConfig.CollectCores}" : $"{Mathf.RoundToInt(s.StageProg * 100)}%";
            }
            else
            {
                c.Title.text = "VAULT OPENED";
                c.Desc.text = "Reach the extraction and hold it";
                c.Progress.text = "DONE";
            }
            c.Desc.supportRichText = true;
            c.Icon.sprite = TaskIcon(task);
            c.Bar.Set(s.Stage < 4 ? s.StageProg : 1, Time.deltaTime);
            c.Check.gameObject.SetActive(s.Stage >= 4);
            c.Tag.text = "YOUR SQUAD";

            // card 1: the route — own progress dots + every rival squad's stage (public info)
            var r = _objectives[1];
            var dots = new System.Text.StringBuilder();
            for (int i = 0; i < 5; i++) dots.Append(i < s.Stage ? "<color=#7dff9a>●</color>" : i == s.Stage ? "<color=#ffd84a>●</color>" : "<color=#555a7a>○</color>");
            r.Title.text = "ROUTE  " + dots;
            r.Title.supportRichText = true;
            var rivals = new System.Text.StringBuilder();
            for (int q = 0; q < GameConfig.SquadCount; q++)
            {
                if (q == _m.LocalSquad) continue;
                if (rivals.Length > 0) rivals.Append("  ");
                rivals.Append($"{(char)('A' + q)}: {StageName(s.SquadStage[q])}");
            }
            r.Desc.text = rivals.ToString();
            r.Icon.sprite = Icons.Players;
            r.Bar.Set((s.Stage + (s.Stage < 4 ? s.StageProg : 0)) / 4f, Time.deltaTime);
            r.Progress.text = StageName(s.Stage);
            r.Check.gameObject.SetActive(false);
            r.Tag.text = "RIVAL SQUADS";

            // card 2: extraction
            var x = _objectives[2];
            x.Icon.sprite = Icons.Trophy;
            x.Title.text = "EXTRACTION";
            float mine = s.SquadExtract[_m.LocalSquad];
            int lead = -1; float leadP = 0;
            for (int q = 0; q < GameConfig.SquadCount; q++) if (s.SquadExtract[q] > leadP) { leadP = s.SquadExtract[q]; lead = q; }
            if (!s.ExtractRevealed) { x.Desc.text = s.ExtractActive ? "Open your Vault to see where it is" : "Open your Vault to reveal it"; x.Progress.text = "LOCKED"; }
            else
            {
                float de = Vec2.Dist(me.Pos, s.ExtractPos);
                string holder = s.ExtractLockT > 0 ? $"<color=#ffd84a>opens in {s.ExtractLockT:0}s</color>"
                    : s.ExtractContested ? "<color=#ff5a6a>CONTESTED</color>"
                    : s.ExtractController < 0 ? "nobody holding"
                    : s.ExtractController == _m.LocalSquad ? "<color=#7dff9a>YOU HOLD IT</color>" : $"<color=#ff5a6a>SQUAD {(char)('A' + s.ExtractController)} HOLDS IT</color>";
                x.Desc.text = $"{holder} · {de:0} m" + (s.Stage < 4 ? " · open the Vault to extract" : "");
                x.Desc.supportRichText = true;
                x.Progress.text = $"{Mathf.RoundToInt(mine * 100)}%";
            }
            x.Bar.Set(mine, Time.deltaTime);
            x.Check.gameObject.SetActive(false);
            x.Tag.text = lead >= 0 && lead != _m.LocalSquad ? $"LEADER: SQUAD {(char)('A' + lead)} {Mathf.RoundToInt(leadP * 100)}%" : "FIRST TO 100% WINS";
        }

        private void SetObjective(ObjectiveCard c, ObjectiveState o, PlayerState me)
        {
            c.Title.text = ObjectiveState.Title(o.Type);
            c.Icon.sprite = ObjectiveIcon(o.Type);
            c.Desc.text = o.Describe(id => _m.NameOf(id));
            float frac = o.Type == ObjectiveType.VaultRaid && !o.Done && !o.IsSquad ? Mathf.Min(me.Keys, GameConfig.VaultKeys) / (float)GameConfig.VaultKeys : o.Fraction;
            c.Bar.Set(o.Done ? 1 : frac, Time.deltaTime);
            string prog;
            switch (o.Type)
            {
                case ObjectiveType.TowerControl: prog = $"{Mathf.FloorToInt(o.Progress)}/{o.Target:0}s"; break;
                case ObjectiveType.VaultRaid: prog = o.Done ? "DONE" : o.IsSquad ? $"{Mathf.FloorToInt(o.Progress)}/{o.Target:0} vault" : $"{Mathf.Min(me.Keys, 3)}/3 keys"; break;
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
            if (me.ZoneId >= 0 && me.Alive && !(GameConfig.ExtractionMode && _m.Map.Zones[me.ZoneId].Type == ZoneType.Tower))
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
                    bool mine = z.Squad == _m.LocalSquad;
                    float k = mine ? GameConfig.MarketDiscount : 1f;
                    text = $"MARKET  [1] Speed {GameConfig.MarketSpeedCost * k:0}   [2] Shield {GameConfig.MarketShieldCost * k:0}   [3] Key {GameConfig.MarketKeyCost * k:0}" + (mine ? "  (owner discount)" : "");
                }
                if (def.Capturable && z.Squad != _m.LocalSquad && text.Length == 0 || def.Capturable && z.CapturerSquad == _m.LocalSquad && z.Progress > 0)
                {
                    if (z.Contested) text = $"{def.Name.ToUpper()} CONTESTED — drive them out!";
                    else if (z.CapturerSquad == _m.LocalSquad) { text = $"CAPTURING {def.Name.ToUpper()}…"; bar = z.Progress; }
                }
                else if (def.Capturable && z.Squad == _m.LocalSquad && text.Length == 0) text = $"Your squad controls the {def.Name}";
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

        private Text _wpSite, _wpExtract, _wpCentre;
        private HackPanel _hack;
        private ExtractPanel _extractPanel;
        private float _sirenT;
        private UnityEngine.UI.Button _hackBtn;
        private CircuitPuzzle _puzzle;
        public bool PuzzleOpen => _puzzle != null && _puzzle.Open;
        public void DebugShowPuzzle() { _puzzleDebug = true; _puzzle?.Show(); }
        public void DebugHidePuzzle() { _puzzleDebug = false; _puzzle?.Close(); }
        private bool _puzzleDebug;
        private CompassBar _compass;
        private RectTransform _scope;
        private MapScreen _mapScreen;
        public bool MapOpen => _mapScreen != null && _mapScreen.Open;
        private readonly Text[] _wpNodes = new Text[GameConfig.HackNodes];
        private bool _announced;

        /// <summary>Screen waypoint for a world point: follows it on screen, sticks to the screen edge when off-screen.</summary>
        private void Waypoint(ref Text t, bool show, Vec2 at, string label, Color c, PlayerState me)
        {
            if (t == null)
            {
                t = UIKit.LabelAt(Root, "", 20, Color.white, new Vector2(0, 0), Vector2.zero, new Vector2(260, 52), TextAnchor.MiddleCenter, UIKit.BoldFont);
                t.supportRichText = true;
                UIKit.Outline(t, new Color(0, 0, 0, 0.85f), 2);
            }
            t.gameObject.SetActive(show);
            if (!show) return;
            var canvasRt = (RectTransform)Root;
            Vector3 world = new Vector3(at.X, 3.5f, at.Y);
            Vector3 sp = _cam.WorldToScreenPoint(world);
            bool behind = sp.z < 0;
            if (behind) sp = new Vector3(Screen.width - sp.x, Screen.height - sp.y, 0);
            float mx = 90, my = 70;
            bool off = behind || sp.x < mx || sp.x > Screen.width - mx || sp.y < my || sp.y > Screen.height - my;
            if (off)
            {
                // push to the screen edge in the direction of the target
                Vector2 ctr = new Vector2(Screen.width / 2f, Screen.height / 2f);
                Vector2 dir = ((Vector2)sp - ctr);
                if (behind && dir.sqrMagnitude < 1) dir = Vector2.down;
                float k = Mathf.Min((Screen.width / 2f - mx) / Mathf.Max(1e-3f, Mathf.Abs(dir.x)), (Screen.height / 2f - my) / Mathf.Max(1e-3f, Mathf.Abs(dir.y)));
                sp = ctr + dir * k;
            }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, sp, null, out var lp);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            t.rectTransform.anchoredPosition = lp;
            t.text = $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>▼ {label}</color>\n<size=16>{Vec2.Dist(me.Pos, at):0} m</size>";
        }

        private void UpdateWorldLabels(PlayerState me, Snapshot s)
        {
            if (GameConfig.ExtractionMode)
            {
                Waypoint(ref _wpSite, s.Stage < 4 && s.Task != ChainTask.Collect && s.Nodes.Count == 0, s.Site, s.Stage == 0 ? "ENEMY TERMINAL" : s.Stage < 3 ? TaskTitle(s.Task) : "VAULT", new Color(1f, 0.82f, 0.3f), me);
                Waypoint(ref _wpCentre, s.Stage == 0 && s.Nodes.Count == 0, Vec2.Zero, "CENTRAL TERMINAL +BONUS", new Color(0.8f, 0.55f, 1f), me);
                for (int i = 0; i < _wpNodes.Length; i++)
                {
                    bool on = i < s.Nodes.Count;
                    var nd = on ? s.Nodes[i] : null;
                    Waypoint(ref _wpNodes[i], on, on ? nd.Pos : Vec2.Zero, on ? $"{HackPanel.KindName(nd.Kind)} {Mathf.RoundToInt(nd.Prog * 100)}%" : "", on ? HackPanel.KindColor(nd.Kind) : Color.white, me);
                }
                Waypoint(ref _wpExtract, s.ExtractRevealed && Vec2.Dist(me.Pos, s.ExtractPos) > GameConfig.ExtractRadius, s.ExtractPos, "EXTRACTION",
                    s.ExtractContested || (s.ExtractController >= 0 && s.ExtractController != _m.LocalSquad) ? new Color(1f, 0.35f, 0.35f) : s.ExtractController == _m.LocalSquad ? new Color(0.45f, 1f, 0.55f) : Color.white, me);
            }
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
                bool ally = _m.IsAlly(av.OwnerId);
                bool decoy = av.AvatarId >= 1000;
                np.Name.text = av.IsMyDecoy ? "Your Decoy" : ally && decoy ? $"{name}'s decoy" : name;
                np.Name.supportRichText = true;
                if (av.Downed) np.Name.text += ally ? (av.ReviveProg > 0.01f ? $"  <color=#7dff9a>REVIVING {Mathf.RoundToInt(av.ReviveProg * 100)}%</color>" : "  <color=#ff5a6a>DOWNED · go revive</color>") : "  <color=#ff5a6a>DOWNED</color>";
                np.Name.color = av.IsMyDecoy || (ally && decoy) ? Theme.PurpleLight : ally ? Theme.Green : Theme.Text;
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
                string owner = z.Squad < 0 ? "" : z.Squad == _m.LocalSquad ? " <color=#6bff6b>●</color>" : $" <color=#ff6b6b>● {(char)('A' + z.Squad)}</color>";
                zm.Label.text = $"{def.Name.ToUpper()}{owner} <size=13><color=#aab0d8>{dist:0}m</color></size>";
                zm.Root.localScale = Vector3.one * Mathf.Clamp(1.2f - dist / 160f, 0.65f, 1f);
            }
        }

        private void FillScoreboard(Snapshot s)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<color=#aab0d8>NAME                          STATUS</color>\n");
            for (int sq = 0; sq < GameConfig.SquadCount; sq++)
            {
                int squad = (sq + _m.LocalSquad) % GameConfig.SquadCount;   // your squad first
                bool mine = squad == _m.LocalSquad;
                sb.Append(mine ? $"\n<color=#7dff9a>SQUAD {(char)('A' + squad)} (yours) · {s.SquadTotal} pts</color>\n" : $"\n<color=#c7a6ff>SQUAD {(char)('A' + squad)}</color>\n");
                foreach (var r in _m.Roster)
                {
                    if (r.Squad != squad) continue;
                    bool me = r.Id == _m.LocalId;
                    bool seen = false;
                    foreach (var a in s.Avatars) if (a.OwnerId == r.Id && a.Vis == Visibility.Full) seen = true;
                    string status = me ? $"<color=#ffd84a>{_m.Predicted.Score.Total} pts</color>" : mine ? (seen ? "<color=#7dff9a>alive</color>" : "<color=#ff7a8a>down</color>") : seen ? "<color=#ff8a8a>in sight</color>" : "<color=#6a6f90>unknown</color>";
                    string name = me ? $"<color=#ffd84a>{r.Name}</color>" : r.Name;
                    sb.Append($"  {name,-28}{status}\n");
                }
            }
            sb.Append("\n<color=#8a90b8>Enemy scores stay hidden until the match ends.</color>");
            _scoreboardText.text = sb.ToString();
        }
    }
}
