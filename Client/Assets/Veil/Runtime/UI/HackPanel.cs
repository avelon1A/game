using UnityEngine;
using UnityEngine.UI;
using Veil.Sim;

namespace Veil.UI
{
    /// <summary>
    /// Hack Terminal panel (docs/HACK_TERMINAL.md §6, §8, §13): own progress + stability, hackers / speed,
    /// contest state, the stabilization nodes during an instability, and every squad's progress.
    /// Shown at the bottom centre while the squad's current step is the Hack Terminal.
    /// </summary>
    public sealed class HackPanel
    {
        private readonly RectTransform _root;
        private readonly Image _back;
        private readonly Text _title, _pct, _state, _nodes, _squads;
        private readonly Bar _progress, _stability;
        private readonly int _localSquad;

        public HackPanel(Transform parent, int localSquad)
        {
            _localSquad = localSquad;
            _root = UIKit.At(parent, "HackPanel", new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(560, 176));
            _root.pivot = new Vector2(0.5f, 0);
            _back = UIKit.Image(_root, UIKit.Rounded, new Color(0.06f, 0.07f, 0.14f, 0.82f));

            _title = UIKit.LabelAt(_root, "HACKING TERMINAL", 22, Color.white, new Vector2(0, 1), new Vector2(20, -12), new Vector2(330, 28), TextAnchor.MiddleLeft, UIKit.BoldFont);
            _title.rectTransform.pivot = new Vector2(0, 1);
            _title.supportRichText = true;
            _pct = UIKit.LabelAt(_root, "0%", 26, Theme.Gold, new Vector2(1, 1), new Vector2(-20, -10), new Vector2(120, 32), TextAnchor.MiddleRight, UIKit.TitleFont);
            _pct.rectTransform.pivot = new Vector2(1, 1);

            _progress = new Bar(_root, new Vector2(0, 1), new Vector2(20, -46), new Vector2(520, 16), Theme.Gold, new Color(1, 1, 1, 0.1f));
            _progress.Root.pivot = new Vector2(0, 1);

            var stl = UIKit.LabelAt(_root, "STABILITY", 13, Theme.TextDim, new Vector2(0, 1), new Vector2(20, -68), new Vector2(90, 18), TextAnchor.MiddleLeft, UIKit.BoldFont);
            stl.rectTransform.pivot = new Vector2(0, 1);
            _stability = new Bar(_root, new Vector2(0, 1), new Vector2(110, -72), new Vector2(200, 9), Theme.Cyan, new Color(1, 1, 1, 0.1f));
            _stability.Root.pivot = new Vector2(0, 1);
            _state = UIKit.LabelAt(_root, "", 15, Theme.Text, new Vector2(1, 1), new Vector2(-20, -66), new Vector2(220, 20), TextAnchor.MiddleRight, UIKit.BoldFont);
            _state.rectTransform.pivot = new Vector2(1, 1);
            _state.supportRichText = true;

            _nodes = UIKit.LabelAt(_root, "", 17, Color.white, new Vector2(0, 1), new Vector2(20, -94), new Vector2(520, 44), TextAnchor.UpperLeft, UIKit.BoldFont);
            _nodes.rectTransform.pivot = new Vector2(0, 1);
            _nodes.supportRichText = true;

            _squads = UIKit.LabelAt(_root, "", 15, Theme.TextDim, new Vector2(0, 0), new Vector2(20, 10), new Vector2(520, 20), TextAnchor.MiddleLeft, UIKit.BoldFont);
            _squads.rectTransform.pivot = new Vector2(0, 0);
            _squads.supportRichText = true;
        }

        public static string KindName(NodeKind k) => k == NodeKind.Destroy ? "DESTROY" : k == NodeKind.Stabilize ? "STABILIZE" : "OVERRIDE";
        public static string KindHint(NodeKind k) => k == NodeKind.Destroy ? "shoot it" : k == NodeKind.Stabilize ? "stand next to it" : "hold it (slips back)";
        public static Color KindColor(NodeKind k) => k == NodeKind.Destroy ? new Color(1f, 0.3f, 0.45f) : k == NodeKind.Stabilize ? new Color(0.25f, 0.9f, 1f) : new Color(0.75f, 0.45f, 1f);

