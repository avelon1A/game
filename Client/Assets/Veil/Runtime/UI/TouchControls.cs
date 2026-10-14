using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Veil.Match;
using Veil.Sim;

namespace Veil.UI
{
    /// <summary>
    /// Mobile controls: floating joystick (left), look pad (right), Fire/Jump/Dash/Pulse/Decoy buttons,
    /// contextual Market buttons, pause and scoreboard buttons. Writes into <see cref="VirtualInput"/>.
    /// </summary>
    public sealed class TouchControls
    {
        public readonly RectTransform Root;
        private readonly ClientMatch _m;
        private readonly Joystick _stick;
        private readonly ActionButton _fire, _jump, _dash, _pulse, _decoy, _switch;
        private readonly bool _edit;
        public readonly List<(RectTransform rt, string key, Vector2 pos)> Controls = new List<(RectTransform, string, Vector2)>();
        private readonly RectTransform _market;
        public bool ScoreboardOpen { get; private set; }

        public TouchControls(Transform canvas, ClientMatch match, Action onPause, bool editMode = false)
        {
            _m = match;
            _edit = editMode;
            Root = UIKit.Fill(canvas, "TouchControls");
            Root.gameObject.AddComponent<SafeArea>();
            _introFade = Root.gameObject.AddComponent<CanvasGroup>();
            if (!editMode) _introFade.alpha = 0f;   // fades in once the match has updated once (no flash of every button)
            if (!editMode)
            {
                VirtualInput.Reset();
                VirtualInput.Active = true;
            }

            // look pad covers the screen behind everything else
            var look = UIKit.Fill(Root, "LookPad");
            UIKit.Image(look, UIKit.Square, new Color(0, 0, 0, 0), !editMode);
            if (!editMode) look.gameObject.AddComponent<LookPad>();

            // joystick zone: bottom-left
            var zone = UIKit.Rect(Root, "StickZone", new Vector2(0, 0), new Vector2(0.42f, 0.62f), new Vector2(0, 0), Vector2.zero, Vector2.zero);
            UIKit.Image(zone, UIKit.Square, new Color(0, 0, 0, 0), true);
            _stick = zone.gameObject.AddComponent<Joystick>();
            _stick.Build();

            // action cluster: bottom-right
            _fire = Btn("FIRE", Icons.Blaster, new Vector2(-150, 150), 190, Theme.Yellow, true, Buttons.Fire);
            _jump = Btn("JUMP", Icons.Arrow, new Vector2(-380, 90), 118, Theme.Text, true, Buttons.Jump);
            _dash = Btn("DASH", Icons.Dash, new Vector2(-350, 270), 118, Theme.Cyan, false, Buttons.Dash);
            _pulse = Btn("PULSE", Icons.Pulse, new Vector2(-230, 390), 118, Theme.Purple, false, Buttons.Pulse);
            _decoy = Btn("DECOY", Icons.Decoy, new Vector2(-70, 400), 118, Theme.PurpleLight, false, Buttons.Decoy);
            // sniper scope: tap to zoom in / out (only shown when you carry the sniper)
            _scope = Btn("SCOPE", Icons.Target, new Vector2(-62, 292), 92, Theme.Cyan, false, 0);
            _scope.OnTap = () => VirtualInput.ScopeOn = !VirtualInput.ScopeOn;
            // gun <-> fists: a round icon button like the other controls
            _switch = Btn("FISTS", Icons.Fist, new Vector2(-520, 110), 104, Theme.Text, false, Buttons.Switch);
            // grenade throw (only while carrying some)
            _grenade = UIKit.Button(Root, "GRENADE", new Vector2(1, 0), new Vector2(-520, 250), new Vector2(130, 64), UIKit.ButtonStyle.Primary, () => VirtualInput.Press(Buttons.Grenade), 18);
            ((RectTransform)_grenade.transform).pivot = new Vector2(0.5f, 0.5f);
            Reg((RectTransform)_grenade.transform, "grenade");

            // squad voice: hold to talk (push-to-talk mode)
            var talk = UIKit.At(Root, "Btn_MIC", new Vector2(1, 0.5f), new Vector2(-110, 140), new Vector2(104, 104));
            talk.pivot = new Vector2(0.5f, 0.5f);
            talk.gameObject.AddComponent<TalkButton>().Build();
            Reg(talk, "talk");

            // market purchases (only visible inside the Market)
            _market = UIKit.At(Root, "Market", new Vector2(0.5f, 0), new Vector2(0, 250), new Vector2(620, 90));
            string[] labels = { "SPEED", "SHIELD", "KEY" };
            Buttons[] buys = { Buttons.Buy1, Buttons.Buy2, Buttons.Buy3 };
            for (int i = 0; i < 3; i++)
            {
                var b = buys[i];
                UIKit.Button(_market, labels[i], new Vector2(0.5f, 0.5f), new Vector2(-210 + i * 210, 0), new Vector2(190, 80), UIKit.ButtonStyle.Secondary, () => VirtualInput.Press(b), 24);
            }
            _market.gameObject.SetActive(false);

            // top buttons
            var pause = UIKit.Button(Root, "II", new Vector2(1, 1), new Vector2(-300, -24), new Vector2(90, 72), UIKit.ButtonStyle.Ghost, () => onPause?.Invoke(), 30);
            var board = UIKit.Button(Root, "LIST", new Vector2(1, 1), new Vector2(-400, -24), new Vector2(90, 72), UIKit.ButtonStyle.Ghost, () => ScoreboardOpen = !ScoreboardOpen, 20);
            if (editMode) { pause.gameObject.SetActive(false); board.gameObject.SetActive(false); _market.gameObject.SetActive(false); }

            if (!editMode) ControlLayout.Changed += Reapply;
            if (editMode)
            {
                _scope.gameObject.SetActive(true);
                _grenade.gameObject.SetActive(true);
                foreach (var c in Controls) LayoutHandle.Attach(c.rt, c.key, this);
            }
        }

