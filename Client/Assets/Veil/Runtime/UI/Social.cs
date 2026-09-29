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
            text.font = UIKit.BoldFont; text.fontSize = 22; text.color = Theme.Text; text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            var phRt = UIKit.Fill(fRt, "Placeholder", 0);
            phRt.offsetMin = new Vector2(14, 4); phRt.offsetMax = new Vector2(-14, -4);
            var ph = phRt.gameObject.AddComponent<Text>();
            ph.font = UIKit.BodyFont; ph.fontSize = 20; ph.color = new Color(1, 1, 1, 0.35f); ph.alignment = TextAnchor.MiddleLeft; ph.text = placeholder;
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
                return new Appearance { Outfit = o, Hair = h, HairColor = hc, Accessory = ac, Color = c };
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
        }

        private sealed class Plate
        {
            public RectTransform Root;
            public Image Crown, Spk, ReadyBg, ReadyIcon;
            public Text Name, Level;
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
            _squadPanel = (RectTransform)UIKit.Panel(tab, "Squad", new Vector2(1, 1), new Vector2(-24, -104), new Vector2(440, 512)).transform;
            _squadPanel.pivot = new Vector2(1, 1);
            _title = UIKit.LabelAt(_squadPanel, "SQUAD", 30, Theme.Text, new Vector2(0, 1), new Vector2(22, -16), new Vector2(240, 44), TextAnchor.MiddleLeft, UIKit.TitleFont);
            _title.rectTransform.pivot = new Vector2(0, 1);
            _title.supportRichText = true;
            _leave = SocialUi.SmallButton(_squadPanel, "LEAVE", new Vector2(1, 1), new Vector2(-16, -38), 104, UIKit.ButtonStyle.Ghost, Leave, 15);
            _joinCode = SocialUi.SmallButton(_squadPanel, "JOIN CODE", new Vector2(1, 1), new Vector2(-126, -38), 124, UIKit.ButtonStyle.Ghost, () => ShowJoinPopup(), 14);
            for (int i = 0; i < _rows.Length; i++) _rows[i] = MakeRow(_squadPanel, i);
            _invite = IconButton(_squadPanel, Icons.Plus, "INVITE FRIEND", new Vector2(0, 0), new Vector2(16, 18), new Vector2(200, 50), () => OpenFriends?.Invoke());
            _copy = IconButton(_squadPanel, Icons.Copy, "COPY CODE", new Vector2(1, 0), new Vector2(-16, 18), new Vector2(200, 50), CopyCode);

            // ---------------- mode card
            _modePanel = (RectTransform)UIKit.Panel(tab, "Mode", new Vector2(1, 1), new Vector2(-24, -628), new Vector2(440, 176)).transform;
            _modePanel.pivot = new Vector2(1, 1);
            var thumb = UIKit.At(_modePanel, "Thumb", new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(130, 138));
            thumb.pivot = new Vector2(0, 0.5f);
            var tImg = UIKit.Image(thumb, UIKit.Rounded, Color.white);
            tImg.sprite = UIKit.Gradient;
            tImg.color = new Color(0.45f, 0.35f, 0.95f);
            var ic = UIKit.At(thumb, "Icon", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(70, 70));
            _modeThumbIcon = UIKit.Image(ic, Icons.Players, Color.white);
            _modeTitle = UIKit.LabelAt(_modePanel, "", 24, Theme.Text, new Vector2(0, 1), new Vector2(160, -16), new Vector2(270, 32), TextAnchor.MiddleLeft, UIKit.TitleFont);
            _modeTitle.rectTransform.pivot = new Vector2(0, 1);
            _modeSub = UIKit.LabelAt(_modePanel, "", 15, Theme.TextDim, new Vector2(0, 1), new Vector2(160, -50), new Vector2(270, 44), TextAnchor.UpperLeft, UIKit.BodyFont);
            _modeSub.rectTransform.pivot = new Vector2(0, 1);
            _modeSub.horizontalOverflow = HorizontalWrapMode.Wrap;
            _modeSub.supportRichText = true;
            _connectRow = UIKit.At(_modePanel, "Connect", new Vector2(0, 0), new Vector2(160, 16), new Vector2(264, 48));
            _connectRow.pivot = new Vector2(0, 0);
            _hostField = SocialUi.Field(_connectRow, new Vector2(0, 0.5f), Vector2.zero, 150, "server", 40);
            _hostField.text = app.Profile.ServerAddress;
            _hostField.onEndEdit.AddListener(v => app.Profile.SetServerAddress(v));
            SocialUi.SmallButton(_connectRow, "CONNECT", new Vector2(1, 0.5f), Vector2.zero, 106, UIKit.ButtonStyle.Secondary, () => app.GoOnline(), 14);
            var change = SocialUi.SmallButton(_modePanel, "CHANGE MODE", new Vector2(1, 0), new Vector2(-16, 38), 150, UIKit.ButtonStyle.Ghost, () => SetMode(!_online), 14);
            _changeBtn = change;

            // ---------------- length, status, ready
            _length = new ChipRowCompact(tab, new Vector2(1, 1), new Vector2(-24, -840), new[] { "5 MIN", "10 MIN", "15 MIN" },
                app.Profile.MatchMinutes >= 15 ? 2 : app.Profile.MatchMinutes >= 10 ? 1 : 0,
                i => { app.Profile.MatchMinutes = i == 0 ? 5 : i == 1 ? 10 : 15; app.Profile.Save(); UpdateStatus(); });
            _status = UIKit.LabelAt(tab, "", 16, Theme.Text, new Vector2(1, 1), new Vector2(-24, -886), new Vector2(440, 24), TextAnchor.MiddleCenter, UIKit.BodyFont);
            _status.rectTransform.pivot = new Vector2(1, 0.5f);
            _status.supportRichText = true;
            UIKit.Shadow(_status);
            _action = UIKit.Button(tab, "READY", new Vector2(1, 0), new Vector2(-116, 24), new Vector2(348, 88), UIKit.ButtonStyle.Primary, OnAction, 46);
            ((RectTransform)_action.transform).pivot = new Vector2(1, 0);
            _actionLabel = UIKit.ButtonLabel(_action);
            _actionLabel.fontStyle = FontStyle.Italic;
            _side = UIKit.Button(tab, "", new Vector2(1, 0), new Vector2(-24, 24), new Vector2(86, 88), UIKit.ButtonStyle.Primary, () => OpenFriends?.Invoke(), 20);
            ((RectTransform)_side.transform).pivot = new Vector2(1, 0);
            var sideIc = UIKit.At((RectTransform)_side.transform, "Icon", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44, 44));
            UIKit.Image(sideIc, Icons.Players, new Color(0.15f, 0.1f, 0.05f));
            _countdown = UIKit.LabelAt(tab, "", 130, Color.white, new Vector2(0.5f, 0.5f), new Vector2(-240, 40), new Vector2(400, 200), TextAnchor.MiddleCenter, UIKit.TitleFont);
            UIKit.Outline(_countdown, new Color(0.4f, 0.15f, 0.9f), 5);

            // ---------------- voice pill (bottom-left) + how to play
            _voicePill = UIKit.At(tab, "Voice", new Vector2(0, 0), new Vector2(100, 24), new Vector2(330, 64));
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
            var help = UIKit.Button(tab, "?", new Vector2(0, 0), new Vector2(24, 24), new Vector2(64, 64), UIKit.ButtonStyle.Secondary, () => _howTo.gameObject.SetActive(!_howTo.gameObject.activeSelf), 30);
            ((RectTransform)help.transform).pivot = new Vector2(0, 0);
            _howTo = BuildHowTo(tab);
            _howTo.gameObject.SetActive(false);

            _popup = UIKit.Fill(tab, "Popup");
            _popup.gameObject.SetActive(false);

            app.Gateway.Changed += Refresh;
            app.Gateway.Notice += t => Flash(t);
            SetOnline(false);
        }

        private Button _changeBtn;

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
            r.Name = UIKit.LabelAt(r.Root, "", 20, Theme.Text, new Vector2(0, 1), new Vector2(86, -12), new Vector2(200, 28), TextAnchor.MiddleLeft, UIKit.BoldFont);
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
            r.Empty = UIKit.Fill(r.Root, "Empty");
            var eb = UIKit.Button(r.Empty, "+  INVITE A FRIEND", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, 46), UIKit.ButtonStyle.Ghost, () => OpenFriends?.Invoke(), 16);
            UIKit.ButtonLabel(eb).color = Theme.PurpleLight;
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

        private static Plate MakePlate(RectTransform parent, int i)
        {
            var p = new Plate();
            p.Root = UIKit.At(parent, "Plate" + i, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(168, 50));
            UIKit.Image(p.Root, UIKit.RoundedSmall, new Color(0.05f, 0.06f, 0.16f, 0.82f));
            var cr = UIKit.At(p.Root, "Crown", new Vector2(0, 0.5f), new Vector2(8, 2), new Vector2(22, 22));
            cr.pivot = new Vector2(0, 0.5f);
            p.Crown = UIKit.Image(cr, Icons.Crown, Theme.Gold);
            p.Name = UIKit.LabelAt(p.Root, "", 17, Theme.Text, new Vector2(0, 1), new Vector2(34, -3), new Vector2(92, 24), TextAnchor.MiddleLeft, UIKit.BoldFont);
            p.Name.horizontalOverflow = HorizontalWrapMode.Wrap;
            p.Name.rectTransform.pivot = new Vector2(0, 1);
            p.Level = UIKit.LabelAt(p.Root, "", 13, Theme.TextDim, new Vector2(0, 0), new Vector2(34, 3), new Vector2(92, 20), TextAnchor.MiddleLeft, UIKit.BodyFont);
            p.Level.rectTransform.pivot = new Vector2(0, 0);
            var sp = UIKit.At(p.Root, "Spk", new Vector2(1, 0.5f), new Vector2(-38, 0), new Vector2(20, 20));
            sp.pivot = new Vector2(1, 0.5f);
            p.Spk = UIKit.Image(sp, Icons.Speaker, Theme.TextDim);
            var rd = UIKit.At(p.Root, "Ready", new Vector2(1, 0.5f), new Vector2(-8, 0), new Vector2(24, 24));
            rd.pivot = new Vector2(1, 0.5f);
            p.ReadyBg = UIKit.Image(rd, UIKit.Circle, Theme.Green);
            var ri = UIKit.At(rd, "I", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18, 18));
            p.ReadyIcon = UIKit.Image(ri, Icons.Check, new Color(0.05f, 0.2f, 0.08f));
            return p;
        }

        private RectTransform BuildHowTo(RectTransform tab)
        {
            var info = UIKit.Panel(tab, "HowTo", new Vector2(0, 0), new Vector2(24, 104), new Vector2(560, 330));
            info.rectTransform.pivot = new Vector2(0, 0);
            var h = UIKit.LabelAt(info.transform, "HOW TO PLAY", 26, Theme.Yellow, new Vector2(0, 1), new Vector2(26, -22), new Vector2(500, 34), TextAnchor.MiddleLeft, UIKit.TitleFont);
            h.rectTransform.pivot = new Vector2(0, 1);
            var body = UIKit.LabelAt(info.transform,
                "Squads of <color=#ffd84a>4</color>, four squads per match. Each of you has a <color=#c7a6ff>secret objective</color>, " +
                "your squad shares one more — and your vision. Outthink the other squads.\n\n" +
                (Veil.Match.Platform.IsMobile
                    ? "<color=#ffd84a>Left thumb</color> move · <color=#ffd84a>right side</color> look · <color=#ffd84a>FIRE</color> hold\n<color=#ffd84a>DASH  PULSE  DECOY</color> abilities · <color=#ffd84a>TALK</color> hold for voice"
                    : "<color=#ffd84a>WASD</color> move · <color=#ffd84a>Mouse</color> aim · <color=#ffd84a>LMB</color> blast · <color=#ffd84a>Space</color> jump\n" +
                      "<color=#ffd84a>Shift</color> sprint · <color=#ffd84a>Q</color> Dash · <color=#ffd84a>E</color> Pulse · <color=#ffd84a>R</color> Decoy\n" +
                      "<color=#ffd84a>1/2/3</color> Market · <color=#ffd84a>V</color> push-to-talk · <color=#ffd84a>Tab</color> players · <color=#ffd84a>Esc</color> pause"),
                18, Theme.Text, new Vector2(0, 1), new Vector2(26, -66), new Vector2(510, 250), TextAnchor.UpperLeft, UIKit.BodyFont);
            body.rectTransform.pivot = new Vector2(0, 1);
            body.supportRichText = true;
            body.lineSpacing = 1.15f;
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            return info.rectTransform;
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
            _leave.gameObject.SetActive(_online && g.Online && !party.Empty && party.members.Count > 1 && idle);
            _joinCode.gameObject.SetActive(_online && g.Online && idle);
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
                if (!has) continue;
                var m = r.M;
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
            _length.Root.gameObject.SetActive(!_online || party.Empty || g.IsLeader);
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
                action = _offlineCountdown ? "CANCEL" : "READY";
            }
            else if (!g.Online)
            {
                s = g.Status == GatewayClient.State.Connecting ? "Connecting to the VEIL server…" :
                    string.IsNullOrEmpty(g.LastError) ? "Connect to a VEIL server to play online" : $"<color=#ff9a8a>Offline: {g.LastError}</color>";
                action = "CONNECT";
            }
            else
            {
                var party = g.Party;
                if (g.Assignment != null && _app.State != GameApp.AppState.Match) { s = "<color=#ffd84a>Your match is still running!</color>"; action = "REJOIN"; }
                else if (party.Empty) { s = "Creating your squad…"; action = "READY"; }
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
                        action = all ? (party.members.Count == 1 ? "READY" : "START") : "WAITING";
                        actionOn = all;
                        s = all ? $"{minutes} min match · press {(party.members.Count == 1 ? "READY" : "START")} to find a match" : $"Waiting for squad to ready up ({ready}/{party.members.Count})";
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
            _status.text = s;
            _actionLabel.text = action;
            _action.interactable = actionOn;
        }

        public void Update(float dt)
        {
            if (_transientT > 0) { _transientT -= dt; if (_transientT <= 0) UpdateStatus(); }
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
                bool spkOff = r.M.Me ? v.Deafened : muted;
                r.Spk.sprite = spkOff ? Icons.SpeakerOff : Icons.Speaker;
                r.Spk.color = spkOff ? Theme.Red : Theme.TextDim;
            }
            // voice pill (voice only exists online)
            _voicePill.gameObject.SetActive(_online);
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
                if (!has) continue;
                var m = _members[i];
                var sp = _app.Cam.WorldToScreenPoint(_app.Stage.HeadPoint(i) + Vector3.down * 0.4f);
                if (sp.z <= 0) { p.Root.gameObject.SetActive(false); continue; }
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_plates, sp, null, out var lp);
                p.Root.anchoredPosition = lp + new Vector2(0, 30);   // pinned to the head, no sliding
                p.Name.text = m.Name;
                p.Level.text = m.Bot ? "BOT" : $"Lv. {m.Level}";
                bool crown = m.Leader && !m.Bot && _online;
                p.Crown.gameObject.SetActive(crown);
                p.Name.rectTransform.anchoredPosition = new Vector2(crown ? 34 : 12, -3);
                p.Level.rectTransform.anchoredPosition = new Vector2(crown ? 34 : 12, 3);
                bool talking = voiceOn && !m.Bot && v.IsSpeaking(m.Id);
                p.Spk.gameObject.SetActive(!m.Bot);
                p.Spk.color = talking ? Theme.Green : new Color(1, 1, 1, 0.55f);
                bool ready = m.Ready || m.Bot;
                p.ReadyBg.color = ready ? Theme.Green : new Color(1, 1, 1, 0.12f);
                p.ReadyIcon.enabled = ready;
            }

            if (_online && _app.Gateway.Party.phase == (int)PartyPhase.Queued) UpdateStatus();
            if (!_online && _offlineCountdown)
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

        public void Notice(string text)
        {
            _noticeText.text = text;
            _noticeT = 3.5f;
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
