using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Veil.App;
using Veil.Audio;
using Veil.Net;
using Veil.Sim;
using Veil.View;
using Veil.Voice;

namespace Veil.UI
{
    /// <summary>Small helpers shared by the social screens.</summary>
    internal static class SocialUi
    {
        public static InputField Field(Transform parent, Vector2 anchor, Vector2 pos, float width, string placeholder, int limit = 24)
        {
            var fRt = UIKit.At(parent, "Field", anchor, pos, new Vector2(width, 48));
            fRt.pivot = new Vector2(0, 0.5f);
            var img = UIKit.Image(fRt, UIKit.RoundedSmall, new Color(1, 1, 1, 0.1f), true);
            var textRt = UIKit.Fill(fRt, "Text", 0);
            textRt.offsetMin = new Vector2(14, 4); textRt.offsetMax = new Vector2(-14, -4);
            var text = textRt.gameObject.AddComponent<Text>();
            text.font = UIKit.BoldFont; text.fontSize = UIKit.Fs(22); text.color = Theme.Text; text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            var phRt = UIKit.Fill(fRt, "Placeholder", 0);
            phRt.offsetMin = new Vector2(14, 4); phRt.offsetMax = new Vector2(-14, -4);
            var ph = phRt.gameObject.AddComponent<Text>();
            ph.font = UIKit.BodyFont; ph.fontSize = UIKit.Fs(20); ph.color = new Color(1, 1, 1, 0.35f); ph.alignment = TextAnchor.MiddleLeft; ph.text = placeholder;
            var field = fRt.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.placeholder = ph;
            field.targetGraphic = img;
            field.characterLimit = limit;
            return field;
        }

        public static Button SmallButton(Transform parent, string text, Vector2 anchor, Vector2 pos, float width, UIKit.ButtonStyle style, Action onClick, int size = 16)
        {
            var b = UIKit.Button(parent, text, anchor, pos, new Vector2(width, 40), style, onClick, size);
            ((RectTransform)b.transform).pivot = new Vector2(anchor.x, 0.5f);
            return b;
        }

        public static RawImage Portrait(RectTransform parent, Appearance look, float size, Vector2 pos)
        {
            var face = UIKit.At(parent, "Face", new Vector2(0, 0.5f), pos, new Vector2(size, size));
            face.pivot = new Vector2(0, 0.5f);
            UIKit.Image(face, UIKit.Circle, new Color(0.1f, 0.1f, 0.22f, 1f));
            var inner = UIKit.Fill(face, "Img", 3);
            var raw = inner.gameObject.AddComponent<RawImage>();
            raw.texture = PortraitStudio.Get(look);
            raw.raycastTarget = false;
            return raw;
        }

        public static Appearance ParseLook(string s)
        {
            var f = (s ?? "").Split(',');
            if (f.Length >= 5 && byte.TryParse(f[0], out var o) && byte.TryParse(f[1], out var h) && byte.TryParse(f[2], out var hc) && byte.TryParse(f[3], out var ac) && byte.TryParse(f[4], out var c))
                return new Appearance { Outfit = o, Hair = h, HairColor = hc, Accessory = ac, Color = c, Weapon = f.Length >= 6 && byte.TryParse(f[5], out var wp) ? wp : (byte)0 };
            return Appearance.Preset(0);
        }

        public static string CharacterName(Appearance a) => Palette.Outfits[a.Outfit % Palette.Outfits.Length].Name;

        public static Color StatusColor(int status) => status switch
        {
            (int)PresenceStatus.InMatch => Theme.Yellow,
            (int)PresenceStatus.InQueue => Theme.Cyan,
            (int)PresenceStatus.InParty => Theme.PurpleLight,
            (int)PresenceStatus.Online => Theme.Green,
            _ => new Color(0.5f, 0.52f, 0.62f),
        };

        public static void Clear(RectTransform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(root.GetChild(i).gameObject);
        }
    }

    // ====================================================================== squad lobby (PLAY tab)

    /// <summary>One squad member as the lobby shows it (party member, you, or a practice bot).</summary>
    public sealed class SquadMember
    {
        public string Id = "", Name = "", Handle = "";
        public Appearance Look;
        public int Level = 1, Ping;
        public bool Ready, Leader, Online = true, Bot, Me;
    }

    /// <summary>
    /// Squad lobby: member list (voice + ready), invite / copy code, mode card, match length, READY / START,
    /// name tags over the squad standing on the stage, and the voice controls pill. Online = Gateway party;
    /// VS BOTS = you + 3 practice bots.
    /// </summary>
    public sealed class SquadPanel
    {
        private sealed class Row
        {
            public RectTransform Root;
            public Image Bg, Mic, Spk;
            public Button SpkBtn, MicBtn, RowBtn;
            public RawImage Face;
            public Image Crown;
            public Text Name, Level, Status;
            public RectTransform Empty;
            public SquadMember M;
            public string LastId = "";
            public bool LastReady;
        }

        private sealed class Plate
        {
            public RectTransform Root;
            public Image Crown, Spk, ReadyBg, ReadyIcon, Accent, LevelBg;
            public Text Name, Level;
            public bool Shown, LastReady;
        }

        private readonly GameApp _app;
        private readonly RectTransform _tab, _squadPanel, _modePanel, _plates, _voicePill, _popup, _howTo;
        private readonly Text _title, _modeTitle, _modeSub, _status, _actionLabel, _countdown, _voiceMode;
        private readonly Button _leave, _joinCode, _action, _side, _copy, _invite;
        private readonly Image _modeThumbIcon, _micPillIcon, _spkPillIcon;
        private readonly InputField _hostField;
        private readonly RectTransform _connectRow;
        private readonly ChipRowCompact _length;
        private readonly Row[] _rows = new Row[GameConfig.SquadSize];
        private readonly Plate[] _plate = new Plate[GameConfig.SquadSize];
        private readonly List<SquadMember> _members = new List<SquadMember>();
        private bool _online, _offlineCountdown;
        private float _countT, _transientT;
        private string _transient = "";
        private string _stageKey = "";
        public Action OpenFriends;

        public bool Online => _online;