        private UnityEngine.UI.Button _grenade;
        private ActionButton _scope;

        private void Reg(RectTransform rt, string key)
        {
            Controls.Add((rt, key, rt.anchoredPosition));
            ControlLayout.Apply(rt, key);
        }

        private void Reapply()
        {
            foreach (var c in Controls)
            {
                if (!c.rt) continue;
                c.rt.anchoredPosition = c.pos;
                c.rt.localScale = Vector3.one;
                ControlLayout.Apply(c.rt, c.key);
            }
        }

        private RectTransform _glow;

        /// <summary>Tutorial: a pulsing ring around one control (null clears it).</summary>
        public void Highlight(string key)
        {
            if (_glow) UnityEngine.Object.Destroy(_glow.gameObject);
            if (key == null) return;
            foreach (var c in Controls)
            {
                if (c.key != key || !c.rt) continue;
                _glow = UIKit.At(c.rt, "TutorialGlow", new Vector2(0.5f, 0.5f), Vector2.zero, c.rt.sizeDelta * 1.35f);
                UIKit.Image(_glow, UIKit.Ring, Theme.Yellow).raycastTarget = false;
                _glow.gameObject.AddComponent<Pulse>();
                return;
            }
        }

        public LayoutHandle Selected;
        public Action<LayoutHandle> SelectionChanged;

        private ActionButton Btn(string name, Sprite icon, Vector2 pos, float size, Color c, bool hold, Buttons b)
        {
            var rt = UIKit.At(Root, "Btn_" + name, new Vector2(1, 0), pos, new Vector2(size, size));
            rt.pivot = new Vector2(0.5f, 0.5f);
            var btn = rt.gameObject.AddComponent<ActionButton>();
            btn.Build(name, icon, size, c, hold, b);
            Reg(rt, name.ToLowerInvariant());
            return btn;
        }

        private CanvasGroup _introFade;
        private int _introFrames;
        private bool _shownAlive = true;

