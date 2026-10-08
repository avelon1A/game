using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Veil.Match;
using Veil.Sim;
using Veil.View;
using Veil.Audio;
using EventType = Veil.Sim.EventType;

namespace Veil.UI
{
    /// <summary>
    /// Combat feel and squad awareness on top of the HUD: damage numbers, damage-direction arrows, hit ticks,
    /// squad pings, tag redeploy progress, world events (supply drop, hack surge, bounty), sabotage,
    /// "your terminal is being hacked" alerts, the picked-up weapon / grenade chip, the extraction finale countdown
    /// and the squad-wipe slow-motion.
    /// </summary>
    public sealed class HudExtras
    {
        private readonly RectTransform _root;
        private readonly ClientMatch _m;
        private readonly Camera _cam;
        private readonly Minimap _minimap;
        private readonly Action<string, Color> _feed;
        private readonly Action<string, string, float> _banner;
        private readonly Action<string> _popup;

        private sealed class Floater { public Text Text; public Vector3 World; public float T, Life; }
        private sealed class Arrow { public Image Img; public Vec2 From; public float T; }
        private sealed class Marker { public Text Text; public Vec2 Pos; public float T; public string Label; public Color Color; public Sprite Icon; public int Follow = -1; }

        private readonly List<Floater> _numbers = new List<Floater>();
        private readonly List<Arrow> _arrows = new List<Arrow>();
        private readonly List<Marker> _markers = new List<Marker>();
        private readonly Dictionary<int, Text> _tagLabels = new Dictionary<int, Text>();
        private readonly Text _status, _weapon, _tag, _finale;
        private float _slowT, _finaleBeepT;
        private int _lastFinaleSec = -1;

        public HudExtras(RectTransform root, ClientMatch m, Camera cam, Minimap minimap, bool mobile,
                         Action<string, Color> feed, Action<string, string, float> banner, Action<string> popup)
        {
            _root = root; _m = m; _cam = cam; _minimap = minimap; _feed = feed; _banner = banner; _popup = popup;
            _status = UIKit.LabelAt(root, "", 20, Theme.Text, new Vector2(0.5f, 1), new Vector2(0, mobile ? -150 : -46), new Vector2(900, 28), TextAnchor.MiddleCenter, UIKit.BoldFont);
            _status.supportRichText = true;
            UIKit.Outline(_status, new Color(0, 0, 0, 0.8f), 2);
            _weapon = UIKit.LabelAt(root, "", 22, Theme.Gold, new Vector2(0.5f, 0), new Vector2(0, mobile ? 120 : 110), new Vector2(520, 30), TextAnchor.MiddleCenter, UIKit.TitleFont);
            _weapon.supportRichText = true;
            UIKit.Outline(_weapon, new Color(0, 0, 0, 0.8f), 2);
            _tag = UIKit.LabelAt(root, "", 26, Theme.Green, new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(800, 36), TextAnchor.MiddleCenter, UIKit.TitleFont);
            UIKit.Outline(_tag, new Color(0, 0, 0, 0.85f), 2);
            _finale = UIKit.LabelAt(root, "", 84, Theme.Red, new Vector2(0.5f, 0.5f), new Vector2(0, 150), new Vector2(400, 100), TextAnchor.MiddleCenter, UIKit.TitleFont);
            UIKit.Outline(_finale, new Color(0, 0, 0, 0.9f), 4);
        }

        private string Sq(int q) => q == _m.LocalSquad ? "YOUR SQUAD" : "SQUAD " + (char)('A' + q);

        // ------------------------------------------------------------------ events

