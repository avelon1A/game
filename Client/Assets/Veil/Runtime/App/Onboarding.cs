using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Veil.Audio;
using Veil.Match;
using Veil.Sim;
using Veil.UI;
using Veil.View;
using EventType = Veil.Sim.EventType;

namespace Veil.App
{
    /// <summary>
    /// Cinematic island tour: letterboxed camera moves over the real island with chapter captions.
    /// Used for the first-launch intro and for HOW TO PLAY (the full tutorial).
    /// </summary>
    public sealed class CinematicTour
    {
        public struct Chapter
        {
            public string Kicker, Title, Body;
            public Vector3 From, To, LookFrom, LookTo;
            public float Seconds;
            public bool Heli;
            public Vector3 HeliAt;
        }

        private Transform _heli, _rotor, _tail;

        private readonly List<Chapter> _chapters;
        private readonly RectTransform _root, _top, _bottom, _card;
        private readonly Text _kicker, _title, _body, _count;
        private readonly CanvasGroup _cardFade;
        private readonly Action _done;
        private readonly Action _primary;
        private readonly string _primaryLabel;
        private int _i = -1;
        private float _t, _bars;
        private bool _finished;

        public CinematicTour(RectTransform canvas, List<Chapter> chapters, Action done, string primaryLabel = null, Action primary = null)
        {
            _chapters = chapters;
            _done = done;
            _primary = primary;
            _primaryLabel = primaryLabel;
            _root = UIKit.Fill(canvas, "CinematicTour");
            _root.SetAsLastSibling();
            var tap = UIKit.Image(_root, UIKit.Square, new Color(0, 0, 0, 0), true);
            var tb = _root.gameObject.AddComponent<Button>(); tb.targetGraphic = tap; tb.transition = Selectable.Transition.None;
            tb.onClick.AddListener(Next);

            _top = UIKit.Rect(_root, "Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, Vector2.zero);
            UIKit.Image(_top, UIKit.Square, Color.black);
            _bottom = UIKit.Rect(_root, "Bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), Vector2.zero, Vector2.zero);
            UIKit.Image(_bottom, UIKit.Square, Color.black);

            _card = UIKit.At(_root, "Card", new Vector2(0, 0), new Vector2(110, 150), new Vector2(980, 260));
            _card.pivot = new Vector2(0, 0);
            var scrim = UIKit.Rect(_root, "Scrim", new Vector2(0, 0), new Vector2(0.75f, 0.55f), new Vector2(0, 0), Vector2.zero, Vector2.zero);
            var si = UIKit.Image(scrim, UIKit.Gradient, new Color(0, 0, 0, 0.55f));
            si.raycastTarget = false;
            scrim.localScale = new Vector3(1, -1, 1);
            scrim.pivot = new Vector2(0, 0.5f);
            scrim.SetSiblingIndex(_card.GetSiblingIndex());
            _cardFade = _card.gameObject.AddComponent<CanvasGroup>();
            _cardFade.blocksRaycasts = false;
            var accent = UIKit.At(_card, "Accent", new Vector2(0, 0), new Vector2(0, 0), new Vector2(6, 200));
            accent.pivot = new Vector2(0, 0);
            UIKit.Image(accent, UIKit.Square, Theme.Yellow);
            _kicker = UIKit.LabelAt(_card, "", 22, Theme.Yellow, new Vector2(0, 0), new Vector2(28, 166), new Vector2(900, 30), TextAnchor.MiddleLeft, UIKit.BoldFont);
            _kicker.rectTransform.pivot = new Vector2(0, 0);
            _title = UIKit.LabelAt(_card, "", 64, Color.white, new Vector2(0, 0), new Vector2(26, 92), new Vector2(940, 76), TextAnchor.MiddleLeft, UIKit.TitleFont);
            _title.rectTransform.pivot = new Vector2(0, 0); _title.fontStyle = FontStyle.Italic;
            UIKit.Shadow(_title, 3);
            _body = UIKit.LabelAt(_card, "", 26, new Color(0.88f, 0.9f, 1f), new Vector2(0, 0), new Vector2(28, 0), new Vector2(920, 90), TextAnchor.UpperLeft, UIKit.BodyFont);
            _body.rectTransform.pivot = new Vector2(0, 0);
            UIKit.Shadow(_body, 2);

            _count = UIKit.LabelAt(_root, "", 18, Theme.TextDim, new Vector2(1, 0), new Vector2(-40, 40), new Vector2(400, 26), TextAnchor.MiddleRight, UIKit.BoldFont);
            _count.rectTransform.pivot = new Vector2(1, 0);
            var skip = UIKit.Button(_root, "SKIP", new Vector2(1, 1), new Vector2(-40, -36), new Vector2(150, 56), UIKit.ButtonStyle.Ghost, Finish, 22);
            ((RectTransform)skip.transform).pivot = new Vector2(1, 1);
            Sfx.Play(Sfx.Start, 0.5f);
            Next();
        }

        private void Next()
        {
            if (_finished) return;
            _i++;
            if (_i >= _chapters.Count) { Finish(); return; }
            _t = 0;
            var c = _chapters[_i];
            _kicker.text = c.Kicker;
            _title.text = c.Title;
            _body.text = c.Body;
            _count.text = $"{_i + 1} / {_chapters.Count}   ·   TAP TO CONTINUE";
            Sfx.Play(Sfx.Whoosh != null ? Sfx.Whoosh : Sfx.Dash, 0.35f, 0.8f);
        }

        private void Finish()
        {
            if (_finished) return;
            _finished = true;
            if (_primary != null)
            {
                ShowEnd();
                return;
            }
            Close();
            _done?.Invoke();
        }

        private void ShowEnd()
        {
            _card.gameObject.SetActive(false);
            _count.text = "";
            var panel = UIKit.At(_root, "End", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720, 300));
            UIKit.Image(panel, UIKit.RoundedSmall, new Color(0.04f, 0.05f, 0.12f, 0.92f), true);
            var t = UIKit.LabelAt(panel, "READY TO DROP IN?", 44, Color.white, new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(680, 60), TextAnchor.MiddleCenter, UIKit.TitleFont);
            t.fontStyle = FontStyle.Italic;
            UIKit.Button(panel, _primaryLabel, new Vector2(0.5f, 0), new Vector2(-170, 70), new Vector2(320, 76), UIKit.ButtonStyle.Primary, () => { Close(); _primary(); }, 24);
            UIKit.Button(panel, "BACK TO LOBBY", new Vector2(0.5f, 0), new Vector2(170, 70), new Vector2(300, 76), UIKit.ButtonStyle.Secondary, () => { Close(); _done?.Invoke(); }, 24);
        }

        private void Close()
        {
            if (_root) UnityEngine.Object.Destroy(_root.gameObject);
            if (_heli) UnityEngine.Object.Destroy(_heli.gameObject);
        }

        /// <summary>Helicopter chapter: lifts off the extraction zone and banks out to sea; the camera chases it.</summary>
        private void HeliShot(Chapter c, float dt, CameraRig cam)
        {
            if (!_heli) _heli = MatchView.HelicopterModel(null, out _rotor, out _tail, out _);
            _heli.gameObject.SetActive(true);
            var flat = new Vector3(c.HeliAt.x, 0, c.HeliAt.z);
            var dir = flat.sqrMagnitude > 1f ? flat.normalized : Vector3.right;
            float k = Mathf.Clamp01(_t / c.Seconds);
            float lift = Mathf.SmoothStep(0, 1, Mathf.Clamp01(k / 0.35f));
            float fly = Mathf.Pow(Mathf.Clamp01((k - 0.25f) / 0.75f), 1.6f);
            var pos = flat + Vector3.up * (0.2f + lift * 9f + fly * 30f) + dir * fly * 140f;
            var side = Vector3.Cross(Vector3.up, dir);
            float bank = Mathf.Sin(fly * Mathf.PI) * 22f;
            _heli.SetPositionAndRotation(pos, Quaternion.LookRotation(dir) * Quaternion.Euler(-8f * fly + 4f * lift, 0, -bank));
            _rotor.localRotation = Quaternion.AngleAxis(Time.time * 1500f, Vector3.up);
            _tail.localRotation = Quaternion.AngleAxis(Time.time * 2400f, Vector3.right);
            // first a low hero angle at the skids, then a wide chase from behind and to the side
            Vector3 camPos = Vector3.Lerp(flat - dir * 9f + side * 7f + Vector3.up * 2.2f, pos - dir * 24f + side * 10f + Vector3.up * 9f, Mathf.SmoothStep(0, 1, Mathf.Clamp01((k - 0.15f) / 0.5f)));
            if (_t < dt * 1.5f) cam.Snap(camPos, pos + Vector3.up * 2f); else cam.Shot(camPos, pos + Vector3.up * 2f, dt, 5f);
        }

        /// <summary>Drives the camera; returns false once the tour has closed.</summary>
        public bool Tick(float dt, CameraRig cam)
        {
            if (!_root) return false;
            _bars = Mathf.MoveTowards(_bars, 1f, dt * 1.6f);
            float h = _root.rect.height * 0.11f * Mathf.SmoothStep(0, 1, _bars);
            _top.sizeDelta = new Vector2(0, h);
            _bottom.sizeDelta = new Vector2(0, h);
            if (_i < 0 || _i >= _chapters.Count) return true;
            var c = _chapters[_i];
            _t += dt;
            if (c.Heli) HeliShot(c, dt, cam);
            else
            {
                if (_heli) _heli.gameObject.SetActive(false);
                float k = Mathf.SmoothStep(0, 1, Mathf.Clamp01(_t / c.Seconds));
                var pos = Vector3.Lerp(c.From, c.To, k);
                var look = Vector3.Lerp(c.LookFrom, c.LookTo, k);
                if (_t < dt * 1.5f) cam.Snap(pos, look); else cam.Shot(pos, look, dt, 6f);
            }
            _cardFade.alpha = Mathf.Clamp01(_t * 2.5f) * (_finished ? 0 : 1);
            _card.anchoredPosition = new Vector2(110 + (1 - Mathf.Clamp01(_t * 3f)) * -60f, 150);
            if (_t >= c.Seconds + 1.5f && !_finished) Next();
            return true;
        }

        // ------------------------------------------------------------------ chapter scripts

        private static Vector3 V(Vec2 p, float y = 0) => new Vector3(p.X, y, p.Y);

        private static Chapter Heli(string kicker, string title, string body, Vec2 at, float seconds)
            => new Chapter { Kicker = kicker, Title = title, Body = body, Heli = true, HeliAt = V(at) + (at.LengthSq > 1f ? V(at).normalized : Vector3.right) * 30f, Seconds = seconds };

        private static Vec2 ZoneAt(MapData map, ZoneType t, Vec2 fallback)
        {
            foreach (var z in map.Zones) if (z.Type == t) return z.Center;
            return fallback;
        }

        private static Chapter Orbit(string kicker, string title, string body, Vec2 at, float radius, float height, float a0, float a1, float seconds, float lookY = 2f)
        {
            Vector3 P(float a) => V(at) + new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * radius, height, Mathf.Cos(a * Mathf.Deg2Rad) * radius);
            return new Chapter { Kicker = kicker, Title = title, Body = body, From = P(a0), To = P(a1), LookFrom = V(at, lookY), LookTo = V(at, lookY), Seconds = seconds };
        }