        public void Update()
        {
            if (_edit) return;
            if (_m.Latest != null && ++_introFrames > 2) _introFade.alpha = Mathf.MoveTowards(_introFade.alpha, 1f, Time.unscaledDeltaTime * 3f);
            // eliminated: hide the move stick and action buttons (mic, map, list and pause stay)
            bool aliveNow = _m.Predicted.Alive;
            if (aliveNow != _shownAlive)
            {
                _shownAlive = aliveNow;
                _stick.gameObject.SetActive(aliveNow);
                foreach (var c in Controls) if (c.rt && c.key != "talk") c.rt.gameObject.SetActive(aliveNow);
                if (!aliveNow) { VirtualInput.Move = Vector2.zero; VirtualInput.FireHeld = false; VirtualInput.SprintLock = false; }
            }
            var me = _m.Predicted;
            _dash.SetCooldown(me.DashCd / GameConfig.DashCooldown, me.Energy >= GameConfig.DashCost);
            _pulse.SetCooldown(me.PulseCd / GameConfig.PulseCooldown, me.Energy >= GameConfig.PulseCost);
            _decoy.SetCooldown(me.DecoyCd / GameConfig.DecoyCooldown, me.Energy >= GameConfig.DecoyCost);
            var self = _m.Latest?.Self;
            bool fistsNow = self?.Fists ?? false;
            int special = self?.Special ?? 0;
            var ws = GameConfig.Current(me.Look.Weapon, fistsNow, special);
            _fire.SetCooldown(me.FireCd / ws.Cooldown, true);
            _switch.SetFace(fistsNow ? Icons.Rifle : Icons.Fist, fistsNow ? (special > 0 ? SpecialName(special) : me.Look.Weapon == 1 ? "SNIPER" : "RIFLE") : "FISTS");
            int nades = self?.Grenades ?? 0;
            if (_grenade.gameObject.activeSelf != (nades > 0 && me.Alive)) _grenade.gameObject.SetActive(nades > 0 && me.Alive);
            UIKit.ButtonLabel(_grenade).text = $"GRENADE ×{nades}";
            bool sniper = me.Look.Weapon == 1 && me.Alive && !fistsNow && special == 0;
            if (_scope.gameObject.activeSelf != sniper) _scope.gameObject.SetActive(sniper);
            if (!sniper) VirtualInput.ScopeOn = false;
            _scope.SetFace(Icons.Target, VirtualInput.ScopeOn ? "UNSCOPE" : "SCOPE");
            bool market = me.Alive && me.ZoneId >= 0 && _m.Map.Zones[me.ZoneId].Type == ZoneType.Market;
            if (_market.gameObject.activeSelf != market) _market.gameObject.SetActive(market);
            Root.gameObject.SetActive(!_m.Ended && !_m.Paused);
        }

        public static string SpecialName(int special) => special == GameConfig.Shotgun ? "SHOTGUN" : special == GameConfig.Smg ? "SMG" : "";

        public void Dispose()
        {
            if (!_edit)
            {
                ControlLayout.Changed -= Reapply;
                VirtualInput.Reset();
                VirtualInput.Active = false;
            }
            UnityEngine.Object.Destroy(Root.gameObject);
        }
    }

    /// <summary>Squad voice: one tap toggles the mic (open mic when on). Green ring while you are heard.</summary>
    public sealed class TalkButton : MonoBehaviour, IPointerDownHandler
    {
        private Image _back, _icon, _ring;
        private Text _label;

        public void Build()
        {
            var rt = (RectTransform)transform;
            _back = UIKit.Image(rt, UIKit.Circle, new Color(0.08f, 0.08f, 0.2f, 0.6f), true);
            var ring = UIKit.Fill(rt, "Ring");
            _ring = UIKit.Image(ring, UIKit.Ring, new Color(0.4f, 1f, 0.5f, 0.8f));
            var ic = UIKit.At(rt, "Icon", new Vector2(0.5f, 0.5f), new Vector2(0, 8), new Vector2(44, 44));
            _icon = UIKit.Image(ic, Icons.Mic, Color.white);
            _label = UIKit.LabelAt(rt, "MIC", 15, Theme.Text, new Vector2(0.5f, 0), new Vector2(0, 14), new Vector2(104, 22), TextAnchor.MiddleCenter, UIKit.BoldFont);
            UIKit.Outline(_label, new Color(0, 0, 0, 0.7f), 1.5f);
        }

        public void OnPointerDown(PointerEventData e)
        {
            var v = Veil.App.GameApp.I != null ? Veil.App.GameApp.I.Voice : null;
            if (v == null) return;
            v.MicMuted = !v.MicMuted;
            PlayerPrefs.SetInt("micMuted", v.MicMuted ? 1 : 0);
        }

