using System;
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
        private readonly ActionButton _fire, _jump, _dash, _pulse, _decoy;
        private readonly RectTransform _market;
        public bool ScoreboardOpen { get; private set; }

        public TouchControls(Transform canvas, ClientMatch match, Action onPause)
        {
            _m = match;
            Root = UIKit.Fill(canvas, "TouchControls");
            Root.gameObject.AddComponent<SafeArea>();
            VirtualInput.Reset();
            VirtualInput.Active = true;

            // look pad covers the screen behind everything else
            var look = UIKit.Fill(Root, "LookPad");
            UIKit.Image(look, UIKit.Square, new Color(0, 0, 0, 0), true);
            look.gameObject.AddComponent<LookPad>();

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

            // squad voice: hold to talk (push-to-talk mode)
            var talk = UIKit.At(Root, "Btn_TALK", new Vector2(1, 0.5f), new Vector2(-110, 140), new Vector2(104, 104));
            talk.pivot = new Vector2(0.5f, 0.5f);
            talk.gameObject.AddComponent<TalkButton>().Build();

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
        }

        private ActionButton Btn(string name, Sprite icon, Vector2 pos, float size, Color c, bool hold, Buttons b)
        {
            var rt = UIKit.At(Root, "Btn_" + name, new Vector2(1, 0), pos, new Vector2(size, size));
            rt.pivot = new Vector2(0.5f, 0.5f);
            var btn = rt.gameObject.AddComponent<ActionButton>();
            btn.Build(name, icon, size, c, hold, b);
            return btn;
        }

        public void Update()
        {
            var me = _m.Predicted;
            _dash.SetCooldown(me.DashCd / GameConfig.DashCooldown, me.Energy >= GameConfig.DashCost);
            _pulse.SetCooldown(me.PulseCd / GameConfig.PulseCooldown, me.Energy >= GameConfig.PulseCost);
            _decoy.SetCooldown(me.DecoyCd / GameConfig.DecoyCooldown, me.Energy >= GameConfig.DecoyCost);
            _fire.SetCooldown(0, true);
            bool market = me.Alive && me.ZoneId >= 0 && _m.Map.Zones[me.ZoneId].Type == ZoneType.Market;
            if (_market.gameObject.activeSelf != market) _market.gameObject.SetActive(market);
            Root.gameObject.SetActive(!_m.Ended && !_m.Paused);
        }

        public void Dispose()
        {
            VirtualInput.Reset();
            VirtualInput.Active = false;
            UnityEngine.Object.Destroy(Root.gameObject);
        }
    }

    /// <summary>Hold-to-talk button for squad voice.</summary>
    public sealed class TalkButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        private Image _back;
        private Text _label;

        public void Build()
        {
            var rt = (RectTransform)transform;
            _back = UIKit.Image(rt, UIKit.Circle, new Color(0.08f, 0.08f, 0.2f, 0.55f), true);
            var ring = UIKit.Fill(rt, "Ring");
            UIKit.Image(ring, UIKit.Ring, new Color(0.4f, 1f, 0.5f, 0.8f));
            _label = UIKit.Label(rt, "TALK", 20, Theme.Text, TextAnchor.MiddleCenter, UIKit.BoldFont);
            UIKit.Outline(_label, new Color(0, 0, 0, 0.7f), 1.5f);
        }

        public void OnPointerDown(PointerEventData e) => VirtualInput.TalkHeld = true;
        public void OnPointerUp(PointerEventData e) => VirtualInput.TalkHeld = false;

        private void Update()
        {
            var v = Veil.App.GameApp.I != null ? Veil.App.GameApp.I.Voice : null;
            bool on = v != null && v.LocalSpeaking;
            _back.color = on ? new Color(0.2f, 0.7f, 0.3f, 0.75f) : new Color(0.08f, 0.08f, 0.2f, 0.55f);
            bool usable = v != null && v.Mode == Veil.Voice.VoiceMode.PushToTalk && !string.IsNullOrEmpty(v.Channel);
            if (_label.gameObject.activeSelf != usable) { _label.gameObject.SetActive(usable); _back.enabled = usable; transform.GetChild(0).gameObject.SetActive(usable); }
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

        public void OnPointerDown(PointerEventData e) { if (_pointer == int.MinValue) _pointer = e.pointerId; }
        public void OnPointerUp(PointerEventData e) { if (e.pointerId == _pointer) _pointer = int.MinValue; }

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
        private RectTransform _base, _knob, _rt;
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
            SetAlpha(0.45f);
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
            _origin = Local(e);
            _base.anchoredPosition = _origin;
            _knob.anchoredPosition = Vector2.zero;
            SetAlpha(1f);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != _pointer) return;
            Vector2 d = Local(e) - _origin;
            if (d.magnitude > Radius) d = d.normalized * Radius;
            _knob.anchoredPosition = d;
            VirtualInput.Move = d / Radius;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != _pointer) return;
            _pointer = int.MinValue;
            VirtualInput.Move = Vector2.zero;
            _knob.anchoredPosition = Vector2.zero;
            _base.anchoredPosition = new Vector2(260, 260);
            SetAlpha(0.45f);
        }
    }

    /// <summary>Round action button: hold (Fire/Jump) or tap (abilities), with cooldown sweep.</summary>
    public sealed class ActionButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private Image _cd, _icon, _back;
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
            else VirtualInput.Press(_button);
        }

        public void OnPointerUp(PointerEventData e) { if (_hold) Set(false); }
        public void OnPointerExit(PointerEventData e) { }
    }
}