        public void HandleEvent(SimEvent e)
        {
            switch (e.Type)
            {
                case EventType.Hit:
                    if (e.A == _m.LocalId && e.B >= 0 && e.B != _m.LocalId)
                    {
                        Number(e.Pos, e.Value, e.Value >= 30 ? Theme.Gold : Color.white);
                        Sfx.Play(Sfx.HitTick, 0.55f, 1f + Mathf.Clamp01(e.Value / 60f) * 0.3f);
                    }
                    if (e.B == _m.LocalId && e.A >= 0 && e.A != _m.LocalId) DamageFrom(e.A);
                    break;
                case EventType.Explosion:
                    Sfx.PlayAt(Sfx.Boom, new Vector3(e.Pos.X, 0.5f, e.Pos.Y), 1f);
                    break;
                case EventType.Ping:
                {
                    var kind = (PingKind)e.B;
                    string who = e.A == _m.LocalId ? "You" : _m.NameOf(e.A);
                    string label = kind == PingKind.Enemy ? "ENEMY" : kind == PingKind.Help ? "NEED HELP" : kind == PingKind.Loot ? LootName((PickupType)e.Value) : "GO HERE";
                    Color c = kind == PingKind.Enemy ? Theme.Red : kind == PingKind.Help ? Theme.Green : kind == PingKind.Loot ? Theme.Gold : Theme.Cyan;
                    // one ping per player: a new one replaces the old
                    _markers.RemoveAll(mk => { bool same = mk.Label.EndsWith("·" + e.A); if (same) UnityEngine.Object.Destroy(mk.Text.gameObject); return same; });
                    AddMarker(e.Pos, label + "·" + e.A, c, kind == PingKind.Enemy ? 6f : 9f, kind == PingKind.Enemy ? e.Value : kind == PingKind.Help ? e.A : -1);
                    _feed($"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{who}: {label}</color>", Theme.Text);
                    Sfx.Play(Sfx.PingSnd, 0.6f, kind == PingKind.Enemy ? 1.15f : 1f);
                    break;
                }
                case EventType.Redeployed:
                    if (e.B == _m.LocalId) _banner("BACK IN THE FIGHT", $"{_m.NameOf(e.A)} redeployed you", 2.5f);
                    else if (e.A == _m.LocalId) _popup($"REDEPLOYED {_m.NameOf(e.B).ToUpper()}  +{GameConfig.RevivePoints}");
                    else _feed($"{_m.NameOf(e.A)} redeployed {_m.NameOf(e.B)}", Theme.TextDim);
                    Sfx.Play(Sfx.Capture, 0.7f);
                    break;
                case EventType.Eliminated:
                    if (_m.IsAlly(e.B) && e.B != _m.LocalId) _feed($"<color=#7dff9a>{_m.NameOf(e.B)}'s tag dropped</color> · stand on it", Theme.Text);
                    break;
                case EventType.SquadWiped:
                    if (e.B == _m.LocalSquad) _banner("SQUAD WIPED", "Redeploying at your spawn", 3f);
                    else
                    {
                        bool ours = e.A >= 0 && _m.IsAlly(e.A);
                        if (ours) { _banner("SQUAD WIPED", $"{Sq(e.B)} is out — push their objective!", 3f); SlowMo(); }
                        else _feed($"<color=#ffb057>{Sq(e.B)} was wiped</color>", Theme.Text);
                    }
                    break;
                case EventType.WorldEvent:
                    WorldEvent(e);
                    break;
                case EventType.Sabotage:
                    if (e.B == _m.LocalSquad) { _banner("SABOTAGED!", $"{Sq(e.A)} jammed your progress for {e.Value}s — and revealed you", 4f); Sfx.Play(Sfx.Alarm, 0.7f); }
                    else if (e.A == _m.LocalSquad) { _banner("COMEBACK: SABOTAGE", $"{Sq(e.B)} is jammed for {e.Value}s and revealed — catch up!", 4f); Sfx.Play(Sfx.Objective, 0.8f); }
                    else _feed($"<color=#ffb057>{Sq(e.A)} sabotaged {Sq(e.B)}</color>", Theme.Text);
                    break;
                case EventType.BeingHacked:
                    _banner("YOUR TERMINAL IS UNDER ATTACK", $"{Sq(e.A)} is hacking it · {e.Value}% — go defend!", 3.5f);
                    _markers.RemoveAll(mk => { bool same = mk.Label == "DEFEND"; if (same) UnityEngine.Object.Destroy(mk.Text.gameObject); return same; });
                    AddMarker(e.Pos, "DEFEND", Theme.Red, 10f);
                    Sfx.Play(Sfx.Alarm, 0.6f);
                    break;
                case EventType.AbilityPlay:
                    if (e.A == _m.LocalId && e.B == 3) _popup("SQUAD OVERCHARGE · allies shielded");
                    else if (e.A == _m.LocalId && e.B == 4) _popup("SQUAD RUSH · speed burst");
                    break;
            }
        }

