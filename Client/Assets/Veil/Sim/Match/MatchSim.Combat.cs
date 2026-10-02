using System;
using System.Collections.Generic;

namespace Veil.Sim
{
    public sealed partial class MatchSim
    {
        // ------------------------------------------------------------------ actions

        private void HandleActions(PlayerState p, in InputCmd cmd)
        {
            // Blaster (LMB)
            if (cmd.Has(Buttons.Fire) && p.FireCd <= 0 && p.DashT <= 0)
            {
                var ws = GameConfig.Weapon(p.Look.Weapon);
                p.FireCd = ws.Cooldown;
                p.FireSeq++;
                p.NoiseT = GameConfig.FireNoiseTime;
                p.SpawnProtT = 0;
                Vec2 dir = Vec2.FromYaw(cmd.Yaw);
                Vec2 origin = p.Pos + dir * 0.6f;
                if (!Map.BlocksShotAt(origin, GameConfig.ProjectileHeight + p.H, GameConfig.ProjectileRadius))
                {
                    Projectiles.Add(new Projectile
                    {
                        Id = NextEntityId(), Owner = p.Id, Pos = origin, Vel = dir * ws.Speed, Damage = ws.Damage, Range = ws.Range,
                    });
                }
                Events.Add(new SimEvent(EventType.Fire, p.Id, 0, 0, p.Pos));
            }

            // Pulse (E) — reveal nearby players, pop decoys
            if (cmd.Has(Buttons.Pulse) && p.PulseCd <= 0 && p.Energy >= GameConfig.PulseCost)
            {
                p.PulseCd = GameConfig.PulseCooldown;
                p.Energy -= GameConfig.PulseCost;
                p.CastSeq++;
                Events.Add(new SimEvent(EventType.PulseCast, p.Id, 0, 0, p.Pos));
                int revealed = 0;
                float r2 = GameConfig.PulseRadius * GameConfig.PulseRadius;
                foreach (var o in Players)
                {
                    if (o == p || Allies(o, p) || !o.Alive || Vec2.DistSq(o.Pos, p.Pos) > r2) continue;
                    o.RevealedTo[p.Id] = GameConfig.PulseRevealTime;
                    revealed++;
                    Events.Add(new SimEvent(EventType.Revealed, p.Id, o.Id, 0, o.Pos));
                }
                foreach (var d in Decoys)
                {
                    if (d.Dead || Allies(Players[d.Owner], p) || Vec2.DistSq(d.Pos, p.Pos) > r2) continue;
                    PopDecoy(d);
                }
                if (revealed > 0) AbilityPlay(p, 1);
            }

            // Decoy (R) — a fake copy that keeps running
            if (cmd.Has(Buttons.Decoy) && p.DecoyCd <= 0 && p.Energy >= GameConfig.DecoyCost)
            {
                p.DecoyCd = GameConfig.DecoyCooldown;
                p.Energy -= GameConfig.DecoyCost;
                p.CastSeq++;
                Vec2 m = cmd.Move;
                Vec2 dir = m.LengthSq > 0.04f ? m.Normalized : Vec2.FromYaw(cmd.Yaw);
                var d = new Decoy
                {
                    AvatarId = _nextDecoyAvatar++, Owner = p.Id, Pos = p.Pos, Vel = dir * GameConfig.RunSpeed,
                    Yaw = dir.Yaw, Life = GameConfig.DecoyLifetime,
                };
                Decoys.Add(d);
                Events.Add(new SimEvent(EventType.DecoySpawn, d.AvatarId, p.Id, 0, p.Pos));
            }

            // Market purchases (1/2/3) — only inside the Market
            if (p.ZoneId == MarketZone && p.BuyCd <= 0)
            {
                int item = cmd.Has(Buttons.Buy1) ? 1 : cmd.Has(Buttons.Buy2) ? 2 : cmd.Has(Buttons.Buy3) ? 3 : 0;
                if (item > 0) TryBuy(p, item);
            }
        }

        public float MarketPrice(PlayerState p, int item)
        {
            float baseCost = item == 1 ? GameConfig.MarketSpeedCost : item == 2 ? GameConfig.MarketShieldCost : GameConfig.MarketKeyCost;
            return Zones[MarketZone].Squad == p.Squad ? baseCost * GameConfig.MarketDiscount : baseCost;
        }