        public static List<Chapter> Intro(MapData map)
        {
            return new List<Chapter>
            {
                Orbit("WELCOME TO", "RILO ISLAND", "Four squads drop in. Only one gets off the island.", new Vec2(0, 0), 190, 90, -20, 25, 6f, 0),
                Orbit("FIRST", "LEARN THE BASICS", "A quick training drop: move, look, shoot, jump and dash.", new Vec2(0, 0), 60, 20, 90, 140, 4f, 4f),
            };
        }

        /// <summary>After the basics: how a squad wins (objectives → Vault → helicopter take-off), then the lobby.</summary>
        public static List<Chapter> Outro(MapData map)
        {
            var vault = ZoneAt(map, ZoneType.Vault, new Vec2(0, 0));
            var tower = ZoneAt(map, ZoneType.Tower, new Vec2(60, 40));
            var reactor = ZoneAt(map, ZoneType.Reactor, new Vec2(-40, -50));
            return new List<Chapter>
            {
                Orbit("HOW TO WIN · 1", "COMPLETE OBJECTIVES", "Follow the route line to your squad's terminals and HACK them.", reactor, 26, 12, 10, 70, 5f),
                Orbit("HOW TO WIN · 2", "OPEN THE VAULT", "Finished objectives unlock your squad's Vault.", vault, 28, 13, 120, 180, 5f),
                Heli("HOW TO WIN · 3", "ESCAPE BY HELICOPTER", "An open Vault reveals the chopper. Hold the zone, board, and the first squad out wins.", tower, 9f),
            };
        }

