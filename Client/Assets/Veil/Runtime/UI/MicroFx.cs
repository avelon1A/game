using UnityEngine;
using UnityEngine.UI;

namespace Veil.UI
{
    // Small lobby micro-animations. All use unscaled time and never move the camera or the characters.

    /// <summary>Fades + slides an element in every time it is shown (tab switch, back from a match).</summary>
    public sealed class EnterFx : MonoBehaviour
    {
        public Vector2 From = new Vector2(0, -24);
        public float Delay, Duration = 0.38f;
        private CanvasGroup _cg;
        private RectTransform _rt;
        private Vector2 _base;
        private float _t;
        private bool _running;

        public static EnterFx Add(Component c, Vector2 from, float delay = 0f)
        {
            var fx = c.gameObject.AddComponent<EnterFx>();
            fx.From = from; fx.Delay = delay;
            fx.Begin();
            return fx;
        }

        private void OnEnable() { if (_rt != null) Begin(); }

        private void Begin()
        {
            _rt = (RectTransform)transform;
            _cg = _cg != null ? _cg : GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            if (!_running) _base = _rt.anchoredPosition;
            _t = -Delay;
            _running = true;
            Apply(0);
        }

        private void OnDisable() { if (_running) { _running = false; Apply(1); } }

        private void Apply(float k)
        {
            float e = 1 - (1 - k) * (1 - k) * (1 - k);   // ease-out cubic
            _rt.anchoredPosition = _base + From * (1 - e);
            _cg.alpha = e;
        }

        private void Update()
        {
            if (!_running) return;
            _t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_t / Duration);
            Apply(k);
            if (k >= 1) _running = false;
        }
    }

    /// <summary>Quick scale "punch" (join, ready, badge, countdown tick). Don't combine with ButtonFx.</summary>
    public sealed class PunchFx : MonoBehaviour
    {
        private float _a;
        public float Base = 1f;

        public static PunchFx On(Component c) => c.GetComponent<PunchFx>() ?? c.gameObject.AddComponent<PunchFx>();
        public void Kick(float amount = 0.12f) => _a = amount;
        private void OnDisable() { _a = 0; transform.localScale = Vector3.one * Base; }

        private void Update()
        {
            if (Mathf.Abs(_a) <= 0.0005f) { if (_a != 0) { _a = 0; transform.localScale = Vector3.one * Base; } return; }
            _a *= Mathf.Exp(-9f * Time.unscaledDeltaTime);
            transform.localScale = Vector3.one * (Base + _a);
        }
    }

    /// <summary>Gentle vertical float (idle icons).</summary>
    public sealed class BobFx : MonoBehaviour
    {
        public float Amp = 4f, Speed = 1.6f, Phase;
        private RectTransform _rt;
        private Vector2 _base;

        private void Awake() { _rt = (RectTransform)transform; _base = _rt.anchoredPosition; }
        private void OnDisable() { if (_rt != null) _rt.anchoredPosition = _base; }
        private void Update() => _rt.anchoredPosition = _base + new Vector2(0, Mathf.Sin(Time.unscaledTime * Speed + Phase) * Amp);
    }

    /// <summary>A light streak that sweeps across a button now and then (only while it's clickable).</summary>
    public sealed class ShineFx : MonoBehaviour
    {
        public float Every = 3.2f, Sweep = 0.7f;
        private RectTransform _bar, _clip;
        private Selectable _sel;
        private float _t;

        public static ShineFx Add(Component c)
        {
            var fx = c.gameObject.AddComponent<ShineFx>();
            fx._sel = c.GetComponent<Selectable>();
            fx._clip = UIKit.Fill(c.transform, "ShineClip", 2);
            fx._clip.gameObject.AddComponent<RectMask2D>();
            fx._clip.SetSiblingIndex(0);   // under the label
            fx._bar = UIKit.At(fx._clip, "Shine", new Vector2(0, 0.5f), Vector2.zero, new Vector2(46, 200));
            fx._bar.localRotation = Quaternion.Euler(0, 0, -22);
            UIKit.Image(fx._bar, UIKit.Square, new Color(1, 1, 1, 0.35f));
            fx._t = 0.8f;
            return fx;
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            if (_t > Every) _t = 0;
            bool on = _sel == null || _sel.interactable;
            float w = _clip.rect.width;
            float k = _t / Sweep;
            _bar.gameObject.SetActive(on && k <= 1);
            if (k <= 1) _bar.anchoredPosition = new Vector2(Mathf.Lerp(-60, w + 60, k * k * (3 - 2 * k)), 0);
        }
    }

    /// <summary>Soft alpha breathing for "empty slot" style hints.</summary>
    public sealed class BreatheFx : MonoBehaviour
    {
        public float Min = 0.45f, Speed = 2.2f, Phase;
        private Graphic _g;
        private void Awake() => _g = GetComponent<Graphic>();
        private void Update()
        {
            if (_g == null) return;
            var c = _g.color;
            c.a = Mathf.Lerp(Min, 1f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Speed + Phase));
            _g.color = c;
        }
    }
}