        private void Update()
        {
            var v = Veil.App.GameApp.I != null ? Veil.App.GameApp.I.Voice : null;
            bool usable = v != null && !string.IsNullOrEmpty(v.Channel);
            for (int i = 0; i < transform.childCount; i++) if (transform.GetChild(i).gameObject.activeSelf != usable && transform.GetChild(i).name != "EditHandle") transform.GetChild(i).gameObject.SetActive(usable);
            _back.enabled = usable;
            if (!usable) return;
            bool muted = v.MicMuted;
            _icon.sprite = muted ? Icons.MicOff : Icons.Mic;
            _icon.color = muted ? Theme.Red : Color.white;
            _label.text = muted ? "MIC OFF" : "MIC ON";
            _ring.color = muted ? new Color(1f, 0.35f, 0.4f, 0.8f) : v.LocalSpeaking ? new Color(0.4f, 1f, 0.5f, 1f) : new Color(0.4f, 1f, 0.5f, 0.45f);
            _back.color = !muted && v.LocalSpeaking ? new Color(0.2f, 0.7f, 0.3f, 0.75f) : new Color(0.08f, 0.08f, 0.2f, 0.6f);
        }
    }

    /// <summary>Keeps children inside the device safe area (notches, rounded corners, home bar).</summary>
    public sealed class SafeArea : MonoBehaviour
    {
        private Rect _last;

        private void Update()
        {
            var sa = Screen.safeArea;
            if (sa == _last || Screen.width == 0) return;
            _last = sa;
            var rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
            rt.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }

    /// <summary>Drag anywhere not covered by other controls to turn the camera.</summary>
    public sealed class LookPad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private int _pointer = int.MinValue;

        private Vector2 _downPos;
        private float _downTime;

