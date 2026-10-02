namespace Veil.Sim
{
    /// <summary>
    /// Every gameplay tunable lives here so balancing never requires hunting through systems.
    /// Numbers follow the GDD examples and are expected to change during playtesting.
    /// </summary>
    public static class GameConfig
    {
        public const int ProtocolVersion = 9;

        // ---- Simulation ----
        public const int TickRate = 30;
        public const float Dt = 1f / TickRate;
        public const int SnapshotEveryTicks = 2;           // 15 Hz snapshots on the server
        public const bool IslandMap = true;                // Rilo island (400 x 400 m, Sim/Map/IslandMap.cs); false = the old 150 m arena
        public const float MapHalf = IslandMap ? 200f : 75f;
        public const int MaxPlayers = 16;
        public const int SquadSize = 4;                    // party / squad size
        public const int SquadCount = 4;                   // 4 squads x 4 = 16 players

        // ---- Movement ----
        public const float PlayerRadius = 0.45f;
        public const float PlayerHeight = 1.7f;
        public const float RunSpeed = 6.2f;
        public const float SprintSpeed = 8.8f;
        public const float GroundAccel = 60f;
        public const float GroundDecel = 80f;              // stop quickly when input is released
        public const float GroundTurnAccel = 110f;         // snappy direction reversals
        public const float FallGravityMult = 1.4f;         // weightier fall
        public const float ShortHopGravityMult = 2.3f;     // release jump early = short hop
        public const float AirAccel = 22f;
        public const float JumpVelocity = 7.4f;
        public const float Gravity = 21f;                  // jump apex ≈ 1.3m → low walls (1.0m) are jumpable
        public const float DashSpeed = 25f;
        public const float DashDuration = 0.18f;
        public const float KnockbackDecay = 28f;

        // ---- Vitals ----
        public const float MaxHealth = 100f;
        public const float MaxEnergy = 100f;
        public const float StartEnergy = 50f;
        public const float HealthRegenDelay = 5f;
        public const float HealthRegenPerSec = 7f;
        public const float RespawnTime = 5f;
        public const float SpawnProtection = 2f;

        // ---- extraction mode (Rilo v2): Objective 1 → 2 → 3 → central Vault → Extraction, one winning squad ----
        public const bool ExtractionMode = true;      // false = the old score-only match
        // Hack Terminal (docs/HACK_TERMINAL.md): one shared terminal in the central plaza, every squad has its own progress
        public const float HackTime = 14f;             // base time for one hacker
        public const float HackRadius = 6.5f;          // interaction zone around the terminal (the plaza ring inside the low walls)
        public static readonly float[] HackSpeed = { 0f, 1f, 1.5f, 1.75f, 1.9f };   // by hackers in the zone: never linear
        public static readonly float[] HackInstability = { 0.35f, 0.67f };          // progress where the terminal destabilises
        public const int HackNodes = 3;                // nodes per instability: one Destroy, one Stabilize, one Override
        public const float HackNodeRadius = 0.9f;      // hit radius of a Destroy node
        public const int HackNodeHp = 4;               // hits to destroy
        public const float NodeStandRadius = 2.4f;     // stand this close for Stabilize / Override
        public const float StabilizeTime = 3f;
        public const float OverrideTime = 5f;          // Override slips back when nobody holds it
        public const float HackActivityCooldown = 12f; // "TERMINAL ACTIVITY DETECTED" at most this often per squad
        public const float PadCaptureTime = 12f;       // hold your squad's capture pad (more members = faster)
        public const float CaptureRadius = 5f;
        public const int CollectCores = 3;             // energy cores picked up by the squad
        public const float VaultTime = 10f;            // channel at the central Vault
        public const float VaultRadius = 6f;
        public const float ExtractTime = 60f;          // hold the extraction point uncontested
        public const float ExtractRadius = 7f;
        public const float ExtractDistance = IslandMap ? 160f : 40f;      // extraction points sit between the squads' spawns
        public const float ExtractRespawnTime = 15f;   // longer re-entry once extraction is revealed
        public const float ExtractCircleMin = IslandMap ? 172f : 52f;     // the collapse never closes over the extraction points
        public const int StagePoints = 100;            // per member, per completed stage (tie-breaks)
        public const float StageRevealRadius = 30f;    // completing a stage reveals nearby enemies...
        public const float StageRevealTime = 3f;       // ...for this long

        // ---- downed & revive (Rilo v2, Milestone 1) — switched off: eliminated players respawn at their squad's spawn ----
        public const bool DownedEnabled = false;
        public const float DownedHealth = 60f;        // "bleed HP": enemies must deal this much more to finish a downed player
        public const float DownedBleedTime = 25f;     // downed this long without a revive = eliminated
        public const float DownedSpeed = 1.4f;        // crawl speed
        public const float ReviveTime = 4f;           // a squadmate standing close (and not shooting) revives in this long
        public const float ReviveRadius = 2.3f;
        public const float ReviveHealth = 30f;
        public const int RevivePoints = 50;
        public const int AssistPoints = 30;
        public const float AssistWindow = 8f;         // damage within this many seconds before an elimination = assist
        public const float DeathEnergyKeep = 0.6f;

        // ---- Abilities (Q / E / R) ----
        public const float DashCost = 15f, DashCooldown = 3f;
        public const float PulseCost = 25f, PulseCooldown = 10f, PulseRadius = 24f, PulseRevealTime = 4f;
        public const float DecoyCost = 30f, DecoyCooldown = 14f, DecoyLifetime = 5f;

        // ---- Combat ----
        public const float FireCooldown = 0.3f;
        public const float ProjectileSpeed = 900f;          // ~instant: covers the full range in one 30 Hz tick
        public const float ProjectileRange = 30f;
        public const float ProjectileDamage = 11f;          // quicker fire rate, similar time-to-kill
        public const float ProjectileRadius = 0.22f;
        public const float ProjectileHeight = 1.0f;
        public const float HitRadius = 0.55f;
        public const float Knockback = 7f;
        public const float FireNoiseTime = 1.5f;           // firing pings you on nearby minimaps
        public const float FireNoiseRadius = 45f;
        public const float ExplorationDamageScale = 0.5f;  // combat is limited in phase 1

        // ---- Information ----
        public const float VisionRadius = 32f;
        public const float StealthRadius = 7f;             // inside Ruins you are hidden beyond this
        public const float VisionBlockHeight = 2.4f;

        // ---- Zones ----
        public const float CaptureTime = 6f;
        public const float TowerTerritoryPerSec = 0.6f;
        public const float ZoneTerritoryPerSec = 0.3f;
        public const float TowerRevealInterval = 12f;
        public const float TowerRevealDuration = 3f;
        public const float ReactorEnergyPerSec = 7f;
        public const float ReactorControllerBonus = 3f;
        public const float MarketDiscount = 0.6f;          // controller pays 60%

        // ---- Market (1 / 2 / 3) ----
        public const float MarketSpeedCost = 20f, SpeedBuffTime = 8f, SpeedBuffMult = 1.3f;
        public const float MarketShieldCost = 25f, ShieldAmount = 40f;
        public const float MarketKeyCost = 45f;
        public const float MarketBuyCooldown = 1f;

        // ---- Vault ----
        public const int VaultKeys = 3;
        public const float VaultChannelTime = 2.5f;
        public const float VaultCooldown = 30f;
        public const int VaultResourcePoints = 200;

        // ---- Pickups ----
        public const int MaxOrbs = 45;
        public const float OrbSpawnInterval = 0.9f;
        public const float OrbEnergy = 8f;
        public const int ActiveCores = 6;
        public const float CoreRespawn = 18f;
        public const float CoreEnergy = 15f;
        public const int ActiveKeys = 6;
        public const float KeyRespawn = 22f;
        public const int MaxKeysCarried = 5;
        public const float PickupRadius = 1.3f;

        // ---- Scoring (GDD §8) ----
        public const int PrimaryPoints = 500;
        public const int SecondaryPoints = 250;
        public const int EliminationPoints = 100;         // halves for each repeat on the same victim (min 25)
        public const int OrbPoints = 2;
        public const int CorePoints = 20;
        public const int KeyPoints = 10;
        public const int AbilityPlayPoints = 30;
        public const int SurvivalMax = 150;
        public const int SurvivalPenaltyPerDeath = 30;
        public const int FinalTowerBonus = 300;
        public const int SquadObjectivePoints = 400;      // split between the squad's members

        // ---- Objectives (scaled by match length) ----
        public const float TowerControlSeconds = 90f;
        public const int CoresNeeded = 5;
        public const float EnergyThreshold = 70f;
        public const int ZonesToCapture = 2;
        public const int NemesisEliminations = 2;
        public const float SquadTowerSeconds = 150f;       // squad objectives (shared progress)
        public const int SquadCoresNeeded = 12;
        public const int SquadZonesToCapture = 3;
        public const int SquadVaultsNeeded = 1;

        // ---- Collapse ----
        public const float CircleStartRadius = IslandMap ? 290f : 112f;
        public const float CircleCollapseEnd = IslandMap ? 110f : 34f;
        public const float CircleFinalRadius = IslandMap ? 60f : 16f;
        public const float OutsideDamagePerSec = 7f;

        // ---- Phase boundaries (fraction of match, GDD §5: 3/7/11/14 of 15 minutes) ----
        public const float PhaseCompetition = 3f / 15f;
        public const float PhaseManipulation = 7f / 15f;
        public const float PhaseCollapse = 11f / 15f;
        public const float PhaseFinal = 14f / 15f;
    }

    /// <summary>Per-match options chosen in the lobby.</summary>
    public sealed class MatchSettings
    {
        public int MatchSeconds = 900;
        public int TotalPlayers = GameConfig.MaxPlayers;
        public int Seed = 1;
        public bool FillWithBots = true;

        /// <summary>Objective targets scale down for short test matches.</summary>
        public float ObjectiveScale => MathUtil.Clamp(MatchSeconds / 900f, 0.4f, 1f);
    }
}