        private void WorldEvent(SimEvent e)
        {
            switch ((WorldEventKind)e.B)
            {
                case WorldEventKind.SupplyDrop:
                    _banner("SUPPLY DROP", "Shotgun, SMG and grenades — first come, first served", 4f);
                    AddMarker(e.Pos, "SUPPLY DROP", Theme.Gold, 45f);
                    Sfx.Play(Sfx.Capture, 0.9f);
                    break;
                case WorldEventKind.HackSurge:
                    _banner("HACK SURGE", $"Every objective fills twice as fast for {e.Value}s", 4f);
                    Sfx.Play(Sfx.Reveal, 0.9f);
                    break;
                case WorldEventKind.SurgeOver:
                    _feed("Hack surge over", Theme.TextDim);
                    break;
                case WorldEventKind.Bounty:
                    if (e.A == _m.LocalId) _banner("YOU ARE THE BOUNTY", $"Everyone can see you for {e.Value}s — survive!", 4f);
                    else _banner("BOUNTY", $"{_m.NameOf(e.A)} is marked · eliminate them for +{GameConfig.BountyPoints}", 4f);
                    Sfx.Play(Sfx.Reveal, 0.9f);
                    break;
                case WorldEventKind.BountyClaimed:
                    _feed($"<color=#ffd84a>{(e.A == _m.LocalId ? "You" : _m.NameOf(e.A))} claimed the bounty +{e.Value}</color>", Theme.Text);
                    if (e.A == _m.LocalId) _popup($"BOUNTY CLAIMED  +{e.Value}");
                    break;
            }
        }

        private static string LootName(PickupType t) => t == PickupType.Shotgun ? "SHOTGUN" : t == PickupType.Smg ? "SMG" : t == PickupType.Grenades ? "GRENADES" : "LOOT";

        private void Number(Vec2 at, int value, Color c)
        {
            var t = UIKit.LabelAt(_root, value.ToString(), 26, c, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120, 34), TextAnchor.MiddleCenter, UIKit.TitleFont);
            UIKit.Outline(t, new Color(0, 0, 0, 0.85f), 2);
            _numbers.Add(new Floater { Text = t, World = new Vector3(at.X + UnityEngine.Random.Range(-0.3f, 0.3f), 2.1f, at.Y), Life = 0.85f });
            if (_numbers.Count > 24) { UnityEngine.Object.Destroy(_numbers[0].Text.gameObject); _numbers.RemoveAt(0); }
        }

