using System;
using System.Collections.Generic;

namespace Veil.Sim
{
    /// <summary>
    /// Match spice: weapon pickups, squad pings, tag redeploys, squad combos, random world events
    /// (supply drop, hack surge, bounty), the comeback sabotage and "your terminal is being hacked" alerts.
    /// </summary>
    public sealed partial class MatchSim
    {
        public float HackSurgeT { get; private set; }
        public int BountyTarget { get; private set; } = -1;
        private float _bountyT, _worldEventT = GameConfig.WorldEventFirst, _weaponT, _sabotageCheckT = 30f;
        private int _worldEventN;
        private const int MaxWeaponPickups = 10;

        /// <summary>Chain progress speed for a squad right now (hack surge doubles it, sabotage slows it).</summary>
        public float ProgressMult(SquadState sq) => (HackSurgeT > 0 ? GameConfig.HackSurgeMult : 1f) * (sq.JamT > 0 ? GameConfig.SabotageSlow : 1f);

        private void UpdateSpice(float dt)
        {
            foreach (var p in Players) p.PingCd = MathF.Max(0, p.PingCd - dt);
            UpdateWeaponSpawns(dt);
            UpdateTags(dt);
            UpdateWorldEvents(dt);
            UpdateSabotage(dt);
            foreach (var sq in Squads) sq.JamT = MathF.Max(0, sq.JamT - dt);
        }

        // ------------------------------------------------------------------ weapon pickups

        private void UpdateWeaponSpawns(float dt)
        {
            _weaponT -= dt;
            if (_weaponT > 0) return;
            _weaponT = 20f;
            int n = 0;
            foreach (var pk in Pickups.Values) if (IsLoot(pk.Type)) n++;
            for (; n < MaxWeaponPickups; n++)
            {
                Vec2 at = Map.Nav.RandomWalkable(Rng);
                if (at.Length > CircleRadius - 4) continue;
                AddPickup(RandomLoot(), at, -1);
            }
        }

        public static bool IsLoot(PickupType t) => t == PickupType.Shotgun || t == PickupType.Smg || t == PickupType.Grenades;
        private PickupType RandomLoot() { int r = Rng.Int(3); return r == 0 ? PickupType.Shotgun : r == 1 ? PickupType.Smg : PickupType.Grenades; }

        // ------------------------------------------------------------------ pings

        private void Ping(PlayerState p, float yaw)
        {
            p.PingCd = GameConfig.PingCooldown;
            Vec2 dir = Vec2.FromYaw(yaw);
            // low health: "need help" on yourself
            if (p.HealthFrac < 0.35f) { Events.Add(new SimEvent(EventType.Ping, p.Id, (int)PingKind.Help, -1, p.Pos)); return; }
            // an enemy the squad can see near the aim line
            PlayerState best = null; float bestScore = float.MaxValue;
            foreach (var o in Players)
            {
                if (!o.Alive || Allies(o, p) || SquadVisibility(p.Squad, o) == Visibility.None) continue;
                Vec2 to = o.Pos - p.Pos; float d = to.Length;
                if (d > GameConfig.PingRange || d < 0.5f) continue;
                float off = MathF.Abs(MathUtil.DeltaAngle(yaw, to.Yaw));
                if (off > 9f + 120f / d) continue;
                if (off < bestScore) { bestScore = off; best = o; }
            }
            if (best != null) { Events.Add(new SimEvent(EventType.Ping, p.Id, (int)PingKind.Enemy, best.Id, best.Pos)); return; }
            // loot near the aim line
            foreach (var pk in Pickups.Values)
            {
                if (!IsLoot(pk.Type)) continue;
                Vec2 to = pk.Pos - p.Pos; float d = to.Length;
                if (d > 40f || d < 0.5f || MathF.Abs(MathUtil.DeltaAngle(yaw, to.Yaw)) > 6f + 60f / d) continue;
                Events.Add(new SimEvent(EventType.Ping, p.Id, (int)PingKind.Loot, (int)pk.Type, pk.Pos));
                return;
            }
            // otherwise "go here": the first wall along the aim line, or max range
            Vec2 at = p.Pos;
            for (float s = 2f; s <= GameConfig.PingRange; s += 1f)
            {
                Vec2 q = p.Pos + dir * s;
                if (Map.BlocksShotAt(q, 1f, 0.2f)) break;
                at = q;
            }
            Events.Add(new SimEvent(EventType.Ping, p.Id, (int)PingKind.Go, -1, at));
        }

        // ------------------------------------------------------------------ tags (squad redeploy)

