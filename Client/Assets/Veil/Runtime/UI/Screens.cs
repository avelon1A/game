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
            var t = UIKit.LabelAt(parent, "RILO", size, Color.white, anchor, pos, new Vector2(size * 3f, size * 1.2f), TextAnchor.MiddleLeft, UIKit.TitleFont);
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
        private readonly RectTransform _choice;
        private readonly Text _googleLabel;
        private bool _busy;

        /// <summary>Phones, not signed in: the title shows GOOGLE / GUEST instead of "press any key".</summary>
        public bool Choosing => _choice.gameObject.activeSelf;

        public TitleScreen(RectTransform canvas, GameApp app) : base(canvas, app, "Title")
        {
            // key art (Meshy) fills the screen, cropped to any aspect — replaces the map fly-over behind the title
            var art = Resources.Load<Texture2D>("UI/title_keyart");
            if (art != null)
            {
                var artRt = UIKit.At(Root, "KeyArt", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(art.width, art.height));
                var raw = artRt.gameObject.AddComponent<RawImage>();
                raw.texture = art; raw.raycastTarget = false;
                var fit = artRt.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio = 1344f / 768f;              // source image aspect (never the imported texture's)
                artRt.pivot = new Vector2(0.5f, 0.3f);       // wide phones: crop more sky than cliff, keep the heroes
                artRt.pivot = new Vector2(0.5f, 0.25f);   // wide phones: crop the sky, keep the heroes on the cliff
            }
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

            // sign-in choice: 1) Google (progress saved to your account)  2) Guest (this device only)
            _choice = UIKit.At(Root, "SignIn", new Vector2(0.5f, 0), new Vector2(0, 190), new Vector2(560, 230));
            var head = UIKit.LabelAt(_choice, "HOW DO YOU WANT TO PLAY?", 24, Color.white, new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(560, 30), TextAnchor.MiddleCenter, UIKit.TitleFont);
            UIKit.Shadow(head, 2);
            var g = UIKit.GoogleButton(_choice, new Vector2(0.5f, 1), new Vector2(0, -72), new Vector2(460, 70), Google, 26);
            _googleLabel = UIKit.ButtonLabel(g);
            UIKit.Button(_choice, "PLAY AS GUEST", new Vector2(0.5f, 1), new Vector2(0, -152), new Vector2(460, 60), UIKit.ButtonStyle.Secondary, Guest, 22);
            var note = UIKit.LabelAt(_choice, "Google keeps your progress on every device · Guest saves on this phone only", 15, Theme.TextDim, new Vector2(0.5f, 0), new Vector2(0, -2), new Vector2(560, 22), TextAnchor.MiddleCenter, UIKit.BodyFont);
            UIKit.Shadow(note);
        }

        public override void Show(bool v)
        {
            base.Show(v);
            if (!v) return;
            bool choose = (Veil.Net.GoogleSignIn.Supported || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-signin-ui") >= 0) && !App.SignedInWithGoogle;
            _choice.gameObject.SetActive(choose);
            _press.gameObject.SetActive(!choose);
            _busy = false; _googleLabel.text = "Sign in with Google";
        }

        private void Google()
        {
            if (_busy) return;
            _busy = true; _googleLabel.text = "Signing in…";
            App.SignInWithGoogle(ok =>
            {
                _busy = false; _googleLabel.text = "Sign in with Google";
                if (ok) App.GoMenu(0);
            });
        }

        private void Guest() { if (!_busy) App.GoMenu(0); }

        public override void Update(float dt)
        {
            _press.color = new Color(Theme.Yellow.r, Theme.Yellow.g, Theme.Yellow.b, 0.55f + Mathf.Sin(Time.time * 3f) * 0.45f);
        }
    }

    // ====================================================================== MENU (lobby-style, tabs)

    public sealed class MenuScreen : ScreenBase
    {
        public int Tab { get; private set; }
        private readonly RectTransform[] _tabs = new RectTransform[5];   // play, characters, leaderboard, settings, store
        private int _lastBadge;
        private Text _profileName, _profileStatus, _profileLevel, _friendsBadge;
        private RawImage _profileFace;
        private Image _friendsBadgeBg;
        private Bar _xpBar;
        private Text _coins, _gems;
        private Image _netIcon;
        private readonly Image[] _navBg = new Image[7], _navIcon = new Image[7];
        private readonly Text[] _navLabel = new Text[7];

        // play tab
        public SquadPanel Squad { get; private set; }
        public FriendsDrawer Friends { get; private set; }

        // characters tab
        private InputField _nameField;

        // leaderboard

        public MenuScreen(RectTransform canvas, GameApp app) : base(canvas, app, "Menu")
        {
            // ---------------- top bar (dockyard lobby concept): logo + profile left, currencies + icons right
            var bar = UIKit.Rect(Root, "TopBar", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 110));
            var shade = UIKit.Image(bar, UIKit.Gradient, new Color(0.02f, 0.03f, 0.08f, 0.75f));
            shade.raycastTarget = false;
            var logo = UIKit.LabelAt(bar, "RILO", 76, Color.white, new Vector2(0, 0.5f), new Vector2(36, 2), new Vector2(240, 90), TextAnchor.MiddleLeft, UIKit.TitleFont);
            logo.rectTransform.pivot = new Vector2(0, 0.5f); logo.fontStyle = FontStyle.Italic;
            UIKit.Outline(logo, new Color(0.95f, 0.72f, 0.1f), 3f);
            UIKit.Shadow(logo, 3, 0.7f);

            var card = UIKit.At(bar, "Profile", new Vector2(0, 0.5f), new Vector2(290, 2), new Vector2(330, 74));
            card.pivot = new Vector2(0, 0.5f);
            var cardBg = UIKit.Image(card, UIKit.RoundedSmall, new Color(0.04f, 0.05f, 0.1f, 0.78f), true);
            var cardBtn = card.gameObject.AddComponent<Button>();   // tap your profile → Settings (account / Google sign-in)
            cardBtn.targetGraphic = cardBg;
            cardBtn.onClick.AddListener(() => { Sfx.Play(Sfx.Click, 0.5f); SelectTab(3); });
            var faceRt = UIKit.At(card, "Face", new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(60, 60));
            faceRt.pivot = new Vector2(0, 0.5f);
            UIKit.Image(faceRt, UIKit.RoundedSmall, Theme.Yellow);
            var fi = UIKit.Fill(faceRt, "Img", 3);
            _profileFace = fi.gameObject.AddComponent<RawImage>();
            _profileFace.raycastTarget = false;
            _profileName = UIKit.LabelAt(card, "", 22, Color.white, new Vector2(0, 1), new Vector2(80, -8), new Vector2(170, 30), TextAnchor.MiddleLeft, UIKit.BoldFont);
            _profileName.rectTransform.pivot = new Vector2(0, 1); UIKit.Fit(_profileName, 12);
            _profileLevel = UIKit.LabelAt(card, "", 18, Theme.TextDim, new Vector2(1, 1), new Vector2(-12, -8), new Vector2(80, 30), TextAnchor.MiddleRight, UIKit.BoldFont);
            _profileLevel.rectTransform.pivot = new Vector2(1, 1);
            _profileStatus = UIKit.LabelAt(card, "", 14, Theme.Yellow, new Vector2(0, 0), new Vector2(80, 8), new Vector2(60, 20), TextAnchor.MiddleLeft, UIKit.BoldFont);
            _profileStatus.rectTransform.pivot = new Vector2(0, 0); _profileStatus.supportRichText = true;
            _xpBar = new Bar(card, new Vector2(0, 0), new Vector2(138, 12), new Vector2(178, 10), Theme.Yellow, new Color(1, 1, 1, 0.15f));
            _xpBar.Root.pivot = new Vector2(0, 0);

            // right: coins, gems, friends, mail, settings, connection
            float rx = -24;
            Button IconBtn(Sprite icon, System.Action onClick, out RectTransform rt)
            {
                rt = UIKit.At(bar, "Icon", new Vector2(1, 0.5f), new Vector2(rx, 2), new Vector2(70, 64));
                rt.pivot = new Vector2(1, 0.5f);
                var bg = UIKit.Image(rt, UIKit.RoundedSmall, new Color(0.04f, 0.05f, 0.1f, 0.78f), true);
                var b = rt.gameObject.AddComponent<Button>(); b.targetGraphic = bg;
                b.onClick.AddListener(() => { Sfx.Play(Sfx.Click, 0.5f); onClick(); });
                var ic = UIKit.At(rt, "I", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(36, 36));
                UIKit.Image(ic, icon, Color.white);
                rx -= 80;
                return b;
            }
            _netIcon = UIKit.Image(UIKit.At(bar, "Net", new Vector2(1, 0.5f), new Vector2(rx + 6, 2), new Vector2(34, 30)), Icons.Energy, Theme.Green);
            ((RectTransform)_netIcon.transform).pivot = new Vector2(1, 0.5f);
            rx -= 50;
            IconBtn(Icons.Gear, () => SelectTab(3), out _);
            IconBtn(Icons.Copy, () => App.Toast("No new mail"), out var mailRt);
            var mailDot = UIKit.At(mailRt, "Dot", new Vector2(1, 1), new Vector2(-6, -6), new Vector2(16, 16));
            mailDot.pivot = new Vector2(1, 1); UIKit.Image(mailDot, UIKit.Circle, Theme.Red);
            IconBtn(Icons.Players, () => { SelectTab(0); Friends.Show(!Friends.Visible); }, out var fRt);
            var badge = UIKit.At(fRt, "Badge", new Vector2(1, 1), new Vector2(2, 2), new Vector2(26, 26));
            badge.pivot = new Vector2(1, 1);
            _friendsBadgeBg = UIKit.Image(badge, UIKit.Circle, Theme.Red);
            _friendsBadge = UIKit.Label(badge, "", 14, Color.white, TextAnchor.MiddleCenter, UIKit.BoldFont);
            Text Currency(Sprite icon, Color c, out RectTransform rt)
            {
                rt = UIKit.At(bar, "Currency", new Vector2(1, 0.5f), new Vector2(rx - 6, 2), new Vector2(200, 60));
                rt.pivot = new Vector2(1, 0.5f);
                UIKit.Image(rt, UIKit.RoundedSmall, new Color(0.04f, 0.05f, 0.1f, 0.78f));
                var ic = UIKit.At(rt, "I", new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(40, 40));
                ic.pivot = new Vector2(0, 0.5f);
                UIKit.Image(ic, UIKit.Circle, c);
                var ii = UIKit.At(ic, "S", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24, 24));
                UIKit.Image(ii, icon, new Color(0, 0, 0, 0.55f));
                var t = UIKit.LabelAt(rt, "0", 24, Color.white, new Vector2(0, 0.5f), new Vector2(58, 0), new Vector2(96, 40), TextAnchor.MiddleLeft, UIKit.TitleFont);
                t.rectTransform.pivot = new Vector2(0, 0.5f); UIKit.Fit(t, 14);
                var plus = UIKit.Button(rt, "+", new Vector2(1, 0.5f), new Vector2(-6, 0), new Vector2(40, 44), UIKit.ButtonStyle.Ghost, () => App.Toast("The store opens soon — earn coins by playing matches"), 30);
                ((RectTransform)plus.transform).pivot = new Vector2(1, 0.5f);
                rx -= 212;
                return t;
            }
            _gems = Currency(UIKit.Diamond, new Color(0.3f, 0.75f, 1f), out _);
            _coins = Currency(Icons.Core, new Color(1f, 0.78f, 0.15f), out _);

            // ---------------- left menu
            var nav = UIKit.At(Root, "Nav", new Vector2(0, 1), new Vector2(28, -150), new Vector2(256, 7 * 82));
            nav.pivot = new Vector2(0, 1);
            string[] navNames = { "LOBBY", "CHARACTERS", "LOADOUT", "BATTLE PASS", "STORE", "EVENTS", "RANKINGS" };
            Sprite[] navIcons = { Icons.Tower, Icons.Players, Icons.Blaster, Icons.Shield, Icons.Core, Icons.Clock, Icons.Trophy };
            for (int i = 0; i < navNames.Length; i++)
            {
                int idx = i;
                var rt = UIKit.At(nav, "Nav" + i, new Vector2(0, 1), new Vector2(0, -i * 82), new Vector2(256, 72));
                rt.pivot = new Vector2(0, 1);
                _navBg[i] = UIKit.Image(rt, UIKit.RoundedSmall, new Color(0.04f, 0.05f, 0.1f, 0.78f), true);
                var b = rt.gameObject.AddComponent<Button>(); b.targetGraphic = _navBg[i];
                b.onClick.AddListener(() => { Sfx.Play(Sfx.Click, 0.5f); OnNav(idx); });
                rt.gameObject.AddComponent<ButtonFx>();
                var ic = UIKit.At(rt, "I", new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(38, 38));
                ic.pivot = new Vector2(0, 0.5f);
                _navIcon[i] = UIKit.Image(ic, navIcons[i], Color.white);
                _navLabel[i] = UIKit.LabelAt(rt, navNames[i], 22, Color.white, new Vector2(0, 0.5f), new Vector2(72, 0), new Vector2(176, 40), TextAnchor.MiddleLeft, UIKit.BoldFont);
                _navLabel[i].rectTransform.pivot = new Vector2(0, 0.5f); UIKit.Fit(_navLabel[i], 13);
                if (i == 3 || i == 5)
                {
                    var dot = UIKit.At(rt, "Dot", new Vector2(1, 1), new Vector2(-8, -8), new Vector2(14, 14));
                    dot.pivot = new Vector2(1, 1); UIKit.Image(dot, UIKit.Circle, Theme.Red);
                }
                EnterFx.Add(rt, new Vector2(-40, 0), 0.04f * i);
            }

            for (int i = 0; i < _tabs.Length; i++) _tabs[i] = UIKit.Fill(Root, "Tab" + i);
            BuildPlay(_tabs[0]);
            BuildCharacters(_tabs[1]);
            BuildLeaderboard(_tabs[2]);
            BuildSettings(_tabs[3]);
            BuildStore(_tabs[4]);
            App.StoreChanged += () => { RefreshStore(); RefreshProfileChip(); };
            ((RectTransform)bar.transform).SetAsLastSibling();

            Friends = new FriendsDrawer(_tabs[0], app);
            Squad.OpenFriends = () => Friends.Show(true);
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

        /// <summary>Left menu: LOBBY, CHARACTERS, LOADOUT, BATTLE PASS, STORE, EVENTS, RANKINGS.</summary>
        private void OnNav(int i)
        {
            switch (i)
            {
                case 0: SelectTab(0); break;
                case 1: case 2: SelectTab(1); _nav = i; HighlightNav(); break;
                case 6: SelectTab(2); break;
                case 3: App.Toast("BATTLE PASS — Season 1 is coming soon"); break;
                case 4: SelectTab(4); break;
                case 5: App.Toast("EVENTS — coming soon"); break;
            }
        }

        private int _nav;

        private void HighlightNav()
        {
            for (int k = 0; k < _navBg.Length; k++)
            {
                bool on = k == _nav;
                _navBg[k].color = on ? Theme.Yellow : new Color(0.04f, 0.05f, 0.1f, 0.78f);
                _navIcon[k].color = on ? new Color(0.08f, 0.06f, 0.02f) : Color.white;
                _navLabel[k].color = on ? new Color(0.08f, 0.06f, 0.02f) : Color.white;
            }
            if (_nav >= 0) PunchFx.On(_navLabel[_nav]).Kick(0.12f);
        }

        public void SelectTab(int i)
        {
            Tab = i;
            for (int k = 0; k < _tabs.Length; k++) _tabs[k].gameObject.SetActive(k == i);
            if (i != 0 && Friends != null) Friends.Show(false);
            _nav = i == 0 ? 0 : i == 1 ? (_nav == 2 ? 2 : 1) : i == 2 ? 6 : i == 4 ? 4 : -1;
            if (i == 4) RefreshStore();
            HighlightNav();
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
            _profileStatus.text = "";
            _profileLevel.text = op != null ? $"Lv. {op.level}" : "Lv. 1";
            _xpBar.Set(op != null && op.xpToNext > 0 ? (float)op.xp / op.xpToNext : 0f, 10f);
            _coins.text = App.Coins.ToString("N0");
            _gems.text = App.Gems.ToString("N0");
            _netIcon.color = g.Online ? Theme.Green : Theme.Red;
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
            note.rectTransform.sizeDelta = new Vector2(660, 24); UIKit.Fit(note, 11);
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
            for (int i = 0; i < outfitNames.Length; i++)
            {
                var it = StoreCatalog.Get(StoreCatalog.HeroId(i));
                if (it != null && !App.Owns(it.Id)) outfitNames[i] = $"{outfitNames[i]} ●{it.Price}";
            }
            ChipRow r1 = null;
            r1 = new ChipRow(p, new Vector2(0, 1), new Vector2(30, -210), "HERO", outfitNames, prof.Look.Outfit, i =>
            {
                if (!App.Owns(StoreCatalog.HeroId(i)))
                {
                    var it = StoreCatalog.Get(StoreCatalog.HeroId(i));
                    r1.Select(prof.Look.Outfit);
                    App.Toast($"{it?.Name} is locked — unlock it in the STORE for {it?.Price} coins");
                    SelectTab(4);
                    return;
                }
                var l = prof.Look; l.Outfit = (byte)i; prof.Look = l; Changed();
            }, 108, 100);
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
                var wr = new ChipRow(p, new Vector2(0, 1), new Vector2(30, -390), "WEAPON", new[] { "RIFLE", "SNIPER" }, Mathf.Min(prof.Look.Weapon, 1), i =>
                {
                    var l = prof.Look; l.Weapon = (byte)i;
                    var skin = StoreCatalog.Get(StoreCatalog.SkinId(l.Accessory));
                    if (skin != null && skin.WeaponType != i) l.Accessory = 0;
                    prof.Look = l; Changed();
                }, 150);
                wr.Root.pivot = new Vector2(0, 0.5f);
                var wdesc = UIKit.LabelAt(p, "RIFLE: fast, 30 m  ·  SNIPER: 48 dmg, 75 m, scope  ·  in a match switch to FISTS anytime (X key / FISTS button)", 15, Theme.TextDim, new Vector2(0, 1), new Vector2(210, -432), new Vector2(760, 24), TextAnchor.MiddleLeft, UIKit.BoldFont);
                wdesc.rectTransform.pivot = new Vector2(0, 0.5f);
                // gun skin (Appearance.Accessory): STANDARD, or a Meshy store gun — picking one also picks its gun type
                string[] skinNames = { "STANDARD", "PLASMA" + (App.Owns("gun:1") ? "" : " ●900"), "DRAGON" + (App.Owns("gun:2") ? "" : " ●1500") };
                ChipRow sr = null;
                sr = new ChipRow(p, new Vector2(0, 1), new Vector2(30, -480), "GUN SKIN", skinNames, Mathf.Min(prof.Look.Accessory, 2), i =>
                {
                    if (i > 0 && !App.Owns(StoreCatalog.SkinId(i)))
                    {
                        var it = StoreCatalog.Get(StoreCatalog.SkinId(i));
                        sr.Select(Mathf.Min(prof.Look.Accessory, 2));
                        App.Toast($"{it?.Name} is locked — unlock it in the STORE for {it?.Price} coins");
                        SelectTab(4);
                        return;
                    }
                    var l = prof.Look; l.Accessory = (byte)i;
                    if (i > 0) { l.Weapon = (byte)StoreCatalog.Get(StoreCatalog.SkinId(i)).WeaponType; wr.Select(l.Weapon); }
                    prof.Look = l; Changed();
                }, 150);
                sr.Root.pivot = new Vector2(0, 0.5f);
                presetsT.gameObject.SetActive(false);
                foreach (var b in presetButtons) b.SetActive(false);
            }
            var play = UIKit.Button(p, "PLAY", new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(680, 84), UIKit.ButtonStyle.Primary, () => SelectTab(0), 44);
        }

        // ------------------------------------------------------------------ STORE tab

        private readonly List<(StoreCatalog.Item item, Text price, Button buy, Text buyLabel, Image bg)> _storeCards = new List<(StoreCatalog.Item, Text, Button, Text, Image)>();
        private Text _storeCoins;

        /// <summary>Coins store: unlock heroes and Meshy gun skins (cosmetic only).</summary>
        private void BuildStore(RectTransform tab)
        {
            var panel = UIKit.Panel(tab, "Store", new Vector2(1, 0.5f), new Vector2(-40, -40), new Vector2(760, 820));
            EnterFx.Add(panel, new Vector2(56, 0));
            var p = (RectTransform)panel.transform;
            var title = UIKit.LabelAt(p, "STORE", 34, Theme.Text, new Vector2(0, 1), new Vector2(30, -24), new Vector2(300, 40), TextAnchor.MiddleLeft, UIKit.TitleFont);
            title.rectTransform.pivot = new Vector2(0, 1);
            _storeCoins = UIKit.LabelAt(p, "", 24, Theme.Yellow, new Vector2(1, 1), new Vector2(-30, -26), new Vector2(360, 36), TextAnchor.MiddleRight, UIKit.TitleFont);
            _storeCoins.rectTransform.pivot = new Vector2(1, 1);
            var sub = UIKit.LabelAt(p, "Earn coins every match · everything is cosmetic", 15, Theme.TextDim, new Vector2(0, 1), new Vector2(30, -66), new Vector2(700, 22), TextAnchor.MiddleLeft, UIKit.BodyFont);
            sub.rectTransform.pivot = new Vector2(0, 1);
            int n = 0;
            foreach (var item in StoreCatalog.Items)
            {
                if (item.Price == 0) continue;
                int col = n % 2, row = n / 2; n++;
                var card = UIKit.At(p, item.Id, new Vector2(0, 1), new Vector2(26 + col * 358, -104 - row * 166), new Vector2(348, 156));
                card.pivot = new Vector2(0, 1);
                var bg = UIKit.Image(card, UIKit.RoundedSmall, new Color(1, 1, 1, 0.06f), true);
                var face = UIKit.At(card, "Face", new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(116, 132)); face.pivot = new Vector2(0, 0.5f);
                UIKit.Image(face, UIKit.RoundedSmall, item.Hero >= 0 ? new Color(0.14f, 0.13f, 0.3f) : new Color(0.92f, 0.92f, 0.96f));
                if (item.Hero >= 0)
                {
                    var raw = UIKit.Fill(face, "Img", 3).gameObject.AddComponent<RawImage>();
                    raw.texture = PortraitStudio.Get(new Appearance { Outfit = (byte)item.Hero, Color = 2 });
                    raw.raycastTarget = false;
                }
                else
                {
                    var gunPic = Resources.Load<Texture2D>("UI/store_gun_" + item.Skin);   // Meshy render of the gun
                    if (gunPic != null)
                    {
                        var raw = UIKit.Fill(face, "Img", 2).gameObject.AddComponent<RawImage>();
                        raw.texture = gunPic; raw.uvRect = new Rect(0.1f, 0.25f, 0.8f, 0.5f); raw.raycastTarget = false;
                        ((RectTransform)raw.transform).localRotation = Quaternion.Euler(0, 0, 20f);
                    }
                    else
                    {
                        var ic = UIKit.At(face, "Gun", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(84, 84));
                        UIKit.Image(ic, Icons.Blaster, item.WeaponType == 1 ? new Color(1f, 0.45f, 0.25f) : new Color(1f, 0.82f, 0.3f));
                    }
                }
                var nm = UIKit.LabelAt(card, item.Name, 22, Color.white, new Vector2(0, 1), new Vector2(140, -14), new Vector2(200, 28), TextAnchor.MiddleLeft, UIKit.TitleFont);
                nm.rectTransform.pivot = new Vector2(0, 1); UIKit.Fit(nm, 12);
                var ds = UIKit.LabelAt(card, item.Desc, 14, Theme.TextDim, new Vector2(0, 1), new Vector2(140, -44), new Vector2(200, 20), TextAnchor.MiddleLeft, UIKit.BodyFont);
                ds.rectTransform.pivot = new Vector2(0, 1); UIKit.Fit(ds, 9);
                var price = UIKit.LabelAt(card, "", 20, Theme.Yellow, new Vector2(0, 1), new Vector2(140, -70), new Vector2(200, 26), TextAnchor.MiddleLeft, UIKit.TitleFont);
                price.rectTransform.pivot = new Vector2(0, 1);
                var it = item;
                var buy = UIKit.Button(card, "BUY", new Vector2(1, 0), new Vector2(-12, 12), new Vector2(190, 46), UIKit.ButtonStyle.Primary, () => OnStoreButton(it), 20);
                ((RectTransform)buy.transform).pivot = new Vector2(1, 0);
                _storeCards.Add((item, price, buy, UIKit.ButtonLabel(buy), bg));
            }
        }

        private void OnStoreButton(StoreCatalog.Item it)
        {
            var prof = App.Profile;
            if (!App.Owns(it.Id)) { App.Buy(it.Id); return; }
            // owned → equip
            var l = prof.Look;
            if (it.Hero >= 0) l.Outfit = (byte)it.Hero;
            else { l.Accessory = (byte)it.Skin; l.Weapon = (byte)it.WeaponType; }
            prof.Look = l; prof.Save(); App.Stage.UpdateLook(l);
            App.Toast($"{it.Name} equipped");
            RefreshStore();
        }

        private void RefreshStore()
        {
            if (_storeCoins == null) return;
            _storeCoins.text = $"● {App.Coins:N0} COINS";
            var look = App.Profile.Look;
            foreach (var (item, price, buy, label, bg) in _storeCards)
            {
                bool owned = App.Owns(item.Id);
                bool equipped = owned && (item.Hero >= 0 ? look.Outfit == item.Hero : look.Accessory == item.Skin);
                price.text = owned ? (equipped ? "EQUIPPED" : "OWNED") : $"● {item.Price:N0}";
                price.color = owned ? Theme.Green : Theme.Yellow;
                label.text = owned ? (equipped ? "EQUIPPED" : "EQUIP") : (App.Coins >= item.Price ? "BUY" : "NEED COINS");
                buy.interactable = !equipped && (owned || App.Coins >= item.Price);
                bg.color = equipped ? new Color(1f, 0.85f, 0.3f, 0.16f) : new Color(1, 1, 1, 0.06f);
            }
        }

        // ------------------------------------------------------------------ LEADERBOARD tab

        private RectTransform _lbContent;
        private Text _lbStatus;
        private bool _lbFriends;
        private ProfileDto[] _lbPlayers;
        private readonly Button[] _lbTabs = new Button[2];

        /// <summary>Leaderboard (concept from Meshy): GLOBAL / FRIENDS, your profile card, top-3 podium, ranked rows, your row highlighted.</summary>
        private void BuildLeaderboard(RectTransform tab)
        {
            var panel = UIKit.Panel(tab, "Board", new Vector2(1, 0.5f), new Vector2(-40, -40), new Vector2(760, 820));
            EnterFx.Add(panel, new Vector2(56, 0));
            var p = panel.transform;
            var title = UIKit.LabelAt(p, "LEADERBOARD", 34, Theme.Text, new Vector2(0, 1), new Vector2(30, -24), new Vector2(400, 40), TextAnchor.MiddleLeft, UIKit.TitleFont);
            title.rectTransform.pivot = new Vector2(0, 1);
            for (int i = 0; i < 2; i++)
            {
                int k = i;
                _lbTabs[i] = UIKit.Button(p, i == 0 ? "GLOBAL" : "FRIENDS", new Vector2(1, 1), new Vector2(-30 - (1 - i) * 150, -28), new Vector2(140, 40), UIKit.ButtonStyle.Ghost, () => { _lbFriends = k == 1; RenderLeaderboard(); }, 16);
                ((RectTransform)_lbTabs[i].transform).pivot = new Vector2(1, 1);
            }
            _lbContent = UIKit.Rect(p, "Content", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _lbStatus = UIKit.LabelAt(p, "", 18, Theme.TextDim, new Vector2(0.5f, 0), new Vector2(0, 160), new Vector2(680, 60), TextAnchor.MiddleCenter, UIKit.BodyFont);
            _lbStatus.supportRichText = true;
        }

        private void FetchLeaderboard()
        {
            _lbStatus.text = "Loading…";
            RenderLeaderboard();
            App.FetchLeaderboard(lb => { _lbPlayers = lb.players ?? new ProfileDto[0]; _lbStatus.text = ""; RenderLeaderboard(); },
                e => { _lbPlayers = null; _lbStatus.text = "<color=#ff9a8a>Can't reach the server</color>\n<color=#8a90b8>Go online (PLAY) and try again</color>"; RenderLeaderboard(); });
        }

        private static readonly Color Gold = new Color(1f, 0.8f, 0.25f), Silver = new Color(0.82f, 0.85f, 0.95f), Bronze = new Color(0.9f, 0.58f, 0.35f);

        private void RenderLeaderboard()
        {
            for (int i = _lbContent.childCount - 1; i >= 0; i--) Object.Destroy(_lbContent.GetChild(i).gameObject);
            for (int i = 0; i < 2; i++)
            {
                bool on = (i == 1) == _lbFriends;
                ((Image)_lbTabs[i].targetGraphic).color = on ? new Color(0.62f, 0.38f, 1f, 0.9f) : new Color(1, 1, 1, 0.08f);
                UIKit.ButtonLabel(_lbTabs[i]).color = on ? Color.white : Theme.TextDim;
            }
            var me = App.OnlineProfile;
            string myId = me != null ? me.id : App.Profile.BackendId;

            // ---- your profile card
            var card = UIKit.At(_lbContent, "Me", new Vector2(0.5f, 1), new Vector2(0, -86), new Vector2(700, 138));
            card.pivot = new Vector2(0.5f, 1);
            UIKit.Image(card, UIKit.Rounded, new Color(0.62f, 0.38f, 1f, 0.16f));
            var ol = card.gameObject.AddComponent<Outline>(); ol.effectColor = new Color(0.7f, 0.45f, 1f, 0.7f); ol.effectDistance = new Vector2(1.5f, -1.5f);
            var face = SocialUi.Portrait(card, App.Profile.Look, 92, new Vector2(16, 14));
            ((RectTransform)face.transform.parent).anchorMin = ((RectTransform)face.transform.parent).anchorMax = new Vector2(0, 0.5f);
            var nm = UIKit.LabelAt(card, me != null ? me.name : App.Profile.Name, 26, Color.white, new Vector2(0, 1), new Vector2(124, -14), new Vector2(330, 32), TextAnchor.MiddleLeft, UIKit.TitleFont);
            nm.rectTransform.pivot = new Vector2(0, 1); UIKit.Fit(nm, 14);
            var lv = UIKit.LabelAt(card, me != null ? $"LEVEL {me.level}  ·  {me.xp}/{me.xpToNext} XP" : "Play online to get ranked", 15, Theme.PurpleLight, new Vector2(0, 1), new Vector2(124, -48), new Vector2(330, 20), TextAnchor.MiddleLeft, UIKit.BoldFont);
            lv.rectTransform.pivot = new Vector2(0, 1);
            var trophy = UIKit.At(card, "Trophy", new Vector2(1, 1), new Vector2(-24, -20), new Vector2(34, 34));
            trophy.pivot = new Vector2(1, 1);
            UIKit.Image(trophy, Icons.Trophy, Gold);
            var rating = UIKit.LabelAt(card, me != null ? me.rating.ToString() : "—", 40, Color.white, new Vector2(1, 1), new Vector2(-66, -14), new Vector2(200, 46), TextAnchor.MiddleRight, UIKit.TitleFont);
            rating.rectTransform.pivot = new Vector2(1, 1);
            string[] stat = { "MATCHES", "WINS", "TOP 3", "ELIMS" };
            int[] val = me != null ? new[] { me.matches, me.wins, me.top3, me.eliminations } : new int[4];
            for (int i = 0; i < 4; i++)
            {
                var chip = UIKit.At(card, "Stat", new Vector2(0, 0), new Vector2(124 + i * 140, 12), new Vector2(132, 40));
                chip.pivot = Vector2.zero;
                UIKit.Image(chip, UIKit.RoundedSmall, new Color(0, 0, 0, 0.3f));
                var t = UIKit.LabelAt(chip, $"<color=#9aa0c8>{stat[i]}</color>  {val[i]}", 15, Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(128, 36), TextAnchor.MiddleCenter, UIKit.BoldFont);
                t.supportRichText = true; UIKit.Fit(t, 10);
            }

            // ---- list (global or you + your friends)
            var list = new System.Collections.Generic.List<ProfileDto>();
            if (_lbPlayers != null)
            {
                var friendIds = new System.Collections.Generic.HashSet<string>();
                foreach (var f in App.Gateway.Friends.friends) friendIds.Add(f.id);
                foreach (var pl in _lbPlayers) if (!_lbFriends || pl.id == myId || friendIds.Contains(pl.id)) list.Add(pl);
            }
            if (_lbPlayers != null && list.Count == 0) _lbStatus.text = _lbFriends ? "<color=#8a90b8>No friends ranked yet — add friends in FRIENDS</color>" : "<color=#8a90b8>No ranked matches yet — play an online match!</color>";
            else if (_lbPlayers != null) _lbStatus.text = "";

            // ---- podium: 2nd · 1st · 3rd
            int[] order = { 1, 0, 2 };
            float[] px = { -220, 0, 220 }, ph = { 70, 96, 56 };
            Color[] pc = { Silver, Gold, Bronze };
            for (int k = 0; k < 3; k++)
            {
                int r = order[k];
                if (r >= list.Count) continue;
                var pl = list[r];
                var col = UIKit.At(_lbContent, "Podium" + r, new Vector2(0.5f, 1), new Vector2(px[k], -246), new Vector2(200, 220));
                col.pivot = new Vector2(0.5f, 1);
                var block = UIKit.At(col, "Block", new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(196, ph[k]));
                block.pivot = new Vector2(0.5f, 0);
                UIKit.Image(block, UIKit.RoundedSmall, new Color(pc[k].r * 0.75f, pc[k].g * 0.75f, pc[k].b * 0.75f, 0.95f));
                var bn = UIKit.LabelAt(block, pl.name, 18, new Color(0.1f, 0.08f, 0.16f), new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(184, 24), TextAnchor.MiddleCenter, UIKit.TitleFont); UIKit.Fit(bn, 11);
                var br = UIKit.LabelAt(block, $"{pl.rating}", 15, new Color(0.15f, 0.1f, 0.2f), new Vector2(0.5f, 1), new Vector2(0, -38), new Vector2(184, 20), TextAnchor.MiddleCenter, UIKit.BoldFont);
                var ring = UIKit.At(col, "Ring", new Vector2(0.5f, 0), new Vector2(0, ph[k] + 8), new Vector2(84, 84));
                ring.pivot = new Vector2(0.5f, 0);
                UIKit.Image(ring, UIKit.Circle, pc[k]);
                var f2 = SocialUi.Portrait(ring, SocialUi.ParseLook(pl.appearance), 76, new Vector2(4, 0));
                var crown = UIKit.At(col, "Crown", new Vector2(0.5f, 0), new Vector2(0, ph[k] + 90), new Vector2(r == 0 ? 44 : 34, r == 0 ? 36 : 28));
                crown.pivot = new Vector2(0.5f, 0);
                UIKit.Image(crown, Icons.Crown, pc[k]);
            }

            // ---- ranked rows from 4th; your row is always visible (pinned last if you're further down)
            const int rows = 6;
            int myIdx = list.FindIndex(x => x.id == myId);
            var show = new System.Collections.Generic.List<int>();
            for (int i = 3; i < list.Count && show.Count < rows; i++) show.Add(i);
            if (myIdx >= 3 && !show.Contains(myIdx)) { if (show.Count == rows) show[rows - 1] = myIdx; else show.Add(myIdx); }
            for (int n = 0; n < show.Count; n++)
            {
                int i = show[n];
                var pl = list[i];
                bool mine = i == myIdx;
                var row = UIKit.At(_lbContent, "Row", new Vector2(0.5f, 1), new Vector2(0, -486 - n * 50), new Vector2(700, 44));
                row.pivot = new Vector2(0.5f, 1);
                UIKit.Image(row, UIKit.RoundedSmall, mine ? new Color(1f, 0.82f, 0.25f, 0.95f) : new Color(1, 1, 1, n % 2 == 0 ? 0.06f : 0.03f));
                Color ink = mine ? new Color(0.12f, 0.08f, 0.2f) : Color.white, dim = mine ? new Color(0.25f, 0.18f, 0.3f) : Theme.TextDim;
                var rk = UIKit.LabelAt(row, $"{i + 1}", 18, dim, new Vector2(0, 0.5f), new Vector2(26, 0), new Vector2(40, 30), TextAnchor.MiddleCenter, UIKit.TitleFont);
                UIKit.Fit(rk, 11);
                SocialUi.Portrait(row, SocialUi.ParseLook(pl.appearance), 36, new Vector2(70, 0));
                var rn = UIKit.LabelAt(row, mine ? pl.name + "  (YOU)" : pl.name, 18, ink, new Vector2(0, 0.5f), new Vector2(116, 0), new Vector2(300, 30), TextAnchor.MiddleLeft, UIKit.BoldFont);
                rn.rectTransform.pivot = new Vector2(0, 0.5f); UIKit.Fit(rn, 11);
                var rl = UIKit.LabelAt(row, $"LV {pl.level}", 15, dim, new Vector2(1, 0.5f), new Vector2(-250, 0), new Vector2(90, 30), TextAnchor.MiddleCenter, UIKit.BoldFont);
                var rw = UIKit.LabelAt(row, $"{pl.wins} W", 15, dim, new Vector2(1, 0.5f), new Vector2(-160, 0), new Vector2(80, 30), TextAnchor.MiddleCenter, UIKit.BoldFont);
                var rr = UIKit.LabelAt(row, pl.rating.ToString(), 20, ink, new Vector2(1, 0.5f), new Vector2(-56, 0), new Vector2(90, 30), TextAnchor.MiddleRight, UIKit.TitleFont);
                var ti = UIKit.At(row, "Trophy", new Vector2(1, 0.5f), new Vector2(-24, 0), new Vector2(22, 22));
                UIKit.Image(ti, Icons.Trophy, mine ? new Color(0.45f, 0.3f, 0.05f) : Gold);
            }
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
            // one-tap music pause next to the volume slider
            UnityEngine.UI.Button mt = null;
            mt = UIKit.Button(p, prof.MusicOn ? "MUSIC ON" : "MUSIC OFF", new Vector2(1, 1), new Vector2(-225, -30), new Vector2(150, 48), UIKit.ButtonStyle.Secondary, () =>
            {
                prof.MusicOn = !prof.MusicOn; Save();
                UIKit.ButtonLabel(mt).text = prof.MusicOn ? "MUSIC ON" : "MUSIC OFF";
            }, 18);
            ((RectTransform)mt.transform).pivot = new Vector2(1, 1);
            Widgets.SliderRow(p, new Vector2(0.5f, 1), new Vector2(0, -270), "SOUND EFFECTS", 0f, 1f, prof.SfxVolume, v => { prof.SfxVolume = v; Save(); }, v => Mathf.RoundToInt(v * 100) + "%");
            var q = new ChipRow(p, new Vector2(0.5f, 1), new Vector2(-10, -330), "GRAPHICS", new[] { "PERFORMANCE", "QUALITY" }, prof.Quality, i => { prof.Quality = i; Save(); }, 200);
            var f = new ChipRow(p, new Vector2(0.5f, 1), new Vector2(-10, -390), "DISPLAY", new[] { "WINDOWED", "FULLSCREEN" }, prof.Fullscreen ? 1 : 0, i => { prof.Fullscreen = i == 1; Save(); }, 200);
            if (Application.isMobilePlatform) f.Root.gameObject.SetActive(false);   // phones are always fullscreen
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

    /// <summary>
    /// End-of-match results (concept from Meshy, pro shooter style): big placement + winner banner, your score with the
    /// level bar, your squad's objective chain, stat tiles, your squad's player cards, PLAY AGAIN / LOBBY, and the top 3
    /// heroes on glowing podiums in front of the painted island deck.
    /// </summary>
    public sealed class ResultsScreen : ScreenBase
    {
        private readonly Text _rank, _placed, _title, _sub, _score, _level, _full;
        private readonly Bar _xp;
        private readonly RectTransform _chain, _tiles, _squad, _fullPanel;
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<RectTransform> _podiumTags = new List<RectTransform>();
        private float _t;
        private int _targetScore;

        /// <summary>Extraction mode: the squad that extracted (-1 = time ran out). Set before Fill.</summary>
        public static int Winner = -1;
        /// <summary>Chain stage reached by each squad at the end (0..4, 4 = Vault opened). Set before Fill.</summary>
        public static byte[] SquadStages;

        private static readonly Color Ink = new Color(0.03f, 0.04f, 0.1f, 0.78f);

        public ResultsScreen(RectTransform canvas, GameApp app) : base(canvas, app, "Results")
        {
            // ---- header: #4 PLACED | SQUAD D WINS / EXTRACTION COMPLETE
            var head = UIKit.At(Root, "Header", new Vector2(0, 1), new Vector2(0, -24), new Vector2(1180, 150));
            head.pivot = new Vector2(0, 1);
            var hb = UIKit.Image(head, UIKit.GradientH, new Color(0.03f, 0.03f, 0.1f, 0.85f));
            _rank = UIKit.LabelAt(head, "#1", 112, Theme.PurpleLight, new Vector2(0, 1), new Vector2(30, 4), new Vector2(230, 116), TextAnchor.MiddleCenter, UIKit.TitleFont);
            _rank.rectTransform.pivot = new Vector2(0, 1); _rank.fontStyle = FontStyle.Italic;
            UIKit.Shadow(_rank, 4, 0.7f);
            _placed = UIKit.LabelAt(head, "PLACED", 22, Theme.Gold, new Vector2(0, 0), new Vector2(145, 18), new Vector2(200, 28), TextAnchor.MiddleCenter, UIKit.BoldFont);
            var div = UIKit.At(head, "Div", new Vector2(0, 0.5f), new Vector2(290, 0), new Vector2(3, 104));
            UIKit.Image(div, UIKit.Square, new Color(1f, 0.82f, 0.3f, 0.8f));
            _title = UIKit.LabelAt(head, "", 64, Theme.Gold, new Vector2(0, 1), new Vector2(330, -14), new Vector2(820, 76), TextAnchor.MiddleLeft, UIKit.TitleFont);
            _title.rectTransform.pivot = new Vector2(0, 1); _title.fontStyle = FontStyle.Italic; UIKit.Fit(_title, 30);
            UIKit.Shadow(_title, 3, 0.6f);
            _sub = UIKit.LabelAt(head, "", 28, new Color(0.3f, 0.95f, 1f), new Vector2(0, 1), new Vector2(334, -94), new Vector2(820, 36), TextAnchor.MiddleLeft, UIKit.BoldFont);
            _sub.rectTransform.pivot = new Vector2(0, 1); _sub.fontStyle = FontStyle.Italic; UIKit.Fit(_sub, 14);

            // ---- main card: score + level, objective chain, stat tiles
            var card = UIKit.Panel(Root, "Card", new Vector2(0, 1), new Vector2(40, -196), new Vector2(720, 470), Ink).rectTransform;
            card.pivot = new Vector2(0, 1);
            var ys = UIKit.LabelAt(card, "YOUR SCORE", 18, Theme.TextDim, new Vector2(0, 1), new Vector2(28, -20), new Vector2(300, 24), TextAnchor.MiddleLeft, UIKit.BoldFont);
            ys.rectTransform.pivot = new Vector2(0, 1);
            _score = UIKit.LabelAt(card, "0", 60, Theme.Gold, new Vector2(0, 1), new Vector2(26, -42), new Vector2(330, 70), TextAnchor.MiddleLeft, UIKit.TitleFont);
            _score.rectTransform.pivot = new Vector2(0, 1);
            _level = UIKit.LabelAt(card, "", 18, Theme.Text, new Vector2(1, 1), new Vector2(-28, -24), new Vector2(330, 26), TextAnchor.MiddleRight, UIKit.BoldFont);
            _level.rectTransform.pivot = new Vector2(1, 1); _level.supportRichText = true; UIKit.Fit(_level, 11);
            _xp = new Bar(card, new Vector2(0, 1), new Vector2(28, -126), new Vector2(664, 12), new Color(0.3f, 0.95f, 1f), new Color(1, 1, 1, 0.1f));
            _xp.Root.pivot = new Vector2(0, 1);
            var ct = UIKit.LabelAt(card, "SQUAD OBJECTIVES", 16, Theme.TextDim, new Vector2(0, 1), new Vector2(28, -152), new Vector2(300, 22), TextAnchor.MiddleLeft, UIKit.BoldFont);
            ct.rectTransform.pivot = new Vector2(0, 1);
            _chain = UIKit.At(card, "Chain", new Vector2(0, 1), new Vector2(28, -180), new Vector2(664, 110));
            _chain.pivot = new Vector2(0, 1);
            _tiles = UIKit.At(card, "Tiles", new Vector2(0, 0), new Vector2(28, 24), new Vector2(664, 132));
            _tiles.pivot = Vector2.zero;

            // ---- your squad's cards (bottom left)
            _squad = UIKit.At(Root, "Squad", new Vector2(0, 0), new Vector2(40, 28), new Vector2(1000, 132));
            _squad.pivot = Vector2.zero;

            // ---- buttons (bottom right)
            var again = UIKit.Button(Root, "PLAY AGAIN", new Vector2(1, 0), new Vector2(-40, 120), new Vector2(380, 84), UIKit.ButtonStyle.Primary, PlayAgain, 38);
            ((RectTransform)again.transform).pivot = new Vector2(1, 0);
            var lobby = UIKit.Button(Root, "LOBBY", new Vector2(1, 0), new Vector2(-40, 28), new Vector2(380, 76), UIKit.ButtonStyle.Secondary, () => App.GoMenu(0), 28);
            ((RectTransform)lobby.transform).pivot = new Vector2(1, 0);
            var fullBtn = UIKit.Button(Root, "FULL RESULTS", new Vector2(1, 1), new Vector2(-40, -40), new Vector2(240, 50), UIKit.ButtonStyle.Ghost, ToggleFull, 18);
            ((RectTransform)fullBtn.transform).pivot = new Vector2(1, 1);

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

        /// <summary>Who stands on the podium: the squad that extracted (else the best squad), MVP first.</summary>
        public static List<PlayerResult> PodiumSquad(List<PlayerResult> results)
        {
            if (results.Count == 0) return new List<PlayerResult>();
            int sq = Winner >= 0 ? Winner : results[0].Squad;
            var squad = results.FindAll(r => r.Squad == sq);
            squad.Sort((a, b) => b.Total.CompareTo(a.Total));
            return squad;
        }

        private T Keep<T>(T c) where T : Component { _spawned.Add(c.gameObject); return c; }

        public void Fill(List<PlayerResult> results, int localId, bool online)
        {
            foreach (var g in _spawned) Object.Destroy(g);
            _spawned.Clear();
            _podiumTags.Clear();
            _fullPanel.gameObject.SetActive(false);
            PlayerResult me = null;
            foreach (var r in results) if (r.PlayerId == localId) me = r;
            if (me == null && results.Count > 0) me = results[0];
            PlayerResult mvp = results[0];
            foreach (var r in results) if (r.Total > mvp.Total) mvp = r;

            // header
            bool won = Winner >= 0 && Winner == me.Squad;
            _rank.text = "#" + me.SquadRank;
            _rank.fontSize = UIKit.Fs(112);
            _rank.color = me.SquadRank == 1 ? Theme.Gold : Theme.PurpleLight;
            if (!GameConfig.ExtractionMode) { _title.text = me.SquadRank == 1 ? "VICTORY" : $"SQUAD {(char)('A' + me.Squad)}"; _sub.text = "MATCH COMPLETE"; }
            else if (Winner < 0) { _title.text = "TIME UP"; _sub.text = "NO SQUAD EXTRACTED"; }
            else if (won) { _title.text = "VICTORY"; _sub.text = "YOUR SQUAD EXTRACTED"; }
            else { _title.text = $"SQUAD {(char)('A' + Winner)} WINS"; _sub.text = "EXTRACTION COMPLETE"; }

            // score + level
            _targetScore = me.Total;
            _t = 0;
            var op = App.OnlineProfile;
            _level.text = online && op != null ? $"LEVEL <color=#ffd84a>{op.level}</color>   {op.xp}/{op.xpToNext} XP" : "<color=#8a90b8>Offline match · no XP</color>";
            _xp.Root.gameObject.SetActive(online && op != null);
            if (op != null) _xp.Set(op.xpToNext > 0 ? (float)op.xp / op.xpToNext : 0, 10f);
            if (online) App.StartCoroutine(RefreshOnline());

            // objective chain: HACK → CAPTURE → COLLECT → VAULT → EXTRACT
            string[] steps = { "HACK", "CAPTURE", "COLLECT", "VAULT", "EXTRACT" };
            Sprite[] icons = { Icons.Target, Icons.Tower, Icons.Core, Icons.Vault, Icons.Trophy };
            int stage = SquadStages != null && me.Squad < SquadStages.Length ? SquadStages[me.Squad] : 0;
            for (int i = 0; i < 5; i++)
            {
                bool done = i < 4 ? stage > i : won;
                float x = i * 133f + 54f;
                if (i > 0)
                {
                    var link = Keep(UIKit.At(_chain, "Link", new Vector2(0, 1), new Vector2(x - 102, -33), new Vector2(71, 4)));
                    UIKit.Image(link, UIKit.Square, done ? new Color(0.3f, 0.95f, 1f, 0.9f) : new Color(1, 1, 1, 0.15f));
                }
                var hex = Keep(UIKit.At(_chain, "Step", new Vector2(0, 1), new Vector2(x - 31, -4), new Vector2(62, 62)));
                UIKit.Image(hex, UIKit.Circle, done ? new Color(0.62f, 0.38f, 1f, 1f) : new Color(1, 1, 1, 0.08f));
                var ring = UIKit.Fill(hex, "Ring", 0);
                UIKit.Image(ring, UIKit.Ring, done ? new Color(0.3f, 0.95f, 1f) : new Color(1, 1, 1, 0.25f));
                var ic = UIKit.At(hex, "Icon", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(32, 32));
                UIKit.Image(ic, icons[i], done ? Color.white : new Color(1, 1, 1, 0.35f));
                var lb = Keep(UIKit.LabelAt(_chain, steps[i], 15, done ? Color.white : Theme.TextDim, new Vector2(0, 1), new Vector2(x - 65, -74), new Vector2(130, 20), TextAnchor.MiddleCenter, UIKit.BoldFont));
                UIKit.Fit(lb, 10);
            }

            // stat tiles
            (string label, string value, Sprite icon, Color c)[] tiles =
            {
                ("ELIMINATIONS", $"{me.Elims}<size=16> / {me.Assists} / {me.Deaths}</size>", Icons.Target, new Color(1f, 0.4f, 0.45f)),
                ("RESOURCES", $"+{me.Resources}", Icons.Core, new Color(0.3f, 0.95f, 1f)),
                ("SURVIVAL", $"+{me.Survival}", Icons.Shield, new Color(0.45f, 1f, 0.55f)),
                ("BONUS", $"+{me.Bonus + me.SquadPoints}", Icons.Trophy, Theme.Gold),
            };
            for (int i = 0; i < tiles.Length; i++)
            {
                var tl = Keep(UIKit.At(_tiles, "Tile", new Vector2(0, 0), new Vector2(i * 168f, 0), new Vector2(160, 132)));
                tl.pivot = Vector2.zero;
                UIKit.Image(tl, UIKit.RoundedSmall, new Color(1, 1, 1, 0.05f));
                var ol = tl.gameObject.AddComponent<Outline>(); ol.effectColor = new Color(tiles[i].c.r, tiles[i].c.g, tiles[i].c.b, 0.35f); ol.effectDistance = new Vector2(1, -1);
                var ic = UIKit.At(tl, "Icon", new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(34, 34));
                ic.pivot = new Vector2(0.5f, 1);
                UIKit.Image(ic, tiles[i].icon, tiles[i].c);
                var v = UIKit.LabelAt(tl, tiles[i].value, 30, Color.white, new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(150, 40), TextAnchor.MiddleCenter, UIKit.TitleFont);
                v.supportRichText = true; UIKit.Fit(v, 14);
                var l = UIKit.LabelAt(tl, tiles[i].label, 14, Theme.TextDim, new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(150, 20), TextAnchor.MiddleCenter, UIKit.BoldFont);
                UIKit.Fit(l, 9);
            }

            // your squad's player cards
            var mates = results.FindAll(r => r.Squad == me.Squad);
            PlayerResult squadMvp = mates.Count > 0 ? mates[0] : me;
            foreach (var r in mates) if (r.Total > squadMvp.Total) squadMvp = r;
            for (int i = 0; i < mates.Count && i < 4; i++)
            {
                var r = mates[i];
                bool mine = r.PlayerId == localId;
                var pc = Keep(UIKit.At(_squad, "Mate", new Vector2(0, 0), new Vector2(i * 252f, 0), new Vector2(240, 120)));
                pc.pivot = Vector2.zero;
                UIKit.Image(pc, UIKit.RoundedSmall, mine ? new Color(0.62f, 0.38f, 1f, 0.55f) : Ink);
                var ol = pc.gameObject.AddComponent<Outline>(); ol.effectColor = mine ? new Color(0.8f, 0.6f, 1f, 0.9f) : new Color(1, 1, 1, 0.12f); ol.effectDistance = new Vector2(1.5f, -1.5f);
                var face = SocialUi.Portrait(pc, r.Look, 84, new Vector2(10, 0));
                var nm = UIKit.LabelAt(pc, r.Name, 19, Color.white, new Vector2(0, 1), new Vector2(102, -14), new Vector2(132, 26), TextAnchor.MiddleLeft, UIKit.BoldFont);
                nm.rectTransform.pivot = new Vector2(0, 1); UIKit.Fit(nm, 11);
                var k = UIKit.LabelAt(pc, $"KILLS  <color=#ffffff>{r.Elims}</color>", 15, Theme.TextDim, new Vector2(0, 1), new Vector2(102, -46), new Vector2(132, 22), TextAnchor.MiddleLeft, UIKit.BoldFont);
                k.rectTransform.pivot = new Vector2(0, 1); k.supportRichText = true;
                var sc = UIKit.LabelAt(pc, $"SCORE  <color=#ffd84a>{r.Total:N0}</color>", 15, Theme.TextDim, new Vector2(0, 1), new Vector2(102, -70), new Vector2(132, 22), TextAnchor.MiddleLeft, UIKit.BoldFont);
                sc.rectTransform.pivot = new Vector2(0, 1); sc.supportRichText = true; UIKit.Fit(sc, 10);
                if (r == squadMvp)
                {
                    var badge = UIKit.At(pc, "MVP", new Vector2(0, 0), new Vector2(14, 8), new Vector2(76, 24));
                    badge.pivot = Vector2.zero;
                    UIKit.Image(badge, UIKit.Pill, Theme.Gold);
                    UIKit.LabelAt(badge, "★ MVP", 14, new Color(0.15f, 0.1f, 0.2f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(76, 24), TextAnchor.MiddleCenter, UIKit.TitleFont);
                }
            }

            // podium name tags (follow the heroes' heads): the winning squad, MVP first
            var podium = PodiumSquad(results);
            for (int i = 0; i < Mathf.Min(1, podium.Count); i++)   // the MVP only (close-up)
            {
                var r = podium[i];
                var tag = Keep(UIKit.At(Root, "Podium" + i, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(260, 70)));
                _podiumTags.Add(tag);
                var mv = UIKit.LabelAt(tag, $"★ MVP · SQUAD {(char)('A' + r.Squad)}", 18, Theme.Gold, new Vector2(0.5f, 1), new Vector2(0, 30), new Vector2(260, 24), TextAnchor.MiddleCenter, UIKit.TitleFont);
                UIKit.Outline(mv, new Color(0, 0, 0, 0.8f), 2);
                var n = UIKit.LabelAt(tag, r.Name, 26, Color.white, new Vector2(0.5f, 1), Vector2.zero, new Vector2(260, 30), TextAnchor.MiddleCenter, UIKit.BoldFont);
                UIKit.Outline(n, new Color(0, 0, 0, 0.8f), 2);
                var s2 = UIKit.LabelAt(tag, $"{r.Total:N0}", 30, i == 0 ? Theme.Gold : Theme.Text, new Vector2(0.5f, 0), Vector2.zero, new Vector2(260, 36), TextAnchor.MiddleCenter, UIKit.TitleFont);
                UIKit.Outline(s2, new Color(0, 0, 0, 0.8f), 2);
            }

            // full table
            var sb = new System.Text.StringBuilder("<color=#aab0d8>    NAME            TOTAL    RES  ELIM  SURV  BONUS   K/A/D</color>\n");
            int lastSquad = -1;
            foreach (var r in results)
            {
                if (r.Squad != lastSquad)
                {
                    lastSquad = r.Squad;
                    sb.Append($"\n<color={(r.Squad == me.Squad ? "#7dff9a" : "#c7a6ff")}>#{r.SquadRank}  SQUAD {(char)('A' + r.Squad)}  ·  {r.SquadTotal:N0}{(r.Squad == Winner ? "  · EXTRACTED" : "")}</color>\n");
                }
                string line = $"    {r.Name,-15} {r.Total,5}   {r.Resources,4}  {r.Eliminations,4}  {r.Survival,4}  {r.Bonus + r.SquadPoints,5}   {r.Elims}/{r.Assists}/{r.Deaths}{(r == mvp ? "  MVP" : "")}";
                sb.Append(r.PlayerId == localId ? $"<color=#ffd84a>{line}</color>\n" : line + "\n");
            }
            _full.text = sb.ToString();
            Sfx.Play(won || me.SquadRank == 1 ? Sfx.Objective : Sfx.Capture, 0.9f);
        }

        private IEnumerator RefreshOnline()
        {
            yield return new WaitForSeconds(1f);
            if (string.IsNullOrEmpty(App.Profile.BackendId)) yield break;
            int oldRating = App.OnlineProfile != null ? App.OnlineProfile.rating : 0;
            int oldCoins = App.OnlineProfile != null ? App.OnlineProfile.coins : 0;
            App.FetchProfile(p =>
            {
                App.OnlineProfile = p;
                int d = p.rating - oldRating;
                int dc = p.coins - oldCoins;
                _level.text = $"LEVEL <color=#ffd84a>{p.level}</color>  {p.xp}/{p.xpToNext} XP  ·  RATING {p.rating} <color={(d >= 0 ? "#7dff9a" : "#ff7a8a")}>({(d >= 0 ? "+" : "")}{d})</color>" +
                              (dc > 0 ? $"  ·  <color=#ffd84a>+{dc} COINS</color>" : "");
                _xp.Root.gameObject.SetActive(true);
                _xp.Set(p.xpToNext > 0 ? (float)p.xp / p.xpToNext : 0, 10f);
            }, e => { });
        }

        public override void Update(float dt)
        {
            for (int i = 0; i < _podiumTags.Count; i++)
            {
                var sp = App.Cam.WorldToScreenPoint(App.Stage.HeadPoint(i));
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)Root, sp, null, out var lp);
                // close-up: the head is near the top edge — keep the MVP tag on screen, beside the face
                float top = ((RectTransform)Root).rect.height * 0.5f - 150f;
                _podiumTags[i].anchoredPosition = new Vector2(lp.x + 260f, Mathf.Min(lp.y + 40f, top));
            }
            _t += dt;
            float k = Mathf.Clamp01(_t / 1.6f);
            _score.text = Mathf.RoundToInt(_targetScore * (1 - Mathf.Pow(1 - k, 3))).ToString("N0");
            _xp.Set(_xp.Value, dt);
        }
    }

    // ====================================================================== PAUSE

    public sealed class PauseScreen : ScreenBase
    {
        public PauseScreen(RectTransform canvas, GameApp app) : base(canvas, app, "Pause")
        {
            var dim = UIKit.Fill(Root, "Dim");
            UIKit.Image(dim, UIKit.Square, new Color(0.02f, 0.02f, 0.08f, 0.6f), true);
            var panel = UIKit.Panel(Root, "Panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 720));
            var p = panel.transform;
            var t = UIKit.LabelAt(p, "PAUSED", 48, Theme.Text, new Vector2(0.5f, 1), new Vector2(0, -50), new Vector2(500, 60), TextAnchor.MiddleCenter, UIKit.TitleFont);
            UIKit.Button(p, "RESUME", new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(420, 76), UIKit.ButtonStyle.Primary, () => App.SetPaused(false), 36);
            Widgets.SliderRow(p, new Vector2(0.5f, 1), new Vector2(60, -250), "SENSITIVITY", 0.03f, 0.4f, app.Profile.Sensitivity, v => { app.Profile.Sensitivity = v; app.Profile.Save(); app.ApplySettings(); }, v => (v * 10).ToString("0.0"));
            Widgets.SliderRow(p, new Vector2(0.5f, 1), new Vector2(60, -480), "CAMERA", 2.6f, 10f, app.Profile.CamDistance, v => { app.Profile.CamDistance = v; app.Profile.Save(); app.CamRig.Distance = v; }, v => v.ToString("0.0") + " m");
            new ChipRow(p, new Vector2(0.5f, 1), new Vector2(-40, -410), "MUSIC", new[] { "ON", "OFF" }, app.Profile.MusicOn ? 0 : 1,
                i => { app.Profile.MusicOn = i == 0; app.Profile.Save(); app.ApplySettings(); }, 120);
            new ChipRow(p, new Vector2(0.5f, 1), new Vector2(-40, -330), "GYRO", new[] { "OFF", "FIRING", "ALWAYS" }, app.Profile.GyroMode,
                i => { app.Profile.GyroMode = i; app.Profile.Save(); }, 120);
            UIKit.Button(p, "LEAVE MATCH", new Vector2(0.5f, 0), new Vector2(0, 50), new Vector2(420, 64), UIKit.ButtonStyle.Secondary, () => { App.SetPaused(false); App.LeaveMatch(); }, 26);
            var note = UIKit.LabelAt(p, "Offline matches pause. Online matches keep running.", 16, Theme.TextDim, new Vector2(0.5f, 0), new Vector2(0, 130), new Vector2(500, 26), TextAnchor.MiddleCenter, UIKit.BodyFont);
        }
    }
}
