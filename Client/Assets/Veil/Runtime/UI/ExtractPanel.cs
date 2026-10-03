using UnityEngine;
using UnityEngine.UI;
using Veil.Sim;
using Veil.View;
using Veil.Audio;

namespace Veil.UI
{
    /// <summary>
    /// Extraction status strip (top centre) once the point is revealed: one clear state line + every squad's progress.
    /// LOCKED 14s → OPEN → SQUAD B SECURING → SQUAD B EXTRACTING 62% → CONTESTED → FINAL PHASE.
    /// </summary>
    public sealed class ExtractPanel
    {
        private readonly RectTransform _root;
        private readonly Image _back;
        private readonly Text _state, _hint;
        private readonly Image[] _fill = new Image[GameConfig.SquadCount];
        private readonly Text[] _label = new Text[GameConfig.SquadCount];
        private readonly int _local;
        private const float BarW = 108f;

        public ExtractPanel(Transform parent, int localSquad, bool mobile)
        {
            _local = localSquad;
            _root = UIKit.At(parent, "ExtractPanel", new Vector2(0.5f, 1), new Vector2(0, mobile ? -178 : -68), new Vector2(560, 118));
            _root.pivot = new Vector2(0.5f, 1);
            _back = UIKit.Image(_root, UIKit.Rounded, new Color(0.04f, 0.05f, 0.12f, 0.72f));
            _state = UIKit.Fit(UIKit.LabelAt(_root, "", 24, Color.white, new Vector2(0.5f, 1), new Vector2(0, -19), new Vector2(530, 30), TextAnchor.MiddleCenter, UIKit.TitleFont), 14);
            _state.supportRichText = true;
            _hint = UIKit.Fit(UIKit.LabelAt(_root, "", 14, Theme.TextDim, new Vector2(0.5f, 1), new Vector2(0, -47), new Vector2(530, 18), TextAnchor.MiddleCenter, UIKit.BoldFont), 10);
            for (int i = 0; i < GameConfig.SquadCount; i++)
            {
                float x = (i - (GameConfig.SquadCount - 1) * 0.5f) * (BarW + 22);
                var back = UIKit.At(_root, "Bar", new Vector2(0.5f, 0), new Vector2(x, 14), new Vector2(BarW, 10));
                UIKit.Image(back, UIKit.Pill, new Color(1, 1, 1, 0.12f));
                var f = UIKit.At(back, "Fill", new Vector2(0, 0.5f), Vector2.zero, new Vector2(0, 10));
                f.pivot = new Vector2(0, 0.5f);
                _fill[i] = UIKit.Image(f, UIKit.Pill, Color.white);
                _label[i] = UIKit.LabelAt(_root, "", 13, Theme.Text, new Vector2(0.5f, 0), new Vector2(x, 32), new Vector2(BarW + 20, 16), TextAnchor.MiddleCenter, UIKit.BoldFont);
                _label[i].supportRichText = true;
            }
            _root.gameObject.SetActive(false);
        }

        private static string Sq(int q) => "SQUAD " + (char)('A' + q);

        public bool Visible => _root.gameObject.activeSelf;
        public float Bottom => -_root.anchoredPosition.y + _root.sizeDelta.y;

        public void Update(Snapshot s, PlayerState me)
        {
            bool on = GameConfig.ExtractionMode && s.ExtractRevealed;
            _root.gameObject.SetActive(on);
            if (!on) return;
            bool mineDone = s.Stage >= 4;
            int c = s.ExtractController;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 8f);
            Color back = new Color(0.04f, 0.05f, 0.12f, 0.72f);
            float de = Vec2.Dist(me.Pos, s.ExtractPos);
            if (s.ExtractLockT > 0)
            {
                _state.text = $"EXTRACTION OPENS IN <color=#ffd84a>{s.ExtractLockT:0}s</color>";
                _hint.text = mineDone ? $"{de:0} m away · get there first and hold it" : $"{de:0} m away · rotate to defend, ambush or block — or finish the Vault";
            }
            else if (s.ExtractContested)
            {
                _state.text = "<color=#ff5a6a>CONTESTED</color> · progress paused";
                _hint.text = "clear every enemy out of the circle";
                back = Color.Lerp(back, new Color(0.45f, 0.05f, 0.08f, 0.8f), pulse * 0.6f);
            }
            else if (c < 0)
            {
                _state.text = "EXTRACTION OPEN · nobody holding";
                _hint.text = mineDone ? $"{de:0} m · stand in the circle — alone — to extract" : $"{de:0} m · open the Vault first; you can still block others";
            }
            else
            {
                bool mine = c == _local;
                bool canWin = s.SquadStage[c] >= 4;
                string who = mine ? "<color=#7dff9a>YOUR SQUAD</color>" : $"<color=#ff5a6a>{Sq(c)}</color>";
                int pct = Mathf.RoundToInt(s.SquadExtract[c] * 100);
                if (!canWin) { _state.text = $"{who} IS BLOCKING"; _hint.text = "they haven't opened the Vault — they can't extract"; }
                else if (s.ExtractSecure < 1f) { _state.text = $"{who} SECURING… {Mathf.RoundToInt(s.ExtractSecure * 100)}%"; _hint.text = mine ? "stay alone in the circle" : "get in the circle to stop them"; }
                else if (s.ExtractFinal)
                {
                    float left = (1f - s.SquadExtract[c]) * GameConfig.ExtractTime;
                    _state.text = $"FINAL PHASE · {who} WINS IN <color=#ffd84a>{left:0}s</color>";
                    _hint.text = mine ? "HOLD ON — enemies can see you" : "CONTEST NOW — they are visible to you";
                    back = Color.Lerp(back, new Color(0.55f, 0.04f, 0.06f, 0.88f), pulse);
                }
                else { _state.text = $"{who} EXTRACTING · {pct}%"; _hint.text = mine ? "defend the circle — you're pinged every few seconds" : "they're pinged on your map — go contest"; }
            }
            _back.color = back;
            for (int i = 0; i < GameConfig.SquadCount; i++)
            {
                float p = s.SquadExtract[i];
                bool done = s.SquadStage[i] >= 4;
                _fill[i].rectTransform.sizeDelta = new Vector2(BarW * p, 10);
                _fill[i].color = i == _local ? Palette.Health : i == c ? new Color(1f, 0.35f, 0.4f) : new Color(0.85f, 0.85f, 0.95f);
                string tag = i == _local ? "YOU" : ((char)('A' + i)).ToString();
                _label[i].text = done ? $"{tag} {Mathf.RoundToInt(p * 100)}%" : $"<color=#8a90b8>{tag} · no vault</color>";
            }
        }
    }
}