        /// <summary>Stability drains from 100% towards the next instability, refills after it (purely a readout of progress).</summary>
        public static float Stability(float prog, int nodesLeft)
        {
            if (nodesLeft > 0) return 0f;
            float prev = 0f;
            foreach (float at in GameConfig.HackInstability)
            {
                if (prog < at - 1e-4f) return 1f - (prog - prev) / (at - prev) * 0.85f;
                prev = at;
            }
            return 1f;
        }

        public void Update(Snapshot s, float dt)
        {
            bool show = GameConfig.ExtractionMode && s.Stage == 0;
            _root.gameObject.SetActive(show);
            if (!show) return;

            bool unstable = s.Nodes.Count > 0;
            _progress.Set(s.StageProg, dt);
            _progress.SetColor(unstable ? new Color(1f, 0.35f, 0.4f) : s.HackContested ? new Color(1f, 0.55f, 0.2f) : Theme.Gold);
            _pct.text = $"{Mathf.FloorToInt(s.StageProg * 100)}%";
            float st = Stability(s.StageProg, s.Nodes.Count);
            _stability.Set(st, dt);
            _stability.SetColor(st < 0.3f ? new Color(1f, 0.35f, 0.4f) : Theme.Cyan);

            float pulse = 0.55f + Mathf.Sin(Time.time * 8f) * 0.45f;
            if (unstable)
            {
                _title.text = $"<color=#ff5a6a>TERMINAL INSTABILITY</color>";
                _state.text = "STABILIZATION REQUIRED";
                _back.color = new Color(0.22f, 0.05f, 0.1f, 0.86f);
            }
            else if (s.HackContested)
            {
                _title.text = $"<color=#ffa040>TERMINAL CONTESTED</color>";
                _state.text = "<color=#ffa040>progress paused · clear the zone</color>";
                _back.color = new Color(0.2f, 0.11f, 0.04f, 0.86f);
            }
            else if (s.Hackers > 0)
            {
                int pct = Mathf.RoundToInt(GameConfig.HackSpeed[Mathf.Min(s.Hackers, GameConfig.HackSpeed.Length - 1)] * 100);
                _title.text = "HACKING TERMINAL";
                _state.text = $"{s.Hackers} hacking · <color=#ffd84a>{pct}% speed</color>";
                _back.color = new Color(0.06f, 0.07f, 0.14f, 0.82f);
            }
            else
            {
                _title.text = "HACK TERMINAL";
                _state.text = "<color=#aab0d8>enter the plaza ring to hack</color>";
                _back.color = new Color(0.06f, 0.07f, 0.14f, 0.82f);
            }

            if (unstable)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var n in s.Nodes)
                {
                    var c = ColorUtility.ToHtmlStringRGB(KindColor(n.Kind));
                    string prog = n.Contested ? "<color=#ffa040>contested</color>" : $"{Mathf.RoundToInt(n.Prog * 100)}%";
                    sb.Append($"<color=#{c}>● {KindName(n.Kind)}</color> {prog}    ");
                }
                sb.Append($"\n<size=13><color=#aab0d8>Split up: destroy = shoot · stabilize = stand · override = hold</color></size>");
                _nodes.text = sb.ToString();
                _nodes.color = new Color(1, 1, 1, 0.75f + pulse * 0.25f);
            }
            else
            {
                _nodes.text = s.Hackers == 0 && !s.HackContested
                    ? "<color=#aab0d8>More hackers = faster · keep someone on the bridges</color>"
                    : "<color=#aab0d8>Instability at 35% and 67% · progress is never lost</color>";
                _nodes.color = Color.white;
            }

            // every squad's terminal progress (public)
            var sq = new System.Text.StringBuilder();
            for (int q = 0; q < GameConfig.SquadCount; q++)
            {
                string pr = s.SquadStage[q] > 0 ? "DONE" : $"{Mathf.FloorToInt(s.SquadProg[q] * 100)}%";
                bool me = q == _localSquad;
                sq.Append(me ? $"<color=#7dff9a>YOU {pr}</color>" : $"SQUAD {(char)('A' + q)} <color=#ffffff>{pr}</color>");
                if (q < GameConfig.SquadCount - 1) sq.Append("   ·   ");
            }
            _squads.text = sq.ToString();
        }
    }
}