        public static List<Chapter> Full(MapData map)
        {
            var vault = ZoneAt(map, ZoneType.Vault, new Vec2(0, 0));
            var tower = ZoneAt(map, ZoneType.Tower, new Vec2(60, 40));
            var market = ZoneAt(map, ZoneType.Market, new Vec2(-50, 30));
            var ruins = ZoneAt(map, ZoneType.Ruins, new Vec2(40, -60));
            var reactor = ZoneAt(map, ZoneType.Reactor, new Vec2(-40, -50));
            return new List<Chapter>
            {
                Orbit("CHAPTER 1", "THE ISLAND", "4 squads of 4 drop into eight regions. There is no timer: the match ends when a squad escapes.", new Vec2(0, 0), 200, 95, -30, 30, 7f, 0),
                Orbit("CHAPTER 2", "SQUAD OBJECTIVES", "Follow the route line to your squad's terminals. Stand in the zone and HACK to complete each step.", reactor, 26, 12, 10, 80, 7f),
                Orbit("CHAPTER 3", "THE VAULT", "Finish your objectives and the Vault opens for your squad only. It is required to escape.", vault, 28, 13, 120, 190, 7f),
                Heli("CHAPTER 4", "HELICOPTER ESCAPE", "With the Vault open you can see the extraction. Hold the zone 25 seconds, board, and the first squad out wins.", tower, 9f),
                Orbit("CHAPTER 5", "COMBAT", "Aim with the centre crosshair, slide on FIRE to track targets. Headshots deal 1.8x damage. Moving closes the sniper scope.", market, 18, 6, 300, 350, 8f, 1.6f),
                Orbit("CHAPTER 6", "ABILITIES", "DASH dodges fire, PULSE reveals enemies, DECOY fools them. Jump pads launch you across the island.", ruins, 22, 9, 40, 100, 7f),
                Orbit("CHAPTER 7", "TAGS & REDEPLOY", "When a squadmate falls they drop a tag. Stand on it to bring them back into the fight.", new Vec2(0, 0), 40, 14, 150, 210, 7f, 1f),
            };
        }
    }

