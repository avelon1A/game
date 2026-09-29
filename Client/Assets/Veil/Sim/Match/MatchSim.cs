using System;
using System.Collections.Generic;

namespace Veil.Sim
{
    /// <summary>
    /// The authoritative VEIL match. Runs identically inside the Unity client (offline)
    /// and inside Veil.Server (online). Fixed 30 Hz tick.
    /// </summary>
    public sealed partial class MatchSim
    {
        public readonly MapData Map;
        public readonly MatchSettings Settings;
        public readonly Rng Rng;

        public readonly List<PlayerState> Players = new List<PlayerState>();
        public readonly List<Projectile> Projectiles = new List<Projectile>();
        public readonly List<Decoy> Decoys = new List<Decoy>();
        public readonly Dictionary<int, Pickup> Pickups = new Dictionary<int, Pickup>();
        public ZoneState[] Zones;
        public readonly List<BotBrain> Bots = new List<BotBrain>();

        /// <summary>Events produced during the last tick.</summary>
        public readonly List<SimEvent> Events = new List<SimEvent>();

        public int Tick { get; private set; }
        public float Time { get; private set; }
        public MatchPhase Phase { get; private set; } = MatchPhase.Exploration;
        public float CircleRadius { get; private set; } = GameConfig.CircleStartRadius;
        public bool Started { get; private set; }
        public bool Ended => Phase == MatchPhase.Ended;
        public List<PlayerResult> Results { get; private set; }

        public float Duration => Settings.MatchSeconds;
        public float TimeLeft => MathF.Max(0, Duration - Time);

        public readonly int TowerZone, VaultZone, ReactorZone, MarketZone, RuinsZone;

        private int _nextEntityId = 1;
        private int _nextDecoyAvatar = 1000;
        private float _orbTimer;
        private readonly List<(float t, PickupType type)> _respawns = new List<(float, PickupType)>();

        public MatchSim(MapData map, MatchSettings settings)
        {
            Map = map;
            Settings = settings;
            Rng = new Rng(settings.Seed);
            Zones = new ZoneState[map.Zones.Count];
            for (int i = 0; i < Zones.Length; i++) Zones[i] = new ZoneState { Id = i };
            TowerZone = map.Zone(ZoneType.Tower).Id;
            VaultZone = map.Zone(ZoneType.Vault).Id;
            ReactorZone = map.Zone(ZoneType.Reactor).Id;
            MarketZone = map.Zone(ZoneType.Market).Id;
            RuinsZone = map.Zone(ZoneType.Ruins).Id;
        }

        // ------------------------------------------------------------------ setup

        public PlayerState AddPlayer(string name, Appearance look, bool isBot, BotKind kind = BotKind.None)
        {
            if (Players.Count >= GameConfig.MaxPlayers) throw new InvalidOperationException("Match is full");
            var p = new PlayerState { Id = Players.Count, Name = name, Look = look, IsBot = isBot, BotKind = kind };
            Players.Add(p);
            if (isBot) Bots.Add(new BotBrain(this, p, Rng.Int(int.MaxValue)));
            return p;
        }

        public static readonly string[] BotNames =
        {
            "Kai", "Mira", "Juno", "Rex", "Nova", "Pixel", "Echo", "Zed", "Luna", "Bolt", "Ivy", "Orion", "Sage", "Tank", "Wisp", "Fable",
        };

        /// <summary>Adds bots until the match has TotalPlayers participants. Bot kinds rotate through all five behaviours.</summary>
        public void FillBots()
        {
            var kinds = new[] { BotKind.Explorer, BotKind.Collector, BotKind.Hunter, BotKind.Defender, BotKind.Opportunist };
            int i = 0;
            var used = new HashSet<string>();
            foreach (var p in Players) used.Add(p.Name);
            while (Players.Count < Math.Min(Settings.TotalPlayers, GameConfig.MaxPlayers))
            {
                string name = BotNames[(i + Settings.Seed) % BotNames.Length];
                if (used.Contains(name)) name += i;
                used.Add(name);
                var look = Appearance.Preset(Players.Count);
                look.Color = (byte)((Players.Count * 3) % 8);
                look.HairColor = (byte)((Players.Count * 5 + 1) % 8);
                AddPlayer(name, look, true, kinds[i % kinds.Length]);
                i++;
            }
        }