        public SquadPanel(RectTransform tab, GameApp app)
        {
            _app = app;
            _tab = tab;

            // ---------------- name tags over the squad on the stage
            _plates = UIKit.Fill(tab, "Plates");
            for (int i = 0; i < _plate.Length; i++) _plate[i] = MakePlate(_plates, i);

            // ---------------- squad panel
            _squadPanel = (RectTransform)UIKit.Panel(tab, "Squad", new Vector2(1, 0), new Vector2(-24, 128), new Vector2(440, 512)).transform;
            _squadPanel.pivot = new Vector2(1, 0);
            _title = UIKit.LabelAt(_squadPanel, "SQUAD", 30, Theme.Text, new Vector2(0, 1), new Vector2(22, -16), new Vector2(240, 44), TextAnchor.MiddleLeft, UIKit.TitleFont);
            _title.rectTransform.pivot = new Vector2(0, 1);
            _title.supportRichText = true;
            _leave = SocialUi.SmallButton(_squadPanel, "LEAVE", new Vector2(1, 1), new Vector2(-16, -38), 104, UIKit.ButtonStyle.Ghost, Leave, 15);
            _joinCode = SocialUi.SmallButton(_squadPanel, "JOIN CODE", new Vector2(1, 1), new Vector2(-126, -38), 124, UIKit.ButtonStyle.Ghost, () => ShowJoinPopup(), 14);
            for (int i = 0; i < _rows.Length; i++) _rows[i] = MakeRow(_squadPanel, i);
            _invite = IconButton(_squadPanel, Icons.Plus, "INVITE FRIEND", new Vector2(0, 0), new Vector2(16, 18), new Vector2(200, 50), () => OpenFriends?.Invoke());
            _copy = IconButton(_squadPanel, Icons.Copy, "COPY CODE", new Vector2(1, 0), new Vector2(-16, 18), new Vector2(200, 50), CopyCode);

            // ---------------- mode card
            _modePanel = (RectTransform)UIKit.Panel(tab, "Mode", new Vector2(1, 0), new Vector2(-24, 128), new Vector2(440, 176)).transform;
            _modePanel.pivot = new Vector2(1, 0);
            var thumb = UIKit.At(_modePanel, "Thumb", new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(130, 138));
            thumb.pivot = new Vector2(0, 0.5f);
            var tImg = UIKit.Image(thumb, UIKit.Rounded, new Color(0.1f, 0.12f, 0.2f));
            var modeArt = Resources.Load<Texture2D>("UI/title_keyart");
            if (modeArt != null)
            {
                var ar = UIKit.Fill(thumb, "Art", 3).gameObject.AddComponent<RawImage>();
                ar.texture = modeArt; ar.uvRect = new Rect(0.35f, 0.15f, 0.45f, 0.7f); ar.raycastTarget = false;
            }
            var ic = UIKit.At(thumb, "Icon", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(70, 70));
            _modeThumbIcon = UIKit.Image(ic, Icons.Players, Color.white);
            _modeTitle = UIKit.LabelAt(_modePanel, "", 24, Theme.Text, new Vector2(0, 1), new Vector2(160, -16), new Vector2(270, 32), TextAnchor.MiddleLeft, UIKit.TitleFont);
            _modeTitle.rectTransform.pivot = new Vector2(0, 1);
            _modeSub = UIKit.LabelAt(_modePanel, "", 15, Theme.TextDim, new Vector2(0, 1), new Vector2(160, -50), new Vector2(270, 60), TextAnchor.UpperLeft, UIKit.BodyFont);
            _modeSub.rectTransform.pivot = new Vector2(0, 1);
            _modeSub.horizontalOverflow = HorizontalWrapMode.Wrap;
            _modeSub.supportRichText = true;
            UIKit.Fit(_modeSub);   // shrinks instead of running into CHANGE MODE
            _connectRow = UIKit.At(_modePanel, "Connect", new Vector2(0, 0), new Vector2(160, 16), new Vector2(264, 48));
            _connectRow.pivot = new Vector2(0, 0);
            _hostField = SocialUi.Field(_connectRow, new Vector2(0, 0.5f), Vector2.zero, 150, "server", 40);
            _hostField.text = app.Profile.ServerAddress;
            _hostField.onEndEdit.AddListener(v => { if (v.Trim() != app.Profile.ServerAddress) app.Profile.SetServerAddress(v); });   // only a real edit pins a server
            SocialUi.SmallButton(_connectRow, "CONNECT", new Vector2(1, 0.5f), Vector2.zero, 106, UIKit.ButtonStyle.Secondary, () => app.GoOnline(), 14);
            var change = SocialUi.SmallButton(_modePanel, "CHANGE MODE", new Vector2(1, 0), new Vector2(-16, 38), 150, UIKit.ButtonStyle.Ghost, () => SetMode(!_online), 14);
            _changeBtn = change;

            // ---------------- length, status, ready
            _length = new ChipRowCompact(tab, new Vector2(1, 0), new Vector2(-24, 318), new[] { "5 MIN", "10 MIN", "15 MIN" },
                app.Profile.MatchMinutes >= 15 ? 2 : app.Profile.MatchMinutes >= 10 ? 1 : 0,
                i => { app.Profile.MatchMinutes = i == 0 ? 5 : i == 1 ? 10 : 15; app.Profile.Save(); UpdateStatus(); });
            // status sits beside READY (the right column has no spare height on 20:9 phones)
            _status = UIKit.LabelAt(tab, "", 17, Theme.Text, new Vector2(1, 0), new Vector2(-484, 68), new Vector2(620, 30), TextAnchor.MiddleRight, UIKit.BoldFont);
            _status.rectTransform.pivot = new Vector2(1, 0.5f);
            _status.supportRichText = true;
            UIKit.Outline(_status, new Color(0, 0, 0, 0.85f), 2);
            UIKit.Shadow(_status);
            _action = UIKit.Button(tab, "START", new Vector2(1, 0), new Vector2(-24, 24), new Vector2(340, 92), UIKit.ButtonStyle.Primary, OnAction, 50);
            ((RectTransform)_action.transform).pivot = new Vector2(1, 0);
            _actionLabel = UIKit.ButtonLabel(_action);
            _actionLabel.fontStyle = FontStyle.Italic;
            // checkered-flag corner on START (concept art)
            var flag = UIKit.At((RectTransform)_action.transform, "Flag", new Vector2(1, 0.5f), new Vector2(-14, 0), new Vector2(56, 56));
            flag.pivot = new Vector2(1, 0.5f);
            for (int fy = 0; fy < 4; fy++) for (int fx = 0; fx < 4; fx++)
                if ((fx + fy) % 2 == 0) { var sq = UIKit.At(flag, "Sq", new Vector2(0, 0), new Vector2(fx * 14, fy * 14), new Vector2(14, 14)); sq.pivot = Vector2.zero; UIKit.Image(sq, UIKit.Square, new Color(0.08f, 0.06f, 0.02f, 0.85f)).raycastTarget = false; }
            // squad button left of START: opens / closes the squad list
            _side = UIKit.Button(tab, "", new Vector2(1, 0), new Vector2(-380, 24), new Vector2(92, 92), UIKit.ButtonStyle.Secondary, ToggleSquadList, 20);
            ((RectTransform)_side.transform).pivot = new Vector2(1, 0);
            ((Image)_side.targetGraphic).color = new Color(0.04f, 0.05f, 0.1f, 0.85f);
            var sideIc = UIKit.At((RectTransform)_side.transform, "Icon", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(46, 46));
            UIKit.Image(sideIc, Icons.Players, Color.white);
            _squadPanel.gameObject.SetActive(false);

            // ---------------- right column cards (concept art): new map, battle pass, missions, daily rewards
            _cards = UIKit.At(tab, "Cards", new Vector2(1, 1), new Vector2(-24, -128), new Vector2(440, 470));
            _cards.pivot = new Vector2(1, 1);
            BuildCards(_cards);
            // ---------------- world chat bar (bottom-left)
            BuildChat(tab);
            // ---------------- "+" invite markers standing at the empty squad spots
            for (int i = 0; i < _plus.Length; i++)
            {
                var pb = UIKit.Button(tab, "+", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(80, 80), UIKit.ButtonStyle.Secondary, () => OpenFriends?.Invoke(), 52);
                ((Image)pb.targetGraphic).color = new Color(0.04f, 0.05f, 0.1f, 0.7f);
                _plus[i] = (RectTransform)pb.transform;
            }
            _countdown = UIKit.LabelAt(tab, "", 130, Color.white, new Vector2(0.5f, 0.5f), new Vector2(-240, 40), new Vector2(400, 200), TextAnchor.MiddleCenter, UIKit.TitleFont);
            UIKit.Outline(_countdown, new Color(0.4f, 0.15f, 0.9f), 5);

            // ---------------- voice pill (bottom-left) + how to play
            _voicePill = UIKit.At(tab, "Voice", new Vector2(0, 0), new Vector2(28, 110), new Vector2(330, 64));
            _voicePill.pivot = new Vector2(0, 0);
            UIKit.Image(_voicePill, UIKit.Pill, new Color(0.05f, 0.06f, 0.15f, 0.88f));
            var micB = PillIcon(_voicePill, Icons.Mic, new Vector2(12, 0), () => { _app.Voice.MicMuted = !_app.Voice.MicMuted; });
            _micPillIcon = micB;
            var spkB = PillIcon(_voicePill, Icons.Speaker, new Vector2(68, 0), () => { _app.Voice.Deafened = !_app.Voice.Deafened; });
            _spkPillIcon = spkB;
            var modeBtn = UIKit.Button(_voicePill, "", new Vector2(0, 0.5f), new Vector2(124, 0), new Vector2(194, 48), UIKit.ButtonStyle.Ghost, CycleVoiceMode, 15);
            ((RectTransform)modeBtn.transform).pivot = new Vector2(0, 0.5f);
            _voiceMode = UIKit.ButtonLabel(modeBtn);
            _voiceMode.supportRichText = true;
            var help = UIKit.Button(tab, "?", new Vector2(0, 0), new Vector2(366, 110), new Vector2(64, 64), UIKit.ButtonStyle.Secondary, () => { _howTo.gameObject.SetActive(!_howTo.gameObject.activeSelf); _howTo.SetAsLastSibling(); }, 30);
            ((RectTransform)help.transform).pivot = new Vector2(0, 0);
            _help = (RectTransform)help.transform;
            _howTo = BuildHowTo(tab);
            _howTo.gameObject.SetActive(false);

            _popup = UIKit.Fill(tab, "Popup");
            _popup.gameObject.SetActive(false);

            // ---------------- micro animations: panels glide in each time the lobby shows
            EnterFx.Add(_squadPanel, new Vector2(48, 0));
            for (int i = 0; i < _rows.Length; i++) EnterFx.Add(_rows[i].Root, new Vector2(36, 0), 0.08f + i * 0.05f);
            EnterFx.Add(_modePanel, new Vector2(48, 0), 0.1f);
            _length.Root.gameObject.SetActive(false);
            EnterFx.Add(_action, new Vector2(0, -28), 0.2f);
            EnterFx.Add(_side, new Vector2(0, -28), 0.24f);
            EnterFx.Add(_voicePill, new Vector2(-36, 0), 0.2f);
            EnterFx.Add(help, new Vector2(-36, 0), 0.16f);
            _action.GetComponent<ButtonFx>().Breathe = 0.025f;
            ShineFx.Add(_action);
            _modeThumbIcon.gameObject.AddComponent<BobFx>();
            PunchFx.On(_countdown);
            BuildMatchmaking(tab);

            app.Gateway.Changed += Refresh;
            app.Gateway.Notice += t => Flash(t);
            SetOnline(true);   // always opens in online squads; CHANGE MODE (practice) lasts for this session only
        }

        private Button _changeBtn;
        // ---------------- matchmaking card (top centre): FINDING MATCH → MATCH FOUND → countdown
        private RectTransform _mm, _mmRadar, _mmSweep;
        private Text _mmTitle, _mmTime, _mmSub;
        private Button _mmCancel;
        private readonly Image[] _mmDots = new Image[4];
        private int _mmState;          // 0 hidden, 1 searching, 2 found / countdown
        private float _mmFoundT;

        private void BuildMatchmaking(RectTransform tab)
        {
            _mm = UIKit.At(tab, "Matchmaking", new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(560, 190));
            _mm.pivot = new Vector2(0.5f, 1);
            UIKit.Image(_mm, UIKit.RoundedSmall, new Color(0.03f, 0.04f, 0.09f, 0.9f), true);
            var edge = UIKit.At(_mm, "Edge", new Vector2(0.5f, 1), Vector2.zero, new Vector2(560, 5)); edge.pivot = new Vector2(0.5f, 1);
            UIKit.Image(edge, UIKit.Square, Theme.Yellow);
            // radar: two rings + a rotating sweep
            _mmRadar = UIKit.At(_mm, "Radar", new Vector2(0, 0.5f), new Vector2(26, 0), new Vector2(130, 130)); _mmRadar.pivot = new Vector2(0, 0.5f);
            UIKit.Image(_mmRadar, UIKit.Ring, new Color(1f, 0.82f, 0.2f, 0.9f));
            var inner = UIKit.At(_mmRadar, "Inner", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(78, 78));
            UIKit.Image(inner, UIKit.Ring, new Color(1f, 0.82f, 0.2f, 0.45f));
            _mmSweep = UIKit.At(_mmRadar, "Sweep", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8, 62));
            _mmSweep.pivot = new Vector2(0.5f, 0f);
            UIKit.Image(_mmSweep, UIKit.Pill, Theme.Yellow);
            _mmTitle = UIKit.LabelAt(_mm, "", 34, Color.white, new Vector2(0, 1), new Vector2(180, -22), new Vector2(360, 44), TextAnchor.MiddleLeft, UIKit.TitleFont);
            _mmTitle.rectTransform.pivot = new Vector2(0, 1); _mmTitle.fontStyle = FontStyle.Italic; UIKit.Fit(_mmTitle, 18);
            _mmTime = UIKit.LabelAt(_mm, "", 46, Theme.Yellow, new Vector2(0, 1), new Vector2(180, -66), new Vector2(200, 56), TextAnchor.MiddleLeft, UIKit.TitleFont);
            _mmTime.rectTransform.pivot = new Vector2(0, 1);
            _mmSub = UIKit.LabelAt(_mm, "", 16, Theme.TextDim, new Vector2(0, 0), new Vector2(180, 18), new Vector2(240, 24), TextAnchor.MiddleLeft, UIKit.BoldFont);
            _mmSub.rectTransform.pivot = new Vector2(0, 0); _mmSub.supportRichText = true; UIKit.Fit(_mmSub, 10);
            for (int i = 0; i < 4; i++)
            {
                var d = UIKit.At(_mm, "Dot", new Vector2(1, 0), new Vector2(-150 + i * 34, 26), new Vector2(24, 24)); d.pivot = new Vector2(0, 0);
                _mmDots[i] = UIKit.Image(d, UIKit.Circle, new Color(1, 1, 1, 0.15f));
            }
            _mmCancel = UIKit.Button(_mm, "CANCEL", new Vector2(1, 1), new Vector2(-18, -20), new Vector2(130, 46), UIKit.ButtonStyle.Ghost, OnAction, 17);
            ((RectTransform)_mmCancel.transform).pivot = new Vector2(1, 1);
            _mm.gameObject.SetActive(false);
            _countdown.gameObject.SetActive(false);   // the countdown lives in the card now
        }