        private void TryBuy(PlayerState p, int item)
        {
            float cost = MarketPrice(p, item);
            if (p.Energy < cost) return;
            if (item == 3 && p.Keys >= GameConfig.MaxKeysCarried) return;
            p.Energy -= cost;
            p.BuyCd = GameConfig.MarketBuyCooldown;
            if (item == 1) p.SpeedBuffT = GameConfig.SpeedBuffTime;
            else if (item == 2) p.Shield = GameConfig.ShieldAmount;
            else p.Keys++;
            Events.Add(new SimEvent(EventType.Purchase, p.Id, item, (int)cost, p.Pos));
        }

        private void AbilityPlay(PlayerState p, int kind)
        {
            p.Score.Bonus += GameConfig.AbilityPlayPoints;
            Events.Add(new SimEvent(EventType.AbilityPlay, p.Id, kind, GameConfig.AbilityPlayPoints, p.Pos));
        }

        // ------------------------------------------------------------------ projectiles

        private void UpdateProjectiles(float dt)
        {
            float hitR = GameConfig.HitRadius + GameConfig.ProjectileRadius;
            float hitR2 = hitR * hitR;
            foreach (var pr in Projectiles)
            {
                if (pr.Dead) continue;
                Vec2 delta = pr.Vel * dt;
                float len = delta.Length;
                int steps = Math.Max(1, (int)MathF.Ceiling(len / 0.4f));
                Vec2 step = delta / steps;
                for (int s = 0; s < steps && !pr.Dead; s++)
                {
                    pr.Pos += step;
                    pr.Travelled += len / steps;
                    if (pr.Travelled > pr.Range) { pr.Dead = true; break; }
                    if (Map.BlocksShotAt(pr.Pos, GameConfig.ProjectileHeight, GameConfig.ProjectileRadius))
                    {
                        pr.Dead = true;
                        Events.Add(new SimEvent(EventType.Hit, pr.Owner, -1, 0, pr.Pos));
                        break;
                    }
                    var shooter = Players[pr.Owner];
                    if (HitNode(shooter, pr.Pos)) { pr.Dead = true; break; }
                    if (pr.Dead) break;
                    foreach (var p in Players)
                    {
                        if (p.Id == pr.Owner || Allies(p, shooter) || !p.Alive) continue;   // no friendly fire
                        if (Vec2.DistSq(p.Pos, pr.Pos) > hitR2) continue;
                        Damage(p, pr.Owner, pr.Damage, pr.Vel.Normalized);
                        pr.Dead = true;
                        break;
                    }
                    if (pr.Dead) break;
                    foreach (var d in Decoys)
                    {
                        if (d.Dead || Allies(Players[d.Owner], shooter)) continue;
                        if (Vec2.DistSq(d.Pos, pr.Pos) > hitR2) continue;
                        pr.Dead = true;
                        PopDecoy(d);
                        AbilityPlay(Players[d.Owner], 2); // the decoy fooled someone
                        break;
                    }
                }
            }
            Projectiles.RemoveAll(x => x.Dead);
        }

        // ------------------------------------------------------------------ decoys

        private void UpdateDecoys(float dt)
        {
            foreach (var d in Decoys)
            {
                if (d.Dead) continue;
                d.Life -= dt;
                if (d.Life <= 0 || !Players[d.Owner].Alive) { PopDecoy(d); continue; }
                Vec2 want = d.Pos + d.Vel * dt;
                Vec2 np = want;
                Map.ResolveCircle(ref np, GameConfig.PlayerRadius, 0);
                if (Vec2.Dist(np, d.Pos) < d.Vel.Length * dt * 0.4f)
                {
                    // blocked: turn towards open space
                    d.Vel = Vec2.RotateYaw(d.Vel, Rng.Chance(0.5f) ? 90 : -90);
                    d.Yaw = d.Vel.Yaw;
                }
                d.Pos = np;
            }
            Decoys.RemoveAll(x => x.Dead);
        }

        private void PopDecoy(Decoy d)
        {
            if (d.Dead) return;
            d.Dead = true;
            Events.Add(new SimEvent(EventType.DecoyPop, d.AvatarId, d.Owner, 0, d.Pos));
        }

        // ------------------------------------------------------------------ damage