        public void Start()
        {
            if (Started) return;
            Started = true;

            // spawn points shuffled
            var spawns = new List<Vec2>(Map.SpawnPoints);
            for (int i = spawns.Count - 1; i > 0; i--) { int j = Rng.Int(i + 1); (spawns[i], spawns[j]) = (spawns[j], spawns[i]); }
            for (int i = 0; i < Players.Count; i++)
            {
                var p = Players[i];
                p.Pos = spawns[i % spawns.Count];
                p.Yaw = (-p.Pos).Yaw;
                p.SpawnProtT = GameConfig.SpawnProtection;
                AssignObjectives(p);
            }

            // pickups
            SpawnAtSpots(PickupType.Core, GameConfig.ActiveCores);
            SpawnAtSpots(PickupType.Key, GameConfig.ActiveKeys);
            for (int i = 0; i < 30; i++) SpawnOrb();

            Events.Add(new SimEvent(EventType.PhaseChanged, -1, (int)Phase, 0, Vec2.Zero));
        }

        public PlayerState Player(int id) => id >= 0 && id < Players.Count ? Players[id] : null;

        /// <summary>Queue an input from a human (server) — bots generate their own.</summary>
        public void SubmitInput(int playerId, InputCmd cmd)
        {
            var p = Player(playerId);
            if (p == null || cmd.Seq <= p.LastSeq) return;
            foreach (var q in p.PendingInputs) if (q.Seq >= cmd.Seq) return;
            p.PendingInputs.Enqueue(cmd);
            while (p.PendingInputs.Count > 6) p.PendingInputs.Dequeue();
        }

        // ------------------------------------------------------------------ tick

        public void Step()
        {
            if (!Started || Ended) return;
            float dt = GameConfig.Dt;
            Events.Clear();
            Tick++;
            Time += dt;

            UpdatePhase();

            foreach (var b in Bots) b.Update(dt);

            foreach (var p in Players)
            {
                InputCmd cmd;
                if (p.PendingInputs.Count > 0) cmd = p.PendingInputs.Dequeue();
                else { cmd = p.LastInput.HeldOnly(); cmd.Seq = p.LastSeq; }
                p.LastSeq = cmd.Seq;
                p.LastInput = cmd;

                UpdateTimers(p, dt);
                if (!p.Alive) { UpdateRespawn(p, dt); continue; }

                HandleActions(p, cmd);
                Movement.Step(p, cmd, Map, dt, Events);
                p.ZoneId = Map.ZoneAt(p.Pos);
            }

            UpdateProjectiles(dt);
            UpdateDecoys(dt);
            UpdatePickups(dt);
            UpdateZones(dt);
            UpdateCollapse(dt);
            UpdateObjectives();

            if (Time >= Duration) EndMatch();
        }

        private void UpdateTimers(PlayerState p, float dt)
        {
            p.FireCd = MathF.Max(0, p.FireCd - dt);
            p.PulseCd = MathF.Max(0, p.PulseCd - dt);
            p.DecoyCd = MathF.Max(0, p.DecoyCd - dt);
            p.BuyCd = MathF.Max(0, p.BuyCd - dt);
            p.SpawnProtT = MathF.Max(0, p.SpawnProtT - dt);
            p.NoiseT = MathF.Max(0, p.NoiseT - dt);
            p.PublicPingT = MathF.Max(0, p.PublicPingT - dt);
            p.TowerSightT = MathF.Max(0, p.TowerSightT - dt);
            for (int i = 0; i < p.RevealedTo.Length; i++)
                if (p.RevealedTo[i] > 0) p.RevealedTo[i] = MathF.Max(0, p.RevealedTo[i] - dt);
            p.SinceDamage += dt;
            if (p.Alive && p.SinceDamage > GameConfig.HealthRegenDelay && p.Health < GameConfig.MaxHealth && p.Pos.Length <= CircleRadius)
                p.Health = MathF.Min(GameConfig.MaxHealth, p.Health + GameConfig.HealthRegenPerSec * dt);
        }

        // ------------------------------------------------------------------ phases

        public static MatchPhase PhaseAt(float frac)
        {
            if (frac >= 1f) return MatchPhase.Ended;
            if (frac >= GameConfig.PhaseFinal) return MatchPhase.Final;
            if (frac >= GameConfig.PhaseCollapse) return MatchPhase.Collapse;
            if (frac >= GameConfig.PhaseManipulation) return MatchPhase.Manipulation;
            if (frac >= GameConfig.PhaseCompetition) return MatchPhase.Competition;
            return MatchPhase.Exploration;
        }

