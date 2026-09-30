using System;
using System.Collections.Generic;

namespace Veil.Sim
{
    public enum MatchPhase : byte { Exploration, Competition, Manipulation, Collapse, Final, Ended }

    public enum BotKind : byte { None, Explorer, Collector, Hunter, Defender, Opportunist }

    public enum PickupType : byte { Orb, Core, Key }

    public enum ObjectiveType : byte { TowerControl, CollectCores, VaultRaid, CaptureTwo, HighEnergy, Nemesis }

    [Flags]
    public enum Buttons : ushort
    {
        None = 0,
        Jump = 1 << 0,
        Sprint = 1 << 1,
        Fire = 1 << 2,
        Dash = 1 << 3,
        Pulse = 1 << 4,
        Decoy = 1 << 5,
        Buy1 = 1 << 6,
        Buy2 = 1 << 7,
        Buy3 = 1 << 8,
    }

    /// <summary>One tick of player intent. Move is world-space (already rotated by the camera).</summary>
    public struct InputCmd
    {
        public int Seq;
        public float MoveX, MoveY;
        public float Yaw;
        public Buttons Buttons;

        public bool Has(Buttons b) => (Buttons & b) != 0;
        public Vec2 Move => new Vec2(MoveX, MoveY).ClampLength(1f);

        /// <summary>Keeps held inputs, drops one-shot presses (used when repeating a lost input).</summary>
        public InputCmd HeldOnly()
        {
            var c = this;
            c.Buttons &= Buttons.Jump | Buttons.Sprint | Buttons.Fire;
            return c;
        }
    }

    /// <summary>Cosmetic only — no gameplay effect (GDD §23).</summary>
    [Serializable]
    public struct Appearance
    {
        public byte Outfit;     // jacket style / palette preset
        public byte Hair;       // hair style
        public byte HairColor;
        public byte Accessory;  // goggles, headphones, visor, hood...
        public byte Color;      // accent colour

        public static Appearance Preset(int i)
        {
            i = ((i % 6) + 6) % 6;
            switch (i)
            {
                case 0: return new Appearance { Outfit = 0, Hair = 4, HairColor = 0, Accessory = 0, Color = 5 }; // ranger
                case 1: return new Appearance { Outfit = 1, Hair = 4, HairColor = 0, Accessory = 0, Color = 1 }; // huntress
                case 2: return new Appearance { Outfit = 2, Hair = 4, HairColor = 0, Accessory = 0, Color = 4 }; // warden
                case 3: return new Appearance { Outfit = 3, Hair = 4, HairColor = 0, Accessory = 0, Color = 2 }; // scout
                case 4: return new Appearance { Outfit = 4, Hair = 0, HairColor = 0, Accessory = 0, Color = 0 }; // drifter
                default: return new Appearance { Outfit = 5, Hair = 1, HairColor = 0, Accessory = 0, Color = 3 }; // wanderer
            }
        }
    }

    public sealed class ScoreBreakdown
    {
        public int Primary, Secondary, Resources, Territory, Eliminations, Survival, Bonus, Squad;
        public float TerritoryAccum;

        public int Total => Primary + Secondary + Resources + Territory + Eliminations + Survival + Bonus + Squad;

        public void CopyFrom(ScoreBreakdown o)
        {
            Primary = o.Primary; Secondary = o.Secondary; Resources = o.Resources; Territory = o.Territory;
            Eliminations = o.Eliminations; Survival = o.Survival; Bonus = o.Bonus; Squad = o.Squad; TerritoryAccum = o.TerritoryAccum;
        }
    }

    public sealed class ObjectiveState
    {
        public ObjectiveType Type;
        public bool IsPrimary;
        public bool IsSquad;
        public float Progress;
        public float Target;
        public bool Done;
        public int TargetPlayer = -1;

        public float Fraction => Target <= 0 ? 0 : MathUtil.Clamp01(Progress / Target);

        public void CopyFrom(ObjectiveState o)
        {
            Type = o.Type; IsPrimary = o.IsPrimary; IsSquad = o.IsSquad; Progress = o.Progress; Target = o.Target; Done = o.Done; TargetPlayer = o.TargetPlayer;
        }

        public static string Title(ObjectiveType t)
        {
            switch (t)
            {
                case ObjectiveType.TowerControl: return "CONTROL TOWER";
                case ObjectiveType.CollectCores: return "COLLECT RESOURCES";
                case ObjectiveType.VaultRaid: return "REACH VAULT";
                case ObjectiveType.CaptureTwo: return "CAPTURE LOCATIONS";
                case ObjectiveType.HighEnergy: return "STAY CHARGED";
                default: return "PREVENT OTHERS";
            }
        }

        public string Describe(Func<int, string> nameOf)
        {
            if (IsSquad)
            {
                switch (Type)
                {
                    case ObjectiveType.TowerControl: return $"Squad: hold the Tower for {Target:0} seconds";
                    case ObjectiveType.CollectCores: return $"Squad: gather {Target:0} energy cores together";
                    case ObjectiveType.VaultRaid: return "Squad: unlock the Vault";
                    case ObjectiveType.CaptureTwo: return $"Squad: capture {Target:0} different locations";
                }
            }
            switch (Type)
            {
                case ObjectiveType.TowerControl: return $"Hold the Tower for {Target:0} seconds";
                case ObjectiveType.CollectCores: return $"Gather {Target:0} energy cores";
                case ObjectiveType.VaultRaid: return $"Find {GameConfig.VaultKeys} keys and unlock the Vault";
                case ObjectiveType.CaptureTwo: return $"Capture {Target:0} different locations";
                case ObjectiveType.HighEnergy: return $"Finish with at least {Target:0}% energy";
                default: return $"Stop {(nameOf != null ? nameOf(TargetPlayer) : "your target")} ({Target:0} eliminations)";
            }
        }
    }

