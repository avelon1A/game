using System;
using System.Collections.Generic;

namespace Veil.Sim
{
    public sealed partial class MatchSim
    {
        // ------------------------------------------------------------------ pickups

        private Pickup AddPickup(PickupType type, Vec2 pos, int spot)
        {
            var pk = new Pickup { Id = NextEntityId(), Type = type, Pos = pos, Spot = spot };
            Pickups[pk.Id] = pk;
            Events.Add(new SimEvent(EventType.PickupSpawned, pk.Id, (int)type, spot, pos));
            return pk;
        }

        private void SpawnAtSpots(PickupType type, int count)
        {
            for (int i = 0; i < count; i++) SpawnAtFreeSpot(type);
        }

        private readonly List<int> _freeSpots = new List<int>();

        private bool SpawnAtFreeSpot(PickupType type)
        {
            var spots = type == PickupType.Core ? Map.CoreSpots : Map.KeySpots;
            _freeSpots.Clear();
            for (int s = 0; s < spots.Count; s++)
            {
                if (spots[s].Length > CircleRadius - 2) continue;
                bool used = false;
                foreach (var pk in Pickups.Values)
                    if (pk.Type == type && pk.Spot == s) { used = true; break; }
                if (!used) _freeSpots.Add(s);
            }
            if (_freeSpots.Count == 0) return false;
            int pick = _freeSpots[Rng.Int(_freeSpots.Count)];
            AddPickup(type, spots[pick], pick);
            return true;
        }

        private void SpawnOrb()
        {
            for (int tries = 0; tries < 10; tries++)
            {
                Vec2 p = Map.Nav.RandomWalkable(Rng) + new Vec2(Rng.Range(-0.3f, 0.3f), Rng.Range(-0.3f, 0.3f));
                if (p.Length > CircleRadius - 3) continue;
                AddPickup(PickupType.Orb, p, -1);
                return;
            }
        }

        private readonly List<int> _collected = new List<int>();

        private void UpdatePickups(float dt)
        {
            // spawn orbs over time
            _orbTimer += dt;
            if (_orbTimer >= GameConfig.OrbSpawnInterval)
            {
                _orbTimer = 0;
                int orbs = 0;
                foreach (var pk in Pickups.Values) if (pk.Type == PickupType.Orb) orbs++;
                if (orbs < GameConfig.MaxOrbs) SpawnOrb();
            }

            // respawn cores / keys
            for (int i = _respawns.Count - 1; i >= 0; i--)
            {
                var r = _respawns[i];
                r.t -= dt;
                if (r.t <= 0)
                {
                    _respawns.RemoveAt(i);
                    if (!SpawnAtFreeSpot(r.type)) _respawns.Add((5f, r.type));
                }
                else _respawns[i] = r;
            }

            // collection
            _collected.Clear();
            float r2 = GameConfig.PickupRadius * GameConfig.PickupRadius;
            foreach (var pk in Pickups.Values)
            {
                foreach (var p in Players)
                {
                    if (!p.Alive || p.Downed || Vec2.DistSq(p.Pos, pk.Pos) > r2 || p.H > 1.6f) continue;
                    if (!Collect(p, pk)) continue;
                    _collected.Add(pk.Id);
                    break;
                }
            }
            foreach (int id in _collected) Pickups.Remove(id);

            // collapse removes pickups outside the circle
            if (CircleRadius < GameConfig.CircleStartRadius)
            {
                _collected.Clear();
                foreach (var pk in Pickups.Values)
                    if (pk.Pos.Length > CircleRadius + 1) _collected.Add(pk.Id);
                foreach (int id in _collected)
                {
                    var pk = Pickups[id];
                    Pickups.Remove(id);
                    if (pk.Spot >= 0) _respawns.Add((1f, pk.Type));
                    Events.Add(new SimEvent(EventType.PickupCollected, -1, id, (int)pk.Type, pk.Pos));
                }
            }
        }

