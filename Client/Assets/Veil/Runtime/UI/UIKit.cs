using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Veil.UI
{
    /// <summary>Theme colours for all screens (dark navy panels, VEIL purple, bright yellow CTA).</summary>
    public static class Theme
    {
        public static readonly Color Panel = new Color(0.07f, 0.08f, 0.16f, 0.86f);
        public static readonly Color PanelLight = new Color(0.13f, 0.15f, 0.28f, 0.92f);
        public static readonly Color PanelEdge = new Color(0.45f, 0.5f, 0.95f, 0.35f);
        public static readonly Color Text = new Color(0.95f, 0.96f, 1f);
        public static readonly Color TextDim = new Color(0.7f, 0.74f, 0.9f);
        public static readonly Color Purple = new Color(0.62f, 0.38f, 1f);
        public static readonly Color PurpleLight = new Color(0.78f, 0.65f, 1f);
        public static readonly Color Yellow = new Color(1f, 0.82f, 0.16f);
        public static readonly Color YellowDark = new Color(0.85f, 0.6f, 0.05f);
        public static readonly Color Cyan = new Color(0.25f, 0.9f, 1f);
        public static readonly Color Green = new Color(0.4f, 1f, 0.45f);
        public static readonly Color Red = new Color(1f, 0.32f, 0.38f);
        public static readonly Color Gold = new Color(1f, 0.8f, 0.25f);
    }

    /// <summary>Procedural UGUI construction kit: fonts, sprites, panels, labels, buttons, bars.</summary>
    public static class UIKit
    {
        public static Font TitleFont, BoldFont, BodyFont;
        public static Sprite Rounded, RoundedSmall, Circle, Ring, Glow, Pill, Square, Diamond, Gradient, GradientH;

        public static void Init()
        {
            if (Rounded != null) return;
            TitleFont = Resources.Load<Font>("Fonts/LilitaOne-Regular") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BoldFont = Resources.Load<Font>("Fonts/ChakraPetch-Bold") ?? TitleFont;
            BodyFont = Resources.Load<Font>("Fonts/ChakraPetch-SemiBold") ?? BoldFont;
            Rounded = RoundedRect(64, 18, 22);
            RoundedSmall = RoundedRect(32, 7, 10);
            Pill = RoundedRect(64, 31, 31);
            Circle = MakeCircle(128, 0);
            Ring = MakeCircle(128, 10);
            Glow = MakeGlow(128);
            Square = MakeSquare();
            Diamond = MakeDiamond(64);
            Gradient = MakeGradient();
            GradientH = MakeGradientH();
        }

        // ------------------------------------------------------------------ sprites

        private static Sprite RoundedRect(int size, float radius, int border)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float cx = Mathf.Clamp(px, radius, size - radius), cy = Mathf.Clamp(py, radius, size - radius);
                    float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy)) - radius;
                    float a = Mathf.Clamp01(0.5f - d);
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        private static Sprite MakeCircle(int size, float ringWidth)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            float r = size / 2f - 1;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f));
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    if (ringWidth > 0) a *= Mathf.Clamp01(d - (r - ringWidth) + 0.5f);
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100);
        }

        private static Sprite MakeGlow(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                    float a = Mathf.Pow(Mathf.Clamp01(1 - d), 2.2f);
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100);
        }

        private static Sprite MakeSquare()
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = Color.white;
            t.SetPixels(px);
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100);
        }

        private static Sprite MakeDiamond(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Abs(x + 0.5f - size / 2f) + Mathf.Abs(y + 0.5f - size / 2f);
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(size / 2f - 1 - d)));
                }
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100);
        }

        private static Sprite MakeGradientH()
        {
            var t = new Texture2D(64, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < 64; x++)
                for (int y = 0; y < 4; y++) t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Pow(1 - x / 63f, 1.5f)));
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, 64, 4), new Vector2(0.5f, 0.5f), 100);
        }

        private static Sprite MakeGradient()
        {
            var t = new Texture2D(4, 64, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 4; x++) t.SetPixel(x, y, new Color(1, 1, 1, y / 63f));
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, 4, 64), new Vector2(0.5f, 0.5f), 100);
        }

        // ------------------------------------------------------------------ layout helpers

        public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>Rect anchored at a single point (anchor = pivot).</summary>
        public static RectTransform At(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
            => Rect(parent, name, anchor, anchor, anchor, pos, size);

        public static RectTransform Fill(Transform parent, string name, float inset = 0)
        {
            var rt = Rect(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-inset * 2, -inset * 2));
            return rt;
        }

        public static Image Image(RectTransform rt, Sprite sprite, Color color, bool raycast = false)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycast;
            if (sprite != null && sprite.border != Vector4.zero) img.type = UnityEngine.UI.Image.Type.Sliced;
            return img;
        }

        public static Image Panel(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color? color = null, bool edge = true)
        {
            var rt = At(parent, name, anchor, pos, size);
            var img = Image(rt, Rounded, color ?? Theme.Panel, true);
            if (edge)
            {
                var o = rt.gameObject.AddComponent<Outline>();
                o.effectColor = Theme.PanelEdge;
                o.effectDistance = new Vector2(1.5f, -1.5f);
            }
            return img;
        }

        /// <summary>Phone screens are small: scale font sizes up there (small text the most) so everything stays readable.</summary>
        public static int Fs(int size)
        {
            if (!Veil.Match.Platform.IsMobile) return size;
            float k = size <= 18 ? 1.35f : size <= 26 ? 1.25f : size <= 40 ? 1.15f : 1.05f;
            return Mathf.RoundToInt(size * k);
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft, Font font = null, string name = "Label")
        {
            var rt = Rect(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font ?? BodyFont;
            t.fontSize = Fs(size);
            t.color = color;
            t.alignment = align;
            t.text = text;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>Shrink a one-line label to its box when the (phone-scaled) text would spill over its neighbours.</summary>
        public static void Fit(Text t)
        {
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = true;
            t.resizeTextMaxSize = t.fontSize;
            t.resizeTextMinSize = Mathf.Min(12, t.fontSize);
        }

        public static Text LabelAt(Transform parent, string text, int size, Color color, Vector2 anchor, Vector2 pos, Vector2 box, TextAnchor align = TextAnchor.MiddleLeft, Font font = null)
        {
            var rt = At(parent, "Label", anchor, pos, box);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font ?? BodyFont;
            t.fontSize = Fs(size);
            t.color = color;
            t.alignment = align;
            t.text = text;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>Shrink the text (down to minSize) so it always fits inside its box instead of spilling out.</summary>
        public static Text Fit(Text t, int minSize = 10)
        {
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextMaxSize = t.fontSize;
            t.resizeTextMinSize = Mathf.Min(Fs(minSize), t.fontSize);
            t.resizeTextForBestFit = true;
            return t;
        }

        public static void Shadow(Graphic g, float dist = 2f, float alpha = 0.6f)
        {
            var s = g.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0, 0, 0, alpha);
            s.effectDistance = new Vector2(dist, -dist);
        }

        public static void Outline(Graphic g, Color c, float dist = 2f)
        {
            var o = g.gameObject.AddComponent<Outline>();
            o.effectColor = c;
            o.effectDistance = new Vector2(dist, -dist);
        }

        public enum ButtonStyle { Primary, Secondary, Ghost, Tab }

        public static Button Button(Transform parent, string text, Vector2 anchor, Vector2 pos, Vector2 size, ButtonStyle style, Action onClick, int fontSize = 26)
        {
            var rt = At(parent, "Btn_" + text, anchor, pos, size);
            Color bg, fg;
            switch (style)
            {
                case ButtonStyle.Primary: bg = Theme.Yellow; fg = new Color(0.12f, 0.08f, 0.2f); break;
                case ButtonStyle.Secondary: bg = new Color(0.3f, 0.22f, 0.6f, 0.95f); fg = Theme.Text; break;
                case ButtonStyle.Tab: bg = new Color(1, 1, 1, 0f); fg = Theme.TextDim; break;
                default: bg = new Color(1, 1, 1, 0.08f); fg = Theme.Text; break;
            }
            var img = Image(rt, style == ButtonStyle.Primary ? RoundedSmall : Rounded, bg, true);
            if (style == ButtonStyle.Primary)
            {
                var sh = rt.gameObject.AddComponent<Shadow>();
                sh.effectColor = Theme.YellowDark;
                sh.effectDistance = new Vector2(0, -5);
            }
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            var label = Label(rt, text, fontSize, fg, TextAnchor.MiddleCenter, style == ButtonStyle.Primary ? TitleFont : BoldFont);
            if (style == ButtonStyle.Primary) label.fontStyle = FontStyle.Italic;
            btn.onClick.AddListener(() => { Audio.Sfx.Play(Audio.Sfx.Click, 0.6f); onClick?.Invoke(); });
            rt.gameObject.AddComponent<ButtonFx>();
            return btn;
        }

        public static Text ButtonLabel(Button b) => b.GetComponentInChildren<Text>();
    }

    /// <summary>Hover/press juice for buttons.</summary>
    public sealed class ButtonFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private float _target = 1f, _scale = 1f;
        /// <summary>Idle "breathing" scale amount (e.g. 0.025 for the main call-to-action), only while clickable.</summary>
        public float Breathe;
        private Selectable _sel;

        public void OnPointerEnter(PointerEventData e) { _target = 1.05f; Audio.Sfx.Play(Audio.Sfx.Hover, 0.25f); }
        public void OnPointerExit(PointerEventData e) => _target = 1f;
        public void OnPointerDown(PointerEventData e) => _target = 0.96f;
        public void OnPointerUp(PointerEventData e) => _target = 1.05f;
        private void OnDisable() { _target = 1f; _scale = 1f; transform.localScale = Vector3.one; }

        private void Update()
        {
            _scale = Mathf.Lerp(_scale, _target, 1 - Mathf.Exp(-20f * Time.unscaledDeltaTime));
            float b = 0;
            if (Breathe > 0)
            {
                if (_sel == null) _sel = GetComponent<Selectable>();
                if (_sel == null || _sel.interactable) b = Breathe * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.6f));
            }
            transform.localScale = Vector3.one * (_scale + b);
        }
    }

    /// <summary>Horizontal bar with a background, a delayed "damage" trail and a fill.</summary>
    public sealed class Bar
    {
        public readonly RectTransform Root;
        private readonly Image _fill, _trail;
        private float _trailValue = 1f;
        public float Value { get; private set; } = 1f;

        public Bar(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color color, Color back)
        {
            Root = UIKit.At(parent, "Bar", anchor, pos, size);
            UIKit.Image(Root, UIKit.Pill, back);
            var trailRt = UIKit.Fill(Root, "Trail", 3);
            _trail = UIKit.Image(trailRt, UIKit.Pill, new Color(1, 1, 1, 0.6f));
            var fillRt = UIKit.Fill(Root, "Fill", 3);
            _fill = UIKit.Image(fillRt, UIKit.Pill, color);
            _trailRt = trailRt; _fillRt = fillRt;
        }

        private readonly RectTransform _trailRt, _fillRt;

        public void Set(float v, float dt)
        {
            Value = Mathf.Clamp01(v);
            if (Value > _trailValue) _trailValue = Value;
            else _trailValue = Mathf.MoveTowards(_trailValue, Value, dt * 0.6f);
            Apply(_fillRt, Value);
            Apply(_trailRt, _trailValue);
        }

        public void SetColor(Color c) => _fill.color = c;

        private static void Apply(RectTransform rt, float v)
        {
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(Mathf.Max(0.001f, v), 1);
            rt.offsetMin = new Vector2(3, 3);
            rt.offsetMax = new Vector2(-3, -3);
            rt.gameObject.SetActive(v > 0.01f);
        }
    }
}
