using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Veil.UI
{
    /// <summary>Row of selectable chips (one active).</summary>
    public sealed class ChipRow
    {
        public readonly RectTransform Root;
        private readonly List<Image> _chips = new List<Image>();
        private readonly List<Text> _labels = new List<Text>();
        public int Selected { get; private set; }

        public ChipRow(Transform parent, Vector2 anchor, Vector2 pos, string title, string[] options, int selected, Action<int> onPick, float chipWidth = 120, float labelWidth = 180)
        {
            float width = options.Length * (chipWidth + 8) + labelWidth;
            Root = UIKit.At(parent, "Chips_" + title, anchor, pos, new Vector2(width, 48));
            var t = UIKit.LabelAt(Root, title, 20, Theme.TextDim, new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(170, 40), TextAnchor.MiddleLeft, UIKit.BoldFont); UIKit.Fit(t);
            t.rectTransform.pivot = new Vector2(0, 0.5f);
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                var b = UIKit.Button(Root, options[i], new Vector2(0, 0.5f), new Vector2(labelWidth + i * (chipWidth + 8), 0), new Vector2(chipWidth, 44), UIKit.ButtonStyle.Ghost, () => { Select(idx); onPick(idx); }, options[i].Length > 7 ? 15 : 18);
                ((RectTransform)b.transform).pivot = new Vector2(0, 0.5f);
                _chips.Add((Image)b.targetGraphic);
                var lbl = UIKit.ButtonLabel(b);
                UIKit.Fit(lbl, 10);   // long options shrink to fit their chip instead of spilling out
                _labels.Add(lbl);
            }
            Select(selected);
        }

        public void Select(int i)
        {
            Selected = i;
            for (int k = 0; k < _chips.Count; k++)
            {
                _chips[k].color = k == i ? new Color(0.62f, 0.38f, 1f, 0.9f) : new Color(1, 1, 1, 0.08f);
                _labels[k].color = k == i ? Color.white : Theme.TextDim;
            }
        }
    }

    /// <summary>Row of colour swatches.</summary>
    public sealed class SwatchRow
    {
        private readonly List<Image> _rings = new List<Image>();

        public SwatchRow(Transform parent, Vector2 anchor, Vector2 pos, string title, Color[] colors, int selected, Action<int> onPick)
        {
            var root = UIKit.At(parent, "Swatches_" + title, anchor, pos, new Vector2(colors.Length * 56 + 180, 52));
            var t = UIKit.LabelAt(root, title, 20, Theme.TextDim, new Vector2(0, 0.5f), Vector2.zero, new Vector2(170, 40), TextAnchor.MiddleLeft, UIKit.BoldFont); UIKit.Fit(t);
            t.rectTransform.pivot = new Vector2(0, 0.5f);
            for (int i = 0; i < colors.Length; i++)
            {
                int idx = i;
                var rt = UIKit.At(root, "Swatch", new Vector2(0, 0.5f), new Vector2(180 + i * 56, 0), new Vector2(46, 46));
                rt.pivot = new Vector2(0, 0.5f);
                var img = UIKit.Image(rt, UIKit.Circle, colors[i], true);
                var btn = rt.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => { Audio.Sfx.Play(Audio.Sfx.Click, 0.5f); Select(idx); onPick(idx); });
                rt.gameObject.AddComponent<ButtonFx>();
                var ring = UIKit.Fill(rt, "Ring", -5);
                _rings.Add(UIKit.Image(ring, UIKit.Ring, Color.white));
            }
            Select(selected);
        }

        public void Select(int i)
        {
            for (int k = 0; k < _rings.Count; k++) _rings[k].enabled = k == i;
        }
    }

    public static class Widgets
    {
        public static Slider SliderRow(Transform parent, Vector2 anchor, Vector2 pos, string title, float min, float max, float value, Action<float> onChange, Func<float, string> fmt = null)
        {
            var root = UIKit.At(parent, "Slider_" + title, anchor, pos, new Vector2(700, 48));
            var t = UIKit.LabelAt(root, title, 20, Theme.TextDim, new Vector2(0, 0.5f), Vector2.zero, new Vector2(240, 40), TextAnchor.MiddleLeft, UIKit.BoldFont); UIKit.Fit(t);
            t.rectTransform.pivot = new Vector2(0, 0.5f);
            var val = UIKit.LabelAt(root, "", 20, Theme.Text, new Vector2(1, 0.5f), Vector2.zero, new Vector2(90, 40), TextAnchor.MiddleRight, UIKit.BoldFont);
            val.rectTransform.pivot = new Vector2(1, 0.5f);

            var sRt = UIKit.At(root, "Slider", new Vector2(0, 0.5f), new Vector2(250, 0), new Vector2(340, 24));
            sRt.pivot = new Vector2(0, 0.5f);
            var bg = UIKit.Image(sRt, UIKit.Pill, new Color(1, 1, 1, 0.12f), true);
            var fillArea = UIKit.Rect(sRt, "FillArea", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-12, -8));
            var fill = UIKit.Rect(fillArea, "Fill", Vector2.zero, new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, new Vector2(0, 0));
            UIKit.Image(fill, UIKit.Pill, Theme.Purple);
            var handleArea = UIKit.Rect(sRt, "HandleArea", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-24, 0));
            var handle = UIKit.Rect(handleArea, "Handle", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30, 10));
            var hImg = UIKit.Image(handle, UIKit.Circle, Color.white, true);
            var s = sRt.gameObject.AddComponent<Slider>();
            s.fillRect = fill;
            s.handleRect = handle;
            s.targetGraphic = hImg;
            s.minValue = min;
            s.maxValue = max;
            s.value = value;
            fmt ??= v => v.ToString("0.00");
            val.text = fmt(value);
            s.onValueChanged.AddListener(v => { val.text = fmt(v); onChange(v); });
            return s;
        }

        public static InputField InputRow(Transform parent, Vector2 anchor, Vector2 pos, string title, string value, Action<string> onChange, float width = 360, string placeholder = null)
        {
            var root = UIKit.At(parent, "Input_" + title, anchor, pos, new Vector2(width + 180, 52));
            var t = UIKit.LabelAt(root, title, 20, Theme.TextDim, new Vector2(0, 0.5f), Vector2.zero, new Vector2(170, 40), TextAnchor.MiddleLeft, UIKit.BoldFont); UIKit.Fit(t);
            t.rectTransform.pivot = new Vector2(0, 0.5f);
            var fRt = UIKit.At(root, "Field", new Vector2(0, 0.5f), new Vector2(180, 0), new Vector2(width, 48));
            fRt.pivot = new Vector2(0, 0.5f);
            var img = UIKit.Image(fRt, UIKit.RoundedSmall, new Color(1, 1, 1, 0.1f), true);
            var textRt = UIKit.Fill(fRt, "Text", 0);
            textRt.offsetMin = new Vector2(14, 4); textRt.offsetMax = new Vector2(-14, -4);
            var text = textRt.gameObject.AddComponent<Text>();
            text.font = UIKit.BoldFont; text.fontSize = UIKit.Fs(22); text.color = Theme.Text; text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            var field = fRt.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.targetGraphic = img;
            field.characterLimit = 32;
            if (!string.IsNullOrEmpty(placeholder))
            {
                var ph = UIKit.Label(textRt, placeholder, 18, new Color(1, 1, 1, 0.4f), TextAnchor.MiddleLeft, UIKit.BodyFont, "Placeholder");
                ph.fontStyle = FontStyle.Italic;
                UIKit.Fit(ph);
                field.placeholder = ph;
            }
            field.text = value;
            field.onEndEdit.AddListener(v => onChange(v));
            field.onValueChanged.AddListener(v => onChange(v));
            return field;
        }
    }
}