        private bool Collect(PlayerState p, Pickup pk)
        {
            switch (pk.Type)
            {
                case PickupType.Orb:
                    p.Energy = MathF.Min(GameConfig.MaxEnergy, p.Energy + GameConfig.OrbEnergy);
                    p.Score.Resources += GameConfig.OrbPoints;
                    break;
                case PickupType.Core:
                    p.CoresCollected++;
                    p.Energy = MathF.Min(GameConfig.MaxEnergy, p.Energy + GameConfig.CoreEnergy);
                    p.Score.Resources += GameConfig.CorePoints;
                    _respawns.Add((GameConfig.CoreRespawn, PickupType.Core));
                    break;
                case PickupType.Key:
                    if (p.Keys >= GameConfig.MaxKeysCarried) return false;
                    p.Keys++;
                    p.Score.Resources += GameConfig.KeyPoints;
                    if (pk.Spot >= 0) _respawns.Add((GameConfig.KeyRespawn, PickupType.Key));
                    break;
            }
            Events.Add(new SimEvent(EventType.PickupCollected, p.Id, pk.Id, (int)pk.Type, pk.Pos));
            return true;
        }

        // ------------------------------------------------------------------ zones

        private readonly int[] _squadCount = new int[GameConfig.SquadCount];

        private void UpdateZones(float dt)
        {
            var vault = Zones[VaultZone];
            vault.Locked = Phase == MatchPhase.Exploration;
            vault.Cooldown = MathF.Max(0, vault.Cooldown - dt);

            for (int zi = 0; zi < Zones.Length; zi++)
            {
                var z = Zones[zi];
                var def = Map.Zones[zi];
                int count = 0, single = -1, squads = 0, squad = -1;
                Array.Clear(_squadCount, 0, _squadCount.Length);
                foreach (var p in Players)
                {
                    if (!p.Alive || p.Downed || p.ZoneId != zi) continue;
                    count++;
                    if (_squadCount[p.Squad]++ == 0) { squads++; squad = p.Squad; single = p.Id; }
                }
                z.Occupants = count;
                z.Contested = squads > 1;

                if (def.Type == ZoneType.Vault) { UpdateVault(z, dt); continue; }

                // ---- capture: one squad alone in the zone captures it (more members = faster) ----
                if (squads == 1)
                {
                    if (z.Squad != squad)
                    {
                        if (z.CapturerSquad != squad) { z.CapturerSquad = squad; z.Capturer = single; z.Progress = 0; }
                        float speed = 1f + 0.25f * (_squadCount[squad] - 1);
                        z.Progress += speed * dt / GameConfig.CaptureTime;
                        if (z.Progress >= 1f)
                        {
                            int prev = z.Controller;
                            z.Controller = z.Capturer >= 0 && Players[z.Capturer].Squad == squad ? z.Capturer : single;
                            z.Squad = squad;
                            z.Progress = 0;
                            z.Capturer = -1;
                            z.CapturerSquad = -1;
                            Squads[squad].CapturedMask |= 1 << zi;
                            foreach (var p in Players) if (p.Squad == squad) p.CapturedMask |= 1 << zi;
                            Events.Add(new SimEvent(EventType.ZoneCaptured, z.Controller, zi, prev, def.Center));
                        }
                    }
                    else z.Progress = MathF.Max(0, z.Progress - dt / GameConfig.CaptureTime);
                }
                else if (squads == 0)
                {
                    z.Progress = MathF.Max(0, z.Progress - dt * 0.5f / GameConfig.CaptureTime);
                    if (z.Progress <= 0) { z.Capturer = -1; z.CapturerSquad = -1; }
                }

                // ---- control rewards (territory to the capturer; Tower time/sight to the whole squad) ----
                if (z.Controller >= 0 && z.Squad >= 0)
                {
                    var c = Players[z.Controller];
                    float rate = def.Type == ZoneType.Tower
                        ? GameConfig.TowerTerritoryPerSec * (Phase == MatchPhase.Final ? 2f : 1f)
                        : GameConfig.ZoneTerritoryPerSec;
                    c.Score.TerritoryAccum += rate * dt;
                    if (c.Score.TerritoryAccum >= 1f)
                    {
                        int whole = (int)c.Score.TerritoryAccum;
                        c.Score.Territory += whole;
                        c.Score.TerritoryAccum -= whole;
                    }
                    if (def.Type == ZoneType.Tower)
                    {
                        Squads[z.Squad].TowerTime += dt;
                        bool ping = false;
                        c.TowerPingTimer += dt;
                        if (c.TowerPingTimer >= GameConfig.TowerRevealInterval) { c.TowerPingTimer = 0; ping = true; }
                        foreach (var m in Players)
                        {
                            if (m.Squad != z.Squad || !m.Alive || m.Downed) continue;
                            m.TowerControlTime += dt;
                            if (ping) m.TowerSightT = GameConfig.TowerRevealDuration;
                        }
                    }
                }

                // ---- reactor charging (everyone inside is exposed) ----
                if (def.Type == ZoneType.Reactor)
                {
                    foreach (var p in Players)
                    {
                        if (!p.Alive || p.Downed || p.ZoneId != zi) continue;
                        float rate = GameConfig.ReactorEnergyPerSec + (z.Squad == p.Squad ? GameConfig.ReactorControllerBonus : 0);
                        p.Energy = MathF.Min(GameConfig.MaxEnergy, p.Energy + rate * dt);
                        p.PublicPingT = 0.35f;
                    }
                }
            }
        }