        public void OnPointerDown(PointerEventData e)
        {
            if (_pointer == int.MinValue) _pointer = e.pointerId;
            _downPos = e.position; _downTime = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId == _pointer) _pointer = int.MinValue;
            // a quick tap (not a camera drag) goes to whatever is underneath, e.g. the minimap
            if (Time.unscaledTime - _downTime < 0.3f && (e.position - _downPos).magnitude < 25f) VirtualInput.Tap?.Invoke(e.position);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != _pointer) return;
            float norm = 1080f / Mathf.Max(1, Screen.height);
            VirtualInput.LookDelta += e.delta * norm * 1.1f;
        }
    }

    /// <summary>Floating joystick: appears where the thumb lands inside its zone.</summary>
    public sealed class Joystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private RectTransform _base, _knob, _rt, _lock;
        private Image _lockImg;
        private Text _lockLabel;
        private bool _overLock;
        private Vector2 _origin;
        private int _pointer = int.MinValue;
        private const float Radius = 120f;

        public void Build()
        {
            _rt = (RectTransform)transform;
            _base = UIKit.At(_rt, "Base", new Vector2(0, 0), new Vector2(260, 260), new Vector2(Radius * 2.1f, Radius * 2.1f));
            _base.pivot = new Vector2(0.5f, 0.5f);
            UIKit.Image(_base, UIKit.Circle, new Color(0.08f, 0.08f, 0.2f, 0.35f));
            var ring = UIKit.Fill(_base, "Ring");
            UIKit.Image(ring, UIKit.Ring, new Color(1, 1, 1, 0.35f));
            _knob = UIKit.At(_base, "Knob", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110, 110));
            UIKit.Image(_knob, UIKit.Circle, new Color(1, 1, 1, 0.75f));
            // sprint lock (PUBG-style): drag up onto the lock, let go = keep sprinting
            _lock = UIKit.At(_rt, "SprintLock", new Vector2(0, 0), Vector2.zero, new Vector2(86, 86));
            _lock.pivot = new Vector2(0.5f, 0.5f);
            _lockImg = UIKit.Image(_lock, UIKit.Circle, new Color(0.08f, 0.08f, 0.2f, 0.6f));
            var la = UIKit.At(_lock, "Arrow", new Vector2(0.5f, 0.5f), new Vector2(0, 8), new Vector2(34, 34));
            UIKit.Image(la, Icons.Arrow, Color.white);
            _lockLabel = UIKit.LabelAt(_lock, "SPRINT", 13, Theme.Text, new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(86, 18), TextAnchor.MiddleCenter, UIKit.BoldFont);
            _lock.gameObject.SetActive(false);
            SetAlpha(0.45f);
        }

        private void Update()
        {
            if (VirtualInput.SprintLock && _pointer == int.MinValue)
            {
                _lock.gameObject.SetActive(true);
                _lock.anchoredPosition = new Vector2(260, 260 + Radius * 1.75f);
                _lockImg.color = new Color(1f, 0.82f, 0.25f, 0.9f);
                _lockLabel.text = "LOCKED";
                _knob.anchoredPosition = new Vector2(0, Radius);
                SetAlpha(0.8f);
            }
        }

        private void SetAlpha(float a)
        {
            var cg = _base.GetComponent<CanvasGroup>();
            if (cg == null) cg = _base.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = a;
            cg.blocksRaycasts = false;
        }

        private Vector2 Local(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, e.position, e.pressEventCamera, out var lp);
            return lp - _rt.rect.min;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (_pointer != int.MinValue) return;
            _pointer = e.pointerId;
            VirtualInput.SprintLock = false;   // touching the stick again ends the sprint lock
            _origin = Local(e);
            _base.anchoredPosition = _origin;
            _knob.anchoredPosition = Vector2.zero;
            SetAlpha(1f);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != _pointer) return;
            Vector2 d = Local(e) - _origin;
            _lock.gameObject.SetActive(d.y > Radius * 0.6f);
            _lock.anchoredPosition = _origin + new Vector2(0, Radius * 1.75f);
            _overLock = d.y > Radius * 1.45f && Mathf.Abs(d.x) < Radius * 0.8f;
            _lockImg.color = _overLock ? new Color(1f, 0.82f, 0.25f, 0.9f) : new Color(0.08f, 0.08f, 0.2f, 0.6f);
            _lockLabel.text = "SPRINT";
            if (d.magnitude > Radius) d = d.normalized * Radius;
            _knob.anchoredPosition = d;
            VirtualInput.Move = d / Radius;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != _pointer) return;
            _pointer = int.MinValue;
            VirtualInput.Move = Vector2.zero;
            if (_overLock) { VirtualInput.SprintLock = true; _overLock = false; return; }
            _lock.gameObject.SetActive(false);
            _knob.anchoredPosition = Vector2.zero;
            _base.anchoredPosition = new Vector2(260, 260);
            SetAlpha(0.45f);
        }
    }

    /// <summary>Round action button: hold (Fire/Jump) or tap (abilities), with cooldown sweep.</summary>
    public sealed class ActionButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IDragHandler
    {
        private Image _cd, _icon, _back;
        private Text _label;
        public Action OnTap;
        private bool _hold;
        private Buttons _button;
        private float _press;

        public void Build(string label, Sprite icon, float size, Color c, bool hold, Buttons b)
        {
            _hold = hold;
            _button = b;
            var rt = (RectTransform)transform;
            _back = UIKit.Image(rt, UIKit.Circle, new Color(0.08f, 0.08f, 0.2f, 0.62f), true);
            var ring = UIKit.Fill(rt, "Ring");
            UIKit.Image(ring, UIKit.Ring, new Color(c.r, c.g, c.b, 0.9f));
            var ic = UIKit.At(rt, "Icon", new Vector2(0.5f, 0.5f), new Vector2(0, 6), new Vector2(size * 0.48f, size * 0.48f));
            _icon = UIKit.Image(ic, icon, c);
            var cd = UIKit.Fill(rt, "Cooldown", 4);
            _cd = UIKit.Image(cd, UIKit.Circle, new Color(0, 0, 0, 0.6f));
            _cd.type = Image.Type.Filled; _cd.fillMethod = Image.FillMethod.Radial360; _cd.fillOrigin = 2; _cd.fillClockwise = false;
            var t = UIKit.LabelAt(rt, label, Mathf.RoundToInt(size * 0.14f), Theme.Text, new Vector2(0.5f, 0), new Vector2(0, size * 0.14f), new Vector2(size, 24), TextAnchor.MiddleCenter, UIKit.BoldFont);
            UIKit.Outline(t, new Color(0, 0, 0, 0.7f), 1.5f);
            _label = t;
        }

        public void SetFace(Sprite icon, string label)
        {
            if (_icon.sprite != icon) _icon.sprite = icon;
            if (_label.text != label) _label.text = label;
        }

        public void SetCooldown(float frac, bool affordable)
        {
            _cd.fillAmount = frac > 0 ? frac : (affordable ? 0 : 1);
            var c = _icon.color; c.a = frac <= 0 && affordable ? 1f : 0.45f; _icon.color = c;
            _press = Mathf.MoveTowards(_press, 0, Time.unscaledDeltaTime * 6f);
            transform.localScale = Vector3.one * (1f - 0.08f * _press);
        }

        private void Set(bool down)
        {
            if (_button == Buttons.Fire) VirtualInput.FireHeld = down;
            else if (_button == Buttons.Jump) VirtualInput.JumpHeld = down;
        }

        public void OnPointerDown(PointerEventData e)
        {
            _press = 1f;
            if (_hold) { Set(true); if (_button == Buttons.Jump) VirtualInput.Press(Buttons.Jump); }
            else if (OnTap != null) OnTap();
            else VirtualInput.Press(_button);
        }

        public void OnPointerUp(PointerEventData e) { if (_hold) Set(false); }
        public void OnPointerExit(PointerEventData e) { }

        // PUBG-style: slide the finger on the fire button to aim while shooting
        public void OnDrag(PointerEventData e)
        {
            if (_button != Buttons.Fire) return;
            float norm = 1080f / Mathf.Max(1, Screen.height);
            VirtualInput.LookDelta += e.delta * norm * 1.1f;
        }
    }
}