        public void Damage(PlayerState victim, int attacker, float amount, Vec2 dir)
        {
            if (!victim.Alive || victim.SpawnProtT > 0) return;
            if (Phase == MatchPhase.Exploration) amount *= GameConfig.ExplorationDamageScale;
            if (victim.Shield > 0)
            {
                float a = MathF.Min(victim.Shield, amount);
                victim.Shield -= a;
                amount -= a;
                if (victim.Shield <= 0) Events.Add(new SimEvent(EventType.ShieldBreak, victim.Id, 0, 0, victim.Pos));
            }
            victim.Health -= amount;
            victim.SinceDamage = 0;
            victim.LastAttacker = attacker;
            if (attacker >= 0 && attacker < victim.DamagedAt.Length) victim.DamagedAt[attacker] = Time;
            victim.HitSeq++;
            victim.VaultChannel = 0;
            victim.Knock += dir * GameConfig.Knockback;
            Events.Add(new SimEvent(EventType.Hit, attacker, victim.Id, (int)MathF.Ceiling(amount), victim.Pos));
            if (victim.Health <= 0) HealthGone(victim, attacker);
        }

        /// <summary>Out of health: knocked down while a squadmate is still standing, otherwise eliminated.</summary>
        private void HealthGone(PlayerState v, int attacker)
        {
            if (!GameConfig.DownedEnabled || v.Downed || !HasStandingMate(v)) { Eliminate(v, v.Downed && attacker < 0 ? v.DownedBy : attacker); return; }
            v.Downed = true;
            v.Health = GameConfig.DownedHealth;
            v.Shield = 0;
            v.BleedT = GameConfig.DownedBleedTime;
            v.ReviveProg = 0;
            v.DownedBy = attacker;
            v.DashT = 0; v.VaultChannel = 0; v.Reviving = -1;
            Events.Add(new SimEvent(EventType.Downed, attacker, v.Id, 0, v.Pos));
            CheckSquadWipe(v.Squad);
        }

        private bool HasStandingMate(PlayerState v)
        {
            foreach (var o in Players) if (o != v && o.Squad == v.Squad && o.Alive && !o.Downed) return true;
            return false;
        }

        /// <summary>Nobody left standing in a squad: everyone still downed is eliminated.</summary>
        private void CheckSquadWipe(int squad)
        {
            foreach (var o in Players) if (o.Squad == squad && o.Alive && !o.Downed) return;
            foreach (var o in Players) if (o.Squad == squad && o.Alive && o.Downed) Eliminate(o, o.DownedBy);
        }

        private void UpdateDowned(PlayerState p, float dt)
        {
            p.BleedT -= dt;
            if (p.BleedT <= 0) { Eliminate(p, p.DownedBy); return; }
            // a squadmate standing close and not shooting revives; progress slowly fades when they step away
            PlayerState reviver = null;
            float r2 = GameConfig.ReviveRadius * GameConfig.ReviveRadius;
            foreach (var o in Players)
            {
                if (o == p || o.Squad != p.Squad || !o.Alive || o.Downed || o.LastInput.Has(Buttons.Fire)) continue;
                if (Vec2.DistSq(o.Pos, p.Pos) <= r2 && (o.Reviving < 0 || o.Reviving == p.Id)) { reviver = o; break; }
            }
            if (reviver == null) { p.ReviveProg = MathF.Max(0, p.ReviveProg - dt / GameConfig.ReviveTime * 0.5f); return; }
            reviver.Reviving = p.Id;
            p.ReviveProg += dt / GameConfig.ReviveTime;
            if (p.ReviveProg < 1f) return;
            p.Downed = false;
            p.ReviveProg = 0;
            p.Health = GameConfig.ReviveHealth;
            p.SinceDamage = 0;
            p.DownedBy = -1;
            reviver.Revives++;
            reviver.Score.Bonus += GameConfig.RevivePoints;
            Events.Add(new SimEvent(EventType.Revived, reviver.Id, p.Id, 0, p.Pos));
        }

        private void ApplyEnvironmentDamage(PlayerState p, float amount)
        {
            if (!p.Alive) return;
            bool recentlyShot = p.LastAttacker >= 0 && p.SinceDamage < 3f;
            p.Health -= amount;
            if (p.Health <= 0) HealthGone(p, recentlyShot ? p.LastAttacker : -1);
        }