        private void DropTag(PlayerState v)
        {
            foreach (var pk in Pickups.Values) if (pk.Type == PickupType.Tag && pk.Spot == v.Id) return;
            var pos = v.Pos;
            Map.ResolveCircle(ref pos, 0.4f, 0);
            AddPickup(PickupType.Tag, pos, v.Id);
        }

        private readonly List<int> _tagGone = new List<int>();

        private void UpdateTags(float dt)
        {
            foreach (var p in Players) if (p.Reviving < 0 || !p.Alive) p.TagProg = 0;
            _tagGone.Clear();
            float r2 = GameConfig.TagRadius * GameConfig.TagRadius;
            foreach (var pk in Pickups.Values)
            {
                if (pk.Type != PickupType.Tag) continue;
                var v = Player(pk.Spot);
                if (v == null || v.Alive) { _tagGone.Add(pk.Id); continue; }
                PlayerState helper = null;
                foreach (var o in Players)
                    if (o.Squad == v.Squad && Standing(o) && !o.LastInput.Has(Buttons.Fire) && Vec2.DistSq(o.Pos, pk.Pos) <= r2) { helper = o; break; }
                if (helper == null) { v.ReviveProg = MathF.Max(0, v.ReviveProg - dt / GameConfig.TagRedeployTime); continue; }
                helper.Reviving = v.Id;
                v.ReviveProg += dt / GameConfig.TagRedeployTime;
                helper.TagProg = v.ReviveProg;
                if (v.ReviveProg < 1f) continue;
                // redeployed next to the helper
                v.ReviveProg = 0; helper.TagProg = 0;
                v.RespawnT = 0;
                Respawn(v, pk.Pos);
                helper.Revives++;
                helper.Score.Bonus += GameConfig.RevivePoints;
                Events.Add(new SimEvent(EventType.Redeployed, helper.Id, v.Id, 0, pk.Pos));
                _tagGone.Add(pk.Id);
            }
            foreach (int id in _tagGone)
            {
                if (!Pickups.TryGetValue(id, out var pk)) continue;
                Pickups.Remove(id);
                Events.Add(new SimEvent(EventType.PickupCollected, -1, id, (int)pk.Type, pk.Pos));
            }
        }

        // ------------------------------------------------------------------ squad combos

        /// <summary>Pulse overcharges nearby squadmates' shields; a dash next to a squadmate gives you both a speed burst.</summary>
        private void SquadCombos()
        {
            float r2 = GameConfig.ComboRadius * GameConfig.ComboRadius;
            int n = Events.Count;
            for (int i = 0; i < n; i++)
            {
                var e = Events[i];
                if (e.Type != EventType.PulseCast && !(e.Type == EventType.DashStart && e.B == 0)) continue;
                var p = Player(e.A);
                if (p == null) continue;
                bool any = false;
                foreach (var o in Players)
                {
                    if (o == p || o.Squad != p.Squad || !Standing(o) || Vec2.DistSq(o.Pos, p.Pos) > r2) continue;
                    any = true;
                    if (e.Type == EventType.PulseCast) o.Shield = MathF.Min(GameConfig.ShieldAmount, o.Shield + GameConfig.ComboShield);
                    else o.SpeedBuffT = MathF.Max(o.SpeedBuffT, GameConfig.ComboSpeedTime);
                }
                if (!any) continue;
                if (e.Type == EventType.DashStart) p.SpeedBuffT = MathF.Max(p.SpeedBuffT, GameConfig.ComboSpeedTime);
                Events.Add(new SimEvent(EventType.AbilityPlay, p.Id, e.Type == EventType.PulseCast ? 3 : 4, 0, p.Pos));
            }
        }

        // ------------------------------------------------------------------ world events