namespace Veil.UI
{
    /// <summary>Saved HUD button positions and sizes (PUBG-style "customize layout").</summary>
    public static class ControlLayout
    {
        private const string Prefix = "layout2.";
        public static event Action Changed;
        public static void NotifyChanged() => Changed?.Invoke();

        public static void Apply(RectTransform rt, string key)
        {
            var v = PlayerPrefs.GetString(Prefix + key, "");
            var parts = v.Split(',');
            if (parts.Length != 3) return;
            if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)) return;
            float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y);
            float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sc);
            rt.anchoredPosition = new Vector2(x, y);
            rt.localScale = Vector3.one * Mathf.Clamp(sc <= 0 ? 1 : sc, 0.5f, 1.8f);
        }

        public static void Save(RectTransform rt, string key)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            PlayerPrefs.SetString(Prefix + key, rt.anchoredPosition.x.ToString(ci) + "," + rt.anchoredPosition.y.ToString(ci) + "," + rt.localScale.x.ToString(ci));
        }

        public static void Clear(string key) => PlayerPrefs.DeleteKey(Prefix + key);
    }

    /// <summary>Edit-mode overlay on a control: drag to move, tap to select (the editor's size slider then scales it).</summary>
    public sealed class LayoutHandle : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public string Key;
        public RectTransform Target;
        private TouchControls _owner;
        private Image _frame;

        public static void Attach(RectTransform rt, string key, TouchControls owner)
        {
            foreach (var b in rt.GetComponentsInChildren<Behaviour>(true))
                if (b is Button || b is ActionButton || b is TalkButton) b.enabled = false;
            foreach (var g in rt.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            var tb = rt.GetComponent<TalkButton>();
            if (tb) { foreach (Transform c in rt) c.gameObject.SetActive(true); var lbl = rt.GetComponentInChildren<Text>(true); if (lbl) lbl.gameObject.SetActive(true); var img = rt.GetComponent<Image>(); if (img) img.enabled = true; }
            var o = UIKit.Fill(rt, "EditHandle");
            var h = o.gameObject.AddComponent<LayoutHandle>();
            h.Key = key; h.Target = rt; h._owner = owner;
            h._frame = UIKit.Image(o, UIKit.RoundedSmall, new Color(1f, 0.85f, 0.25f, 0.12f), true);
        }

        public void SetSelected(bool on) => _frame.color = on ? new Color(1f, 0.85f, 0.25f, 0.4f) : new Color(1f, 0.85f, 0.25f, 0.12f);

        public void OnPointerDown(PointerEventData e)
        {
            if (_owner.Selected) _owner.Selected.SetSelected(false);
            _owner.Selected = this;
            SetSelected(true);
            _owner.SelectionChanged?.Invoke(this);
        }

        public void OnDrag(PointerEventData e)
        {
            var canvas = Target.GetComponentInParent<Canvas>();
            float k = canvas ? canvas.scaleFactor : 1f;
            Target.anchoredPosition += e.delta / k;
        }
    }
}