        private void UpdateMatchmaking(float dt)
        {
            var g = _app.Gateway;
            var party = g.Party;
            bool searching = _online && g.Online && !party.Empty && party.phase == (int)PartyPhase.Queued;
            bool found = (_online && g.Online && !party.Empty && party.phase == (int)PartyPhase.InMatch) || (!_online && _offlineCountdown);
            int state = found ? 2 : searching ? 1 : 0;
            if (state != _mmState)
            {
                if (state > 0 && _mmState == 0) PunchFx.On(_mm).Kick(-0.3f);
                if (state == 2)
                {
                    _mmFoundT = 0f;
                    PunchFx.On(_mm).Kick(0.35f);
                    Sfx.Play(Sfx.Objective, 0.9f);
                    _app.Stage.Cheer(2.2f);   // the squad celebrates on the dock
                }
                _mmState = state;
            }
            _mm.gameObject.SetActive(state > 0 && _app.State == GameApp.AppState.Menu);
            if (state == 0) return;
            _mmSweep.localRotation = Quaternion.Euler(0, 0, -Time.unscaledTime * (state == 1 ? 220f : 600f));
            _mmFoundT += dt;
            int n = Mathf.Max(1, _members.Count);
            if (state == 1)
            {
                int sec = party.queueSeconds;
                _mmTitle.text = "FINDING MATCH";
                _mmTime.text = $"{sec / 60}:{sec % 60:00}";
                _mmSub.text = "Squads · est. 0:30";
                _mmCancel.gameObject.SetActive(g.IsLeader);
                for (int i = 0; i < 4; i++) _mmDots[i].color = i < n ? Theme.Yellow : new Color(1, 1, 1, 0.15f * (1 + Mathf.Sin(Time.unscaledTime * 4 + i)));
            }
            else
            {
                _mmTitle.text = "MATCH FOUND!";
                int c = !_online ? Mathf.Max(0, Mathf.CeilToInt(_countT)) : 0;
                _mmTime.text = !_online ? (c > 0 ? $"STARTING IN {c}" : "GO!") : "DROPPING IN…";
                _mmSub.text = "<color=#7dff9a>●</color> 16 players · Rilo Island";
                _mmCancel.gameObject.SetActive(!_online);
                for (int i = 0; i < 4; i++) _mmDots[i].color = Theme.Green;
            }
        }
        private RectTransform _cards;
        private readonly RectTransform[] _plus = new RectTransform[3];
        private Text _chat;
        private int _chatIdx;
        private float _chatT;
        private Text _bpLevel, _missionsLabel;
        private RectTransform _missionsPanel;

        /// <summary>Today's three missions (Progression.DailyMissions): progress from the backend profile, coins paid automatically.</summary>
        private void ShowMissions(RectTransform anchor)
        {
            if (_missionsPanel != null) { UnityEngine.Object.Destroy(_missionsPanel.gameObject); _missionsPanel = null; }
            var canvas = anchor.GetComponentInParent<Canvas>().transform;
            _missionsPanel = UIKit.Fill(canvas, "Missions");
            var block = UIKit.Image(_missionsPanel, UIKit.Square, new Color(0, 0, 0, 0.6f), true);
            var close = _missionsPanel.gameObject.AddComponent<Button>(); close.targetGraphic = block;
            close.onClick.AddListener(() => { UnityEngine.Object.Destroy(_missionsPanel.gameObject); _missionsPanel = null; });
            var p = UIKit.At(_missionsPanel, "Panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(680, 430));
            UIKit.Image(p, UIKit.Rounded, new Color(0.07f, 0.08f, 0.18f, 0.97f), true);
            var title = UIKit.LabelAt(p, "DAILY MISSIONS", 34, Color.white, new Vector2(0.5f, 1), new Vector2(0, -36), new Vector2(600, 44), TextAnchor.MiddleCenter, UIKit.TitleFont);
            title.fontStyle = FontStyle.Italic;
            var now = System.DateTime.UtcNow;
            var left = now.Date.AddDays(1) - now;
            UIKit.LabelAt(p, $"New missions in {(int)left.TotalHours}h {left.Minutes:00}m · coins are paid when you finish one", 16, Theme.TextDim, new Vector2(0.5f, 1), new Vector2(0, -74), new Vector2(620, 24), TextAnchor.MiddleCenter, UIKit.BoldFont);
            var op = _app.OnlineProfile;
            int day = op != null && op.missionDay > 0 ? op.missionDay : Progression.Today;
            var ms = Progression.DailyMissions(day);
            var prog = Progression.ParseMissions(op?.missions, day);
            for (int i = 0; i < ms.Length && ms[i] != null; i++)
            {
                var m = ms[i];
                bool done = prog[i] >= m.Target;
                var row = UIKit.At(p, "M" + i, new Vector2(0.5f, 1), new Vector2(0, -116 - i * 96), new Vector2(620, 84));
                row.pivot = new Vector2(0.5f, 1);
                UIKit.Image(row, UIKit.RoundedSmall, done ? new Color(0.25f, 0.6f, 0.35f, 0.35f) : new Color(1, 1, 1, 0.06f));
                var t = UIKit.LabelAt(row, m.Title.ToUpper(), 22, Color.white, new Vector2(0, 1), new Vector2(20, -12), new Vector2(420, 30), TextAnchor.MiddleLeft, UIKit.BoldFont);
                t.rectTransform.pivot = new Vector2(0, 1);
                var bar = new Bar(row, new Vector2(0, 0), new Vector2(20, 16), new Vector2(420, 12), done ? Theme.Green : Theme.Yellow, new Color(1, 1, 1, 0.12f));
                bar.Root.pivot = new Vector2(0, 0);
                bar.Set(Mathf.Clamp01(prog[i] / (float)m.Target), 100f);
                var n = UIKit.LabelAt(row, $"{Mathf.Min(prog[i], m.Target):N0}/{m.Target:N0}", 16, Theme.TextDim, new Vector2(0, 0), new Vector2(450, 10), new Vector2(110, 24), TextAnchor.MiddleLeft, UIKit.BoldFont);
                n.rectTransform.pivot = new Vector2(0, 0);
                var rw = UIKit.LabelAt(row, done ? "DONE ✓" : $"● {m.Reward}", 24, done ? Theme.Green : Theme.Gold, new Vector2(1, 0.5f), new Vector2(-20, 0), new Vector2(140, 36), TextAnchor.MiddleRight, UIKit.TitleFont);
                rw.rectTransform.pivot = new Vector2(1, 0.5f);
            }
            if (op == null) UIKit.LabelAt(p, "Sign in and play online to track missions", 16, Theme.Red, new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(620, 24), TextAnchor.MiddleCenter, UIKit.BoldFont);
        }
        private Bar _bpBar;
        private RectTransform _help;

        private void ToggleSquadList()
        {
            bool on = !_squadPanel.gameObject.activeSelf;
            _squadPanel.gameObject.SetActive(on);
            _modePanel.gameObject.SetActive(!on);
            _length.Root.gameObject.SetActive(false);
            _cards.gameObject.SetActive(!on);   // the list takes the right column (it overlapped the cards on phones)
        }

        private static Image Dark(RectTransform rt) => UIKit.Image(rt, UIKit.RoundedSmall, new Color(0.04f, 0.05f, 0.1f, 0.82f), true);

        private void BuildCards(RectTransform root)
        {
            void Soon(string what) => _app.Toast($"{what} — coming soon");
            // NEW MAP banner (painted key art)
            var map = UIKit.At(root, "NewMap", new Vector2(0, 1), Vector2.zero, new Vector2(440, 180));
            map.pivot = new Vector2(0, 1);
            var mapBg = Dark(map);
            var art = Resources.Load<Texture2D>("UI/title_keyart");
            if (art != null) { var ri = UIKit.Fill(map, "Art", 3).gameObject.AddComponent<RawImage>(); ri.texture = art; ri.uvRect = new Rect(0.25f, 0.2f, 0.75f, 0.6f); ri.raycastTarget = false; }
            var shade = UIKit.Rect(map, "Shade", new Vector2(0, 0), new Vector2(1, 0.6f), new Vector2(0.5f, 0), Vector2.zero, Vector2.zero);
            var sh = UIKit.Image(shade, UIKit.Gradient, new Color(0, 0, 0, 0.75f)); sh.raycastTarget = false;
            shade.localScale = new Vector3(1, -1, 1);
            var t1 = UIKit.LabelAt(map, "NEW MAP", 30, Color.white, new Vector2(1, 0), new Vector2(-18, 52), new Vector2(380, 36), TextAnchor.MiddleRight, UIKit.TitleFont);
            t1.rectTransform.pivot = new Vector2(1, 0); t1.fontStyle = FontStyle.Italic; UIKit.Shadow(t1, 2);
            var t2 = UIKit.LabelAt(map, "RILO ISLAND", 30, Theme.Yellow, new Vector2(1, 0), new Vector2(-18, 14), new Vector2(380, 36), TextAnchor.MiddleRight, UIKit.TitleFont);
            t2.rectTransform.pivot = new Vector2(1, 0); t2.fontStyle = FontStyle.Italic; UIKit.Shadow(t2, 2);
            var mb = map.gameObject.AddComponent<Button>(); mb.targetGraphic = mapBg;
            mb.onClick.AddListener(() => _app.Toast("RILO ISLAND — 400 m island, 8 regions. Press START to drop in!"));
            // BATTLE PASS (season progress follows your level for now)
            var bp = UIKit.At(root, "BattlePass", new Vector2(0, 1), new Vector2(0, -192), new Vector2(440, 130));
            bp.pivot = new Vector2(0, 1);
            var bpBg = UIKit.Image(bp, UIKit.RoundedSmall, new Color(0.12f, 0.16f, 0.42f, 0.92f), true);
            var b1 = UIKit.LabelAt(bp, "BATTLE PASS", 28, Color.white, new Vector2(0, 1), new Vector2(20, -14), new Vector2(300, 34), TextAnchor.MiddleLeft, UIKit.TitleFont);
            b1.rectTransform.pivot = new Vector2(0, 1); b1.fontStyle = FontStyle.Italic;
            var b2 = UIKit.LabelAt(bp, "SEASON 1 · COMING SOON", 16, Theme.TextDim, new Vector2(0, 1), new Vector2(20, -48), new Vector2(300, 22), TextAnchor.MiddleLeft, UIKit.BoldFont);
            b2.rectTransform.pivot = new Vector2(0, 1);
            var lvl = UIKit.At(bp, "Lv", new Vector2(0, 0), new Vector2(20, 14), new Vector2(46, 46)); lvl.pivot = Vector2.zero;
            UIKit.Image(lvl, UIKit.Diamond, Theme.Yellow);
            _bpLevel = UIKit.Label(lvl, "1", 18, new Color(0.08f, 0.06f, 0.02f), TextAnchor.MiddleCenter, UIKit.TitleFont);
            _bpBar = new Bar(bp, new Vector2(0, 0), new Vector2(80, 30), new Vector2(336, 12), Theme.Yellow, new Color(1, 1, 1, 0.15f));
            _bpBar.Root.pivot = new Vector2(0, 0);
            var bb = bp.gameObject.AddComponent<Button>(); bb.targetGraphic = bpBg; bb.onClick.AddListener(() => Soon("BATTLE PASS"));
            // MISSIONS, DAILY REWARDS
            Text Row(string label, Sprite icon, float y, bool dot, System.Action onClick = null)
            {
                var rt = UIKit.At(root, label, new Vector2(0, 1), new Vector2(0, y), new Vector2(440, 64));
                rt.pivot = new Vector2(0, 1);
                var bg = Dark(rt);
                var b = rt.gameObject.AddComponent<Button>(); b.targetGraphic = bg;
                b.onClick.AddListener(() => { Sfx.Play(Sfx.Click, 0.5f); if (onClick != null) onClick(); else Soon(label); });
                rt.gameObject.AddComponent<ButtonFx>();
                var ic = UIKit.At(rt, "I", new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(34, 34)); ic.pivot = new Vector2(0, 0.5f);
                UIKit.Image(ic, icon, Color.white);
                var l = UIKit.LabelAt(rt, label, 22, Color.white, new Vector2(0, 0.5f), new Vector2(76, 0), new Vector2(330, 36), TextAnchor.MiddleLeft, UIKit.BoldFont);
                l.rectTransform.pivot = new Vector2(0, 0.5f);
                if (dot) { var d = UIKit.At(rt, "Dot", new Vector2(1, 1), new Vector2(-10, -10), new Vector2(14, 14)); d.pivot = new Vector2(1, 1); UIKit.Image(d, UIKit.Circle, Theme.Red); }
                l.supportRichText = true;
                return l;
            }
            _missionsLabel = Row("MISSIONS", Icons.Target, -334, false, () => ShowMissions(root));
            Row("DAILY REWARDS", Icons.Key, -408, true);
            EnterFx.Add(root, new Vector2(48, 0), 0.06f);
        }

        private void BuildChat(RectTransform tab)
        {
            var bar = UIKit.At(tab, "Chat", new Vector2(0, 0), new Vector2(28, 24), new Vector2(560, 68));
            bar.pivot = Vector2.zero;
            var bg = Dark(bar);
            var b = bar.gameObject.AddComponent<Button>(); b.targetGraphic = bg;
            b.onClick.AddListener(() => _app.Toast("World chat is coming soon — use squad voice for now"));
            var ic = UIKit.At(bar, "I", new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(40, 40)); ic.pivot = new Vector2(0, 0.5f);
            UIKit.Image(ic, UIKit.RoundedSmall, Color.white);
            UIKit.Label(ic, "···", 22, new Color(0.08f, 0.06f, 0.02f), TextAnchor.MiddleCenter, UIKit.TitleFont);
            _chat = UIKit.LabelAt(bar, ChatLines[0], 18, new Color(0.85f, 0.88f, 1f), new Vector2(0, 0.5f), new Vector2(74, 0), new Vector2(470, 40), TextAnchor.MiddleLeft, UIKit.BodyFont);
            _chat.rectTransform.pivot = new Vector2(0, 0.5f); _chat.supportRichText = true; UIKit.Fit(_chat, 11);
            EnterFx.Add(bar, new Vector2(-36, 0), 0.2f);
        }

        private static readonly string[] ChatLines =
        {
            "<color=#ffd84a>[RILO]</color> Welcome to Rilo Island — 4 squads, 1 extraction.",
            "<color=#ffd84a>[Tip]</color> Hack an enemy terminal, then defend your hacker.",
            "<color=#ffd84a>[Tip]</color> Press X (or FISTS) to switch to your hands.",
            "<color=#ffd84a>[Tip]</color> The first squad to hold the extraction wins.",
            "<color=#ffd84a>[RILO]</color> Earn coins every match to unlock heroes.",
        };

        /// <summary>Per frame: rotate the chat ticker, battle-pass bar, and park the "+" markers on the empty squad spots.</summary>
        private void UpdateChrome(float dt)
        {
            _chatT += dt;
            if (_chatT > 6f) { _chatT = 0; _chatIdx = (_chatIdx + 1) % ChatLines.Length; _chat.text = ChatLines[_chatIdx]; }
            var op = _app.OnlineProfile;
            _bpLevel.text = op != null ? op.level.ToString() : "1";
            if (_missionsLabel != null)
            {
                int day = op != null && op.missionDay > 0 ? op.missionDay : Progression.Today;
                var ms = Progression.DailyMissions(day);
                var pr = Progression.ParseMissions(op?.missions, day);
                int done = 0; for (int i = 0; i < ms.Length && ms[i] != null; i++) if (pr[i] >= ms[i].Target) done++;
                _missionsLabel.text = $"MISSIONS  <color=#ffd84a>{done}/{ms.Length}</color>";
            }
            _bpBar.Set(op != null && op.xpToNext > 0 ? (float)op.xp / op.xpToNext : 0f, dt);
            bool show = _app.State == GameApp.AppState.Menu && _app.Cam != null && !_squadPanel.gameObject.activeSelf;
            var slots = Veil.App.Stage.Slots;
            for (int k = 0; k < _plus.Length; k++)
            {
                int slot = k + 1;
                // a "+" on every empty spot of the squad (3 friends to invite)
                bool empty = show && _members.Count <= slot;
                if (!empty) { _plus[k].gameObject.SetActive(false); continue; }
                var wp = _app.Stage.Origin + Quaternion.Euler(0, 180, 0) * (slots[slot] + Vector3.up * 1.0f);
                var sp = _app.Cam.WorldToScreenPoint(wp);
                if (sp.z <= 0) { _plus[k].gameObject.SetActive(false); continue; }
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_tab, sp, null, out var lp);
                _plus[k].gameObject.SetActive(true);
                _plus[k].anchoredPosition = lp;
            }
        }

