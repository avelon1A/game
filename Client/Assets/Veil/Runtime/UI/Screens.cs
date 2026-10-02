using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Veil.App;
using Veil.Audio;
using Veil.Net;
using Veil.Sim;
using Veil.View;

namespace Veil.UI
{
    public abstract class ScreenBase
    {
        public readonly RectTransform Root;
        protected readonly GameApp App;

        protected ScreenBase(RectTransform canvas, GameApp app, string name)
        {
            App = app;
            Root = UIKit.Fill(canvas, name);
        }

        public virtual void Show(bool v) => Root.gameObject.SetActive(v);
        public virtual void Update(float dt) { }

        protected static Text Logo(Transform parent, Vector2 anchor, Vector2 pos, int size)
        {
            var t = UIKit.LabelAt(parent, "VEIL", size, Color.white, anchor, pos, new Vector2(size * 3f, size * 1.2f), TextAnchor.MiddleLeft, UIKit.TitleFont);
            t.fontStyle = FontStyle.Italic;
            UIKit.Outline(t, new Color(0.45f, 0.2f, 0.95f, 1f), size / 22f);
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0.15f, 0.05f, 0.35f, 0.9f);
            sh.effectDistance = new Vector2(size / 18f, -size / 18f);
            return t;
        }
    }

    // ====================================================================== TITLE

    public sealed class TitleScreen : ScreenBase
    {
        private readonly Text _press;

        public TitleScreen(RectTransform canvas, GameApp app) : base(canvas, app, "Title")
        {
            var shade = UIKit.Fill(Root, "Shade");
            UIKit.Image(shade, UIKit.GradientH, new Color(0.05f, 0.03f, 0.18f, 0.75f));
            var logo = Logo(Root, new Vector2(0, 0.5f), new Vector2(150, 90), 250);
            logo.rectTransform.pivot = new Vector2(0, 0.5f);
            var sub1 = UIKit.LabelAt(Root, "4 SQUADS  ·  4 PLAYERS EACH", 40, Color.white, new Vector2(0, 0.5f), new Vector2(170, -70), new Vector2(900, 50), TextAnchor.MiddleLeft, UIKit.BoldFont);
            sub1.rectTransform.pivot = new Vector2(0, 0.5f);
            UIKit.Shadow(sub1, 3);
            var sub2 = UIKit.LabelAt(Root, "INFINITE DECISIONS.", 40, Theme.PurpleLight, new Vector2(0, 0.5f), new Vector2(170, -118), new Vector2(900, 50), TextAnchor.MiddleLeft, UIKit.BoldFont);
            sub2.rectTransform.pivot = new Vector2(0, 0.5f);
            UIKit.Outline(sub2, new Color(0.12f, 0.05f, 0.3f, 0.9f), 2.5f);
            _press = UIKit.LabelAt(Root, "CLICK OR PRESS ANY KEY", 30, Theme.Yellow, new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(900, 40), TextAnchor.MiddleCenter, UIKit.TitleFont);
            UIKit.Shadow(_press, 3);
            var foot = UIKit.LabelAt(Root, "Observe → Predict → Decide → Execute → Adapt", 20, Theme.TextDim, new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(900, 30), TextAnchor.MiddleCenter, UIKit.BodyFont);
            UIKit.Shadow(foot);
        }

        public override void Update(float dt)
        {
            _press.color = new Color(Theme.Yellow.r, Theme.Yellow.g, Theme.Yellow.b, 0.55f + Mathf.Sin(Time.time * 3f) * 0.45f);
        }
    }

    // ====================================================================== MENU (lobby-style, tabs)

    public sealed class MenuScreen : ScreenBase
    {
        public int Tab { get; private set; }
        private readonly RectTransform[] _tabs = new RectTransform[4];
        private readonly Text[] _tabLabels = new Text[5];   // PLAY, CHARACTERS, LEADERBOARD, FRIENDS, SETTINGS
        private readonly float[] _tabX = new float[5];
        private readonly float[] _tabW = new float[5];
        private readonly Image _tabUnderline;
        private Vector2 _ulNow, _ulTarget;   // underline x, width
        private int _lastBadge;
        private Text _profileName, _profileStatus, _profileLevel, _friendsBadge;
        private RawImage _profileFace;
        private Image _friendsBadgeBg;

        // play tab
        public SquadPanel Squad { get; private set; }
        public FriendsDrawer Friends { get; private set; }

        // characters tab
        private InputField _nameField;

        // leaderboard
        private Text _board, _myStats;

        public MenuScreen(RectTransform canvas, GameApp app) : base(canvas, app, "Menu")
        {
            // top bar
            var bar = UIKit.Rect(Root, "TopBar", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 88));
            UIKit.Image(bar, UIKit.Square, new Color(0.04f, 0.04f, 0.12f, 0.9f));
            var edge = UIKit.Rect(bar, "Edge", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(0, 2));
            UIKit.Image(edge, UIKit.Square, new Color(0.55f, 0.35f, 1f, 0.6f));
            var logo = Logo(bar, new Vector2(0, 0.5f), new Vector2(34, 0), 66);
            logo.rectTransform.pivot = new Vector2(0, 0.5f);

            string[] names = { "PLAY", "CHARACTERS", "LEADERBOARD", "FRIENDS", "SETTINGS" };
            float[] widths = { 120, 200, 210, 150, 160 };
            float x = 250;
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                _tabX[i] = x; _tabW[i] = widths[i];
                var b = UIKit.Button(bar, names[i], new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(widths[i], 60), UIKit.ButtonStyle.Tab, () => OnTabButton(idx), 22);
                ((RectTransform)b.transform).pivot = new Vector2(0, 0.5f);
                _tabLabels[i] = UIKit.ButtonLabel(b);
                x += widths[i] + 8;
            }
            var ul = UIKit.At(bar, "Underline", new Vector2(0, 0), new Vector2(250, 6), new Vector2(120, 5));
            ul.pivot = new Vector2(0, 0);
            _tabUnderline = UIKit.Image(ul, UIKit.Pill, Theme.Yellow);

            // profile card (portrait, name, online status, level)
            var card = UIKit.At(bar, "Profile", new Vector2(1, 0.5f), new Vector2(-20, 0), new Vector2(300, 70));
            card.pivot = new Vector2(1, 0.5f);
            var cardBg = UIKit.Image(card, UIKit.RoundedSmall, new Color(1, 1, 1, 0.07f), true);
            var cardBtn = card.gameObject.AddComponent<Button>();   // tap your profile → Settings (account / Google sign-in)
            cardBtn.targetGraphic = cardBg;
            cardBtn.onClick.AddListener(() => { Sfx.Play(Sfx.Click, 0.5f); OnTabButton(4); });
            var faceRt = UIKit.At(card, "Face", new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(56, 56));
            faceRt.pivot = new Vector2(0, 0.5f);
            UIKit.Image(faceRt, UIKit.RoundedSmall, new Color(0.14f, 0.13f, 0.3f));
            var fi = UIKit.Fill(faceRt, "Img", 3);
            _profileFace = fi.gameObject.AddComponent<RawImage>();
            _profileFace.raycastTarget = false;
            _profileName = UIKit.LabelAt(card, "", 20, Theme.Text, new Vector2(0, 1), new Vector2(74, -8), new Vector2(150, 28), TextAnchor.MiddleLeft, UIKit.BoldFont);
            _profileName.rectTransform.pivot = new Vector2(0, 1);
            _profileStatus = UIKit.LabelAt(card, "", 15, Theme.Green, new Vector2(0, 0), new Vector2(74, 8), new Vector2(150, 24), TextAnchor.MiddleLeft, UIKit.BoldFont);
            _profileStatus.rectTransform.pivot = new Vector2(0, 0);
            _profileStatus.supportRichText = true;
            var lvRt = UIKit.At(card, "Lv", new Vector2(1, 0.5f), new Vector2(-10, -10), new Vector2(64, 28));
            lvRt.pivot = new Vector2(1, 0.5f);
            UIKit.Image(lvRt, UIKit.Pill, new Color(0, 0, 0, 0.45f));
            _profileLevel = UIKit.Label(lvRt, "", 15, Theme.Text, TextAnchor.MiddleCenter, UIKit.BoldFont);

            // friends icon (with a badge for requests + invites)
            var fRt = UIKit.At(bar, "FriendsIcon", new Vector2(1, 0.5f), new Vector2(-336, 0), new Vector2(60, 60));
            fRt.pivot = new Vector2(1, 0.5f);
            var fbg = UIKit.Image(fRt, UIKit.Circle, new Color(1, 1, 1, 0.07f), true);
            var fbtn = fRt.gameObject.AddComponent<Button>(); fbtn.targetGraphic = fbg;
            fbtn.onClick.AddListener(() => { Sfx.Play(Sfx.Click, 0.5f); OnTabButton(3); });
            var fic = UIKit.At(fRt, "I", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34, 34));
            UIKit.Image(fic, Icons.Players, Theme.Text);
            var badge = UIKit.At(fRt, "Badge", new Vector2(1, 1), new Vector2(2, 2), new Vector2(26, 26));
            badge.pivot = new Vector2(1, 1);
            _friendsBadgeBg = UIKit.Image(badge, UIKit.Circle, Theme.Red);
            _friendsBadge = UIKit.Label(badge, "", 14, Color.white, TextAnchor.MiddleCenter, UIKit.BoldFont);

            for (int i = 0; i < 4; i++) _tabs[i] = UIKit.Fill(Root, "Tab" + i);
            BuildPlay(_tabs[0]);
            BuildCharacters(_tabs[1]);
            BuildLeaderboard(_tabs[2]);
            BuildSettings(_tabs[3]);
            ((RectTransform)bar.transform).SetAsLastSibling();

            Friends = new FriendsDrawer(_tabs[0], app);
            Squad.OpenFriends = () => { Friends.Show(true); Underline(3); };
            App.Gateway.Changed += RefreshFriendsBadge;
            App.Gateway.Changed += RefreshProfileChip;
        }

        public override void Show(bool v)
        {
            base.Show(v);
            if (v)
            {
                Squad.OnShow();
                RefreshProfileChip();
                RefreshFriendsBadge();
            }
        }

        private void OnTabButton(int button)
        {
            if (button == 3) { SelectTab(0); Friends.Show(!Friends.Visible || Tab != 0); Underline(Friends.Visible ? 3 : 0); return; }
            SelectTab(button == 4 ? 3 : button);
        }

        private void Underline(int button)
        {
            for (int k = 0; k < _tabLabels.Length; k++) _tabLabels[k].color = k == button ? Color.white : Theme.TextDim;
            _ulTarget = new Vector2(_tabX[button], _tabW[button]);   // glides there in Update
            if (_ulNow.y <= 0) _ulNow = _ulTarget;
            PunchFx.On(_tabLabels[button]).Kick(0.12f);
        }

        public void SelectTab(int i)
        {
            Tab = i;
            for (int k = 0; k < 4; k++) _tabs[k].gameObject.SetActive(k == i);
            if (i != 0 && Friends != null) Friends.Show(false);
            Underline(i == 3 ? 4 : i);
            if (i == 2) FetchLeaderboard();
            // the stage shows your squad on PLAY, the character lineup elsewhere
            // painted lobby everywhere: your squad on PLAY, only your hero on Characters / Leaderboard (hidden behind Settings)
            if (i == 0) Squad?.ForceStage(); else App.Stage.SoloPose(App.Profile.Look, visible: i != 3);
        }

        private void RefreshProfileChip()
        {
            if (_profileName == null) return;
            var op = App.OnlineProfile;
            var g = App.Gateway;
            _profileFace.texture = PortraitStudio.Get(App.Profile.Look);
            _profileName.text = App.Profile.Name;
            _profileStatus.text = g.Online ? "<color=#7dff9a>●</color> Online" : "<color=#8a90b8>● Offline</color>";
            _profileLevel.text = op != null ? $"Lv. {op.level}" : "Lv. 1";
        }

        private void RefreshFriendsBadge()
        {
            if (_friendsBadge == null) return;
            var g = App.Gateway;
            int n = g.Friends.incoming.Count + g.Invites.Count;
            _friendsBadge.text = n > 9 ? "9+" : n.ToString();
            _friendsBadgeBg.gameObject.SetActive(n > 0);
            if (n > _lastBadge) PunchFx.On(_friendsBadgeBg).Kick(0.4f);
            _lastBadge = n;
        }

        // ------------------------------------------------------------------ PLAY tab

        private void BuildPlay(RectTransform tab)
        {
            Squad = new SquadPanel(tab, App);
        }

        public override void Update(float dt)
        {
            Squad.Update(dt);
            Friends.Update(dt);
            _ulNow = Vector2.Lerp(_ulNow, _ulTarget, 1 - Mathf.Exp(-16f * Time.unscaledDeltaTime));
            _tabUnderline.rectTransform.anchoredPosition = new Vector2(_ulNow.x, 6);
            _tabUnderline.rectTransform.sizeDelta = new Vector2(_ulNow.y, 5);
        }

        // ------------------------------------------------------------------ CHARACTERS tab

        private void BuildCharacters(RectTransform tab)
        {
            var panel = UIKit.Panel(tab, "Customize", new Vector2(1, 0.5f), new Vector2(-40, -40), new Vector2(760, 820));
            EnterFx.Add(panel, new Vector2(56, 0));
            var p = panel.transform;
            var title = UIKit.LabelAt(p, "CHARACTER", 34, Theme.Text, new Vector2(0, 1), new Vector2(30, -26), new Vector2(500, 40), TextAnchor.MiddleLeft, UIKit.TitleFont);
            title.rectTransform.pivot = new Vector2(0, 1);
            var note = UIKit.LabelAt(p, CharacterRig.HasModel(0) ? "Pick your hero — cosmetic only, no gameplay advantage. Right-drag to rotate." : "Cosmetic only — no gameplay advantage. Right-drag the character to rotate.", 16, Theme.TextDim, new Vector2(0, 1), new Vector2(30, -68), new Vector2(700, 24), TextAnchor.MiddleLeft, UIKit.BodyFont);
            note.rectTransform.pivot = new Vector2(0, 1);

            var prof = App.Profile;
            _nameField = Widgets.InputRow(p, new Vector2(0, 1), new Vector2(30, -130), "NAME", prof.Name, v =>
            {
                var clean = v.Trim();
                if (clean.Length > 0) { prof.Name = clean.Length > 16 ? clean.Substring(0, 16) : clean; prof.Save(); }
            });
            ((RectTransform)_nameField.transform.parent).pivot = new Vector2(0, 0.5f);

            void Changed() { prof.Save(); App.Stage.UpdateLook(prof.Look); }
            var outfitNames = new string[Palette.Outfits.Length];
            for (int i = 0; i < outfitNames.Length; i++) outfitNames[i] = Palette.Outfits[i].Name.ToUpper();
            var r1 = new ChipRow(p, new Vector2(0, 1), new Vector2(30, -210), "HERO", outfitNames, prof.Look.Outfit, i => { var l = prof.Look; l.Outfit = (byte)i; prof.Look = l; Changed(); }, 108, 100);
            r1.Root.pivot = new Vector2(0, 0.5f);
            var hairNames = new string[Palette.HairNames.Length];
            for (int i = 0; i < hairNames.Length; i++) hairNames[i] = Palette.HairNames[i].ToUpper();
            var r2 = new ChipRow(p, new Vector2(0, 1), new Vector2(30, -290), "HAIR", hairNames, prof.Look.Hair, i => { var l = prof.Look; l.Hair = (byte)i; prof.Look = l; Changed(); }, 104);
            r2.Root.pivot = new Vector2(0, 0.5f);
            var r3 = new SwatchRow(p, new Vector2(0, 1), new Vector2(30, -370), "HAIR COLOR", Palette.HairColors, prof.Look.HairColor, i => { var l = prof.Look; l.HairColor = (byte)i; prof.Look = l; Changed(); });
            var accNames = new string[Palette.AccessoryNames.Length];
            for (int i = 0; i < accNames.Length; i++) accNames[i] = Palette.AccessoryNames[i].ToUpper();
            var r4 = new ChipRow(p, new Vector2(0, 1), new Vector2(30, -450), "ACCESSORY", accNames, prof.Look.Accessory, i => { var l = prof.Look; l.Accessory = (byte)i; prof.Look = l; Changed(); }, 104);
            r4.Root.pivot = new Vector2(0, 0.5f);
            var r5 = new SwatchRow(p, new Vector2(0, 1), new Vector2(30, -530), "GLOW COLOR", Palette.AccentColors, prof.Look.Color, i => { var l = prof.Look; l.Color = (byte)i; prof.Look = l; Changed(); });

            var presetsT = UIKit.LabelAt(p, "PRESETS", 20, Theme.TextDim, new Vector2(0, 1), new Vector2(30, -620), new Vector2(170, 40), TextAnchor.MiddleLeft, UIKit.BoldFont);
            presetsT.rectTransform.pivot = new Vector2(0, 0.5f);
            var presetButtons = new List<GameObject>();
            for (int i = 0; i < Palette.Outfits.Length; i++)
            {
                int idx = i;
                var b = UIKit.Button(p, Palette.Outfits[i].Name.ToUpper(), new Vector2(0, 1), new Vector2(200 + i * 90, -620), new Vector2(84, 44), UIKit.ButtonStyle.Secondary, () =>
                {
                    prof.Look = Appearance.Preset(idx);
                    prof.Save();
                    App.Stage.UpdateLook(prof.Look);
                    // rebuild tab to refresh selections
                    Object.Destroy(tab.GetChild(0).gameObject);
                    BuildCharacters(tab);
                }, 15);
                ((RectTransform)b.transform).pivot = new Vector2(0, 0.5f);
                presetButtons.Add(b.gameObject);
            }
            if (CharacterRig.HasModel(0))
            {
                // model heroes: hair / accessories only apply to the old procedural characters, HERO already picks the preset
                foreach (var n in new[] { "Chips_HAIR", "Swatches_HAIR COLOR", "Chips_ACCESSORY" }) { var t = p.Find(n); if (t) t.gameObject.SetActive(false); }
                var glowRow = p.Find("Swatches_GLOW COLOR") as RectTransform;
                if (glowRow) glowRow.anchoredPosition = new Vector2(glowRow.anchoredPosition.x, -300);
                presetsT.gameObject.SetActive(false);
                foreach (var b in presetButtons) b.SetActive(false);
            }
            var play = UIKit.Button(p, "PLAY", new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(680, 84), UIKit.ButtonStyle.Primary, () => SelectTab(0), 44);
        }

        // ------------------------------------------------------------------ LEADERBOARD tab

        private void BuildLeaderboard(RectTransform tab)
        {
            var panel = UIKit.Panel(tab, "Board", new Vector2(1, 0.5f), new Vector2(-40, -40), new Vector2(760, 820));
            EnterFx.Add(panel, new Vector2(56, 0));
            var p = panel.transform;
            var title = UIKit.LabelAt(p, "LEADERBOARD", 34, Theme.Text, new Vector2(0, 1), new Vector2(30, -26), new Vector2(500, 40), TextAnchor.MiddleLeft, UIKit.TitleFont);
            title.rectTransform.pivot = new Vector2(0, 1);
            _myStats = UIKit.LabelAt(p, "", 19, Theme.Text, new Vector2(0, 1), new Vector2(30, -84), new Vector2(700, 110), TextAnchor.UpperLeft, UIKit.BodyFont);
            _myStats.rectTransform.pivot = new Vector2(0, 1);
            _myStats.supportRichText = true;
            _board = UIKit.LabelAt(p, "", 20, Theme.Text, new Vector2(0, 1), new Vector2(30, -210), new Vector2(700, 560), TextAnchor.UpperLeft, UIKit.BodyFont);
            _board.rectTransform.pivot = new Vector2(0, 1);
            _board.supportRichText = true;
        }

        private void FetchLeaderboard()
        {
            _board.text = "Loading from backend…";
            var op = App.OnlineProfile;
            _myStats.text = op == null
                ? "<color=#aab0d8>Connect to a server (PLAY → ONLINE) once to create your profile.\nProgression tracks mastery: level, rating, objectives — never power.</color>"
                : $"<color=#ffd84a>{op.name}</color>   LEVEL {op.level}  ({op.xp}/{op.xpToNext} XP)   RATING {op.rating}\n" +
                  $"Matches {op.matches}   Wins {op.wins}   Top-3 {op.top3}   Best {op.bestScore}   Avg {op.avgScore}\n" +
                  $"Eliminations {op.eliminations}   Objectives {op.objectives}";
            App.FetchLeaderboard(lb =>
            {
                var sb = new System.Text.StringBuilder("<color=#aab0d8>#    PLAYER                 RATING   LV   WINS   BEST</color>\n\n");
                int i = 1;
                if (lb.players != null)
                    foreach (var pl in lb.players)
                        sb.Append($"{i++,-4} {pl.name,-22} {pl.rating,6}   {pl.level,3}   {pl.wins,4}   {pl.bestScore,5}\n");
                if (i == 1) sb.Append("<color=#8a90b8>No ranked matches yet — play an online match!</color>");
                _board.text = sb.ToString();
            }, e => _board.text = $"<color=#ff9a8a>Server not reachable at {App.Profile.ServerAddress}</color>\n<color=#8a90b8>PLAY → ONLINE connects you; start the server with Server/run-server.sh</color>");
        }

        // ------------------------------------------------------------------ SETTINGS tab

        private void BuildSettings(RectTransform tab)
        {
            var panel = UIKit.Panel(tab, "Settings", new Vector2(0.5f, 0.5f), new Vector2(0, -50), new Vector2(900, 900));
            EnterFx.Add(panel, new Vector2(0, -30));
            var p = panel.transform;
            var title = UIKit.LabelAt(p, "SETTINGS", 34, Theme.Text, new Vector2(0, 1), new Vector2(40, -30), new Vector2(500, 40), TextAnchor.MiddleLeft, UIKit.TitleFont);
            title.rectTransform.pivot = new Vector2(0, 1);
            var prof = App.Profile;
            void Save() { prof.Save(); App.ApplySettings(); }

            // account: guest or Google (Android) — signing in keeps your progress and brings it to any phone
            var acct = UIKit.LabelAt(p, "", 17, Theme.TextDim, new Vector2(0, 1), new Vector2(90, -92), new Vector2(420, 32), TextAnchor.MiddleLeft, UIKit.BoldFont);
            acct.rectTransform.pivot = new Vector2(0, 0.5f);
            acct.supportRichText = true;
            UIKit.Fit(acct);
            var gBtn = UIKit.Button(p, "SIGN IN WITH GOOGLE", new Vector2(1, 1), new Vector2(-60, -92), new Vector2(300, 42), UIKit.ButtonStyle.Secondary,
                () => { if (App.SignedInWithGoogle) App.SignOutGoogle(); else App.SignInWithGoogle(); }, 16);
            ((RectTransform)gBtn.transform).pivot = new Vector2(1, 0.5f);
            var gLabel = UIKit.ButtonLabel(gBtn);
            void RefreshAccount()
            {
                var g = App.Gateway;
                string who = !string.IsNullOrEmpty(g.Handle) ? g.Handle : prof.Name;
                acct.text = App.SignedInWithGoogle
                    ? $"ACCOUNT  <color=#ffffff>{who}</color>  <color=#7dff9a>● {prof.GoogleEmail}</color>"
                    : $"ACCOUNT  <color=#ffffff>{who}</color>  <color=#8a90b8>guest</color>";
                gBtn.gameObject.SetActive(Veil.Net.GoogleSignIn.Supported);
                gLabel.text = App.SignedInWithGoogle ? "SIGN OUT" : "SIGN IN WITH GOOGLE";
            }
            RefreshAccount();
            App.AccountChanged += RefreshAccount;
            App.Gateway.Changed += RefreshAccount;
            Widgets.SliderRow(p, new Vector2(0.5f, 1), new Vector2(0, -130), "MOUSE SENSITIVITY", 0.03f, 0.4f, prof.Sensitivity, v => { prof.Sensitivity = v; Save(); }, v => (v * 10).ToString("0.0"));
            Widgets.SliderRow(p, new Vector2(0.5f, 1), new Vector2(0, -200), "MUSIC", 0f, 1f, prof.Music, v => { prof.Music = v; Save(); }, v => Mathf.RoundToInt(v * 100) + "%");
            Widgets.SliderRow(p, new Vector2(0.5f, 1), new Vector2(0, -270), "SOUND EFFECTS", 0f, 1f, prof.SfxVolume, v => { prof.SfxVolume = v; Save(); }, v => Mathf.RoundToInt(v * 100) + "%");
            var q = new ChipRow(p, new Vector2(0.5f, 1), new Vector2(-10, -330), "GRAPHICS", new[] { "PERFORMANCE", "QUALITY" }, prof.Quality, i => { prof.Quality = i; Save(); }, 200);
            var f = new ChipRow(p, new Vector2(0.5f, 1), new Vector2(-10, -390), "DISPLAY", new[] { "WINDOWED", "FULLSCREEN" }, prof.Fullscreen ? 1 : 0, i => { prof.Fullscreen = i == 1; Save(); }, 200);
            // empty = automatic: the server comes from the remote boot config, so moving the server needs no new app
            var hp = Widgets.InputRow(p, new Vector2(0.5f, 1), new Vector2(-150, -450), "SERVER", prof.ServerOverride, v => prof.SetServerAddress(v), 340, "AUTO (recommended)");
            var upd = UIKit.Button(p, "UPDATE", new Vector2(0.5f, 1), new Vector2(330, -450), new Vector2(150, 48), UIKit.ButtonStyle.Ghost, () => App.OpenUpdate(), 18);
            var build = UIKit.LabelAt(p, $"build {BootConfig.Build}", 14, Theme.TextDim, new Vector2(0.5f, 1), new Vector2(330, -496), new Vector2(150, 20), TextAnchor.MiddleCenter, UIKit.BodyFont);
            // squad voice
            var vm = new ChipRow(p, new Vector2(0.5f, 1), new Vector2(-10, -515), "VOICE CHAT", new[] { "PUSH TO TALK", "OPEN MIC", "OFF" }, prof.VoiceMode,
                i => { prof.VoiceMode = i; App.Voice.Mode = (Veil.Voice.VoiceMode)i; Save(); }, 160);
            Widgets.SliderRow(p, new Vector2(0.5f, 1), new Vector2(0, -575), "VOICE VOLUME", 0f, 2f, prof.VoiceVolume, v => { prof.VoiceVolume = v; App.Voice.OutputVolume = v; Save(); }, v => Mathf.RoundToInt(v * 100) + "%");
            Widgets.SliderRow(p, new Vector2(0.5f, 1), new Vector2(0, -630), "OPEN MIC THRESHOLD", 0f, 1f, prof.MicSensitivity, v => { prof.MicSensitivity = v; App.Voice.Sensitivity = v; Save(); }, v => Mathf.RoundToInt(v * 100) + "%");
            // gyroscope aiming (phones)
            var gm = new ChipRow(p, new Vector2(0.5f, 1), new Vector2(-10, -695), "GYROSCOPE", new[] { "OFF", "WHILE FIRING", "ALWAYS" }, prof.GyroMode, i => { prof.GyroMode = i; Save(); }, 160);
            Widgets.SliderRow(p, new Vector2(0.5f, 1), new Vector2(0, -755), "GYRO SENSITIVITY", 0.2f, 3f, prof.GyroSensitivity, v => { prof.GyroSensitivity = v; Save(); }, v => v.ToString("0.0") + "x");
            int inv = (prof.GyroInvertX ? 1 : 0) + (prof.GyroInvertY ? 2 : 0);
            var gi = new ChipRow(p, new Vector2(0.5f, 1), new Vector2(-10, -815), "GYRO INVERT", new[] { "NONE", "HORIZONTAL", "VERTICAL", "BOTH" }, inv,
                i => { prof.GyroInvertX = (i & 1) != 0; prof.GyroInvertY = (i & 2) != 0; Save(); }, 130);
            if (Application.platform != RuntimePlatform.IPhonePlayer)
            {
                var quit = UIKit.Button(p, "QUIT GAME", new Vector2(1, 1), new Vector2(-40, -30), new Vector2(170, 48), UIKit.ButtonStyle.Ghost, () => Application.Quit(), 18);
                ((RectTransform)quit.transform).pivot = new Vector2(1, 1);
            }
            var help = UIKit.LabelAt(p, Veil.Match.Platform.IsMobile ? "Gyroscope: turn and tilt your phone to aim. Hold TALK for squad voice." : "Hold V to talk to your squad. F9 in a match skips 60 seconds (testing).", 16, Theme.TextDim, new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(800, 30), TextAnchor.MiddleCenter, UIKit.BodyFont);
        }
    }

    // ====================================================================== RESULTS

    public sealed class ResultsScreen : ScreenBase
    {
        private readonly Text _rank, _score, _breakdown, _extra, _full;
        private readonly RectTransform _cards, _fullPanel;
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<RectTransform> _podiumTags = new List<RectTransform>();
        private float _t;
        private int _targetScore;

        public ResultsScreen(RectTransform canvas, GameApp app) : base(canvas, app, "Results")
        {
            var panel = UIKit.Panel(Root, "Summary", new Vector2(0, 0.5f), new Vector2(60, 60), new Vector2(620, 720));
            var p = panel.transform;
            _rank = UIKit.LabelAt(p, "#1", 150, Theme.Gold, new Vector2(0, 1), new Vector2(30, -10), new Vector2(260, 170), TextAnchor.MiddleLeft, UIKit.TitleFont);
            _rank.rectTransform.pivot = new Vector2(0, 1);
            _rank.fontStyle = FontStyle.Italic;
            UIKit.Outline(_rank, new Color(0.5f, 0.3f, 0, 0.9f), 4);
            var mc = _placement = UIKit.LabelAt(p, "SQUAD PLACEMENT", 26, Theme.Text, new Vector2(0, 1), new Vector2(290, -48), new Vector2(320, 34), TextAnchor.MiddleLeft, UIKit.BoldFont);
            mc.rectTransform.pivot = new Vector2(0, 1);
            var ys = UIKit.LabelAt(p, "YOUR SCORE", 20, Theme.TextDim, new Vector2(0, 1), new Vector2(290, -86), new Vector2(320, 26), TextAnchor.MiddleLeft, UIKit.BoldFont);
            ys.rectTransform.pivot = new Vector2(0, 1);
            _score = UIKit.LabelAt(p, "0", 64, Theme.Gold, new Vector2(0, 1), new Vector2(288, -108), new Vector2(320, 70), TextAnchor.MiddleLeft, UIKit.TitleFont);
            _score.rectTransform.pivot = new Vector2(0, 1);
            _breakdown = UIKit.LabelAt(p, "", 24, Theme.Text, new Vector2(0, 1), new Vector2(40, -210), new Vector2(540, 330), TextAnchor.UpperLeft, UIKit.BodyFont);
            _breakdown.rectTransform.pivot = new Vector2(0, 1);
            _breakdown.supportRichText = true;
            _breakdown.lineSpacing = 1.25f;
            _extra = UIKit.LabelAt(p, "", 18, Theme.PurpleLight, new Vector2(0, 1), new Vector2(40, -540), new Vector2(560, 60), TextAnchor.UpperLeft, UIKit.BodyFont);
            _extra.rectTransform.pivot = new Vector2(0, 1);
            _extra.supportRichText = true;
            _extra.horizontalOverflow = HorizontalWrapMode.Wrap;
            _extra.verticalOverflow = VerticalWrapMode.Overflow;

            var again = UIKit.Button(p, "PLAY AGAIN", new Vector2(0, 0), new Vector2(30, 30), new Vector2(330, 80), UIKit.ButtonStyle.Primary, PlayAgain, 40);
            ((RectTransform)again.transform).pivot = new Vector2(0, 0);
            var menu = UIKit.Button(p, "MENU", new Vector2(1, 0), new Vector2(-30, 30), new Vector2(220, 80), UIKit.ButtonStyle.Secondary, () => App.GoMenu(0), 26);
            ((RectTransform)menu.transform).pivot = new Vector2(1, 0);

            _cards = UIKit.At(Root, "Cards", new Vector2(1, 0), new Vector2(-60, 60), new Vector2(1060, 150));
            var fullBtn = UIKit.Button(Root, "VIEW FULL RESULTS", new Vector2(1, 0), new Vector2(-60, 220), new Vector2(280, 46), UIKit.ButtonStyle.Ghost, ToggleFull, 18);

            _fullPanel = UIKit.At(Root, "Full", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100, 800));
            UIKit.Image(_fullPanel, UIKit.Rounded, new Color(0.05f, 0.06f, 0.14f, 0.97f), true);
            _full = UIKit.LabelAt(_fullPanel, "", 20, Theme.Text, new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(1040, 720), TextAnchor.UpperLeft, UIKit.BodyFont);
            _full.rectTransform.pivot = new Vector2(0.5f, 1);
            _full.supportRichText = true;
            UIKit.Button(_fullPanel, "CLOSE", new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(200, 50), UIKit.ButtonStyle.Secondary, ToggleFull, 20);
            _fullPanel.gameObject.SetActive(false);
        }

        private void ToggleFull() => _fullPanel.gameObject.SetActive(!_fullPanel.gameObject.activeSelf);

        private void PlayAgain()
        {
            if (App.Gateway.Online) { App.GoMenu(0); App.ShowSquad(); }   // back to your party — the leader starts again
            else App.StartOfflineMatch();
        }

        private Text _placement;
        /// <summary>Extraction mode: the squad that extracted (-1 = time ran out). Set before Fill.</summary>
        public static int Winner = -1;

        public void Fill(List<PlayerResult> results, int localId, bool online)
        {
            foreach (var g in _spawned) Object.Destroy(g);
            _spawned.Clear();
            _podiumTags.Clear();
            _fullPanel.gameObject.SetActive(false);
            PlayerResult me = null;
            foreach (var r in results) if (r.PlayerId == localId) me = r;
            if (me == null && results.Count > 0) me = results[0];
            if (GameConfig.ExtractionMode && me != null && _placement != null)
            {
                _placement.supportRichText = true;
                _placement.text = Winner < 0 ? "TIME UP · NO EXTRACTION"
                    : Winner == me.Squad ? "<color=#7dff9a>VICTORY · EXTRACTED</color>"
                    : $"<color=#ff5a6a>SQUAD {(char)('A' + Winner)} EXTRACTED</color>";
            }
            _rank.text = "#" + me.SquadRank;
            _rank.fontSize = UIKit.Fs(150);
            _rank.color = me.SquadRank == 1 ? Theme.Gold : me.SquadRank == 2 ? Theme.PurpleLight : Theme.Text;
            PlayerResult mvp = results[0];
            foreach (var r in results) if (r.Total > mvp.Total) mvp = r;
            _targetScore = me.Total;
            _t = 0;
            string Row(string label, int v) => $"{label,-22}<color=#ffd84a>+{v}</color>\n";
            _breakdown.text =
                Row("Primary Objective", me.Primary) + Row("Secondary Objective", me.Secondary) + Row("Resources", me.Resources) +
                Row("Territory Control", me.Territory) + Row("Eliminations", me.Eliminations) + Row("Survival", me.Survival) + Row("Bonus", me.Bonus) +
                Row("Squad Objective", me.SquadPoints);
            string pObj = $"{ObjectiveState.Title(me.PrimaryType)} {(me.PrimaryDone ? "<color=#7dff9a>✓</color>" : "<color=#ff7a8a>✗</color>")}";
            string sObj = $"{ObjectiveState.Title(me.SecondaryType)} {(me.SecondaryDone ? "<color=#7dff9a>✓</color>" : "<color=#ff7a8a>✗</color>")}";
            _extra.text = $"Squad {(char)('A' + me.Squad)} total <color=#ffd84a>{me.SquadTotal:N0}</color>   ·   MVP <color=#ffd84a>{mvp.Name}</color> ({mvp.Total:N0})\n" +
                          $"{pObj}   ·   {sObj}   ·   K/D {me.Elims}/{me.Deaths}";
            if (online) App.StartCoroutine(RefreshOnline());

            // cards for ranks 2..8 (top 3 stand on the podium)
            for (int i = 1; i < Mathf.Min(results.Count, 8); i++)
            {
                var r = results[i];
                var card = UIKit.At(_cards, "Card", new Vector2(1, 0), new Vector2(-(7 - i) * 150, 0), new Vector2(140, 150));
                card.pivot = new Vector2(1, 0);
                UIKit.Image(card, UIKit.Rounded, r.PlayerId == localId ? new Color(0.62f, 0.38f, 1f, 0.6f) : Theme.Panel);
                var face = UIKit.At(card, "Face", new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(84, 84));
                face.pivot = new Vector2(0.5f, 1);
                var raw = face.gameObject.AddComponent<RawImage>();
                raw.texture = PortraitStudio.Get(r.Look);
                raw.raycastTarget = false;
                var rk = UIKit.LabelAt(card, $"{(char)('A' + r.Squad)}", 22, r.Squad == me.Squad ? Theme.Green : Theme.Gold, new Vector2(0, 1), new Vector2(10, -6), new Vector2(40, 30), TextAnchor.UpperLeft, UIKit.TitleFont);
                rk.rectTransform.pivot = new Vector2(0, 1);
                UIKit.Shadow(rk);
                UIKit.LabelAt(card, r.Name, 16, Theme.Text, new Vector2(0.5f, 0), new Vector2(0, 38), new Vector2(136, 22), TextAnchor.MiddleCenter, UIKit.BoldFont);
                UIKit.LabelAt(card, r.Total.ToString("N0"), 20, Theme.Gold, new Vector2(0.5f, 0), new Vector2(0, 14), new Vector2(136, 24), TextAnchor.MiddleCenter, UIKit.TitleFont);
                _spawned.Add(card.gameObject);
            }
            // podium labels (world positions are static on the stage)
            for (int i = 0; i < Mathf.Min(3, results.Count); i++)
            {
                var r = results[i];
                var tag = UIKit.At(Root, "Podium" + i, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(260, 70));
                _podiumTags.Add(tag);
                var n = UIKit.LabelAt(tag, $"{r.Name}", 22, Theme.Text, new Vector2(0.5f, 1), new Vector2(0, 0), new Vector2(260, 30), TextAnchor.MiddleCenter, UIKit.BoldFont);
                UIKit.Outline(n, new Color(0, 0, 0, 0.8f), 2);
                var s = UIKit.LabelAt(tag, $"{r.Total:N0}", 30, i == 0 ? Theme.Gold : Theme.Text, new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(260, 36), TextAnchor.MiddleCenter, UIKit.TitleFont);
                UIKit.Outline(s, new Color(0, 0, 0, 0.8f), 2);
                _spawned.Add(tag.gameObject);
            }

            var sb = new System.Text.StringBuilder("<color=#aab0d8>    NAME            TOTAL   PRIM  SEC   RES  TERR  ELIM  SURV  SQUAD   K/A/D  REV</color>\n");
            int lastSquad = -1;
            foreach (var r in results)
            {
                if (r.Squad != lastSquad)
                {
                    lastSquad = r.Squad;
                    sb.Append($"\n<color={(r.Squad == me.Squad ? "#7dff9a" : "#c7a6ff")}>#{r.SquadRank}  SQUAD {(char)('A' + r.Squad)}  ·  {r.SquadTotal:N0}</color>\n");
                }
                string line = $"    {r.Name,-15} {r.Total,5}   {r.Primary,4}  {r.Secondary,3}  {r.Resources,4}  {r.Territory,4}  {r.Eliminations,4}  {r.Survival,4}  {r.SquadPoints,5}   {r.Elims}/{r.Assists}/{r.Deaths}  {r.Revives,3}{(r == mvp ? "  MVP" : "")}";
                sb.Append(r.PlayerId == localId ? $"<color=#ffd84a>{line}</color>\n" : line + "\n");
            }
            _full.text = sb.ToString();
            Sfx.Play(me.SquadRank == 1 ? Sfx.Objective : Sfx.Capture, 0.9f);
        }

        private IEnumerator RefreshOnline()
        {
            yield return new WaitForSeconds(1f);
            var id = App.Profile.BackendId;
            if (string.IsNullOrEmpty(id)) yield break;
            int oldRating = App.OnlineProfile != null ? App.OnlineProfile.rating : 0;
            App.FetchProfile(p =>
            {
                App.OnlineProfile = p;
                int d = p.rating - oldRating;
                _extra.text += $"\n<color=#ffd84a>LEVEL {p.level}</color>  {p.xp}/{p.xpToNext} XP   ·   RATING {p.rating} ({(d >= 0 ? "+" : "")}{d})";
            }, e => { });
        }

        public override void Update(float dt)
        {
            for (int i = 0; i < _podiumTags.Count; i++)
            {
                var sp = App.Cam.WorldToScreenPoint(App.Stage.HeadPoint(i));
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)Root, sp, null, out var lp);
                _podiumTags[i].anchoredPosition = lp + new Vector2(0, 40);
            }
            _t += dt;
            float k = Mathf.Clamp01(_t / 1.6f);
            _score.text = Mathf.RoundToInt(_targetScore * (1 - Mathf.Pow(1 - k, 3))).ToString("N0");
        }
    }

    // ====================================================================== PAUSE

    public sealed class PauseScreen : ScreenBase
    {
        public PauseScreen(RectTransform canvas, GameApp app) : base(canvas, app, "Pause")
        {
            var dim = UIKit.Fill(Root, "Dim");
            UIKit.Image(dim, UIKit.Square, new Color(0.02f, 0.02f, 0.08f, 0.6f), true);
            var panel = UIKit.Panel(Root, "Panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 620));
            var p = panel.transform;
            var t = UIKit.LabelAt(p, "PAUSED", 48, Theme.Text, new Vector2(0.5f, 1), new Vector2(0, -50), new Vector2(500, 60), TextAnchor.MiddleCenter, UIKit.TitleFont);
            UIKit.Button(p, "RESUME", new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(420, 76), UIKit.ButtonStyle.Primary, () => App.SetPaused(false), 36);
            Widgets.SliderRow(p, new Vector2(0.5f, 1), new Vector2(60, -250), "SENSITIVITY", 0.03f, 0.4f, app.Profile.Sensitivity, v => { app.Profile.Sensitivity = v; app.Profile.Save(); app.ApplySettings(); }, v => (v * 10).ToString("0.0"));
            new ChipRow(p, new Vector2(0.5f, 1), new Vector2(-40, -330), "GYRO", new[] { "OFF", "FIRING", "ALWAYS" }, app.Profile.GyroMode,
                i => { app.Profile.GyroMode = i; app.Profile.Save(); }, 120);
            UIKit.Button(p, "LEAVE MATCH", new Vector2(0.5f, 0), new Vector2(0, 50), new Vector2(420, 64), UIKit.ButtonStyle.Secondary, () => { App.SetPaused(false); App.LeaveMatch(); }, 26);
            var note = UIKit.LabelAt(p, "Offline matches pause. Online matches keep running.", 16, Theme.TextDim, new Vector2(0.5f, 0), new Vector2(0, 130), new Vector2(500, 26), TextAnchor.MiddleCenter, UIKit.BodyFont);
        }
    }
}
