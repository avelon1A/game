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
            var sub1 = UIKit.LabelAt(Root, "15 PLAYERS  ·  15 MINUTES", 40, Color.white, new Vector2(0, 0.5f), new Vector2(170, -70), new Vector2(900, 50), TextAnchor.MiddleLeft, UIKit.BoldFont);
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
        private readonly Text[] _tabLabels = new Text[4];
        private readonly Image _tabUnderline;
        private Text _profileChip;

        // play tab
        private ChipRow _mode, _length;
        private RectTransform _onlineRow, _listRoot;
        private InputField _hostField;
        private Text _status, _readyLabel, _lobbyTitle, _countdown;
        private Button _ready;
        private bool _online;
        private bool _offlineCountdown;
        private float _countT;
        private readonly List<RectTransform> _rows = new List<RectTransform>();
        private bool _localReady;

        // characters tab
        private InputField _nameField;

        // leaderboard
        private Text _board, _myStats;

        public MenuScreen(RectTransform canvas, GameApp app) : base(canvas, app, "Menu")
        {
            // top bar
            var bar = UIKit.Rect(Root, "TopBar", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 92));
            UIKit.Image(bar, UIKit.Square, new Color(0.05f, 0.05f, 0.14f, 0.88f));
            var edge = UIKit.Rect(bar, "Edge", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(0, 3));
            UIKit.Image(edge, UIKit.Square, new Color(0.55f, 0.35f, 1f, 0.8f));
            var logo = Logo(bar, new Vector2(0, 0.5f), new Vector2(36, 0), 70);
            logo.rectTransform.pivot = new Vector2(0, 0.5f);

            string[] names = { "PLAY", "CHARACTERS", "LEADERBOARD", "SETTINGS" };
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var b = UIKit.Button(bar, names[i], new Vector2(0, 0.5f), new Vector2(300 + i * 210, 0), new Vector2(200, 60), UIKit.ButtonStyle.Tab, () => SelectTab(idx), 22);
                ((RectTransform)b.transform).pivot = new Vector2(0, 0.5f);
                _tabLabels[i] = UIKit.ButtonLabel(b);
            }
            var ul = UIKit.At(bar, "Underline", new Vector2(0, 0), new Vector2(300, 8), new Vector2(200, 5));
            ul.pivot = new Vector2(0, 0);
            _tabUnderline = UIKit.Image(ul, UIKit.Pill, Theme.Yellow);

            var chipRt = UIKit.At(bar, "Profile", new Vector2(1, 0.5f), new Vector2(-170, 0), new Vector2(330, 56));
            chipRt.pivot = new Vector2(1, 0.5f);
            UIKit.Image(chipRt, UIKit.Pill, new Color(1, 1, 1, 0.08f));
            _profileChip = UIKit.Label(chipRt, "", 20, Theme.Text, TextAnchor.MiddleCenter, UIKit.BoldFont);
            _profileChip.supportRichText = true;
            var quit = UIKit.Button(bar, "QUIT", new Vector2(1, 0.5f), new Vector2(-24, 0), new Vector2(130, 52), UIKit.ButtonStyle.Ghost, () => Application.Quit(), 20);
            ((RectTransform)quit.transform).pivot = new Vector2(1, 0.5f);
            if (Veil.Match.Platform.IsMobile && Application.platform == RuntimePlatform.IPhonePlayer) quit.gameObject.SetActive(false);

            for (int i = 0; i < 4; i++) _tabs[i] = UIKit.Fill(Root, "Tab" + i);
            BuildPlay(_tabs[0]);
            BuildCharacters(_tabs[1]);
            BuildLeaderboard(_tabs[2]);
            BuildSettings(_tabs[3]);
            ((RectTransform)bar.transform).SetAsLastSibling();

            App.Net.LobbyUpdated += _ => RefreshLobby();
            App.Net.Disconnected += reason => { _status.text = $"<color=#ff7a8a>Disconnected: {reason}</color>"; _localReady = false; RefreshLobby(); };
            App.Net.Welcomed += () => { _status.text = $"<color=#7dff9a>Connected to {App.Net.ServerName}</color>"; };
        }

        public override void Show(bool v)
        {
            base.Show(v);
            if (v)
            {
                _offlineCountdown = false;
                _localReady = false;
                RefreshLobby();
                RefreshProfileChip();
            }
        }

        public void SelectTab(int i)
        {
            Tab = i;
            for (int k = 0; k < 4; k++)
            {
                _tabs[k].gameObject.SetActive(k == i);
                _tabLabels[k].color = k == i ? Color.white : Theme.TextDim;
            }
            _tabUnderline.rectTransform.anchoredPosition = new Vector2(300 + i * 210, 8);
            if (i == 2) FetchLeaderboard();
        }

        private void RefreshProfileChip()
        {
            var op = App.OnlineProfile;
            _profileChip.text = op != null
                ? $"{App.Profile.Name}  <color=#c7a6ff>LV {op.level}</color>  <color=#ffd84a>★ {op.rating}</color>"
                : $"{App.Profile.Name}  <color=#8a90b8>offline</color>";
        }

        // ------------------------------------------------------------------ PLAY tab

        private void BuildPlay(RectTransform tab)
        {
            // left: pitch + controls
            var info = UIKit.Panel(tab, "Info", new Vector2(0, 0), new Vector2(40, 40), new Vector2(560, 330));
            info.rectTransform.pivot = new Vector2(0, 0);
            var h = UIKit.LabelAt(info.transform, "HOW TO PLAY", 26, Theme.Yellow, new Vector2(0, 1), new Vector2(26, -24), new Vector2(500, 34), TextAnchor.MiddleLeft, UIKit.TitleFont);
            h.rectTransform.pivot = new Vector2(0, 1);
            var body = UIKit.LabelAt(info.transform,
                "Complete your <color=#c7a6ff>hidden objectives</color>, grab resources and outthink 14 rivals.\n" +
                "Information is power: nobody sees everything.\n\n" +
                (Veil.Match.Platform.IsMobile
                    ? "<color=#ffd84a>Left thumb</color> move (push fully to sprint)\n<color=#ffd84a>Right side</color> drag to look   <color=#ffd84a>FIRE</color> hold to blast\n<color=#ffd84a>DASH  PULSE  DECOY</color> abilities   Market buttons appear inside the Market"
                    : "<color=#ffd84a>WASD</color> move   <color=#ffd84a>Mouse</color> aim   <color=#ffd84a>LMB</color> blast   <color=#ffd84a>Space</color> jump\n" +
                      "<color=#ffd84a>Shift</color> sprint   <color=#ffd84a>Q</color> Dash   <color=#ffd84a>E</color> Pulse   <color=#ffd84a>R</color> Decoy\n" +
                      "<color=#ffd84a>1/2/3</color> Market   <color=#ffd84a>Tab</color> players   <color=#ffd84a>Esc</color> pause"),
                19, Theme.Text, new Vector2(0, 1), new Vector2(26, -70), new Vector2(510, 240), TextAnchor.UpperLeft, UIKit.BodyFont);
            body.rectTransform.pivot = new Vector2(0, 1);
            body.supportRichText = true;
            body.lineSpacing = 1.15f;
            body.horizontalOverflow = HorizontalWrapMode.Wrap;

            // right: lobby panel
            var panel = UIKit.Panel(tab, "Lobby", new Vector2(1, 0.5f), new Vector2(-40, -40), new Vector2(560, 840));
            var p = panel.transform;
            _lobbyTitle = UIKit.LabelAt(p, "LOBBY", 34, Theme.Text, new Vector2(0, 1), new Vector2(28, -26), new Vector2(500, 40), TextAnchor.MiddleLeft, UIKit.TitleFont);
            _lobbyTitle.rectTransform.pivot = new Vector2(0, 1);

            _mode = new ChipRow(p, new Vector2(0, 1), new Vector2(-150, -104), "", new[] { "VS BOTS", "ONLINE" }, 0, i => SetOnline(i == 1), 170);
            _mode.Root.pivot = new Vector2(0, 0.5f);

            _onlineRow = UIKit.At(p, "OnlineRow", new Vector2(0, 1), new Vector2(28, -160), new Vector2(500, 50));
            _onlineRow.pivot = new Vector2(0, 0.5f);
            _hostField = Widgets.InputRow(_onlineRow, new Vector2(0, 0.5f), new Vector2(-160, 0), "", App.Profile.ServerHost, v => App.Profile.ServerHost = v.Trim(), 300);
            ((RectTransform)_hostField.transform.parent).pivot = new Vector2(0, 0.5f);
            var connect = UIKit.Button(_onlineRow, "CONNECT", new Vector2(1, 0.5f), new Vector2(0, 0), new Vector2(170, 48), UIKit.ButtonStyle.Secondary, Connect, 20);
            ((RectTransform)connect.transform).pivot = new Vector2(1, 0.5f);

            _status = UIKit.LabelAt(p, "", 17, Theme.TextDim, new Vector2(0, 1), new Vector2(28, -200), new Vector2(500, 26), TextAnchor.MiddleLeft, UIKit.BodyFont);
            _status.rectTransform.pivot = new Vector2(0, 1);
            _status.supportRichText = true;

            _listRoot = UIKit.At(p, "List", new Vector2(0, 1), new Vector2(28, -236), new Vector2(504, 420));
            _listRoot.pivot = new Vector2(0, 1);

            _length = new ChipRow(p, new Vector2(0, 0), new Vector2(-150, 160), "", new[] { "5 MIN", "10 MIN", "15 MIN" }, App.Profile.MatchMinutes >= 15 ? 2 : App.Profile.MatchMinutes >= 10 ? 1 : 0,
                i => { App.Profile.MatchMinutes = i == 0 ? 5 : i == 1 ? 10 : 15; App.Profile.Save(); }, 112);
            _length.Root.pivot = new Vector2(0, 0.5f);

            _ready = UIKit.Button(p, "READY", new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(500, 92), UIKit.ButtonStyle.Primary, OnReady, 52);
            _readyLabel = UIKit.ButtonLabel(_ready);
            _countdown = UIKit.LabelAt(tab, "", 120, Color.white, new Vector2(0.5f, 0.5f), new Vector2(-260, 60), new Vector2(400, 200), TextAnchor.MiddleCenter, UIKit.TitleFont);
            UIKit.Outline(_countdown, new Color(0.4f, 0.15f, 0.9f), 5);

            SetOnline(false);
            App.Net.ServerFound += OnServerFound;
        }

        // ---- LAN discovery: find the test server on this Wi-Fi so phones don't need an IP typed in
        private bool _discovering;
        private string _found;

        private static bool IsLoopback(string h) => string.IsNullOrWhiteSpace(h) || h == "127.0.0.1" || h == "localhost" || h == "::1";

        private IEnumerator DiscoverRoutine()
        {
            if (_discovering) yield break;
            _discovering = true; _found = null;
            for (int i = 0; i < 6 && _found == null; i++)
            {
                App.Net.Discover(App.Profile.ServerPort);
                float t = 0;
                while (t < 0.5f && _found == null) { t += Time.unscaledDeltaTime; yield return null; }
            }
            _discovering = false;
        }

        private void OnServerFound(string ip, int port, string name, int players)
        {
            if (!_discovering) return;
            _found = ip;
            App.Profile.ServerHost = ip;
            if (port > 0) App.Profile.ServerPort = port;
            App.Profile.Save();
            _hostField.SetTextWithoutNotify(ip);
            _status.text = $"Found <color=#b9f27c>{name}</color> on your Wi-Fi ({ip}) · {players} connected";
        }

        private void SetOnline(bool online)
        {
            if (_online && !online && App.Net.Status == VeilNetClient.State.Connected) App.Net.Disconnect();
            _online = online;
            _onlineRow.gameObject.SetActive(online);
            _status.text = online ? $"Server: {App.Profile.ServerHost}:{App.Profile.ServerPort} (UDP) · API :{App.Profile.HttpPort}" : "Practice against 14 bots. Same rules as online.";
            if (online && App.Net.Status != VeilNetClient.State.Connected && (Veil.Match.Platform.IsMobile || IsLoopback(App.Profile.ServerHost)))
            {
                _status.text = "Looking for a server on your Wi-Fi…";
                App.StartCoroutine(DiscoverRoutine());
            }
            _localReady = false;
            _offlineCountdown = false;
            RefreshLobby();
        }

        private void Connect()
        {
            App.Profile.Save();
            _status.text = "Connecting…";
            App.StartCoroutine(ConnectRoutine());
        }

        private IEnumerator ConnectRoutine()
        {
            // phones can't use 127.0.0.1 (that's the phone itself): find the server on the LAN first
            if (Veil.Match.Platform.IsMobile && IsLoopback(App.Profile.ServerHost))
            {
                _status.text = "Looking for a server on your Wi-Fi…";
                yield return DiscoverRoutine();
                while (_discovering) yield return null;
                if (IsLoopback(App.Profile.ServerHost))
                {
                    _status.text = "<color=#ff7a8a>No server found on this Wi-Fi. Type the server PC's IP above.</color>";
                    yield break;
                }
            }
            // 1) make sure we have a backend profile (REST)
            string url = App.BackendUrl;
            if (string.IsNullOrEmpty(App.Profile.BackendId))
            {
                yield return BackendApi.Register(url, App.Profile.Name, r =>
                {
                    App.Profile.BackendId = r.id;
                    App.Profile.BackendToken = r.token;
                    App.OnlineProfile = r.profile;
                    App.Profile.Save();
                }, e => _status.text = $"<color=#ffb070>Backend unreachable ({e}) — playing without stats</color>");
            }
            else
            {
                yield return BackendApi.GetProfile(url, App.Profile.BackendId, p => App.OnlineProfile = p, e => { });
                if (App.OnlineProfile == null)
                {
                    App.Profile.BackendId = "";
                    yield return BackendApi.Register(url, App.Profile.Name, r =>
                    {
                        App.Profile.BackendId = r.id; App.Profile.BackendToken = r.token; App.OnlineProfile = r.profile; App.Profile.Save();
                    }, e => { });
                }
                else yield return BackendApi.UpdateProfile(url, App.Profile.BackendId, App.Profile.BackendToken, App.Profile.Name, App.Profile.AppearanceString, p => App.OnlineProfile = p, e => { });
            }
            RefreshProfileChip();

            // 2) UDP game connection
            App.Net.Connect(App.Profile.ServerHost, App.Profile.ServerPort, new HelloMsg { Name = App.Profile.Name, Look = App.Profile.Look, ProfileId = App.Profile.BackendId ?? "" });
            float t = 0;
            while (App.Net.Status == VeilNetClient.State.Connecting && t < 6f) { t += Time.deltaTime; yield return null; }
            if (App.Net.Status != VeilNetClient.State.Connected) _status.text = $"<color=#ff7a8a>Could not reach game server {App.Profile.ServerHost}:{App.Profile.ServerPort}</color>";
        }

        private void OnReady()
        {
            if (_online)
            {
                if (App.Net.Status != VeilNetClient.State.Connected) { Connect(); return; }
                _localReady = !_localReady;
                App.Net.SendReady(_localReady, App.Profile.MatchMinutes * 60);
                RefreshLobby();
                return;
            }
            _offlineCountdown = !_offlineCountdown;
            _countT = 3.99f;
            _localReady = _offlineCountdown;
            RefreshLobby();
        }

        private void ClearRows()
        {
            foreach (var r in _rows) Object.Destroy(r.gameObject);
            _rows.Clear();
        }

        private void AddRow(string name, Appearance look, string tag, Color tagColor, bool highlight)
        {
            int i = _rows.Count;
            if (i >= 15) return;
            float h = 27;
            var rt = UIKit.At(_listRoot, "Row", new Vector2(0, 1), new Vector2(0, -i * (h + 1)), new Vector2(504, h));
            rt.pivot = new Vector2(0, 1);
            UIKit.Image(rt, UIKit.RoundedSmall, highlight ? new Color(0.62f, 0.38f, 1f, 0.35f) : new Color(1, 1, 1, i % 2 == 0 ? 0.06f : 0.03f));
            var face = UIKit.At(rt, "Face", new Vector2(0, 0.5f), new Vector2(4, 0), new Vector2(h - 2, h - 2));
            face.pivot = new Vector2(0, 0.5f);
            var raw = face.gameObject.AddComponent<RawImage>();
            raw.texture = PortraitStudio.Get(look);
            raw.raycastTarget = false;
            var n = UIKit.LabelAt(rt, name, 17, Theme.Text, new Vector2(0, 0.5f), new Vector2(40, 0), new Vector2(300, h), TextAnchor.MiddleLeft, UIKit.BoldFont);
            n.rectTransform.pivot = new Vector2(0, 0.5f);
            var tg = UIKit.LabelAt(rt, tag, 15, tagColor, new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(160, h), TextAnchor.MiddleRight, UIKit.BoldFont);
            tg.rectTransform.pivot = new Vector2(1, 0.5f);
            _rows.Add(rt);
        }

        private void RefreshLobby()
        {
            if (_listRoot == null) return;
            ClearRows();
            if (!_online)
            {
                _lobbyTitle.text = "LOBBY  <size=22><color=#aab0d8>vs bots</color></size>";
                _lobbyTitle.supportRichText = true;
                AddRow(App.Profile.Name + " (you)", App.Profile.Look, _localReady ? "● Ready" : "Not ready", _localReady ? Theme.Green : Theme.TextDim, true);
                string[] kinds = { "Explorer", "Collector", "Hunter", "Defender", "Opportunist" };
                for (int i = 0; i < App.Profile.Bots; i++)
                {
                    var look = Appearance.Preset(i + 1);
                    look.Color = (byte)(((i + 1) * 3) % 8);
                    look.HairColor = (byte)(((i + 1) * 5 + 1) % 8);
                    AddRow(MatchSim.BotNames[(i + 1) % MatchSim.BotNames.Length], look, "BOT · " + kinds[i % 5], Theme.Cyan, false);
                }
                _readyLabel.text = _offlineCountdown ? "CANCEL" : "READY";
                return;
            }

            var lobby = App.Net.Lobby;
            bool connected = App.Net.Status == VeilNetClient.State.Connected;
            _lobbyTitle.supportRichText = true;
            _lobbyTitle.text = connected && lobby != null ? $"LOBBY  <size=22><color=#aab0d8>{lobby.ServerName}</color></size>" : "LOBBY  <size=22><color=#aab0d8>online</color></size>";
            if (connected && lobby != null)
            {
                foreach (var e in lobby.Entries)
                {
                    bool me = e.ClientId == App.Net.ClientId;
                    AddRow(e.Name + (me ? " (you)" : "") + (e.IsHost ? "  ★" : ""), e.Look, e.Ready ? "● Ready" : "Not ready", e.Ready ? Theme.Green : Theme.TextDim, me);
                }
                int bots = Mathf.Max(0, lobby.TotalPlayers - lobby.Entries.Count);
                if (bots > 0) AddRow($"+{bots} bots will fill the match", Appearance.Preset(2), "AUTO", Theme.Cyan, false);
                string st = lobby.Status == LobbyStatus.Countdown ? $"Starting in {Mathf.CeilToInt(lobby.Countdown)}…" :
                            lobby.Status == LobbyStatus.InMatch ? "Match in progress — you'll join the next one" :
                            lobby.Status == LobbyStatus.Results ? "Previous match finishing…" : $"Waiting for players to ready up · {lobby.MatchSeconds / 60} min match";
                _status.text = $"<color=#7dff9a>●</color> {st}   <color=#8a90b8>ping {App.Net.Ping}ms</color>";
                _readyLabel.text = _localReady ? "CANCEL" : "READY";
            }
            else _readyLabel.text = "CONNECT";
        }

        public override void Update(float dt)
        {
            if (Tab == 0 && !_online && _offlineCountdown)
            {
                int before = Mathf.CeilToInt(_countT);
                _countT -= dt;
                int after = Mathf.CeilToInt(_countT);
                if (after != before && after > 0) Sfx.Play(Sfx.Beep, 0.8f);
                _countdown.text = after > 0 ? after.ToString() : "GO!";
                if (_countT <= 0)
                {
                    _offlineCountdown = false;
                    _countdown.text = "";
                    App.StartOfflineMatch();
                }
            }
            else if (_online && App.Net.Lobby != null && App.Net.Lobby.Status == LobbyStatus.Countdown)
                _countdown.text = Mathf.CeilToInt(App.Net.Lobby.Countdown).ToString();
            else _countdown.text = "";
        }

        // ------------------------------------------------------------------ CHARACTERS tab

        private void BuildCharacters(RectTransform tab)
        {
            var panel = UIKit.Panel(tab, "Customize", new Vector2(1, 0.5f), new Vector2(-40, -40), new Vector2(760, 820));
            var p = panel.transform;
            var title = UIKit.LabelAt(p, "CHARACTER", 34, Theme.Text, new Vector2(0, 1), new Vector2(30, -26), new Vector2(500, 40), TextAnchor.MiddleLeft, UIKit.TitleFont);
            title.rectTransform.pivot = new Vector2(0, 1);
            var note = UIKit.LabelAt(p, CharacterRig.HasModel(0) ? "AI-generated 3D characters active: OUTFIT picks the character. Cosmetic only." : "Cosmetic only — no gameplay advantage. Right-drag the character to rotate.", 16, Theme.TextDim, new Vector2(0, 1), new Vector2(30, -68), new Vector2(700, 24), TextAnchor.MiddleLeft, UIKit.BodyFont);
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
            var r1 = new ChipRow(p, new Vector2(0, 1), new Vector2(30, -210), "OUTFIT", outfitNames, prof.Look.Outfit, i => { var l = prof.Look; l.Outfit = (byte)i; prof.Look = l; Changed(); }, 102);
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
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                var b = UIKit.Button(p, Palette.Outfits[i].Name.ToUpper(), new Vector2(0, 1), new Vector2(210 + i * 104, -620), new Vector2(96, 44), UIKit.ButtonStyle.Secondary, () =>
                {
                    prof.Look = Appearance.Preset(idx);
                    prof.Save();
                    App.Stage.UpdateLook(prof.Look);
                    // rebuild tab to refresh selections
                    Object.Destroy(tab.GetChild(0).gameObject);
                    BuildCharacters(tab);
                }, 15);
                ((RectTransform)b.transform).pivot = new Vector2(0, 0.5f);
            }
            var play = UIKit.Button(p, "PLAY", new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(680, 84), UIKit.ButtonStyle.Primary, () => SelectTab(0), 44);
        }

        // ------------------------------------------------------------------ LEADERBOARD tab

        private void BuildLeaderboard(RectTransform tab)
        {
            var panel = UIKit.Panel(tab, "Board", new Vector2(1, 0.5f), new Vector2(-40, -40), new Vector2(760, 820));
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
            App.StartCoroutine(BackendApi.Leaderboard(App.BackendUrl, lb =>
            {
                var sb = new System.Text.StringBuilder("<color=#aab0d8>#    PLAYER                 RATING   LV   WINS   BEST</color>\n\n");
                int i = 1;
                if (lb.players != null)
                    foreach (var pl in lb.players)
                        sb.Append($"{i++,-4} {pl.name,-22} {pl.rating,6}   {pl.level,3}   {pl.wins,4}   {pl.bestScore,5}\n");
                if (i == 1) sb.Append("<color=#8a90b8>No ranked matches yet — play an online match!</color>");
                _board.text = sb.ToString();
            }, e => _board.text = $"<color=#ff9a8a>Backend not reachable at {App.BackendUrl}</color>\n<color=#8a90b8>Start the test server: Server/run-server.sh</color>"));
        }

        // ------------------------------------------------------------------ SETTINGS tab

        private void BuildSettings(RectTransform tab)
        {
            var panel = UIKit.Panel(tab, "Settings", new Vector2(0.5f, 0.5f), new Vector2(0, -50), new Vector2(900, 900));
            var p = panel.transform;
            var title = UIKit.LabelAt(p, "SETTINGS", 34, Theme.Text, new Vector2(0, 1), new Vector2(40, -30), new Vector2(500, 40), TextAnchor.MiddleLeft, UIKit.TitleFont);
            title.rectTransform.pivot = new Vector2(0, 1);
            var prof = App.Profile;
            void Save() { prof.Save(); App.ApplySettings(); }
            Widgets.SliderRow(p, new Vector2(0.5f, 1), new Vector2(0, -130), "MOUSE SENSITIVITY", 0.03f, 0.4f, prof.Sensitivity, v => { prof.Sensitivity = v; Save(); }, v => (v * 10).ToString("0.0"));
            Widgets.SliderRow(p, new Vector2(0.5f, 1), new Vector2(0, -200), "MUSIC", 0f, 1f, prof.Music, v => { prof.Music = v; Save(); }, v => Mathf.RoundToInt(v * 100) + "%");
            Widgets.SliderRow(p, new Vector2(0.5f, 1), new Vector2(0, -270), "SOUND EFFECTS", 0f, 1f, prof.SfxVolume, v => { prof.SfxVolume = v; Save(); }, v => Mathf.RoundToInt(v * 100) + "%");
            Widgets.SliderRow(p, new Vector2(0.5f, 1), new Vector2(0, -340), "BOTS (OFFLINE)", 1f, 14f, prof.Bots, v => { prof.Bots = Mathf.RoundToInt(v); Save(); RefreshLobby(); }, v => Mathf.RoundToInt(v).ToString()).wholeNumbers = true;
            var q = new ChipRow(p, new Vector2(0.5f, 1), new Vector2(-10, -420), "GRAPHICS", new[] { "PERFORMANCE", "QUALITY" }, prof.Quality, i => { prof.Quality = i; Save(); }, 200);
            var f = new ChipRow(p, new Vector2(0.5f, 1), new Vector2(-10, -490), "DISPLAY", new[] { "WINDOWED", "FULLSCREEN" }, prof.Fullscreen ? 1 : 0, i => { prof.Fullscreen = i == 1; Save(); }, 200);
            var hp = Widgets.InputRow(p, new Vector2(0.5f, 1), new Vector2(-80, -560), "SERVER", prof.ServerHost, v => { prof.ServerHost = v.Trim(); prof.Save(); }, 340);
            // gyroscope aiming (phones)
            var gm = new ChipRow(p, new Vector2(0.5f, 1), new Vector2(-10, -630), "GYROSCOPE", new[] { "OFF", "WHILE FIRING", "ALWAYS" }, prof.GyroMode, i => { prof.GyroMode = i; Save(); }, 160);
            Widgets.SliderRow(p, new Vector2(0.5f, 1), new Vector2(0, -700), "GYRO SENSITIVITY", 0.2f, 3f, prof.GyroSensitivity, v => { prof.GyroSensitivity = v; Save(); }, v => v.ToString("0.0") + "x");
            int inv = (prof.GyroInvertX ? 1 : 0) + (prof.GyroInvertY ? 2 : 0);
            var gi = new ChipRow(p, new Vector2(0.5f, 1), new Vector2(-10, -770), "GYRO INVERT", new[] { "NONE", "HORIZONTAL", "VERTICAL", "BOTH" }, inv,
                i => { prof.GyroInvertX = (i & 1) != 0; prof.GyroInvertY = (i & 2) != 0; Save(); }, 130);
            var help = UIKit.LabelAt(p, Veil.Match.Platform.IsMobile ? "Gyroscope: turn and tilt your phone to aim." : "F9 in a match skips 60 seconds (testing).", 16, Theme.TextDim, new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(800, 30), TextAnchor.MiddleCenter, UIKit.BodyFont);
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
            var mc = UIKit.LabelAt(p, "MATCH COMPLETE", 26, Theme.Text, new Vector2(0, 1), new Vector2(290, -48), new Vector2(320, 34), TextAnchor.MiddleLeft, UIKit.BoldFont);
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
            if (App.Net.Status == VeilNetClient.State.Connected) App.GoMenu(0);
            else App.StartOfflineMatch();
        }

        public void Fill(List<PlayerResult> results, int localId, bool online)
        {
            foreach (var g in _spawned) Object.Destroy(g);
            _spawned.Clear();
            _podiumTags.Clear();
            _fullPanel.gameObject.SetActive(false);
            PlayerResult me = null;
            foreach (var r in results) if (r.PlayerId == localId) me = r;
            if (me == null && results.Count > 0) me = results[0];
            _rank.text = "#" + me.Rank;
            _rank.fontSize = me.Rank >= 10 ? 104 : 150;
            _rank.color = me.Rank == 1 ? Theme.Gold : me.Rank <= 3 ? Theme.PurpleLight : Theme.Text;
            _targetScore = me.Total;
            _t = 0;
            string Row(string label, int v) => $"{label,-22}<color=#ffd84a>+{v}</color>\n";
            _breakdown.text =
                Row("Primary Objective", me.Primary) + Row("Secondary Objective", me.Secondary) + Row("Resources", me.Resources) +
                Row("Territory Control", me.Territory) + Row("Eliminations", me.Eliminations) + Row("Survival", me.Survival) + Row("Bonus", me.Bonus);
            string pObj = $"{ObjectiveState.Title(me.PrimaryType)} {(me.PrimaryDone ? "<color=#7dff9a>✓</color>" : "<color=#ff7a8a>✗</color>")}";
            string sObj = $"{ObjectiveState.Title(me.SecondaryType)} {(me.SecondaryDone ? "<color=#7dff9a>✓</color>" : "<color=#ff7a8a>✗</color>")}";
            _extra.text = $"{pObj}   ·   {sObj}   ·   K/D {me.Elims}/{me.Deaths}\n<color=#aab0d8>Where did you make the mistake? Outthink them next time.</color>";
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
                var rk = UIKit.LabelAt(card, r.Rank.ToString(), 22, Theme.Gold, new Vector2(0, 1), new Vector2(10, -6), new Vector2(40, 30), TextAnchor.UpperLeft, UIKit.TitleFont);
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

            var sb = new System.Text.StringBuilder("<color=#aab0d8>#   NAME            TOTAL   PRIM  SEC   RES  TERR  ELIM  SURV  BONUS   K/D</color>\n\n");
            foreach (var r in results)
            {
                string line = $"{r.Rank,-3} {r.Name,-15} {r.Total,5}   {r.Primary,4}  {r.Secondary,3}  {r.Resources,4}  {r.Territory,4}  {r.Eliminations,4}  {r.Survival,4}  {r.Bonus,5}   {r.Elims}/{r.Deaths}";
                sb.Append(r.PlayerId == localId ? $"<color=#ffd84a>{line}</color>\n" : line + "\n");
            }
            _full.text = sb.ToString();
            Sfx.Play(me.Rank == 1 ? Sfx.Objective : Sfx.Capture, 0.9f);
        }

        private IEnumerator RefreshOnline()
        {
            yield return new WaitForSeconds(1f);
            var id = App.Profile.BackendId;
            if (string.IsNullOrEmpty(id)) yield break;
            int oldRating = App.OnlineProfile != null ? App.OnlineProfile.rating : 0;
            yield return BackendApi.GetProfile(App.BackendUrl, id, p =>
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
