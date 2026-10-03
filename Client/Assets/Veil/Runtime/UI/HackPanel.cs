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
        public System.Func<int, string> HackerName;
        private readonly Text _log, _header;
        private static Font _mono;
        private static Font Mono => _mono ??= Font.CreateDynamicFontFromOSFont(new[] { "Menlo", "Consolas", "Courier New", "Droid Sans Mono", "monospace" }, 16);
        private static readonly Color Term = new Color(0.24f, 1f, 0.66f), TermDim = new Color(0.18f, 0.66f, 0.45f);

        public HackPanel(Transform parent, int localSquad)
        {
            _localSquad = localSquad;
            _root = UIKit.At(parent, "HackPanel", new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(600, 262));
            _root.pivot = new Vector2(0.5f, 0);
            _back = UIKit.Image(_root, UIKit.Rounded, new Color(0.01f, 0.05f, 0.035f, 0.9f));
            var edge = UIKit.Fill(_root, "Edge");
            UIKit.Image(edge, UIKit.Ring, new Color(0.1f, 1f, 0.6f, 0.0f));
            // scan lines
            for (int y = 4; y < 262; y += 4)
            {
                var ln = UIKit.At(_root, "Scan", new Vector2(0.5f, 0), new Vector2(0, y), new Vector2(584, 1));
                UIKit.Image(ln, UIKit.Square, new Color(1, 1, 1, 0.025f));
            }
            _header = UIKit.LabelAt(_root, "RILO//OS  ·  TERMINAL 07  ·  CENTRAL PLAZA", 13, TermDim, new Vector2(0, 1), new Vector2(20, -8), new Vector2(560, 18), TextAnchor.MiddleLeft);
            _header.font = Mono; _header.rectTransform.pivot = new Vector2(0, 1);
            _log = UIKit.LabelAt(_root, "", 13, TermDim, new Vector2(0, 0), new Vector2(20, 34), new Vector2(560, 74), TextAnchor.LowerLeft);
            _log.font = Mono; _log.rectTransform.pivot = new Vector2(0, 0); _log.supportRichText = true; _log.lineSpacing = 1.05f;

            _title = UIKit.LabelAt(_root, "HACKING TERMINAL", 22, Color.white, new Vector2(0, 1), new Vector2(20, -28), new Vector2(380, 28), TextAnchor.MiddleLeft, UIKit.BoldFont);
            _title.rectTransform.pivot = new Vector2(0, 1); _title.font = Mono;
            _title.supportRichText = true;
            _pct = UIKit.LabelAt(_root, "0%", 26, Term, new Vector2(1, 1), new Vector2(-20, -26), new Vector2(120, 32), TextAnchor.MiddleRight, UIKit.TitleFont);
            _pct.rectTransform.pivot = new Vector2(1, 1);

            _progress = new Bar(_root, new Vector2(0, 1), new Vector2(20, -62), new Vector2(560, 18), Term, new Color(0.1f, 1f, 0.6f, 0.12f));
            _progress.Root.pivot = new Vector2(0, 1);

            var stl = UIKit.LabelAt(_root, "STABILITY", 13, Theme.TextDim, new Vector2(0, 1), new Vector2(20, -88), new Vector2(90, 18), TextAnchor.MiddleLeft, UIKit.BoldFont); stl.font = Mono; stl.color = TermDim;
            stl.rectTransform.pivot = new Vector2(0, 1);
            _stability = new Bar(_root, new Vector2(0, 1), new Vector2(110, -92), new Vector2(200, 9), Theme.Cyan, new Color(1, 1, 1, 0.1f));
            _stability.Root.pivot = new Vector2(0, 1);
            _state = UIKit.LabelAt(_root, "", 15, Theme.Text, new Vector2(1, 1), new Vector2(-20, -86), new Vector2(260, 20), TextAnchor.MiddleRight, UIKit.BoldFont);
            _state.rectTransform.pivot = new Vector2(1, 1);
            _state.supportRichText = true;

            _nodes = UIKit.LabelAt(_root, "", 15, Color.white, new Vector2(0, 1), new Vector2(20, -110), new Vector2(560, 40), TextAnchor.UpperLeft, UIKit.BoldFont);
            _nodes.rectTransform.pivot = new Vector2(0, 1);
            _nodes.supportRichText = true;

            _squads = UIKit.LabelAt(_root, "", 14, TermDim, new Vector2(0, 0), new Vector2(20, 10), new Vector2(560, 20), TextAnchor.MiddleLeft, UIKit.BoldFont);
            _squads.rectTransform.pivot = new Vector2(0, 0);
            _squads.supportRichText = true;
        }

        public static string KindName(NodeKind k) => k == NodeKind.Destroy ? "DESTROY" : k == NodeKind.Stabilize ? "STABILIZE" : "OVERRIDE";
        public static string KindHint(NodeKind k) => k == NodeKind.Destroy ? "shoot it" : k == NodeKind.Stabilize ? "stand next to it" : "hold it (slips back)";
        public static Color KindColor(NodeKind k) => k == NodeKind.Destroy ? new Color(1f, 0.3f, 0.45f) : k == NodeKind.Stabilize ? new Color(0.25f, 0.9f, 1f) : new Color(0.75f, 0.45f, 1f);

        /// <summary>Stability drains from 100% towards the next instability, refills after it (purely a readout of progress).</summary>
        public static float Stability(float prog, int nodesLeft, float[] marks = null)
        {
            if (nodesLeft > 0) return 0f;
            float prev = 0f;
            foreach (float at in marks ?? GameConfig.HackInstability)
            {
                if (prog < at - 1e-4f) return 1f - (prog - prev) / (at - prev) * 0.85f;
                prev = at;
            }
            return 1f;
        }

        /// <summary>Fake console log that follows the real hack state.</summary>
        private static string Log(Snapshot s, float p, int hackers, bool contested, float[] marks, bool unstable)
        {
            var sb = new System.Text.StringBuilder();
            void L(string t, bool done) => sb.Append($"> {t} {(done ? "<color=#c9ffe6>DONE</color>" : "<color=#c9ffe6>RUNNING</color>")}\n");
            sb.Append("> connect 10.7.0.1 :: <color=#c9ffe6>OK</color>\n");
            L("breach firewall.layer[1] .......", p >= 0.2f);
            if (p >= 0.2f) L("breach firewall.layer[2] .......", p >= marks[0]);
            if (p >= marks[0]) L("inject payload rilo.core .......", p >= (marks.Length > 1 ? marks[1] : 1f));
            if (marks.Length > 1 && p >= marks[1]) L("upload keys -> squad uplink ....", p >= 1f);
            if (unstable) sb.Append("> <color=#ff6b7a>FAULT: core unstable — stabilization required</color>\n");
            else if (contested) sb.Append("> <color=#ffb057>WARN: hostile signal in zone — paused</color>\n");
            else if (hackers > 0) sb.Append($"> {hackers} operator(s) linked · speed x{GameConfig.HackSpeed[Mathf.Min(hackers, GameConfig.HackSpeed.Length - 1)]:0.##}_\n");
            else sb.Append("> waiting for operator in range_\n");
            var lines = sb.ToString().TrimEnd('\n').Split('\n');
            int from = Mathf.Max(0, lines.Length - 4);
            return string.Join("\n", lines, from, lines.Length - from);
        }

        public void Update(Snapshot s, PlayerState me, float dt)
        {
            // show the terminal you are closer to: your HOME terminal or the CENTRAL one
            Vec2 centre = Vec2.Zero;
            bool home = Vec2.Dist(me.Pos, s.HomePos) <= Vec2.Dist(me.Pos, centre);
            float prog = home ? s.HomeProg : s.CenterProg;
            int hackers = home ? s.HomeHackers : s.Hackers;
            bool contested = home ? s.HomeContested : s.HackContested;
            var marks = home ? GameConfig.HackInstability : GameConfig.CenterInstability;
            // only while you are actually hacking: inside a terminal ring, or near one of your squad's fault nodes
            float dHome = Vec2.Dist(me.Pos, s.HomePos), dCentre = me.Pos.Length;
            bool inRing = dHome <= GameConfig.HomeHackRadius + 1f || dCentre <= GameConfig.HackRadius + 1f;
            bool nearNode = false;
            foreach (var n in s.Nodes) if (Vec2.Dist(me.Pos, n.Pos) < 20f) nearNode = true;
            bool show = GameConfig.ExtractionMode && s.Stage == 0 && me.Alive && (inRing || nearNode);
            _root.gameObject.SetActive(show);
            if (!show) return;

            int hackerId = home ? s.HomeHacker : s.CenterHacker;
            bool hacking = hackerId >= 0;
            bool mineHack = hackerId == me.Id;
            _progress.Set(prog, dt);
            _progress.SetColor(contested ? new Color(1f, 0.55f, 0.2f) : hacking ? Term : new Color(0.6f, 0.65f, 0.75f));
            _pct.text = $"{Mathf.FloorToInt(prog * 100)}%";
            _pct.color = contested ? new Color(1f, 0.6f, 0.25f) : Term;
            _log.text = Log(s, prog, hacking ? 1 : 0, contested, new[] { 0.35f, 0.67f }, false);
            _header.text = home ? "RILO//OS  ·  ENEMY HOME TERMINAL  ·  20 s" : "RILO//OS  ·  CENTRAL TERMINAL  ·  12 s + BONUS";
            _stability.Set(prog, dt);
            _stability.SetColor(Theme.Cyan);
            string who = hacking ? (mineHack ? "YOU" : HackerName?.Invoke(hackerId) ?? "squadmate") : "";
            if (contested)
            {
                _title.text = "<color=#ffa040>TERMINAL CONTESTED</color>";
                _state.text = "<color=#ffa040>paused · clear the enemy</color>";
                _back.color = new Color(0.14f, 0.07f, 0.01f, 0.92f);
            }
            else if (hacking)
            {
                _title.text = mineHack ? "HACKING… STAY IN THE RING" : $"{who.ToUpper()} IS HACKING";
                _state.text = mineHack ? "<color=#ffd84a>teammates: defend you</color>" : "<color=#7dff9a>defend the hacker</color>";
                _back.color = new Color(0.01f, 0.05f, 0.035f, 0.9f);
            }
            else
            {
                _title.text = prog > 0.001f ? "HACK PAUSED" : (home ? "ENEMY HOME TERMINAL" : "CENTRAL TERMINAL");
                _state.text = "<color=#aab0d8>press HACK to start / take over</color>";
                _back.color = new Color(0.01f, 0.05f, 0.035f, 0.9f);
            }
            _nodes.text = "<color=#aab0d8>One hacker at a time · leaving, dying or an enemy in the ring pauses it · progress is kept</color>";
            _nodes.color = Color.white;

            // every squad's terminal progress (public)
            var sq = new System.Text.StringBuilder();
            for (int q = 0; q < GameConfig.SquadCount; q++)
            {
                string pr = s.SquadStage[q] > 0 ? "DONE" : $"{Mathf.FloorToInt(s.SquadProg[q] * 100)}%";
                bool mine = q == _localSquad;
                sq.Append(mine ? $"<color=#7dff9a>YOU {pr}</color>" : $"SQUAD {(char)('A' + q)} <color=#ffffff>{pr}</color>");
                if (q < GameConfig.SquadCount - 1) sq.Append("   ·   ");
            }
            _squads.text = sq.ToString();
        }
    }
}