    /// <summary>
    /// Guided first match: one step at a time (move, look, shoot, jump, dash, loot, objective, vault, escape),
    /// each with a cinematic card, the matching button highlighted, and a check when done.
    /// </summary>
    public sealed class TutorialDirector
    {
        private enum Step { Move, Look, Shoot, Jump, Dash, Done }

        private readonly ClientMatch _m;
        private readonly LocalMatchDriver _driver;
        private readonly TouchControls _touch;
        private readonly RectTransform _root, _card, _top, _bottom;
        private readonly Text _stepLabel, _title, _body;
        private readonly Image _check;
        private Step _step = (Step)(-1);
        private float _t, _doneT = -1f, _bars;
        private float _moved, _turned, _lastYaw;
        private Vector3 _lastPos;
        private int _fires, _jumps, _lastJumpSeq = -1;
        private bool _dashed, _looted, _hacked, _vault;
        private static readonly bool Mobile = Platform.IsMobile;

        public bool Finished => _step == Step.Done;
        public Action Completed;

        public TutorialDirector(RectTransform canvas, ClientMatch m, LocalMatchDriver driver, TouchControls touch)
        {
            _m = m; _driver = driver; _touch = touch;
            _root = UIKit.Fill(canvas, "Tutorial");
            _root.SetAsLastSibling();
            _top = UIKit.Rect(_root, "Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, Vector2.zero);
            UIKit.Image(_top, UIKit.Square, new Color(0, 0, 0, 0.85f));
            _bottom = UIKit.Rect(_root, "Bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), Vector2.zero, Vector2.zero);
            UIKit.Image(_bottom, UIKit.Square, new Color(0, 0, 0, 0.85f));
            foreach (var g in _root.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;

            _card = UIKit.At(_root, "Card", new Vector2(0.5f, 1), new Vector2(0, -300), new Vector2(760, 132));
            _card.pivot = new Vector2(0.5f, 1);
            var bg = UIKit.Image(_card, UIKit.RoundedSmall, new Color(0.03f, 0.04f, 0.1f, 0.82f)); bg.raycastTarget = false;
            var accent = UIKit.At(_card, "Accent", new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(6, 104));
            accent.pivot = new Vector2(0, 0.5f);
            UIKit.Image(accent, UIKit.Square, Theme.Yellow).raycastTarget = false;
            _stepLabel = UIKit.LabelAt(_card, "", 16, Theme.Yellow, new Vector2(0, 1), new Vector2(26, -12), new Vector2(500, 22), TextAnchor.MiddleLeft, UIKit.BoldFont);
            _stepLabel.rectTransform.pivot = new Vector2(0, 1);
            _title = UIKit.LabelAt(_card, "", 34, Color.white, new Vector2(0, 1), new Vector2(26, -34), new Vector2(640, 42), TextAnchor.MiddleLeft, UIKit.TitleFont);
            _title.rectTransform.pivot = new Vector2(0, 1); _title.fontStyle = FontStyle.Italic;
            _body = UIKit.LabelAt(_card, "", 20, new Color(0.85f, 0.88f, 1f), new Vector2(0, 1), new Vector2(26, -78), new Vector2(700, 48), TextAnchor.UpperLeft, UIKit.BodyFont);
            _body.rectTransform.pivot = new Vector2(0, 1);
            var ck = UIKit.At(_card, "Check", new Vector2(1, 0.5f), new Vector2(-24, -14), new Vector2(56, 56));
            ck.pivot = new Vector2(1, 0.5f);
            _check = UIKit.Image(ck, Icons.Check, Theme.Green);
            _check.raycastTarget = false;
            _check.color = new Color(0, 0, 0, 0);

            var skip = UIKit.Button(_card, "SKIP", new Vector2(1, 1), new Vector2(-10, -10), new Vector2(96, 40), UIKit.ButtonStyle.Ghost, Skip, 16);
            ((RectTransform)skip.transform).pivot = new Vector2(1, 1);

            _driver.Sim.Settings.BotsHoldFire = true;
            _lastPos = m.PredictedPos;
            Go(Step.Move);
        }

        private void Skip()
        {
            _driver.Sim.Settings.BotsHoldFire = false;
            _touch?.Highlight(null);
            _step = Step.Done;
            Onboarding.MarkDone();
            Completed?.Invoke();
            if (_root) UnityEngine.Object.Destroy(_root.gameObject);
        }

        private void Go(Step s)
        {
            _step = s;
            _t = 0; _doneT = -1f; _bars = 0;
            _check.color = new Color(0, 0, 0, 0);
            string key = null;
            int n = (int)Step.Done;
            _stepLabel.text = s == Step.Done ? "TRAINING COMPLETE" : $"TRAINING  ·  STEP {(int)s + 1} / {n}";
            switch (s)
            {
                case Step.Move: Set("MOVE", Mobile ? "Drag the left side of the screen to walk." : "Use W A S D to walk."); break;
                case Step.Look: Set("LOOK AROUND", Mobile ? "Drag on the right side of the screen to turn the camera." : "Move the mouse to look around."); break;
                case Step.Shoot: Set("SHOOT", Mobile ? "Hold FIRE. Slide your finger on it to move the crosshair while shooting." : "Click to shoot. Aim for the head: 1.8x damage."); key = "fire"; break;
                case Step.Jump: Set("JUMP", Mobile ? "Tap JUMP. You also climb over low walls by running into them." : "Press SPACE to jump."); key = "jump"; break;
                case Step.Dash: Set("DASH", Mobile ? "Tap DASH to dodge quickly. It uses energy." : "Press SHIFT to dash."); key = "dash"; break;
                case Step.Done: Set("BASICS DONE", "Now see how a squad wins the match."); break;
            }
            _touch?.Highlight(key);
            Sfx.Play(Sfx.Objective, 0.45f, 1.1f);
        }

        private int Stage()
        {
            var st = _m.Latest?.SquadStage;
            int q = _m.LocalSquad;
            return st != null && q >= 0 && q < st.Length ? st[q] : 0;
        }

        private void Set(string t, string b) { _title.text = t; _body.text = b; }

        public void Update(float dt, float cameraYaw)
        {
            if (!_root) return;
            _t += dt;
            _bars = Mathf.MoveTowards(_bars, _step == Step.Done || _t < 1.4f ? 1f : 0f, dt * 2f);
            float h = _root.rect.height * 0.07f * Mathf.SmoothStep(0, 1, _bars);
            _top.sizeDelta = new Vector2(0, h); _bottom.sizeDelta = new Vector2(0, h);
            float enter = Mathf.SmoothStep(0, 1, Mathf.Clamp01(_t * 3f));
            _card.anchoredPosition = new Vector2(0, -300 + (1 - enter) * 40f);
            _card.localScale = Vector3.one * (0.94f + 0.06f * enter);

            var me = _m.Predicted;
            var pos = _m.PredictedPos;
            if (me.Alive) _moved += new Vector2(pos.x - _lastPos.x, pos.z - _lastPos.z).magnitude;
            _lastPos = pos;
            if (_lastJumpSeq >= 0 && me.JumpSeq != _lastJumpSeq && _step == Step.Jump) _jumps++;
            _lastJumpSeq = me.JumpSeq;
            _turned += Mathf.Abs(Mathf.DeltaAngle(_lastYaw, cameraYaw));
            _lastYaw = cameraYaw;

            if (_doneT >= 0)
            {
                _doneT += dt;
                float k = Mathf.Clamp01(_doneT * 4f);
                _check.color = new Color(Theme.Green.r, Theme.Green.g, Theme.Green.b, k);
                _check.rectTransform.localScale = Vector3.one * (1.6f - 0.6f * k);
                if (_doneT > 1.3f)
                {
                    if (_step == Step.Done) { Onboarding.MarkDone(); UnityEngine.Object.Destroy(_root.gameObject); Completed?.Invoke(); return; }
                    Go(_step + 1);
                }
                return;
            }

            bool ok = false;
            switch (_step)
            {
                case Step.Move: ok = _moved > 8f; break;
                case Step.Look: ok = _turned > 120f && _t > 1f; break;
                case Step.Shoot: ok = _fires >= 4; break;
                case Step.Jump: ok = _jumps >= 1; break;
                case Step.Dash: ok = _dashed; break;
                case Step.Done: ok = _t > 2f; break;
            }
            if (ok)
            {
                _doneT = 0;
                Sfx.Play(Sfx.Capture, 0.6f, 1.2f);
                if (_step == Step.Move) { _turned = 0; _lastYaw = cameraYaw; }
            }
        }

        public void OnEvent(SimEvent e)
        {
            int me = _m.LocalId;
            switch (e.Type)
            {
                case EventType.Fire: if (e.A == me && _step == Step.Shoot) _fires++; break;
                case EventType.DashStart: if (e.A == me) _dashed = true; break;
                case EventType.PickupCollected: if (e.A == me && MatchSim.IsLoot((PickupType)e.Value)) _looted = true; break;
                case EventType.HackActivity: case EventType.StageComplete: case EventType.ZoneCaptured: case EventType.ObjectiveComplete:
                    break;
                case EventType.VaultOpened: if (_m.IsAlly(e.A) || e.A == me) _vault = true; break;
            }
        }

        public void Dispose()
        {
            _touch?.Highlight(null);
            if (_root) UnityEngine.Object.Destroy(_root.gameObject);
        }
    }

    public static class Onboarding
    {
        private const string Key = "onboarded.v1";
        public static bool Done => PlayerPrefs.GetInt(Key, 0) == 1;
        public static void MarkDone() { PlayerPrefs.SetInt(Key, 1); PlayerPrefs.Save(); }
    }
}
