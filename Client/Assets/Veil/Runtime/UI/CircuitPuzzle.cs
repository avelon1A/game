using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Veil.Match;
using Veil.Sim;

namespace Veil.UI
{
    /// <summary>
    /// Quick circuit-connection puzzle shown when you press HACK: connect each coloured wire on the left to the socket of
    /// the same colour on the right (tap/click a wire, then its socket). Solving it sends Buttons.Hack; the server then
    /// makes you the hacker. Takes ~2-4 s — just enough interaction, then back to the fight.
    /// </summary>
    public sealed class CircuitPuzzle
    {
        private static readonly Color[] Colors = { new Color(1f, 0.3f, 0.4f), new Color(0.25f, 0.85f, 1f), new Color(1f, 0.85f, 0.2f) };
        private readonly RectTransform _root, _board;
        private readonly Text _title;
        private readonly List<Image> _left = new List<Image>(), _right = new List<Image>();
        private readonly List<RectTransform> _wires = new List<RectTransform>();
        private readonly int[] _rightColor = new int[3];
        private readonly bool[] _done = new bool[3];
        private int _picked = -1;
        public bool Open { get; private set; }

        public CircuitPuzzle(Transform parent)
        {
            _root = UIKit.Fill(parent, "CircuitPuzzle");
            UIKit.Image(_root, UIKit.Square, new Color(0, 0, 0, 0.35f), true);
            _board = UIKit.At(_root, "Board", new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(560, 380));
            UIKit.Image(_board, UIKit.Rounded, new Color(0.01f, 0.06f, 0.04f, 0.95f), true);
            _title = UIKit.LabelAt(_board, "CONNECT THE CIRCUIT", 26, new Color(0.24f, 1f, 0.66f), new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(520, 34), TextAnchor.MiddleCenter, UIKit.BoldFont);
            var hint = UIKit.LabelAt(_board, "tap a wire, then the socket of the same colour", 15, new Color(0.18f, 0.66f, 0.45f), new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(520, 22), TextAnchor.MiddleCenter, UIKit.BoldFont);
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var w = UIKit.At(_board, "Wire", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10, 10));
                w.pivot = new Vector2(0, 0.5f);
                UIKit.Image(w, UIKit.Square, Colors[i]);
                w.gameObject.SetActive(false);
                _wires.Add(w);
                var l = UIKit.Button(_board, "", new Vector2(0.5f, 0.5f), new Vector2(-190, 70 - i * 90), new Vector2(78, 64), UIKit.ButtonStyle.Ghost, () => Pick(idx), 18);
                var li = (Image)l.targetGraphic; li.color = Colors[i]; _left.Add(li);
                var r = UIKit.Button(_board, "", new Vector2(0.5f, 0.5f), new Vector2(190, 70 - i * 90), new Vector2(78, 64), UIKit.ButtonStyle.Ghost, () => Drop(idx), 18);
                _right.Add((Image)r.targetGraphic);
            }
            var cancel = UIKit.Button(_board, "CANCEL", new Vector2(0.5f, 0), new Vector2(0, 34), new Vector2(160, 44), UIKit.ButtonStyle.Secondary, Close, 16);
            Close();
        }

        public void Show()
        {
            Open = true;
            _root.gameObject.SetActive(true);
            _picked = -1;
            var order = new List<int> { 0, 1, 2 };
            for (int i = 2; i > 0; i--) { int j = Random.Range(0, i + 1); (order[i], order[j]) = (order[j], order[i]); }
            if (order[0] == 0 && order[1] == 1) (order[0], order[1]) = (order[1], order[0]);   // never already solved
            for (int i = 0; i < 3; i++)
            {
                _rightColor[i] = order[i];
                _right[i].color = Colors[order[i]] * 0.55f;
                _done[i] = false;
                _wires[i].gameObject.SetActive(false);
                _left[i].color = Colors[i];
            }
            _title.text = "CONNECT THE CIRCUIT";
        }

        public void Close() { Open = false; _picked = -1; _root.gameObject.SetActive(false); }

        private void Pick(int i)
        {
            if (_done[i]) return;
            _picked = i;
            for (int k = 0; k < 3; k++) _left[k].color = k == i ? Color.white : Colors[k];
        }

        private void Drop(int socket)
        {
            if (_picked < 0) return;
            int color = _picked;
            if (_rightColor[socket] != color) { _title.text = "WRONG SOCKET — TRY AGAIN"; _left[color].color = Colors[color]; _picked = -1; return; }
            _done[color] = true;
            _right[socket].color = Colors[color];
            _left[color].color = Colors[color];
            // draw the wire between the two pads
            Vector2 a = new Vector2(-190 + 39, 70 - color * 90), b = new Vector2(190 - 39, 70 - socket * 90);
            var w = _wires[color];
            w.gameObject.SetActive(true);
            w.anchoredPosition = a;
            w.sizeDelta = new Vector2((b - a).magnitude, 10);
            w.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
            _picked = -1;
            if (_done[0] && _done[1] && _done[2])
            {
                VirtualInput.Press(Buttons.Hack);
                Audio.Sfx.Play(Audio.Sfx.Capture, 0.6f);
                Close();
            }
            else _title.text = "CONNECT THE CIRCUIT";
        }
    }
}