        public float PhaseStartTime(MatchPhase ph)
        {
            switch (ph)
            {
                case MatchPhase.Competition: return GameConfig.PhaseCompetition * Duration;
                case MatchPhase.Manipulation: return GameConfig.PhaseManipulation * Duration;
                case MatchPhase.Collapse: return GameConfig.PhaseCollapse * Duration;
                case MatchPhase.Final: return GameConfig.PhaseFinal * Duration;
                case MatchPhase.Ended: return Duration;
                default: return 0;
            }
        }

        private void UpdatePhase()
        {
            var ph = PhaseAt(Time / Duration);
            if (ph == MatchPhase.Ended) ph = MatchPhase.Final;
            if (ph != Phase)
            {
                Phase = ph;
                Events.Add(new SimEvent(EventType.PhaseChanged, -1, (int)ph, 0, Vec2.Zero));
            }

            float cs = PhaseStartTime(MatchPhase.Collapse), fs = PhaseStartTime(MatchPhase.Final);
            if (Time < cs) CircleRadius = GameConfig.CircleStartRadius;
            else if (Time < fs) CircleRadius = MathUtil.Lerp(GameConfig.CircleStartRadius, GameConfig.CircleCollapseEnd, (Time - cs) / (fs - cs));
            else CircleRadius = MathUtil.Lerp(GameConfig.CircleCollapseEnd, GameConfig.CircleFinalRadius, MathUtil.Clamp01((Time - fs) / (Duration - fs)));
        }

        private void UpdateCollapse(float dt)
        {
            if (CircleRadius >= GameConfig.CircleStartRadius - 0.01f) return;
            foreach (var p in Players)
            {
                if (!p.Alive || p.Pos.Length <= CircleRadius) continue;
                ApplyEnvironmentDamage(p, GameConfig.OutsideDamagePerSec * dt);
            }
        }

        // ------------------------------------------------------------------ end

        private void EndMatch()
        {
            if (Ended) return;
            // Final objective: whoever controls the Tower when time runs out.
            var tower = Zones[TowerZone];
            if (tower.Controller >= 0)
            {
                Players[tower.Controller].Score.Bonus += GameConfig.FinalTowerBonus;
                Events.Add(new SimEvent(EventType.AbilityPlay, tower.Controller, 99, GameConfig.FinalTowerBonus, Map.Zones[TowerZone].Center));
            }

            foreach (var p in Players)
            {
                CheckEndObjective(p, p.Primary);
                CheckEndObjective(p, p.Secondary);
                p.Score.Survival = Math.Max(0, GameConfig.SurvivalMax - p.Deaths * GameConfig.SurvivalPenaltyPerDeath);
                if (!p.Alive) p.Score.Survival = Math.Max(0, p.Score.Survival - 30);
            }

            var list = new List<PlayerResult>();
            foreach (var p in Players)
            {
                list.Add(new PlayerResult
                {
                    PlayerId = p.Id, Name = p.Name, IsBot = p.IsBot, Look = p.Look,
                    Total = p.Score.Total, Primary = p.Score.Primary, Secondary = p.Score.Secondary,
                    Resources = p.Score.Resources, Territory = p.Score.Territory, Eliminations = p.Score.Eliminations,
                    Survival = p.Score.Survival, Bonus = p.Score.Bonus, Elims = p.Elims, Deaths = p.Deaths,
                    PrimaryDone = p.Primary.Done, SecondaryDone = p.Secondary.Done,
                    PrimaryType = p.Primary.Type, SecondaryType = p.Secondary.Type,
                });
            }
            list.Sort((a, b) => b.Total != a.Total ? b.Total.CompareTo(a.Total) : b.Elims.CompareTo(a.Elims));
            for (int i = 0; i < list.Count; i++) list[i].Rank = i + 1;
            Results = list;
            Phase = MatchPhase.Ended;
            Events.Add(new SimEvent(EventType.MatchEnded, -1, 0, 0, Vec2.Zero));
        }

        /// <summary>Test helper: fast-forward the clock (used by the autotest and the server admin).</summary>
        public void DebugSkipTime(float seconds) => Time = MathF.Min(Duration - GameConfig.Dt, Time + seconds);

        internal int NextEntityId() => _nextEntityId++;
    }
}