namespace Veil.UI
{
    /// <summary>Full-screen "customize controls" editor: drag buttons, resize the selected one, save or reset.</summary>
    public sealed class LayoutEditor
    {
        private readonly RectTransform _root;
        private readonly TouchControls _controls;
        private readonly Text _hint;

        public static void Open(Transform canvas) => new LayoutEditor(canvas);

        private LayoutEditor(Transform canvas)
        {
            _root = UIKit.Fill(canvas, "LayoutEditor");
            _root.SetAsLastSibling();
            UIKit.Image(_root, UIKit.Square, new Color(0.02f, 0.03f, 0.08f, 0.82f), true);
            _controls = new TouchControls(_root, null, null, true);
            _controls.SelectionChanged = h => _hint.text = "Selected: " + h.Key.ToUpperInvariant() + "  ·  drag to move";

            var bar = UIKit.At(_root, "Toolbar", new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(1180, 84));
            bar.pivot = new Vector2(0.5f, 1);
            UIKit.Image(bar, UIKit.RoundedSmall, new Color(0.06f, 0.07f, 0.16f, 0.95f), true);
            _hint = UIKit.LabelAt(bar, "Tap a button to select it, drag to move", 18, Theme.Text, new Vector2(0, 0.5f), new Vector2(24, 0), new Vector2(430, 40), TextAnchor.MiddleLeft, UIKit.BoldFont);
            _hint.rectTransform.pivot = new Vector2(0, 0.5f);
            UIKit.Fit(_hint, 11);
            float x = -600 + 470;
            void B(string label, UIKit.ButtonStyle st, Action a, float w)
            {
                var b = UIKit.Button(bar, label, new Vector2(0.5f, 0.5f), new Vector2(x + w / 2, 0), new Vector2(w, 60), st, a, 20);
                x += w + 10;
            }
            B("SIZE −", UIKit.ButtonStyle.Secondary, () => Scale(-0.1f), 110);
            B("SIZE +", UIKit.ButtonStyle.Secondary, () => Scale(0.1f), 110);
            B("RESET", UIKit.ButtonStyle.Ghost, Reset, 110);
            B("CANCEL", UIKit.ButtonStyle.Ghost, Close, 120);
            B("SAVE", UIKit.ButtonStyle.Primary, Save, 120);
        }

        private void Scale(float d)
        {
            var h = _controls.Selected;
            if (!h) { _hint.text = "Select a button first"; return; }
            float s = Mathf.Clamp(h.Target.localScale.x + d, 0.5f, 1.8f);
            h.Target.localScale = Vector3.one * s;
        }

        private void Reset()
        {
            foreach (var c in _controls.Controls)
            {
                ControlLayout.Clear(c.key);
                c.rt.anchoredPosition = c.pos;
                c.rt.localScale = Vector3.one;
            }
            PlayerPrefs.Save();
            ControlLayout.NotifyChanged();
            _hint.text = "Default layout restored";
        }

        private void Save()
        {
            foreach (var c in _controls.Controls) ControlLayout.Save(c.rt, c.key);
            PlayerPrefs.Save();
            Close();
            ControlLayout.NotifyChanged();
        }

        private void Close()
        {
            _controls.Dispose();
            UnityEngine.Object.Destroy(_root.gameObject);
        }
    }
}

namespace Veil.UI
{
    public sealed class Pulse : MonoBehaviour
    {
        private void Update()
        {
            float k = (Mathf.Sin(Time.unscaledTime * 6f) + 1f) * 0.5f;
            transform.localScale = Vector3.one * (1f + 0.12f * k);
            var g = GetComponent<Graphic>();
            if (g) { var c = g.color; c.a = 0.45f + 0.55f * k; g.color = c; }
        }
    }
}