    public sealed class PlayerState
    {
        // identity
        public int Id;
        public string Name = "Player";
        public bool IsBot;
        public BotKind BotKind;
        public Appearance Look;
        public bool Connected = true;
        public int Squad;

        // kinematics (predicted on the client — see Movement)
        public Vec2 Pos, Vel, Knock, DashDir;
        public float H, VH, Yaw;
        public bool Grounded = true;
        public float DashT;
        public float DashCd;
        public float SpeedBuffT;

        // vitals
        public float Health = GameConfig.MaxHealth;
        public float Shield;
        public float Energy = GameConfig.StartEnergy;
        public bool Alive = true;
        public float RespawnT;
        public float SpawnProtT;
        public float SinceDamage = 99f;
        public int LastAttacker = -1;

        // abilities & combat
        public float FireCd, PulseCd, DecoyCd, BuyCd;
        public byte FireSeq, CastSeq, HitSeq, JumpSeq;

        // information
        public readonly float[] RevealedTo = new float[GameConfig.MaxPlayers + 1]; // per-viewer pulse reveal timers
        public float PublicPingT;   // shown on everyone's minimap (Reactor)
        public float NoiseT;        // shown on nearby minimaps (firing)
        public float TowerSightT;   // this player sees everyone (Tower control)
        public float TowerPingTimer;

        // objectives & resources
        public int Keys;
        public int CoresCollected;
        public int ZoneId = -1;
        public float VaultChannel;
        public int CapturedMask;
        public float TowerControlTime;
        public int Deaths, Elims;
        public readonly int[] ElimsOn = new int[GameConfig.MaxPlayers + 1];
        public ObjectiveState Primary = new ObjectiveState();
        public ObjectiveState Secondary = new ObjectiveState();
        public readonly ScoreBreakdown Score = new ScoreBreakdown();

        // networking
        public int LastSeq;
        public InputCmd LastInput;
        public readonly Queue<InputCmd> PendingInputs = new Queue<InputCmd>();

        public bool Sprinting => LastInput.Has(Buttons.Sprint) && LastInput.Move.LengthSq > 0.04f && !LastInput.Has(Buttons.Fire);
        public float HealthFrac => Health / GameConfig.MaxHealth;

        /// <summary>Copies the fields that client prediction owns.</summary>
        public void CopyKinematicsFrom(PlayerState o)
        {
            Pos = o.Pos; Vel = o.Vel; Knock = o.Knock; DashDir = o.DashDir; H = o.H; VH = o.VH; Yaw = o.Yaw;
            Grounded = o.Grounded; DashT = o.DashT; DashCd = o.DashCd; SpeedBuffT = o.SpeedBuffT;
            Energy = o.Energy; Alive = o.Alive;
        }
    }

    public sealed class Projectile
    {
        public int Id;
        public int Owner;
        public Vec2 Pos, Vel;
        public float Travelled;
        public bool Dead;
    }

    public sealed class Decoy
    {
        public int AvatarId;
        public int Owner;
        public Vec2 Pos, Vel;
        public float Yaw;
        public float Life;
        public bool Dead;
    }

    public sealed class Pickup
    {
        public int Id;
        public PickupType Type;
        public Vec2 Pos;
        public int Spot = -1; // index into Key/Core spots, -1 = dropped/random
    }

    public sealed class ZoneState
    {
        public int Id;
        public int Controller = -1;     // player credited with the capture
        public int Capturer = -1;
        public int Squad = -1;          // controlling squad
        public int CapturerSquad = -1;
        public float Progress;
        public bool Contested;
        public int Occupants;
        // vault only
        public float Cooldown;
        public bool Locked = true;
    }

    public enum EventType : byte
    {
        PulseCast, DecoySpawn, DecoyPop, Hit, Eliminated, Respawned, PickupSpawned, PickupCollected,
        ZoneCaptured, VaultOpened, ObjectiveComplete, PhaseChanged, DashStart, Purchase, ShieldBreak,
        AbilityPlay, Fire, Revealed, MatchEnded, Land,
    }

    public struct SimEvent
    {
        public EventType Type;
        public int A;        // actor player / avatar
        public int B;        // target / subtype
        public int Value;
        public Vec2 Pos;

        public SimEvent(EventType t, int a, int b, int v, Vec2 pos) { Type = t; A = a; B = b; Value = v; Pos = pos; }
    }

    /// <summary>Shared squad progress. Only the squad itself can see its objective.</summary>
    public sealed class SquadState
    {
        public int Id;
        public readonly ObjectiveState Objective = new ObjectiveState { IsSquad = true };
        public float TowerTime;
        public int CapturedMask;
        public int Total;       // sum of members' scores (updated at match end / snapshots)
        public int Rank;
    }

    public sealed class PlayerResult
    {
        public int PlayerId;
        public string Name;
        public bool IsBot;
        public int Squad, SquadRank, SquadTotal, SquadPoints;
        public Appearance Look;
        public int Rank;
        public int Total;
        public int Primary, Secondary, Resources, Territory, Eliminations, Survival, Bonus;
        public int Elims, Deaths;
        public bool PrimaryDone, SecondaryDone;
        public ObjectiveType PrimaryType, SecondaryType;
    }
}
