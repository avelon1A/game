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

    // ====================================================================== squad member card

    /// <summary>One of the four squad slots: portrait, name#tag, character, ready, leader, ping, voice.</summary>
    internal sealed class SquadCard
    {
        public readonly RectTransform Root;
        public string ProfileId = "";
        private Image _speakRing;
        private Text _mute;

        public SquadCard(RectTransform parent, int index, float width, float height)
        {
            Root = UIKit.At(parent, "Card" + index, new Vector2(0, 1), new Vector2(0, -index * (height + 6)), new Vector2(width, height));
            Root.pivot = new Vector2(0, 1);
        }

        public void ShowMember(string id, string name, string handle, Appearance look, int level, bool ready, bool leader, bool online, int ping,
            bool me, bool iAmLeader, bool idle, bool bot, Action onKick, Action onPromote, Action onMute)
        {
            SocialUi.Clear(Root);
            ProfileId = id;
            bool idleSolo = false;
            float h = Root.sizeDelta.y;
            UIKit.Image(UIKit.Fill(Root, "Bg"), UIKit.RoundedSmall, me ? new Color(0.62f, 0.38f, 1f, 0.28f) : new Color(1, 1, 1, 0.06f));
            // speaking ring behind the portrait
            var ringRt = UIKit.At(Root, "Speak", new Vector2(0, 0.5f), new Vector2(4, 0), new Vector2(h - 4, h - 4));
            ringRt.pivot = new Vector2(0, 0.5f);
            _speakRing = UIKit.Image(ringRt, UIKit.Ring, new Color(0.4f, 1f, 0.5f, 0));
            var face = SocialUi.Portrait(Root, look, h - 16, new Vector2(10, 0));
            if (!online) face.color = new Color(1, 1, 1, 0.35f);

            string title = (leader ? "<color=#ffd84a>★</color> " : "") + name + (me ? " <color=#aab0d8>(you)</color>" : "");
            var n = UIKit.LabelAt(Root, title, 21, Theme.Text, new Vector2(0, 1), new Vector2(h + 4, -8), new Vector2(260, 28), TextAnchor.MiddleLeft, UIKit.BoldFont);
            n.rectTransform.pivot = new Vector2(0, 1);
            n.supportRichText = true;
            string sub = bot ? $"BOT · {SocialUi.CharacterName(look)}" : $"{handle}  ·  {SocialUi.CharacterName(look)}  ·  LV {level}";
            var s = UIKit.LabelAt(Root, sub, 15, Theme.TextDim, new Vector2(0, 1), new Vector2(h + 4, -36), new Vector2(300, 22), TextAnchor.MiddleLeft, UIKit.BodyFont);
            s.rectTransform.pivot = new Vector2(0, 1);

            // right column: ready + ping
            string st = !online ? "<color=#8a90b8>OFFLINE</color>" : ready ? "<color=#7dff9a>● READY</color>" : leader && !bot && !idleSolo ? "<color=#ffd84a>LEADER</color>" : "<color=#aab0d8>NOT READY</color>";
            var r = UIKit.LabelAt(Root, st, 16, Theme.Text, new Vector2(1, 1), new Vector2(-12, -8), new Vector2(140, 26), TextAnchor.MiddleRight, UIKit.BoldFont);
            r.rectTransform.pivot = new Vector2(1, 1);
            r.supportRichText = true;
            if (!bot && online) PingBars(ping, new Vector2(-12, -40));

            // actions
            float x = h + 4;
            var actions = UIKit.At(Root, "Actions", new Vector2(0, 0), new Vector2(x, 6), new Vector2(360, 30));
            actions.pivot = new Vector2(0, 0);
            float ax = 0;
            if (!me && !bot)
            {
                var mb = UIKit.Button(actions, "MUTE", new Vector2(0, 0.5f), new Vector2(ax, 0), new Vector2(84, 28), UIKit.ButtonStyle.Ghost, onMute, 13);
                ((RectTransform)mb.transform).pivot = new Vector2(0, 0.5f);
                _mute = UIKit.ButtonLabel(mb);
                ax += 90;
            }
            if (iAmLeader && !me && !bot && idle)
            {
                var pb = UIKit.Button(actions, "MAKE LEADER", new Vector2(0, 0.5f), new Vector2(ax, 0), new Vector2(120, 28), UIKit.ButtonStyle.Ghost, onPromote, 13);
                ((RectTransform)pb.transform).pivot = new Vector2(0, 0.5f);
                ax += 126;
                var kb = UIKit.Button(actions, "KICK", new Vector2(0, 0.5f), new Vector2(ax, 0), new Vector2(70, 28), UIKit.ButtonStyle.Ghost, onKick, 13);
                ((RectTransform)kb.transform).pivot = new Vector2(0, 0.5f);
                UIKit.ButtonLabel(kb).color = Theme.Red;
            }
        }

        private void PingBars(int ping, Vector2 pos)
        {
            int bars = ping <= 0 ? 0 : ping < 60 ? 3 : ping < 130 ? 2 : 1;
            Color c = bars == 3 ? Theme.Green : bars == 2 ? Theme.Yellow : Theme.Red;
            var root = UIKit.At(Root, "Ping", new Vector2(1, 1), pos, new Vector2(110, 22));
            root.pivot = new Vector2(1, 1);
            for (int i = 0; i < 3; i++)
            {
                var b = UIKit.At(root, "Bar", new Vector2(0, 0), new Vector2(i * 8, 2), new Vector2(5, 6 + i * 5));
                b.pivot = new Vector2(0, 0);
                UIKit.Image(b, UIKit.Square, i < bars ? c : new Color(1, 1, 1, 0.18f));
            }
            var t = UIKit.LabelAt(root, ping > 0 ? $"{ping} ms" : "— ms", 14, Theme.TextDim, new Vector2(1, 0.5f), Vector2.zero, new Vector2(80, 22), TextAnchor.MiddleRight, UIKit.BodyFont);
            t.rectTransform.pivot = new Vector2(1, 0.5f);
        }

        public void ShowEmpty(Action onInvite, bool canInvite)
        {
            SocialUi.Clear(Root);
            ProfileId = "";
            _speakRing = null; _mute = null;
            UIKit.Image(UIKit.Fill(Root, "Bg"), UIKit.RoundedSmall, new Color(1, 1, 1, 0.025f));
            if (canInvite)
            {
                var b = UIKit.Button(Root, "+  INVITE FRIEND", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, 48), UIKit.ButtonStyle.Ghost, onInvite, 18);
                UIKit.ButtonLabel(b).color = Theme.PurpleLight;
            }
            else
            {
                var t = UIKit.LabelAt(Root, "Open slot — matchmaking fills it", 16, new Color(1, 1, 1, 0.35f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400, 30), TextAnchor.MiddleCenter, UIKit.BodyFont);
            }
        }

        public void UpdateVoice(IVoiceService voice)
        {
            if (_speakRing == null || string.IsNullOrEmpty(ProfileId)) return;
            bool talking = voice != null && voice.IsSpeaking(ProfileId);
            var c = _speakRing.color;
            c.a = Mathf.MoveTowards(c.a, talking ? 1f : 0f, Time.unscaledDeltaTime * 8f);
            _speakRing.color = c;
            if (_mute != null && voice != null) _mute.text = voice.IsMuted(ProfileId) ? "UNMUTE" : "MUTE";
        }
    }

    // ====================================================================== squad panel (PLAY tab, right side)

    /// <summary>
    /// The party lobby: VS BOTS practice squad, or ONLINE room with up to 4 friends — create / join by code, invites,
    /// ready, leader controls (kick, make leader, start), member cards, voice controls and matchmaking status.
    /// </summary>
    public sealed class SquadPanel
    {
        private readonly GameApp _app;
        private readonly RectTransform _panel;
        private readonly ChipRow _mode, _length;
        private readonly RectTransform _connectRow, _roomRow, _cardsRoot;
        private readonly InputField _hostField, _codeField;
        private readonly Text _title, _status, _roomLabel, _actionLabel, _voiceLabel, _countdown;
        private readonly Button _action, _roomBtn, _copyBtn, _joinBtn, _micBtn;
        private readonly SquadCard[] _cards = new SquadCard[GameConfig.SquadSize];
        private bool _online;
        private bool _offlineCountdown;
        private float _countT;
        private string _transient = "";
        private float _transientT;
        public Action OpenFriends;

        public bool Online => _online;

        public SquadPanel(RectTransform tab, GameApp app)
        {
            _app = app;
            var panel = UIKit.Panel(tab, "Squad", new Vector2(1, 0.5f), new Vector2(-40, -40), new Vector2(560, 840));
            _panel = (RectTransform)panel.transform;
            var p = _panel;
            _title = UIKit.LabelAt(p, "SQUAD", 34, Theme.Text, new Vector2(0, 1), new Vector2(28, -26), new Vector2(360, 40), TextAnchor.MiddleLeft, UIKit.TitleFont);
            _title.rectTransform.pivot = new Vector2(0, 1);
            _title.supportRichText = true;

            // voice quick toggle (top-right of the panel)
            _micBtn = UIKit.Button(p, "MIC", new Vector2(1, 1), new Vector2(-24, -30), new Vector2(150, 40), UIKit.ButtonStyle.Ghost, ToggleMic, 15);
            ((RectTransform)_micBtn.transform).pivot = new Vector2(1, 1);
            _voiceLabel = UIKit.ButtonLabel(_micBtn);
            _voiceLabel.supportRichText = true;

            _mode = new ChipRow(p, new Vector2(0, 1), new Vector2(-150, -104), "", new[] { "VS BOTS", "ONLINE" }, 0, i => SetOnline(i == 1), 170);
            _mode.Root.pivot = new Vector2(0, 0.5f);

            // connect row (online, gateway offline)
            _connectRow = UIKit.At(p, "Connect", new Vector2(0, 1), new Vector2(28, -160), new Vector2(504, 50));
            _connectRow.pivot = new Vector2(0, 0.5f);
            _hostField = SocialUi.Field(_connectRow, new Vector2(0, 0.5f), Vector2.zero, 320, "server address", 40);
            _hostField.text = app.Profile.ServerHost;
            _hostField.onEndEdit.AddListener(v => { app.Profile.ServerHost = v.Trim(); app.Profile.Save(); });
            var connect = SocialUi.SmallButton(_connectRow, "CONNECT", new Vector2(1, 0.5f), Vector2.zero, 170, UIKit.ButtonStyle.Secondary, () => app.GoOnline(), 18);

            // room row (online, gateway online)
            _roomRow = UIKit.At(p, "Room", new Vector2(0, 1), new Vector2(28, -160), new Vector2(504, 104));
            _roomRow.pivot = new Vector2(0, 1);
            _roomLabel = UIKit.LabelAt(_roomRow, "", 22, Theme.Text, new Vector2(0, 1), new Vector2(0, 0), new Vector2(250, 44), TextAnchor.MiddleLeft, UIKit.BoldFont);
            _roomLabel.rectTransform.pivot = new Vector2(0, 1);
            _roomLabel.supportRichText = true;
            _copyBtn = SocialUi.SmallButton(_roomRow, "COPY", new Vector2(0, 1), new Vector2(250, -22), 80, UIKit.ButtonStyle.Ghost, CopyCode, 14);
            _roomBtn = SocialUi.SmallButton(_roomRow, "CREATE ROOM", new Vector2(1, 1), new Vector2(0, -22), 170, UIKit.ButtonStyle.Secondary, RoomButton, 15);
            _codeField = SocialUi.Field(_roomRow, new Vector2(0, 1), new Vector2(0, -76), 320, "room code", 8);
            _joinBtn = SocialUi.SmallButton(_roomRow, "JOIN ROOM", new Vector2(1, 1), new Vector2(0, -76), 170, UIKit.ButtonStyle.Secondary, JoinCode, 15);

            _status = UIKit.LabelAt(p, "", 17, Theme.TextDim, new Vector2(0, 1), new Vector2(28, -272), new Vector2(504, 26), TextAnchor.MiddleLeft, UIKit.BodyFont);
            _status.rectTransform.pivot = new Vector2(0, 1);
            _status.supportRichText = true;

            _cardsRoot = UIKit.At(p, "Cards", new Vector2(0, 1), new Vector2(28, -304), new Vector2(504, 4 * 88));
            _cardsRoot.pivot = new Vector2(0, 1);
            for (int i = 0; i < _cards.Length; i++) _cards[i] = new SquadCard(_cardsRoot, i, 504, 82);

            _length = new ChipRow(p, new Vector2(0, 0), new Vector2(-150, 150), "", new[] { "5 MIN", "10 MIN", "15 MIN" }, app.Profile.MatchMinutes >= 15 ? 2 : app.Profile.MatchMinutes >= 10 ? 1 : 0,
                i => { app.Profile.MatchMinutes = i == 0 ? 5 : i == 1 ? 10 : 15; app.Profile.Save(); }, 112);
            _length.Root.pivot = new Vector2(0, 0.5f);

            _action = UIKit.Button(p, "READY", new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(500, 92), UIKit.ButtonStyle.Primary, OnAction, 46);
            _actionLabel = UIKit.ButtonLabel(_action);
            _countdown = UIKit.LabelAt(tab, "", 120, Color.white, new Vector2(0.5f, 0.5f), new Vector2(-260, 60), new Vector2(400, 200), TextAnchor.MiddleCenter, UIKit.TitleFont);
            UIKit.Outline(_countdown, new Color(0.4f, 0.15f, 0.9f), 5);

            app.Gateway.Changed += Refresh;
            app.Gateway.Notice += t => Flash(t);
            SetOnline(false);
        }

        public void SetMode(bool online) { _mode.Select(online ? 1 : 0); SetOnline(online); }

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

        private void ToggleMic()
        {
            var v = _app.Voice;
            // cycle: push-to-talk → open mic → off
            v.Mode = (VoiceMode)(((int)v.Mode + 1) % 3);
            _app.Profile.VoiceMode = (int)v.Mode;
            _app.Profile.Save();
            Flash(v.Mode == VoiceMode.PushToTalk ? (Veil.Match.Platform.IsMobile ? "Voice: push-to-talk (hold TALK)" : "Voice: push-to-talk (hold V)") : v.Mode == VoiceMode.OpenMic ? "Voice: open mic" : "Voice chat off");
        }

        private void CopyCode()
        {
            var code = _app.Gateway.Party.code;
            if (string.IsNullOrEmpty(code)) return;
            GUIUtility.systemCopyBuffer = code;
            Flash($"Room code {code} copied");
        }

        private void RoomButton()
        {
            var g = _app.Gateway;
            if (g.Party.Empty) g.CreateRoom(Result);
            else g.LeaveRoom(Result);
        }

        private void JoinCode()
        {
            var code = _codeField.text.Trim();
            if (code.Length < 4) { Flash("<color=#ff9a8a>Enter the 6-letter room code</color>"); return; }
            _app.Gateway.JoinRoom(code, (ok, err) => { if (ok) _codeField.text = ""; Result(ok, err); });
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
                // solo: create a room and queue straight away (matchmaking fills the squad)
                g.CreateRoom((ok, err) => { if (ok) g.StartQueue(_app.Profile.MatchMinutes * 60, Result); else Result(ok, err); });
                return;
            }
            if (party.phase == (int)PartyPhase.Queued) { if (g.IsLeader) g.CancelQueue(Result); return; }
            if (party.phase == (int)PartyPhase.InMatch) return;
            if (g.IsLeader) g.StartQueue(_app.Profile.MatchMinutes * 60, Result);
            else g.SetReady(!(g.Me?.ready ?? false), Result);
        }

        // ------------------------------------------------------------------ view

        public void Refresh()
        {
            if (_panel == null) return;
            var g = _app.Gateway;
            _connectRow.gameObject.SetActive(_online && !g.Online);
            _roomRow.gameObject.SetActive(_online && g.Online);
            _length.Root.gameObject.SetActive(!_online || g.Party.Empty || g.IsLeader);
            _title.text = _online ? "SQUAD  <size=22><color=#aab0d8>online · 4 × 4</color></size>" : "SQUAD  <size=22><color=#aab0d8>vs bots</color></size>";

            if (!_online) { ShowPractice(); UpdateStatus(); return; }

            var party = g.Party;
            _roomLabel.text = party.Empty ? "<color=#aab0d8>No room yet</color>" : $"ROOM  <color=#ffd84a>{party.code}</color>";
            _copyBtn.gameObject.SetActive(!party.Empty);
            UIKit.ButtonLabel(_roomBtn).text = party.Empty ? "CREATE ROOM" : "LEAVE ROOM";
            bool idle = party.Empty || party.phase == (int)PartyPhase.Idle;
            _joinBtn.interactable = idle;

            for (int i = 0; i < _cards.Length; i++)
            {
                if (party.Empty)
                {
                    if (i == 0) _cards[0].ShowMember(g.MyId, _app.Profile.Name, g.Handle, _app.Profile.Look, _app.OnlineProfile?.level ?? 1, false, true, g.Online, g.PingMs, true, true, true, false, null, null, null);
                    else _cards[i].ShowEmpty(() => OpenFriends?.Invoke(), g.Online);
                    continue;
                }
                if (i < party.members.Count)
                {
                    var m = party.members[i];
                    string mid = m.id;
                    _cards[i].ShowMember(m.id, m.name, m.handle, SocialUi.ParseLook(m.look), m.level, m.ready, m.leader, m.online, m.ping,
                        m.id == g.MyId, g.IsLeader, idle, false,
                        () => g.Kick(mid, Result), () => g.Promote(mid, Result),
                        () => { _app.Voice.SetMuted(mid, !_app.Voice.IsMuted(mid)); Refresh(); });
                }
                else
                {
                    bool pending = i - party.members.Count < party.pending.Count;
                    if (pending)
                    {
                        var inv = party.pending[i - party.members.Count];
                        _cards[i].ShowEmpty(null, false);
                        var t = UIKit.LabelAt(_cards[i].Root, $"Invite sent to {inv.name}…", 17, Theme.PurpleLight, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400, 30), TextAnchor.MiddleCenter, UIKit.BoldFont);
                    }
                    else _cards[i].ShowEmpty(() => OpenFriends?.Invoke(), idle);
                }
            }
            UpdateStatus();
        }

        private void ShowPractice()
        {
            _cards[0].ShowMember("", _app.Profile.Name, "", _app.Profile.Look, _app.OnlineProfile?.level ?? 1, _offlineCountdown, true, true, 0, true, false, false, false, null, null, null);
            for (int i = 1; i < _cards.Length; i++)
            {
                var look = Appearance.Preset(i);
                _cards[i].ShowMember("", MatchSim.BotNames[i % MatchSim.BotNames.Length], "", look, 1, true, false, true, 0, false, false, false, true, null, null, null);
            }
        }

        private void UpdateStatus()
        {
            var g = _app.Gateway;
            string s;
            string action;
            bool actionOn = true;
            if (!_online)
            {
                s = "Practice: your squad + 3 bots vs 3 bot squads. Same rules as online.";
                action = _offlineCountdown ? "CANCEL" : "READY";
            }
            else if (!g.Online)
            {
                s = g.Status == GatewayClient.State.Connecting ? "Connecting to the VEIL server…" :
                    string.IsNullOrEmpty(g.LastError) ? "Enter the server address (auto-detected on your Wi-Fi) and CONNECT." : $"<color=#ff9a8a>Offline: {g.LastError}</color>";
                action = "CONNECT";
            }
            else
            {
                var party = g.Party;
                if (g.Assignment != null && _app.State != GameApp.AppState.Match)
                {
                    s = "<color=#ffd84a>Your match is still running.</color> Rejoin your squad!";
                    action = "REJOIN";
                }
                else if (party.Empty)
                {
                    s = $"<color=#7dff9a>●</color> Online as <color=#ffd84a>{g.Handle}</color> · {g.PingMs} ms · create a room or play solo";
                    action = "PLAY SOLO";
                }
                else if (party.phase == (int)PartyPhase.Queued)
                {
                    s = $"<color=#40e6ff>Finding a match…</color> {party.queueSeconds / 60}:{party.queueSeconds % 60:00}  ·  bots fill empty seats";
                    action = g.IsLeader ? "CANCEL" : "SEARCHING…";
                    actionOn = g.IsLeader;
                }
                else if (party.phase == (int)PartyPhase.InMatch)
                {
                    s = "Match starting…";
                    action = "LOADING…";
                    actionOn = false;
                }
                else
                {
                    int ready = 0;
                    foreach (var m in party.members) if (m.ready || m.leader) ready++;
                    int minutes = g.IsLeader ? _app.Profile.MatchMinutes : party.seconds / 60;
                    s = $"{party.members.Count}/{GameConfig.SquadSize} in squad · {ready} ready · {minutes} min match";
                    if (g.IsLeader) { action = ready == party.members.Count ? "START" : "WAITING…"; actionOn = ready == party.members.Count; }
                    else action = (g.Me?.ready ?? false) ? "UNREADY" : "READY";
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
            foreach (var c in _cards) c.UpdateVoice(_app.Voice);
            var v = _app.Voice;
            string mode = v.Mode == VoiceMode.Off ? "<color=#8a90b8>VOICE OFF</color>" : v.Mode == VoiceMode.OpenMic ? "OPEN MIC" : "PUSH TO TALK";
            if (v.Mode != VoiceMode.Off && v.LocalSpeaking) mode = "<color=#7dff9a>● TALKING</color>";
            _voiceLabel.text = mode;
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
            Refresh();
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