        private void DamageFrom(int attacker)
        {
            var s = _m.Latest;
            if (s == null) return;
            foreach (var a in s.Avatars)
            {
                if (a.OwnerId != attacker || a.AvatarId >= 1000) continue;
                var rt = UIKit.At(_root, "DmgDir", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60, 26));
                var img = UIKit.Image(rt, UIKit.Pill, new Color(1f, 0.25f, 0.3f, 0.9f));
                _arrows.Add(new Arrow { Img = img, From = a.Pos, T = 1.2f });
                return;
            }
        }

        private void AddMarker(Vec2 pos, string label, Color c, float life, int follow = -1)
        {
            var t = UIKit.LabelAt(_root, "", 18, Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240, 48), TextAnchor.MiddleCenter, UIKit.BoldFont);
            t.supportRichText = true;
            UIKit.Outline(t, new Color(0, 0, 0, 0.85f), 2);
            _markers.Add(new Marker { Text = t, Pos = pos, T = life, Label = label, Color = c, Follow = follow });
        }

        private void SlowMo()
        {
            // offline only: online the server keeps its own clock
            if (_m.Driver is LocalMatchDriver) { Time.timeScale = 0.3f; _slowT = 0.55f; }
        }

        // ------------------------------------------------------------------ update

        public void Update(float dt, float cameraYaw)
        {
            if (_slowT > 0) { _slowT -= Time.unscaledDeltaTime; if (_slowT <= 0) Time.timeScale = 1f; }
            var s = _m.Latest;
            if (s == null) return;
            var me = _m.Predicted;
            var self = s.Self;
            _minimap.Blips.Clear();

            // damage numbers float up and fade
            for (int i = _numbers.Count - 1; i >= 0; i--)
            {
                var n = _numbers[i];
                n.T += dt;
                if (n.T >= n.Life) { UnityEngine.Object.Destroy(n.Text.gameObject); _numbers.RemoveAt(i); continue; }
                Vector3 sp = _cam.WorldToScreenPoint(n.World + Vector3.up * n.T * 1.4f);
                n.Text.gameObject.SetActive(sp.z > 0);
                if (sp.z > 0) n.Text.rectTransform.anchoredPosition = Local(sp);
                var c = n.Text.color; c.a = 1f - Mathf.Clamp01((n.T - n.Life * 0.5f) / (n.Life * 0.5f));
                n.Text.color = c;
                n.Text.rectTransform.localScale = Vector3.one * (1f + Mathf.Max(0, 0.15f - n.T) * 3f);
            }

            // damage direction: a red pill on a ring around the crosshair, pointing at the shooter
            for (int i = _arrows.Count - 1; i >= 0; i--)
            {
                var a = _arrows[i];
                a.T -= dt;
                if (a.T <= 0) { UnityEngine.Object.Destroy(a.Img.gameObject); _arrows.RemoveAt(i); continue; }
                float yaw = (a.From - me.Pos).Yaw - cameraYaw;
                var dir = new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad));
                a.Img.rectTransform.anchoredPosition = dir * 150f;
                a.Img.rectTransform.localRotation = Quaternion.Euler(0, 0, -yaw + 90f);
                a.Img.color = new Color(1f, 0.25f, 0.3f, Mathf.Clamp01(a.T) * 0.9f);
            }

            // pings / event markers (world label clamped to the screen edge + minimap blip)
            for (int i = _markers.Count - 1; i >= 0; i--)
            {
                var mk = _markers[i];
                mk.T -= dt;
                if (mk.T <= 0) { UnityEngine.Object.Destroy(mk.Text.gameObject); _markers.RemoveAt(i); continue; }
                if (mk.Follow >= 0)
                    foreach (var av in s.Avatars) if (av.AvatarId == mk.Follow) { mk.Pos = av.Pos; break; }
                string label = mk.Label.Split('·')[0];
                bool blink = mk.T > 0.6f || Mathf.Repeat(Time.time * 6f, 1f) > 0.5f;
                Place(mk.Text, mk.Pos, 2.6f, $"<color=#{ColorUtility.ToHtmlStringRGB(mk.Color)}>◆ {label}</color>\n<size=14>{Vec2.Dist(me.Pos, mk.Pos):0} m</size>", blink);
                _minimap.Blips.Add((mk.Pos, Icons.Target, mk.Color, 22f));
            }

            // squadmates' tags in the world: go stand on them
            var seen = new HashSet<int>();
            foreach (var pk in _m.Pickups.Values)
            {
                if (pk.Type != PickupType.Tag || !_m.IsAlly(pk.Spot) || pk.Spot == _m.LocalId) continue;
                seen.Add(pk.Id);
                if (!_tagLabels.TryGetValue(pk.Id, out var t))
                {
                    t = UIKit.LabelAt(_root, "", 17, Theme.Green, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240, 46), TextAnchor.MiddleCenter, UIKit.BoldFont);
                    t.supportRichText = true;
                    UIKit.Outline(t, new Color(0, 0, 0, 0.85f), 2);
                    _tagLabels[pk.Id] = t;
                }
                Place(t, pk.Pos, 1.6f, $"✚ {_m.NameOf(pk.Spot).ToUpper()}'S TAG\n<size=14>{Vec2.Dist(me.Pos, pk.Pos):0} m</size>", true);
            }
            foreach (var id in new List<int>(_tagLabels.Keys))
                if (!seen.Contains(id)) { UnityEngine.Object.Destroy(_tagLabels[id].gameObject); _tagLabels.Remove(id); }

            // tag redeploy progress (helper and the fallen player)
            if (self.TagProg > 0 && self.Reviving >= 0) _tag.text = $"REDEPLOYING {_m.NameOf(self.Reviving).ToUpper()} · {Mathf.RoundToInt(self.TagProg * 100)}%";
            else if (!self.Alive && self.ReviveProg > 0) _tag.text = $"A SQUADMATE IS BRINGING YOU BACK · {Mathf.RoundToInt(self.ReviveProg * 100)}%";
            else if (!self.Alive) _tag.text = "Squadmates can redeploy you from your tag";
            else _tag.text = "";

            // status pills
            var parts = new List<string>();
            if (s.HackSurgeT > 0) parts.Add($"<color=#5fd8ff>HACK SURGE ×2 · {s.HackSurgeT:0}s</color>");
            if (s.JamT > 0) parts.Add($"<color=#ff5a6a>JAMMED · {s.JamT:0}s</color>");
            if (s.BountyTarget == _m.LocalId) parts.Add("<color=#ffd84a>YOU ARE THE BOUNTY</color>");
            else if (s.BountyTarget >= 0) parts.Add($"<color=#ffd84a>BOUNTY: {_m.NameOf(s.BountyTarget)}</color>");
            _status.text = string.Join("   ·   ", parts);
            if (s.BountyTarget >= 0 && s.BountyTarget != _m.LocalId)
                foreach (var av in s.Avatars) if (av.AvatarId == s.BountyTarget) _minimap.Blips.Add((av.Pos, Icons.Crown, Theme.Gold, 24f));

            // picked-up weapon + grenades
            string w = self.Special > 0 && !self.Fists ? $"{TouchControls.SpecialName(self.Special)} <size=18>{self.Ammo}</size>" : "";
            if (self.Grenades > 0) w += (w.Length > 0 ? "   " : "") + $"GRENADES ×{self.Grenades} <size=16>[G]</size>";
            _weapon.text = me.Alive ? w : "";

            // extraction finale: big countdown, alarm each second, music speeds up
            bool finale = s.ExtractFinal && s.Winner < 0 && s.ExtractController >= 0;
            Sfx.SetIntense(finale);
            if (finale)
            {
                float left = (1f - s.SquadExtract[s.ExtractController]) * GameConfig.ExtractTime;
                int sec = Mathf.CeilToInt(left);
                bool mine = s.ExtractController == _m.LocalSquad;
                _finale.text = sec.ToString();
                _finale.color = mine ? Theme.Green : Theme.Red;
                if (sec != _lastFinaleSec) { _lastFinaleSec = sec; _finaleBeepT = 0.25f; Sfx.Play(Sfx.Beep, 0.7f, mine ? 1.2f : 0.9f); }
                _finaleBeepT = Mathf.Max(0, _finaleBeepT - dt);
                _finale.rectTransform.localScale = Vector3.one * (1f + _finaleBeepT * 1.2f);
            }
            else { _finale.text = ""; _lastFinaleSec = -1; }
        }

        private Vector2 Local(Vector3 sp)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, sp, null, out var lp);
            return lp;
        }

        /// <summary>World label above a ground point, pushed to the screen edge when off screen.</summary>
        private void Place(Text t, Vec2 at, float height, string text, bool visible)
        {
            t.gameObject.SetActive(visible);
            if (!visible) return;
            Vector3 sp = _cam.WorldToScreenPoint(new Vector3(at.X, height, at.Y));
            bool behind = sp.z < 0;
            if (behind) sp = new Vector3(Screen.width - sp.x, Screen.height - sp.y, 0);
            // keep clear of the edges and of the bottom row (vitals, abilities, touch buttons)
            float mx = Screen.width * 0.08f, my = Screen.height * 0.12f, bottom = Screen.height * 0.3f;
            if (behind || sp.x < mx || sp.x > Screen.width - mx || sp.y < bottom || sp.y > Screen.height - my)
            {
                Vector2 ctr = new Vector2(Screen.width / 2f, (bottom + Screen.height - my) / 2f);
                Vector2 dir = (Vector2)sp - ctr;
                if (behind && dir.sqrMagnitude < 1) dir = Vector2.down;
                float k = Mathf.Min((Screen.width / 2f - mx) / Mathf.Max(1e-3f, Mathf.Abs(dir.x)), ((Screen.height - my - bottom) / 2f) / Mathf.Max(1e-3f, Mathf.Abs(dir.y)));
                sp = ctr + dir * k;
            }
            t.rectTransform.anchoredPosition = Local(sp);
            t.text = text;
        }

        public void Dispose()
        {
            if (_slowT > 0) Time.timeScale = 1f;
            Sfx.SetIntense(false);
        }
    }
}