        // ------------------------------------------------------------------ construction helpers

        private Row MakeRow(RectTransform parent, int i)
        {
            var r = new Row();
            r.Root = UIKit.At(parent, "Row" + i, new Vector2(0, 1), new Vector2(16, -76 - i * 90), new Vector2(408, 84));
            r.Root.pivot = new Vector2(0, 1);
            r.Bg = UIKit.Image(r.Root, UIKit.RoundedSmall, new Color(1, 1, 1, 0.05f), true);
            r.RowBtn = r.Root.gameObject.AddComponent<Button>();
            r.RowBtn.targetGraphic = r.Bg;
            int idx = i;
            r.RowBtn.onClick.AddListener(() => RowClicked(idx));
            var faceRt = UIKit.At(r.Root, "Face", new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(68, 68));
            faceRt.pivot = new Vector2(0, 0.5f);
            UIKit.Image(faceRt, UIKit.RoundedSmall, new Color(0.14f, 0.13f, 0.3f));
            var inner = UIKit.Fill(faceRt, "Img", 3);
            r.Face = inner.gameObject.AddComponent<RawImage>();
            r.Face.raycastTarget = false;
            var crownRt = UIKit.At(r.Root, "Crown", new Vector2(0, 1), new Vector2(86, -16), new Vector2(22, 22));
            crownRt.pivot = new Vector2(0, 1);
            r.Crown = UIKit.Image(crownRt, Icons.Crown, Theme.Gold);
            r.Name = UIKit.LabelAt(r.Root, "", 20, Theme.Text, new Vector2(0, 1), new Vector2(86, -12), new Vector2(112, 28), TextAnchor.MiddleLeft, UIKit.BoldFont);
            UIKit.Fit(r.Name);   // stays clear of the mic / speaker icons
            r.Name.rectTransform.pivot = new Vector2(0, 1);
            r.Name.supportRichText = true;
            r.Level = UIKit.LabelAt(r.Root, "", 15, Theme.TextDim, new Vector2(0, 1), new Vector2(86, -46), new Vector2(200, 22), TextAnchor.MiddleLeft, UIKit.BodyFont);
            r.Level.rectTransform.pivot = new Vector2(0, 1);
            r.Level.supportRichText = true;
            r.Mic = IconIn(r.Root, Icons.Mic, new Vector2(-150, 0), out r.MicBtn);
            r.Spk = IconIn(r.Root, Icons.Speaker, new Vector2(-112, 0), out r.SpkBtn);
            r.MicBtn.onClick.AddListener(() => { if (_rows[idx].M != null && _rows[idx].M.Me) _app.Voice.MicMuted = !_app.Voice.MicMuted; });
            r.SpkBtn.onClick.AddListener(() =>
            {
                var m = _rows[idx].M;
                if (m == null) return;
                if (m.Me) _app.Voice.Deafened = !_app.Voice.Deafened;
                else if (!m.Bot) _app.Voice.SetMuted(m.Id, !_app.Voice.IsMuted(m.Id));
            });
            r.Status = UIKit.LabelAt(r.Root, "", 17, Theme.Green, new Vector2(1, 0.5f), new Vector2(-14, 0), new Vector2(96, 30), TextAnchor.MiddleRight, UIKit.BoldFont);
            r.Status.rectTransform.pivot = new Vector2(1, 0.5f);
            r.Status.supportRichText = true;
            UIKit.Fit(r.Status);   // "NOT READY" at phone font size must not run into the mic icon
            r.Empty = UIKit.Fill(r.Root, "Empty");
            var eb = UIKit.Button(r.Empty, "+  INVITE A FRIEND", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, 46), UIKit.ButtonStyle.Ghost, () => OpenFriends?.Invoke(), 16);
            UIKit.ButtonLabel(eb).color = Theme.PurpleLight;
            UIKit.ButtonLabel(eb).gameObject.AddComponent<BreatheFx>().Phase = i * 0.8f;
            return r;
        }

        private static Image IconIn(RectTransform parent, Sprite icon, Vector2 pos, out Button btn)
        {
            var rt = UIKit.At(parent, "Icon", new Vector2(1, 0.5f), pos, new Vector2(34, 34));
            rt.pivot = new Vector2(1, 0.5f);
            var hit = UIKit.Image(rt, UIKit.Circle, new Color(1, 1, 1, 0.001f), true);
            btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = hit;
            var ic = UIKit.At(rt, "I", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26, 26));
            return UIKit.Image(ic, icon, Theme.TextDim);
        }

        private Image PillIcon(RectTransform parent, Sprite icon, Vector2 pos, Action onClick)
        {
            var rt = UIKit.At(parent, "PillIcon", new Vector2(0, 0.5f), pos, new Vector2(48, 48));
            rt.pivot = new Vector2(0, 0.5f);
            var bg = UIKit.Image(rt, UIKit.Circle, new Color(1, 1, 1, 0.08f), true);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            b.onClick.AddListener(() => { Sfx.Play(Sfx.Click, 0.5f); onClick(); });
            var ic = UIKit.At(rt, "I", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(28, 28));
            return UIKit.Image(ic, icon, Theme.Text);
        }

        private static Button IconButton(RectTransform parent, Sprite icon, string text, Vector2 anchor, Vector2 pos, Vector2 size, Action onClick)
        {
            var b = UIKit.Button(parent, "", anchor, pos, size, UIKit.ButtonStyle.Ghost, onClick, 15);
            var rt = (RectTransform)b.transform;
            rt.pivot = new Vector2(anchor.x, 0);
            var ic = UIKit.At(rt, "Icon", new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(20, 20));
            ic.pivot = new Vector2(0, 0.5f);
            UIKit.Image(ic, icon, Theme.Text);
            var l = UIKit.ButtonLabel(b);
            l.text = text;
            l.rectTransform.offsetMin = new Vector2(30, 0);
            return b;
        }

        private readonly List<Plate> _shownPlates = new List<Plate>();

        /// <summary>Cards never overlap: left to right with a gap, kept between the side menu and the right cards,
        /// shrunk a little (down to 75 %) when four don't fit side by side.</summary>
        private void SpreadPlates()
        {
            _shownPlates.Clear();
            foreach (var p in _plate) if (p.Root.gameObject.activeSelf) _shownPlates.Add(p);
            if (_shownPlates.Count == 0) return;
            _shownPlates.Sort((a, b) => a.Root.anchoredPosition.x.CompareTo(b.Root.anchoredPosition.x));
            float halfW = _plates.rect.width * 0.5f;
            float left = -halfW + 430f, right = halfW - 490f;   // free space between the side menu and the right column
            int n = _shownPlates.Count;
            float baseW = 224f, gap = 10f;
            float scale = Mathf.Clamp((right - left - gap * (n - 1)) / (baseW * n), 0.75f, 1f);
            float w = baseW * scale, step = w + gap;
            float minX = left + w * 0.5f, maxX = right - w * 0.5f;
            var xs = new float[n];
            for (int k = 0; k < n; k++) xs[k] = Mathf.Clamp(_shownPlates[k].Root.anchoredPosition.x, minX, maxX);
            for (int k = 1; k < n; k++) xs[k] = Mathf.Max(xs[k], xs[k - 1] + step);              // push right
            for (int k = n - 1; k >= 0; k--) xs[k] = Mathf.Min(xs[k], maxX - (n - 1 - k) * step); // then back inside
            for (int k = 1; k < n; k++) xs[k] = Mathf.Max(xs[k], xs[k - 1] + step);
            // same height for all cards (a tidy row), just above the tallest head
            float y = float.MinValue;
            foreach (var p in _shownPlates) y = Mathf.Max(y, p.Root.anchoredPosition.y);
            for (int k = 0; k < n; k++)
            {
                var rt = _shownPlates[k].Root;
                rt.anchoredPosition = new Vector2(xs[k], y);
                rt.localScale = Vector3.one * scale;
            }
        }

        private static Plate MakePlate(RectTransform parent, int i)
        {
            // player card over each hero: colour strip (you / squadmate / bot), name (scrolls when long), level tag,
            // voice + ready on the right
            var p = new Plate();
            p.Root = UIKit.At(parent, "Plate" + i, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(224, 60));
            UIKit.Image(p.Root, UIKit.RoundedSmall, new Color(0.05f, 0.06f, 0.16f, 0.9f));
            var ol = p.Root.gameObject.AddComponent<Outline>(); ol.effectColor = new Color(1, 1, 1, 0.14f); ol.effectDistance = new Vector2(1.2f, -1.2f);
            var acc = UIKit.At(p.Root, "Accent", new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(6, 48));
            acc.pivot = new Vector2(0, 0.5f);
            p.Accent = UIKit.Image(acc, UIKit.Pill, Theme.Gold);
            var cr = UIKit.At(p.Root, "Crown", new Vector2(0, 0.5f), new Vector2(14, 8), new Vector2(22, 22));
            cr.pivot = new Vector2(0, 0.5f);
            p.Crown = UIKit.Image(cr, Icons.Crown, Theme.Gold);
            var win = UIKit.At(p.Root, "NameWindow", new Vector2(0, 1), new Vector2(40, -6), new Vector2(118, 26));
            win.pivot = new Vector2(0, 1);
            win.gameObject.AddComponent<RectMask2D>();
            p.Name = UIKit.LabelAt(win, "", 19, Color.white, new Vector2(0, 0.5f), Vector2.zero, new Vector2(400, 26), TextAnchor.MiddleLeft, UIKit.BoldFont);
            p.Name.horizontalOverflow = HorizontalWrapMode.Overflow;
            p.Name.rectTransform.pivot = new Vector2(0, 0.5f);
            var lv = UIKit.At(p.Root, "LevelTag", new Vector2(0, 0), new Vector2(40, 6), new Vector2(62, 20));
            lv.pivot = new Vector2(0, 0);
            p.LevelBg = UIKit.Image(lv, UIKit.Pill, new Color(1, 1, 1, 0.1f));
            p.Level = UIKit.Label(lv, "", 13, Theme.TextDim, TextAnchor.MiddleCenter, UIKit.BoldFont);
            UIKit.Fit(p.Level, 9);
            var sp = UIKit.At(p.Root, "Spk", new Vector2(1, 0.5f), new Vector2(-44, 0), new Vector2(22, 22));
            sp.pivot = new Vector2(1, 0.5f);
            p.Spk = UIKit.Image(sp, Icons.Speaker, Theme.TextDim);
            var rd = UIKit.At(p.Root, "Ready", new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(28, 28));
            rd.pivot = new Vector2(1, 0.5f);
            p.ReadyBg = UIKit.Image(rd, UIKit.Circle, Theme.Green);
            var ri = UIKit.At(rd, "I", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20));
            p.ReadyIcon = UIKit.Image(ri, Icons.Check, new Color(0.05f, 0.2f, 0.08f));
            return p;
        }

        private RectTransform BuildHowTo(RectTransform tab)
        {
            // full-screen dim (tap outside to close) + a centred card that always fits its text
            var root = UIKit.Fill(tab, "HowTo");
            var dim = UIKit.Image(root, UIKit.Square, new Color(0, 0, 0, 0.55f), true);
            var dimBtn = root.gameObject.AddComponent<Button>(); dimBtn.targetGraphic = dim;
            dimBtn.onClick.AddListener(() => root.gameObject.SetActive(false));
            var card = UIKit.Panel(root, "Card", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820, 560)).rectTransform;
            var block = card.gameObject.AddComponent<Button>(); block.transition = Selectable.Transition.None;   // taps on the card don't close it
            var h = UIKit.LabelAt(card, "HOW TO PLAY", 34, Theme.Yellow, new Vector2(0, 1), new Vector2(36, -30), new Vector2(560, 44), TextAnchor.MiddleLeft, UIKit.TitleFont);
            h.rectTransform.pivot = new Vector2(0, 1);
            var close = UIKit.Button(card, "CLOSE", new Vector2(1, 1), new Vector2(-28, -28), new Vector2(150, 52), UIKit.ButtonStyle.Secondary, () => root.gameObject.SetActive(false), 20);
            ((RectTransform)close.transform).pivot = new Vector2(1, 1);
            var body = UIKit.LabelAt(card,
                "<color=#ffd84a>4 squads of 4</color> on Rilo Island. Work through your squad's objectives:\n" +
                "<color=#c7a6ff>Hack</color> a terminal → <color=#c7a6ff>capture</color> → <color=#c7a6ff>collect</color> → open your <color=#ffc93a>Vault</color> → " +
                "reach the <color=#7dff9a>helicopter</color> and hold it to escape. <color=#ffd84a>First squad out wins.</color>\n\n" +
                (Veil.Match.Platform.IsMobile
                    ? "<color=#ffd84a>Left thumb</color> move · <color=#ffd84a>right side</color> look · hold <color=#ffd84a>FIRE</color>\n" +
                      "<color=#ffd84a>DASH · PULSE · DECOY</color> abilities · <color=#ffd84a>PING</color> mark for your squad\n" +
                      "<color=#ffd84a>FISTS</color> switch weapon · tap the <color=#ffd84a>minimap</color> for the full map · hold <color=#ffd84a>TALK</color> for voice"
                    : "<color=#ffd84a>WASD</color> move · <color=#ffd84a>Mouse</color> aim · <color=#ffd84a>LMB</color> fire · <color=#ffd84a>Space</color> jump · <color=#ffd84a>Shift</color> sprint\n" +
                      "<color=#ffd84a>Q</color> Dash · <color=#ffd84a>E</color> Pulse · <color=#ffd84a>R</color> Decoy · <color=#ffd84a>G</color> grenade · <color=#ffd84a>X</color> fists\n" +
                      "<color=#ffd84a>Z / middle mouse</color> ping · <color=#ffd84a>M</color> map · <color=#ffd84a>V</color> talk · <color=#ffd84a>Esc</color> pause"),
                24, Theme.Text, new Vector2(0.5f, 1), new Vector2(0, -100), new Vector2(748, 420), TextAnchor.UpperLeft, UIKit.BodyFont);
            body.rectTransform.pivot = new Vector2(0.5f, 1);
            body.supportRichText = true;
            body.lineSpacing = 1.2f;
            UIKit.Fit(body, 14);
            return root;
        }

        // ------------------------------------------------------------------ mode

        public void SetMode(bool online)
        {
            SetOnline(online);
        }

        private void SetOnline(bool online)
        {
            _online = online;
            _offlineCountdown = false;
            if (online && !_app.Gateway.Online && _app.Gateway.Status == GatewayClient.State.Offline) _app.GoOnline();
            Refresh();
        }

        public void Flash(string text, float seconds = 4f) { _transient = text; _transientT = seconds; UpdateStatus(); }
        public void SetHostText(string host) => _hostField.SetTextWithoutNotify(host);

        // ------------------------------------------------------------------ actions

        private void CycleVoiceMode()
        {
            var v = _app.Voice;
            v.Mode = (VoiceMode)(((int)v.Mode + 1) % 3);
            _app.Profile.VoiceMode = (int)v.Mode;
            _app.Profile.Save();
            Flash(v.Mode == VoiceMode.PushToTalk ? (Veil.Match.Platform.IsMobile ? "Voice: push-to-talk (hold TALK)" : "Voice: push-to-talk (hold V)") : v.Mode == VoiceMode.OpenMic ? "Voice: open mic" : "Voice chat off");
        }

        private void CopyCode()
        {
            var g = _app.Gateway;
            if (!_online) { Flash("Switch to ONLINE to invite friends"); return; }
            if (g.Party.Empty) { g.CreateRoom((ok, err) => { if (ok) CopyCode(); else Result(ok, err); }); return; }
            GUIUtility.systemCopyBuffer = g.Party.code;
            Flash($"Room code <color=#ffd84a>{g.Party.code}</color> copied — friends use JOIN CODE");
        }

        private void Leave()
        {
            var g = _app.Gateway;
            if (g.Party.Empty) return;
            g.LeaveRoom(Result);   // the server puts you back in your own solo room
        }

        private void Result(bool ok, string err) { if (!ok && !string.IsNullOrEmpty(err)) Flash($"<color=#ff9a8a>{err}</color>"); }

        private void OnAction()
        {
            if (!_online)
            {
                _offlineCountdown = !_offlineCountdown;
                _countT = 3.99f;
                Refresh();
                return;
            }
            var g = _app.Gateway;
            if (!g.Online) { _app.GoOnline(); return; }
            var party = g.Party;
            if (g.Assignment != null && _app.State != GameApp.AppState.Match) { _app.JoinAssignedMatch(g.Assignment); return; }   // REJOIN
            if (party.Empty)
            {
                g.CreateRoom((ok, err) => { if (ok) g.StartQueue(_app.Profile.MatchMinutes * 60, Result); else Result(ok, err); });
                return;
            }
            if (party.phase == (int)PartyPhase.Queued) { if (g.IsLeader) g.CancelQueue(Result); return; }
            if (party.phase == (int)PartyPhase.InMatch) return;
            if (g.IsLeader) g.StartQueue(_app.Profile.MatchMinutes * 60, Result);
            else g.SetReady(!(g.Me?.ready ?? false), Result);
        }

        private void RowClicked(int i)
        {
            var m = _rows[i].M;
            var g = _app.Gateway;
            if (m == null || m.Me || m.Bot || !_online) return;
            bool leaderActions = g.IsLeader && g.Party.phase == (int)PartyPhase.Idle;
            ShowMemberPopup(m, leaderActions);
        }

        private void ClosePopup() { SocialUi.Clear(_popup); _popup.gameObject.SetActive(false); }

        private RectTransform OpenPopup(string title, float height)
        {
            SocialUi.Clear(_popup);
            _popup.gameObject.SetActive(true);
            _popup.SetAsLastSibling();
            var dim = UIKit.Fill(_popup, "Dim");
            var dimImg = UIKit.Image(dim, UIKit.Square, new Color(0.02f, 0.02f, 0.08f, 0.55f), true);
            var db = dim.gameObject.AddComponent<Button>(); db.targetGraphic = dimImg; db.onClick.AddListener(ClosePopup);
            var p = (RectTransform)UIKit.Panel(_popup, "Box", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420, height)).transform;
            var t = UIKit.LabelAt(p, title, 26, Theme.Text, new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(380, 36), TextAnchor.MiddleCenter, UIKit.TitleFont);
            t.rectTransform.pivot = new Vector2(0.5f, 1);
            return p;
        }

        private void ShowMemberPopup(SquadMember m, bool leaderActions)
        {
            var g = _app.Gateway;
            var p = OpenPopup(m.Name, leaderActions ? 300 : 190);
            float y = -76;
            string id = m.Id;
            void Btn(string text, UIKit.ButtonStyle st, Action a)
            {
                var b = UIKit.Button(p, text, new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(340, 52), st, () => { a(); ClosePopup(); }, 18);
                ((RectTransform)b.transform).pivot = new Vector2(0.5f, 1);
                y -= 62;
            }
            Btn(_app.Voice.IsMuted(id) ? "UNMUTE VOICE" : "MUTE VOICE", UIKit.ButtonStyle.Secondary, () => { _app.Voice.SetMuted(id, !_app.Voice.IsMuted(id)); Refresh(); });
            if (leaderActions)
            {
                Btn("MAKE LEADER", UIKit.ButtonStyle.Secondary, () => g.Promote(id, Result));
                Btn("REMOVE FROM SQUAD", UIKit.ButtonStyle.Ghost, () => g.Kick(id, Result));
            }
        }

        private void ShowJoinPopup()
        {
            if (!_online) { SetMode(true); }
            var p = OpenPopup("JOIN A SQUAD", 250);
            var f = SocialUi.Field(p, new Vector2(0.5f, 1), new Vector2(-170, -100), 340, "6-letter room code", 8);
            var b = UIKit.Button(p, "JOIN", new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(340, 56), UIKit.ButtonStyle.Primary, () =>
            {
                var code = f.text.Trim();
                if (code.Length < 4) { Flash("<color=#ff9a8a>Enter the 6-letter room code</color>"); return; }
                _app.Gateway.JoinRoom(code, Result);
                ClosePopup();
            }, 24);
            ((RectTransform)b.transform).pivot = new Vector2(0.5f, 1);
        }

        // ------------------------------------------------------------------ data

        private void BuildMembers()
        {
            _members.Clear();
            var g = _app.Gateway;
            var prof = _app.Profile;
            if (!_online || !g.Online || g.Party.Empty)
            {
                _members.Add(new SquadMember { Id = g.MyId, Name = prof.Name, Handle = g.Handle, Look = prof.Look, Level = _app.OnlineProfile?.level ?? 1, Leader = true, Me = true, Ready = !_online && _offlineCountdown, Ping = g.PingMs, Online = !_online || g.Online });
                if (!_online)
                    for (int i = 1; i < GameConfig.SquadSize; i++)
                        _members.Add(new SquadMember { Name = MatchSim.BotNames[(i * 3) % MatchSim.BotNames.Length], Look = Appearance.Preset(i == 1 ? 4 : i == 2 ? 1 : 2), Level = 1, Bot = true, Ready = true });
                return;
            }
            // party: you first (centre stage), then the others in join order
            foreach (var m in g.Party.members)
            {
                var sm = new SquadMember
                {
                    Id = m.id, Name = m.name, Handle = m.handle, Look = SocialUi.ParseLook(m.look), Level = m.level, Ready = m.ready,
                    Leader = m.leader, Online = m.online, Ping = m.ping, Me = m.id == g.MyId,
                };
                if (sm.Me) { sm.Look = prof.Look; _members.Insert(0, sm); } else _members.Add(sm);
            }
        }

        // ------------------------------------------------------------------ view

        public void Refresh()
        {
            if (_squadPanel == null) return;
            var g = _app.Gateway;
            // online: the server keeps every player in a room (created on connect / after leaving)
            BuildMembers();
            var party = g.Party;
            bool idle = party.Empty || party.phase == (int)PartyPhase.Idle;
            _title.text = $"SQUAD <color=#aab0d8>({_members.Count}/{GameConfig.SquadSize})</color>";
            bool canLeave = _online && g.Online && !party.Empty && party.members.Count > 1 && idle;
            _leave.gameObject.SetActive(canLeave);
            _joinCode.gameObject.SetActive(_online && g.Online && idle);
            ((RectTransform)_joinCode.transform).anchoredPosition = new Vector2(canLeave ? -126 : -16, -38);   // right edge when there's no LEAVE
            _invite.interactable = _online && g.Online && idle && _members.Count < GameConfig.SquadSize;
            _copy.interactable = _online && g.Online;
            UIKit.ButtonLabel(_copy).text = !party.Empty && _online ? $"COPY {party.code}" : "COPY CODE";

            for (int i = 0; i < _rows.Length; i++)
            {
                var r = _rows[i];
                bool has = i < _members.Count;
                r.M = has ? _members[i] : null;
                foreach (Transform c in r.Root) c.gameObject.SetActive(has ? c.name != "Empty" : c.name == "Empty");
                r.Bg.color = has && r.M.Me ? new Color(0.62f, 0.38f, 1f, 0.22f) : new Color(1, 1, 1, has ? 0.05f : 0.025f);
                r.Empty.gameObject.SetActive(!has && _online && g.Online && idle);
                if (!has) { r.LastId = ""; r.LastReady = false; continue; }
                var m = r.M;
                string id = m.Id ?? m.Name;
                if (id != r.LastId) { if (r.LastId != "") PunchFx.On(r.Root).Kick(0.06f); r.LastId = id; }
                bool rdy = m.Ready || m.Bot;
                if (rdy && !r.LastReady) PunchFx.On(r.Status).Kick(0.3f);
                r.LastReady = rdy;
                r.Face.texture = PortraitStudio.Get(m.Look);
                r.Face.color = m.Online ? Color.white : new Color(1, 1, 1, 0.4f);
                r.Crown.gameObject.SetActive(m.Leader && !m.Bot && _online);
                r.Name.rectTransform.anchoredPosition = new Vector2(m.Leader && !m.Bot && _online ? 112 : 86, -12);
                r.Name.text = m.Name + (m.Me ? " <color=#aab0d8>(You)</color>" : "");
                r.Level.text = m.Bot ? $"BOT · {SocialUi.CharacterName(m.Look)}" : $"Lv. {m.Level}" + (m.Ping > 0 && _online ? $"  <color=#6a6f90>{m.Ping} ms</color>" : "");
                r.Mic.transform.parent.gameObject.SetActive(!m.Bot);
                r.Spk.transform.parent.gameObject.SetActive(!m.Bot);
                r.Status.text = !m.Online ? "<color=#8a90b8>OFFLINE</color>" : m.Ready || m.Bot ? "<color=#7dff9a>READY</color>"
                    : m.Leader && _online && party.phase == (int)PartyPhase.Idle ? "<color=#ffd84a>LEADER</color>" : "<color=#aab0d8>NOT READY</color>";
            }

            // mode card
            _modeTitle.text = _online ? "ONLINE SQUADS" : "SQUAD vs BOTS";
            _modeThumbIcon.sprite = _online ? Icons.Players : Icons.Target;
            _modeThumbIcon.transform.parent.GetComponent<Image>().color = _online ? new Color(0.3f, 0.55f, 1f) : new Color(0.45f, 0.35f, 0.95f);
            bool needConnect = _online && !g.Online;
            _connectRow.gameObject.SetActive(needConnect);
            _modeSub.text = !_online ? "Practice with your squad against AI bots"
                : needConnect ? (g.Status == GatewayClient.State.Connecting ? "Connecting…" : "Server (found automatically on Wi-Fi):")
                : $"4 squads of 4 · bots fill empty seats\n<color=#7dff9a>●</color> {g.Handle} · {g.PingMs} ms";
            _length.Root.gameObject.SetActive(false);   // no match length to pick: a match runs until a squad escapes
            UpdateStage();
            UpdateStatus();
        }

        private bool _creating;

        private void UpdateStage()
        {
            if (_app.State != GameApp.AppState.Menu || _app.MenuTab != 0) return;   // other tabs show only your hero
            var looks = new List<Appearance>();
            var key = new System.Text.StringBuilder();
            foreach (var m in _members) { looks.Add(m.Look); key.Append(m.Look.Outfit).Append(m.Look.Color).Append(','); }
            if (key.ToString() == _stageKey) return;
            _stageKey = key.ToString();
            _app.Stage.SquadPose(looks);
        }

        public void ForceStage() { _stageKey = ""; UpdateStage(); }

        private void UpdateStatus()
        {
            var g = _app.Gateway;
            string s;
            string action;
            bool actionOn = true;
            if (!_online)
            {
                s = "Practice: your squad + 3 bots vs 3 bot squads";
                action = _offlineCountdown ? "CANCEL" : "START";
            }
            else if (!g.Online)
            {
                s = g.Status == GatewayClient.State.Connecting ? "Connecting to the RILO server…" :
                    string.IsNullOrEmpty(g.LastError) ? "Connect to the RILO server to play online" : $"<color=#ff9a8a>Offline: {g.LastError}</color>";
                action = "CONNECT";
            }
            else
            {
                var party = g.Party;
                if (g.Assignment != null && _app.State != GameApp.AppState.Match) { s = "<color=#ffd84a>Your match is still running!</color>"; action = "REJOIN"; }
                else if (party.Empty) { s = "Creating your squad…"; action = "START"; }
                else if (party.phase == (int)PartyPhase.Queued)
                {
                    s = $"<color=#40e6ff>Finding a match…</color> {party.queueSeconds / 60}:{party.queueSeconds % 60:00}";
                    action = g.IsLeader ? "CANCEL" : "SEARCHING";
                    actionOn = g.IsLeader;
                }
                else if (party.phase == (int)PartyPhase.InMatch) { s = "Match starting…"; action = "LOADING"; actionOn = false; }
                else
                {
                    int ready = 0;
                    foreach (var m in party.members) if (m.ready || m.leader) ready++;
                    int minutes = g.IsLeader ? _app.Profile.MatchMinutes : party.seconds / 60;
                    if (g.IsLeader)
                    {
                        bool all = ready == party.members.Count;
                        action = all ? "START" : "WAITING";
                        actionOn = all;
                        s = all ? "Press START to find a match" : $"Waiting for squad to ready up ({ready}/{party.members.Count})";
                    }
                    else
                    {
                        bool me = g.Me?.ready ?? false;
                        action = me ? "UNREADY" : "READY";
                        s = me ? "Waiting for the leader to start" : "Ready up — the leader starts the match";
                    }
                }
            }
            if (_transientT > 0 && !string.IsNullOrEmpty(_transient)) s = _transient;
            if (_status.text != s && !s.StartsWith("<color=#40e6ff>Finding"))
            {
                _status.canvasRenderer.SetAlpha(0.15f);
                _status.CrossFadeAlpha(1f, 0.3f, true);
            }
            _status.text = s;
            _actionLabel.text = action;
            _action.interactable = actionOn;
        }

        public void Update(float dt)
        {
            if (_transientT > 0) { _transientT -= dt; if (_transientT <= 0) UpdateStatus(); }
            UpdateChrome(dt);
            UpdateMatchmaking(dt);
            var v = _app.Voice;
            bool voiceOn = _online && v.Mode != VoiceMode.Off;
            // rows: mic = speaking indicator (you: tap to mute), speaker = mute that player (you: deafen)
            foreach (var r in _rows)
            {
                if (r.M == null || r.M.Bot) continue;
                bool talking = voiceOn && v.IsSpeaking(r.M.Id);
                bool muted = r.M.Me ? v.MicMuted : v.IsMuted(r.M.Id);
                r.Mic.sprite = r.M.Me && v.MicMuted ? Icons.MicOff : Icons.Mic;
                r.Mic.color = talking ? Theme.Green : r.M.Me && v.MicMuted ? Theme.Red : Theme.TextDim;
                r.Mic.transform.localScale = Vector3.one * (talking ? 1.1f + 0.12f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 9f)) : 1f);
                bool spkOff = r.M.Me ? v.Deafened : muted;
                r.Spk.sprite = spkOff ? Icons.SpeakerOff : Icons.Speaker;
                r.Spk.color = spkOff ? Theme.Red : Theme.TextDim;
            }
            // voice pill (voice only exists online)
            _voicePill.gameObject.SetActive(_online);
            if (_help != null) _help.anchoredPosition = new Vector2(_online ? 366 : 28, 110);   // no voice pill offline: take its corner
            _micPillIcon.sprite = v.MicMuted ? Icons.MicOff : Icons.Mic;
            _micPillIcon.color = v.MicMuted ? Theme.Red : v.LocalSpeaking ? Theme.Green : Theme.Text;
            _spkPillIcon.sprite = v.Deafened ? Icons.SpeakerOff : Icons.Speaker;
            _spkPillIcon.color = v.Deafened ? Theme.Red : Theme.Text;
            string pttKey = Veil.Match.Platform.IsMobile ? "TALK" : "V";
            _voiceMode.text = v.Mode == VoiceMode.Off ? "<color=#8a90b8>VOICE OFF</color>" : v.LocalSpeaking ? "<color=#7dff9a>● TALKING</color>"
                : v.Mode == VoiceMode.OpenMic ? "OPEN MIC" : $"HOLD {pttKey} TO TALK";

            // name tags over the squad on the stage
            bool show = _app.State == GameApp.AppState.Menu && _app.Cam != null;
            for (int i = 0; i < _plate.Length; i++)
            {
                var p = _plate[i];
                bool has = show && i < _members.Count;
                p.Root.gameObject.SetActive(has);
                if (!has) { p.Shown = false; continue; }
                if (!p.Shown) { p.Shown = true; PunchFx.On(p.Root).Kick(-0.35f); }
                var m = _members[i];
                var sp = _app.Cam.WorldToScreenPoint(_app.Stage.HeadPoint(i) + Vector3.down * 0.4f);
                if (sp.z <= 0) { p.Root.gameObject.SetActive(false); continue; }
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_plates, sp, null, out var lp);
                // pinned to the head, but kept between the left menu and the right cards
                float halfW = _plates.rect.width * 0.5f;
                float minX = -halfW + 430f + 112f, maxX = halfW - 490f - 112f;
                p.Root.anchoredPosition = new Vector2(Mathf.Clamp(lp.x, minX, Mathf.Max(minX, maxX)), lp.y + 30);
                p.Name.text = m.Name;
                p.Level.text = m.Bot ? "BOT" : $"Lv. {m.Level}";
                bool crown = m.Leader && !m.Bot && _online;
                p.Crown.gameObject.SetActive(crown);
                bool me = i == 0;
                p.Accent.color = me ? Theme.Gold : m.Bot ? new Color(0.6f, 0.62f, 0.75f) : Theme.Green;
                p.LevelBg.color = m.Bot ? new Color(1, 1, 1, 0.08f) : new Color(1f, 0.82f, 0.25f, 0.2f);
                p.Level.color = m.Bot ? Theme.TextDim : Theme.Gold;
                var nameWin = (RectTransform)p.Name.rectTransform.parent;
                nameWin.anchoredPosition = new Vector2(crown ? 40 : 16, -6);
                nameWin.sizeDelta = new Vector2(crown ? 118 : 142, 26);
                ((RectTransform)p.LevelBg.transform).anchoredPosition = new Vector2(crown ? 40 : 16, 6);
                // marquee: hold, scroll to the end, hold, jump back
                float over = p.Name.preferredWidth - nameWin.sizeDelta.x;
                float tm = over > 0 ? Mathf.Repeat(Time.time, 2f + over / 28f + 1.2f) : 0f;
                float shift = over > 0 ? Mathf.Clamp((tm - 1.2f) * 28f, 0f, over) : 0f;
                p.Name.rectTransform.anchoredPosition = new Vector2(-shift, 0);
                bool talking = voiceOn && !m.Bot && v.IsSpeaking(m.Id);
                p.Spk.gameObject.SetActive(!m.Bot);
                p.Spk.color = talking ? Theme.Green : new Color(1, 1, 1, 0.55f);
                bool ready = m.Ready || m.Bot;
                p.ReadyBg.color = ready ? Theme.Green : new Color(1, 1, 1, 0.12f);
                if (ready && !p.LastReady) PunchFx.On(p.ReadyBg).Kick(0.45f);
                p.LastReady = ready;
                p.ReadyIcon.enabled = ready;
            }
            SpreadPlates();

            if (_online && _app.Gateway.Party.phase == (int)PartyPhase.Queued) UpdateStatus();
            if (!_online && _offlineCountdown)
            {
                int before = Mathf.CeilToInt(_countT);
                _countT -= dt;
                int after = Mathf.CeilToInt(_countT);
                if (after != before) { if (after > 0) Sfx.Play(Sfx.Beep, 0.8f); PunchFx.On(_countdown).Kick(0.5f); }
                _countdown.text = after > 0 ? after.ToString() : "GO!";
                if (_countT <= 0)
                {
                    _offlineCountdown = false;
                    _countdown.text = "";
                    _app.StartOfflineMatch();
                }
            }
            else _countdown.text = "";
        }

        public void OnShow()
        {
            _offlineCountdown = false;
            ClosePopup();
            _stageKey = "";
            foreach (var p in _plate) p.Root.anchoredPosition = Vector2.zero;
            Refresh();
        }
    }

    /// <summary>Compact segmented chip row (no title), right-aligned.</summary>
    public sealed class ChipRowCompact
    {
        public readonly RectTransform Root;
        private readonly List<Image> _chips = new List<Image>();
        private readonly List<Text> _labels = new List<Text>();

        public ChipRowCompact(Transform parent, Vector2 anchor, Vector2 pos, string[] options, int selected, Action<int> onPick)
        {
            float w = 440, cw = (w - 8 * (options.Length - 1)) / options.Length;
            Root = UIKit.At(parent, "Chips", anchor, pos, new Vector2(w, 50));
            Root.pivot = new Vector2(1, 0.5f);
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                var b = UIKit.Button(Root, options[i], new Vector2(0, 0.5f), new Vector2(i * (cw + 8), 0), new Vector2(cw, 48), UIKit.ButtonStyle.Ghost, () => { Select(idx); onPick(idx); }, 18);
                ((RectTransform)b.transform).pivot = new Vector2(0, 0.5f);
                _chips.Add((Image)b.targetGraphic);
                _labels.Add(UIKit.ButtonLabel(b));
            }
            Select(selected);
        }

        public void Select(int i)
        {
            for (int k = 0; k < _chips.Count; k++)
            {
                _chips[k].color = k == i ? Theme.Yellow : new Color(0.05f, 0.06f, 0.16f, 0.88f);
                _labels[k].color = k == i ? new Color(0.12f, 0.08f, 0.02f) : Theme.TextDim;
            }
            if (i >= 0 && i < _labels.Count && _chips[i].gameObject.activeInHierarchy) PunchFx.On(_labels[i]).Kick(0.18f);
        }
    }

    // ====================================================================== friends drawer

    /// <summary>Friends list with presence, incoming / outgoing requests, add by Name#1234, invite and join.</summary>
    public sealed class FriendsDrawer
    {
        public readonly RectTransform Root;
        private readonly GameApp _app;
        private readonly Text _me, _tabsInfo;
        private readonly InputField _add;
        private readonly RectTransform _list;
        private readonly ChipRow _tabs;
        private int _tab;
        private readonly Text _msg;
        private float _msgT;

        public bool Visible => Root.gameObject.activeSelf;

        public FriendsDrawer(Transform parent, GameApp app)
        {
            _app = app;
            var panel = UIKit.Panel(parent, "Friends", new Vector2(0, 0.5f), new Vector2(40, -40), new Vector2(580, 840));
            Root = (RectTransform)panel.transform;
            Root.pivot = new Vector2(0, 0.5f);
            var t = UIKit.LabelAt(Root, "FRIENDS", 34, Theme.Text, new Vector2(0, 1), new Vector2(28, -26), new Vector2(300, 40), TextAnchor.MiddleLeft, UIKit.TitleFont);
            t.rectTransform.pivot = new Vector2(0, 1);
            var close = UIKit.Button(Root, "✕", new Vector2(1, 1), new Vector2(-20, -24), new Vector2(52, 44), UIKit.ButtonStyle.Ghost, () => Show(false), 22);
            ((RectTransform)close.transform).pivot = new Vector2(1, 1);

            _me = UIKit.LabelAt(Root, "", 18, Theme.TextDim, new Vector2(0, 1), new Vector2(28, -80), new Vector2(420, 30), TextAnchor.MiddleLeft, UIKit.BodyFont);
            _me.rectTransform.pivot = new Vector2(0, 1);
            _me.supportRichText = true;
            var copy = SocialUi.SmallButton(Root, "COPY MY ID", new Vector2(1, 1), new Vector2(-24, -95), 140, UIKit.ButtonStyle.Ghost, () =>
            {
                GUIUtility.systemCopyBuffer = _app.Gateway.Handle;
                Message($"Copied {_app.Gateway.Handle} — share it so friends can add you");
            }, 14);

            _add = SocialUi.Field(Root, new Vector2(0, 1), new Vector2(28, -150), 360, "Friend's ID, e.g. Nova#1234", 24);
            SocialUi.SmallButton(Root, "ADD FRIEND", new Vector2(1, 1), new Vector2(-24, -150), 150, UIKit.ButtonStyle.Secondary, AddFriend, 15);

            _msg = UIKit.LabelAt(Root, "", 16, Theme.PurpleLight, new Vector2(0, 1), new Vector2(28, -186), new Vector2(520, 24), TextAnchor.MiddleLeft, UIKit.BodyFont);
            _msg.rectTransform.pivot = new Vector2(0, 1);
            _msg.supportRichText = true;

            _tabs = new ChipRow(Root, new Vector2(0, 1), new Vector2(-152, -236), "", new[] { "FRIENDS", "REQUESTS" }, 0, i => { _tab = i; Refresh(); }, 170);
            _tabs.Root.pivot = new Vector2(0, 0.5f);
            _tabsInfo = UIKit.LabelAt(Root, "", 15, Theme.TextDim, new Vector2(1, 1), new Vector2(-24, -236), new Vector2(150, 30), TextAnchor.MiddleRight, UIKit.BodyFont);
            _tabsInfo.rectTransform.pivot = new Vector2(1, 0.5f);

            _list = UIKit.At(Root, "List", new Vector2(0, 1), new Vector2(28, -272), new Vector2(524, 540));
            _list.pivot = new Vector2(0, 1);
            app.Gateway.Changed += () => { if (Visible) Refresh(); };
            Show(false);
        }

        public void Show(bool v)
        {
            Root.gameObject.SetActive(v);
            if (v) { Root.SetAsLastSibling(); _app.Gateway.Request(Gw.FriendList, null); Refresh(); }
        }

        public void Toggle() => Show(!Visible);

        private void Message(string m) { _msg.text = m; _msgT = 5f; }

        private void AddFriend()
        {
            var h = _add.text.Trim();
            if (!h.Contains("#")) { Message("<color=#ff9a8a>Use the full ID with the # number, e.g. Nova#1234</color>"); return; }
            _app.Gateway.AddFriend(h, (ok, err) => { Message(ok ? $"Request sent to {h}" : $"<color=#ff9a8a>{err}</color>"); if (ok) _add.text = ""; });
        }

        public void Refresh()
        {
            var g = _app.Gateway;
            _me.text = g.Online ? $"Your ID  <color=#ffd84a>{g.Handle}</color>" : "<color=#ff9a8a>Offline — PLAY → ONLINE → CONNECT first</color>";
            SocialUi.Clear(_list);
            var fs = g.Friends;
            int online = 0;
            foreach (var f in fs.friends) if (f.status > 0) online++;
            _tabsInfo.text = _tab == 0 ? $"{online}/{fs.friends.Count} online" : $"{fs.incoming.Count} in · {fs.outgoing.Count} out";
            float y = 0;
            const float h = 66;
            if (_tab == 0)
            {
                if (fs.friends.Count == 0) Empty("No friends yet. Add someone with their ID (Name#1234).");
                foreach (var f in fs.friends)
                {
                    if (y > 520) break;
                    var row = Row(ref y, h, f);
                    bool inMyParty = g.Party.members.Exists(m => m.id == f.id);
                    bool partyFull = !g.Party.Empty && g.Party.members.Count >= GameConfig.SquadSize;
                    string fid = f.id;
                    float bx = -8;
                    if (f.status > 0 && !inMyParty && !partyFull && f.status != (int)PresenceStatus.InMatch)
                    {
                        SocialUi.SmallButton(row, "INVITE", new Vector2(1, 0.5f), new Vector2(bx, 0), 96, UIKit.ButtonStyle.Secondary,
                            () => g.Invite(fid, (ok, err) => Message(ok ? $"Invite sent to {f.name}" : $"<color=#ff9a8a>{err}</color>")), 14);
                        bx -= 102;
                    }
                    if (f.joinable && !inMyParty && !string.IsNullOrEmpty(f.partyCode))
                    {
                        string code = f.partyCode;
                        SocialUi.SmallButton(row, "JOIN", new Vector2(1, 0.5f), new Vector2(bx, 0), 76, UIKit.ButtonStyle.Ghost,
                            () => g.JoinRoom(code, (ok, err) => Message(ok ? $"Joined {f.name}'s room" : $"<color=#ff9a8a>{err}</color>")), 14);
                        bx -= 82;
                    }
                    if (inMyParty)
                    {
                        var l = UIKit.LabelAt(row, "IN SQUAD", 14, Theme.PurpleLight, new Vector2(1, 0.5f), new Vector2(bx, 0), new Vector2(100, 30), TextAnchor.MiddleRight, UIKit.BoldFont);
                        l.rectTransform.pivot = new Vector2(1, 0.5f);
                    }
                }
            }
            else
            {
                if (fs.incoming.Count == 0 && fs.outgoing.Count == 0) Empty("No pending requests.");
                foreach (var f in fs.incoming)
                {
                    var row = Row(ref y, h, f, "wants to be your friend");
                    long rid = f.requestId;
                    SocialUi.SmallButton(row, "ACCEPT", new Vector2(1, 0.5f), new Vector2(-8, 0), 96, UIKit.ButtonStyle.Secondary, () => g.AcceptFriend(rid), 14);
                    SocialUi.SmallButton(row, "DECLINE", new Vector2(1, 0.5f), new Vector2(-110, 0), 96, UIKit.ButtonStyle.Ghost, () => g.DeclineFriend(rid), 14);
                }
                foreach (var f in fs.outgoing)
                {
                    var row = Row(ref y, h, f, "request sent · waiting");
                    long rid = f.requestId;
                    SocialUi.SmallButton(row, "CANCEL", new Vector2(1, 0.5f), new Vector2(-8, 0), 96, UIKit.ButtonStyle.Ghost, () => g.CancelFriend(rid), 14);
                }
            }
        }

        private void Empty(string text)
        {
            var t = UIKit.LabelAt(_list, text, 17, Theme.TextDim, new Vector2(0, 1), new Vector2(0, -10), new Vector2(520, 60), TextAnchor.UpperLeft, UIKit.BodyFont);
            t.rectTransform.pivot = new Vector2(0, 1);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private RectTransform Row(ref float y, float h, FriendEntry f, string detailOverride = null)
        {
            var row = UIKit.At(_list, "Row", new Vector2(0, 1), new Vector2(0, -y), new Vector2(524, h));
            row.pivot = new Vector2(0, 1);
            y += h + 6;
            UIKit.Image(row, UIKit.RoundedSmall, new Color(1, 1, 1, 0.05f));
            var face = SocialUi.Portrait(row, SocialUi.ParseLook(f.look), h - 14, new Vector2(8, 0));
            if (f.status == 0 && detailOverride == null) face.color = new Color(1, 1, 1, 0.4f);
            var dot = UIKit.At(row, "Dot", new Vector2(0, 0.5f), new Vector2(h - 16, -16), new Vector2(14, 14));
            UIKit.Image(dot, UIKit.Circle, detailOverride != null ? Theme.PurpleLight : SocialUi.StatusColor(f.status));
            var n = UIKit.LabelAt(row, f.name, 19, Theme.Text, new Vector2(0, 1), new Vector2(h + 4, -8), new Vector2(240, 26), TextAnchor.MiddleLeft, UIKit.BoldFont);
            n.rectTransform.pivot = new Vector2(0, 1);
            var d = UIKit.LabelAt(row, detailOverride ?? $"{f.handle} · {f.detail}", 14, Theme.TextDim, new Vector2(0, 1), new Vector2(h + 4, -36), new Vector2(260, 22), TextAnchor.MiddleLeft, UIKit.BodyFont);
            d.rectTransform.pivot = new Vector2(0, 1);
            return row;
        }

        public void Update(float dt)
        {
            if (_msgT > 0) { _msgT -= dt; if (_msgT <= 0) _msg.text = ""; }
        }
    }

    // ====================================================================== toasts (invites + notices), always on top

    public sealed class Toasts
    {
        private readonly GameApp _app;
        private readonly RectTransform _root;
        private readonly RectTransform _invite;
        private readonly Text _inviteText, _noticeText;
        private readonly Image _inviteTimer;
        private readonly RectTransform _notice;
        private IncomingInvite _current;
        private float _inviteLeft, _inviteTotal, _noticeT;

        public Toasts(Transform canvas, GameApp app)
        {
            _app = app;
            _root = UIKit.Fill(canvas, "Toasts");
            _root.gameObject.AddComponent<SafeArea>();

            _invite = UIKit.At(_root, "Invite", new Vector2(0.5f, 1), new Vector2(0, -110), new Vector2(620, 132));
            _invite.pivot = new Vector2(0.5f, 1);
            UIKit.Image(_invite, UIKit.Rounded, new Color(0.1f, 0.08f, 0.24f, 0.97f), true);
            var edge = UIKit.Fill(_invite, "Edge", -2);
            UIKit.Image(edge, UIKit.Rounded, new Color(0.62f, 0.38f, 1f, 0.5f)).transform.SetAsFirstSibling();
            _inviteText = UIKit.LabelAt(_invite, "", 21, Theme.Text, new Vector2(0, 1), new Vector2(24, -16), new Vector2(560, 56), TextAnchor.UpperLeft, UIKit.BoldFont);
            _inviteText.rectTransform.pivot = new Vector2(0, 1);
            _inviteText.supportRichText = true;
            SocialUi.SmallButton(_invite, "ACCEPT", new Vector2(1, 0), new Vector2(-24, 34), 150, UIKit.ButtonStyle.Primary, Accept, 18);
            SocialUi.SmallButton(_invite, "DECLINE", new Vector2(1, 0), new Vector2(-184, 34), 130, UIKit.ButtonStyle.Ghost, Decline, 16);
            var tRt = UIKit.At(_invite, "Timer", new Vector2(0, 0), new Vector2(24, 12), new Vector2(240, 6));
            tRt.pivot = new Vector2(0, 0.5f);
            UIKit.Image(tRt, UIKit.Pill, new Color(1, 1, 1, 0.12f));
            var fill = UIKit.Fill(tRt, "Fill");
            _inviteTimer = UIKit.Image(fill, UIKit.Pill, Theme.Purple);
            _inviteTimer.type = Image.Type.Filled; _inviteTimer.fillMethod = Image.FillMethod.Horizontal;
            _invite.gameObject.SetActive(false);

            _notice = UIKit.At(_root, "Notice", new Vector2(0.5f, 1), new Vector2(0, -250), new Vector2(760, 46));
            _notice.pivot = new Vector2(0.5f, 1);
            UIKit.Image(_notice, UIKit.Pill, new Color(0.05f, 0.05f, 0.14f, 0.85f));
            _noticeText = UIKit.Label(_notice, "", 19, Theme.Text, TextAnchor.MiddleCenter, UIKit.BoldFont);
            _noticeText.supportRichText = true;
            _notice.gameObject.SetActive(false);

            app.Gateway.InviteReceived += inv => { Sfx.Play(Sfx.Beep, 0.9f); Next(); };
            app.Gateway.Changed += () => { if (_current != null && !_app.Gateway.Invites.Exists(x => x.inviteId == _current.inviteId)) Next(); };
            app.Gateway.Notice += Notice;
        }

        public void Notice(string text) => Notice(text, 3.5f);

        public void Notice(string text, float seconds)
        {
            _noticeText.text = text;
            _noticeT = seconds;
            _notice.gameObject.SetActive(true);
            _root.SetAsLastSibling();
        }

        private void Next()
        {
            var list = _app.Gateway.Invites;
            _current = list.Count > 0 ? list[list.Count - 1] : null;
            _invite.gameObject.SetActive(_current != null);
            if (_current == null) return;
            _inviteTotal = _inviteLeft = Mathf.Max(5, _current.expiresIn);
            _inviteText.text = $"<color=#ffd84a>{_current.fromName}</color> invited you to their squad\n<size=16><color=#aab0d8>{_current.fromHandle} · {_current.members}/{GameConfig.SquadSize} players · room {_current.partyCode}</color></size>";
            _root.SetAsLastSibling();
        }

        private void Accept()
        {
            if (_current == null) return;
            var id = _current.inviteId;
            _app.Gateway.AcceptInvite(id, (ok, err) =>
            {
                if (!ok) Notice($"<color=#ff9a8a>{err}</color>");
                else _app.ShowSquad();
            });
            Next();
        }

        private void Decline()
        {
            if (_current == null) return;
            _app.Gateway.DeclineInvite(_current.inviteId);
            Next();
        }

        public void Update(float dt)
        {
            if (_current != null)
            {
                _inviteLeft -= dt;
                _inviteTimer.fillAmount = Mathf.Clamp01(_inviteLeft / _inviteTotal);
                if (_inviteLeft <= 0) { _app.Gateway.Invites.RemoveAll(x => x.inviteId == _current.inviteId); Next(); }
            }
            if (_noticeT > 0) { _noticeT -= dt; if (_noticeT <= 0) _notice.gameObject.SetActive(false); }
        }
    }
}