        private void UpdateVault(ZoneState z, float dt)
        {
            foreach (var p in Players)
            {
                if (!p.Alive || p.Downed || p.ZoneId != z.Id || p.Keys < GameConfig.VaultKeys || z.Locked || z.Cooldown > 0)
                {
                    if (p.ZoneId != z.Id) p.VaultChannel = 0;
                    continue;
                }
                p.VaultChannel += dt;
                if (p.VaultChannel >= GameConfig.VaultChannelTime)
                {
                    p.VaultChannel = 0;
                    p.Keys -= GameConfig.VaultKeys;
                    p.Energy = GameConfig.MaxEnergy;
                    p.Score.Resources += GameConfig.VaultResourcePoints;
                    VaultsOpened[p.Id]++;
                    z.Cooldown = GameConfig.VaultCooldown;
                    Events.Add(new SimEvent(EventType.VaultOpened, p.Id, 0, 0, Map.Zones[z.Id].Center));
                    break;
                }
            }
        }

        public readonly int[] VaultsOpened = new int[GameConfig.MaxPlayers];

        // ------------------------------------------------------------------ objectives

        private static readonly ObjectiveType[] PrimaryPool =
            { ObjectiveType.TowerControl, ObjectiveType.CollectCores, ObjectiveType.VaultRaid, ObjectiveType.CaptureTwo };

        private static readonly ObjectiveType[] SecondaryPool =
            { ObjectiveType.TowerControl, ObjectiveType.CollectCores, ObjectiveType.VaultRaid, ObjectiveType.CaptureTwo, ObjectiveType.HighEnergy, ObjectiveType.Nemesis, ObjectiveType.HighEnergy, ObjectiveType.Nemesis };

        private void AssignObjectives(PlayerState p)
        {
            var prim = PrimaryPool[Rng.Int(PrimaryPool.Length)];
            ObjectiveType sec;
            do sec = SecondaryPool[Rng.Int(SecondaryPool.Length)]; while (sec == prim);
            Setup(p, p.Primary, prim, true);
            Setup(p, p.Secondary, sec, false);
        }

        private void Setup(PlayerState p, ObjectiveState o, ObjectiveType t, bool primary)
        {
            float s = Settings.ObjectiveScale;
            o.Type = t;
            o.IsPrimary = primary;
            o.Progress = 0;
            o.Done = false;
            switch (t)
            {
                case ObjectiveType.TowerControl: o.Target = MathF.Round(GameConfig.TowerControlSeconds * s); break;
                case ObjectiveType.CollectCores: o.Target = Math.Max(2, (int)MathF.Round(GameConfig.CoresNeeded * s)); break;
                case ObjectiveType.VaultRaid: o.Target = 1; break;
                case ObjectiveType.CaptureTwo: o.Target = GameConfig.ZonesToCapture; break;
                case ObjectiveType.HighEnergy: o.Target = GameConfig.EnergyThreshold; break;
                case ObjectiveType.Nemesis:
                    o.Target = Math.Max(1, (int)MathF.Round(GameConfig.NemesisEliminations * s));
                    int target = -1;
                    for (int tries = 0; tries < 64; tries++)
                    {
                        int t2 = Rng.Int(Players.Count);
                        if (!Allies(Players[t2], p)) { target = t2; break; }   // nemesis is always an enemy
                    }
                    if (target < 0) { o.Type = ObjectiveType.HighEnergy; o.Target = GameConfig.EnergyThreshold; }
                    o.TargetPlayer = target;
                    break;
            }
        }

        private static readonly ObjectiveType[] SquadPool =
            { ObjectiveType.TowerControl, ObjectiveType.CollectCores, ObjectiveType.VaultRaid, ObjectiveType.CaptureTwo };