        private void UpdateWorldEvents(float dt)
        {
            if (HackSurgeT > 0)
            {
                HackSurgeT -= dt;
                if (HackSurgeT <= 0) Events.Add(new SimEvent(EventType.WorldEvent, -1, (int)WorldEventKind.SurgeOver, 0, Vec2.Zero));
            }
            if (BountyTarget >= 0)
            {
                _bountyT -= dt;
                var b = Players[BountyTarget];
                if (_bountyT <= 0) BountyTarget = -1;
                else b.PublicPingT = MathF.Max(b.PublicPingT, 0.5f);
            }
            if (ExtractRevealed) return;   // the finale is its own event
            _worldEventT -= dt;
            if (_worldEventT > 0) return;
            _worldEventT = GameConfig.WorldEventEvery + Rng.Range(-15f, 15f);
            var kind = (WorldEventKind)((_worldEventN++ + Settings.Seed) % 3);
            switch (kind)
            {
                case WorldEventKind.SupplyDrop:
                {
                    // between the squads, away from everyone: a cluster of the best loot
                    Vec2 at = Vec2.FromYaw(Rng.Range(0, 360)) * Rng.Range(30f, 90f);
                    if (!Map.Nav.Walkable(at)) at = Map.Nav.CellCenter(Map.Nav.NearestWalkable(Map.Nav.CellOf(at)));
                    AddPickup(PickupType.Shotgun, at + new Vec2(1.6f, 0), -1);
                    AddPickup(PickupType.Smg, at + new Vec2(-1.6f, 0), -1);
                    AddPickup(PickupType.Grenades, at + new Vec2(0, 1.6f), -1);
                    AddPickup(PickupType.Grenades, at + new Vec2(0, -1.6f), -1);
                    Events.Add(new SimEvent(EventType.WorldEvent, -1, (int)kind, 0, at));
                    break;
                }
                case WorldEventKind.HackSurge:
                    HackSurgeT = GameConfig.HackSurgeTime;
                    Events.Add(new SimEvent(EventType.WorldEvent, -1, (int)kind, (int)GameConfig.HackSurgeTime, Vec2.Zero));
                    break;
                default:
                {
                    // the best player alive becomes the bounty: everyone sees them, eliminating them pays
                    PlayerState best = null;
                    foreach (var p in Players) if (p.Alive && (best == null || p.Score.Total > best.Score.Total)) best = p;
                    if (best == null) break;
                    BountyTarget = best.Id;
                    _bountyT = GameConfig.BountyTime;
                    Events.Add(new SimEvent(EventType.WorldEvent, best.Id, (int)WorldEventKind.Bounty, (int)GameConfig.BountyTime, best.Pos));
                    break;
                }
            }
        }

        /// <summary>Called on every elimination: bounty payout.</summary>
        private void OnBountyKill(PlayerState v, int killer)
        {
            if (v.Id != BountyTarget) return;
            BountyTarget = -1;
            if (killer < 0 || killer == v.Id) return;
            var k = Players[killer];
            k.Score.Bonus += GameConfig.BountyPoints;
            k.Energy = GameConfig.MaxEnergy;
            Events.Add(new SimEvent(EventType.WorldEvent, killer, (int)WorldEventKind.BountyClaimed, GameConfig.BountyPoints, v.Pos));
        }

        // ------------------------------------------------------------------ comeback sabotage

        /// <summary>When one squad runs away with the match, the last squad (two+ steps behind) jams it once:
        /// the leader's progress slows and its members are shown to everyone for a few seconds.</summary>
        private void UpdateSabotage(float dt)
        {
            _sabotageCheckT -= dt;
            if (_sabotageCheckT > 0 || ExtractFinal) return;
            _sabotageCheckT = 10f;
            SquadState lead = null, last = null;
            foreach (var sq in Squads)
            {
                if (lead == null || ChainRankKey(sq) > ChainRankKey(lead)) lead = sq;
                if (last == null || ChainRankKey(sq) < ChainRankKey(last)) last = sq;
            }
            if (lead == null || last == null || lead == last || last.SabotageUsed || lead.JamT > 0) return;
            float gap = (lead.Stage + lead.StageProg + lead.ExtractProg) - (last.Stage + last.StageProg);
            if (gap < 2f) return;
            last.SabotageUsed = true;
            lead.JamT = GameConfig.SabotageTime;
            foreach (var p in Players) if (p.Squad == lead.Id && p.Alive) p.PublicPingT = MathF.Max(p.PublicPingT, GameConfig.SabotageReveal);
            Events.Add(new SimEvent(EventType.Sabotage, last.Id, lead.Id, (int)GameConfig.SabotageTime, Vec2.Zero));
        }

        // ------------------------------------------------------------------ "your terminal is being hacked"

        /// <summary>The squad whose own home terminal sits at this position (-1 none).</summary>
        private int OwnerOfHome(Vec2 at)
        {
            foreach (var sq in Squads) if (Vec2.DistSq(sq.OwnHome, at) < 4f) return sq.Id;
            return -1;
        }

        private void AlertHomeOwner(SquadState hacker, Vec2 at, int pct)
        {
            int owner = OwnerOfHome(at);
            if (owner < 0 || owner == hacker.Id) return;
            Events.Add(new SimEvent(EventType.BeingHacked, hacker.Id, owner, pct, at));
        }

        /// <summary>The last squad left with anyone alive while every other squad is wiped (rare without respawns, still a moment).</summary>
        private void CheckWipe(PlayerState v, int killer)
        {
            foreach (var o in Players) if (o.Squad == v.Squad && o.Alive) return;
            Events.Add(new SimEvent(EventType.SquadWiped, killer, v.Squad, 0, v.Pos));
        }
    }
}