        private void Eliminate(PlayerState v, int killer)
        {
            if (!v.Alive) return;
            v.Alive = false;
            v.Downed = false;
            v.ReviveProg = 0;
            v.Reviving = -1;
            v.Health = 0;
            v.Shield = 0;
            v.RespawnT = ExtractRevealed ? GameConfig.ExtractRespawnTime : GameConfig.RespawnTime;
            v.Deaths++;
            v.Vel = Vec2.Zero;
            v.Knock = Vec2.Zero;
            v.DashT = 0;
            v.H = 0; v.VH = 0; v.Grounded = true;
            v.VaultChannel = 0;
            v.Energy *= GameConfig.DeathEnergyKeep;

            // dropped keys can be stolen
            for (int i = 0; i < v.Keys; i++)
            {
                var off = Vec2.FromYaw(i * 120 + Rng.Range(0, 60)) * 1.4f;
                var pos = v.Pos + off;
                Map.ResolveCircle(ref pos, 0.4f, 0);
                AddPickup(PickupType.Key, pos, -1);
            }
            v.Keys = 0;

            if (killer >= 0 && killer != v.Id)
            {
                var k = Players[killer];
                k.Elims++;
                k.ElimsOn[v.Id]++;
                // farming the same victim is worth less: 100, 50, 25, 25...
                k.Score.Eliminations += Math.Max(25, GameConfig.EliminationPoints >> (k.ElimsOn[v.Id] - 1));
            }
            // assists: other enemies that hurt the victim recently
            for (int i = 0; i < Players.Count && i < v.DamagedAt.Length; i++)
            {
                var a = Players[i];
                if (a.Id == killer || a == v || Allies(a, v) || v.DamagedAt[i] <= 0 || Time - v.DamagedAt[i] > GameConfig.AssistWindow) continue;
                a.Assists++;
                a.Score.Bonus += GameConfig.AssistPoints;
            }
            Array.Clear(v.DamagedAt, 0, v.DamagedAt.Length);
            Events.Add(new SimEvent(EventType.Eliminated, killer, v.Id, 0, v.Pos));
            CheckSquadWipe(v.Squad);
        }

        private void UpdateRespawn(PlayerState p, float dt)
        {
            p.RespawnT -= dt;
            if (p.RespawnT > 0) return;
            p.Pos = PickRespawnPoint(p);
            p.Yaw = (-p.Pos).Yaw;
            p.Health = GameConfig.MaxHealth;
            p.Alive = true;
            p.SpawnProtT = GameConfig.SpawnProtection;
            p.SinceDamage = 99;
            p.LastAttacker = -1;
            p.ZoneId = Map.ZoneAt(p.Pos);
            Events.Add(new SimEvent(EventType.Respawned, p.Id, 0, 0, p.Pos));
        }

        /// <summary>Far from enemies, preferably close to a living squadmate (squads regroup after a death).</summary>
        private Vec2 PickRespawnPoint(PlayerState who)
        {
            if (GameConfig.ExtractionMode)
            {
                // back to your squad's spawn site
                Vec2 home = Squads[who.Squad].Spawn;
                for (int i = 0; i < 12; i++)
                {
                    Vec2 c = home + Vec2.FromYaw(Rng.Range(0, 360)) * Rng.Range(0f, 3f);
                    if (Map.Nav.Walkable(c)) return c;
                }
                return home;
            }
            Vec2 best = Vec2.Zero;
            float bestScore = float.MinValue;
            float limit = CircleRadius * 0.85f;
            var mates = new List<Vec2>();
            foreach (var o in Players) if (o != who && o.Alive && Allies(o, who)) mates.Add(o.Pos);
            for (int i = 0; i < 24 + mates.Count * 3; i++)
            {
                Vec2 c;
                if (i >= 24) c = mates[(i - 24) / 3] + Vec2.FromYaw(Rng.Range(0, 360)) * Rng.Range(3f, 7f);
                else c = i < Map.SpawnPoints.Count ? Map.SpawnPoints[i] : Map.Nav.RandomWalkable(Rng);
                if (c.Length > limit) c = Map.Nav.RandomWalkable(Rng).Normalized * Rng.Range(0, limit);
                if (!Map.Nav.Walkable(c)) c = Map.Nav.CellCenter(Map.Nav.NearestWalkable(Map.Nav.CellOf(c)));
                float enemy = 999, mate = 999;
                foreach (var o in Players)
                {
                    if (!o.Alive || o == who) continue;
                    float d = Vec2.Dist(o.Pos, c);
                    if (Allies(o, who)) mate = MathF.Min(mate, d); else enemy = MathF.Min(enemy, d);
                }
                float score = MathF.Min(enemy, 45f) - (mate < 999 ? mate * 0.25f : 0) + Rng.Range(0, 6);
                if (enemy < 18f) score -= 60f;
                if (score > bestScore) { bestScore = score; best = c; }
            }
            return best;
        }
    }
}