        private void AssignSquadObjective(SquadState sq)
        {
            float s = Settings.ObjectiveScale;
            var o = sq.Objective;
            o.Type = SquadPool[Rng.Int(SquadPool.Length)];
            o.IsPrimary = false; o.IsSquad = true; o.Progress = 0; o.Done = false; o.TargetPlayer = -1;
            switch (o.Type)
            {
                case ObjectiveType.TowerControl: o.Target = MathF.Round(GameConfig.SquadTowerSeconds * s); break;
                case ObjectiveType.CollectCores: o.Target = Math.Max(4, (int)MathF.Round(GameConfig.SquadCoresNeeded * s)); break;
                case ObjectiveType.VaultRaid: o.Target = GameConfig.SquadVaultsNeeded; break;
                default: o.Target = GameConfig.SquadZonesToCapture; break;
            }
        }

        private void UpdateObjectives()
        {
            foreach (var p in Players)
            {
                UpdateObjective(p, p.Primary);
                UpdateObjective(p, p.Secondary);
            }
            foreach (var sq in Squads) UpdateSquadObjective(sq);
        }

        private void UpdateSquadObjective(SquadState sq)
        {
            var o = sq.Objective;
            if (o.Done) return;
            float v = 0;
            switch (o.Type)
            {
                case ObjectiveType.TowerControl: v = sq.TowerTime; break;
                case ObjectiveType.CollectCores: foreach (var p in Players) if (p.Squad == sq.Id) v += p.CoresCollected; break;
                case ObjectiveType.VaultRaid: foreach (var p in Players) if (p.Squad == sq.Id) v += VaultsOpened[p.Id]; break;
                case ObjectiveType.CaptureTwo: v = PopCount(sq.CapturedMask); break;
            }
            o.Progress = v;
            if (v < o.Target) return;
            o.Done = true;
            o.Progress = o.Target;
            int members = Math.Max(1, SquadMembers(sq.Id));
            int share = GameConfig.SquadObjectivePoints / members;
            int first = -1;
            foreach (var p in Players)
            {
                if (p.Squad != sq.Id) continue;
                p.Score.Squad += share;
                if (first < 0) first = p.Id;
            }
            // B = 2 marks a squad objective; A = any member so the filter can route it to the squad
            if (first >= 0) Events.Add(new SimEvent(EventType.ObjectiveComplete, first, 2, (int)o.Type, Players[first].Pos));
        }

        private void UpdateObjective(PlayerState p, ObjectiveState o)
        {
            if (o.Done) return;
            switch (o.Type)
            {
                case ObjectiveType.TowerControl: o.Progress = p.TowerControlTime; break;
                case ObjectiveType.CollectCores: o.Progress = p.CoresCollected; break;
                case ObjectiveType.VaultRaid: o.Progress = VaultsOpened[p.Id] > 0 ? 1 : MathF.Min(p.Keys, GameConfig.VaultKeys) / (float)GameConfig.VaultKeys * 0.99f; break;
                case ObjectiveType.CaptureTwo: o.Progress = PopCount(p.CapturedMask); break;
                case ObjectiveType.HighEnergy: o.Progress = p.Energy; return; // evaluated at the end
                case ObjectiveType.Nemesis: o.Progress = o.TargetPlayer >= 0 ? p.ElimsOn[o.TargetPlayer] : 0; break;
            }
            if (o.Progress >= o.Target) Complete(p, o);
        }

        private void CheckEndObjective(PlayerState p, ObjectiveState o)
        {
            if (o.Done || o.Type != ObjectiveType.HighEnergy) return;
            o.Progress = p.Energy;
            if (p.Energy >= o.Target) Complete(p, o);
        }

        private void Complete(PlayerState p, ObjectiveState o)
        {
            o.Done = true;
            o.Progress = o.Target;
            if (o.IsPrimary) p.Score.Primary += GameConfig.PrimaryPoints;
            else p.Score.Secondary += GameConfig.SecondaryPoints;
            Events.Add(new SimEvent(EventType.ObjectiveComplete, p.Id, o.IsPrimary ? 1 : 0, (int)o.Type, p.Pos));
        }

        private static int PopCount(int v)
        {
            int c = 0;
            while (v != 0) { c += v & 1; v >>= 1; }
            return c;
        }
    }
}
