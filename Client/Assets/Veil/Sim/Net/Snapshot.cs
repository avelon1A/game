using System.Collections.Generic;

namespace Veil.Sim
{
    [System.Flags]
    public enum AvatarFlags : byte
    {
        None = 0,
        Sprinting = 1,
        Dashing = 2,
        Grounded = 4,
        MyDecoy = 8,
        Shield = 16,
        SpawnProtected = 32,
        Stealthed = 64,
        Ally = 128,          // squadmate (or a squadmate's decoy)
    }

    public static class AvatarState
    {
        public const byte Downed = 1, Reviving = 2;
    }

    /// <summary>A visible character. Decoys are sent exactly like players (OwnerId = the player they imitate).</summary>
    public sealed class AvatarSnap
    {
        public int AvatarId;
        public int OwnerId;
        public byte Vis;
        public Vec2 Pos, Vel;
        public float H, Yaw, Health01;
        public AvatarFlags Flags;
        public byte FireSeq, CastSeq, HitSeq, JumpSeq;
        public byte State;          // AvatarState bits
        public float ReviveProg;    // 0..1 while downed and being revived

        public bool Downed => (State & AvatarState.Downed) != 0;
        public bool Reviving => (State & AvatarState.Reviving) != 0;

        public AvatarSnap Clone() => (AvatarSnap)MemberwiseClone();
    }

    public sealed class ProjectileSnap
    {
        public int Id, Owner;
        public Vec2 Pos, Vel;
    }

    public sealed class ZoneSnap
    {
        public int Controller = -1, Capturer = -1, Squad = -1, CapturerSquad = -1;
        public float Progress;
        public bool Contested, Locked;
        public int Occupants;
        public float Cooldown;
    }

    /// <summary>Everything one viewer is allowed to know at one tick.</summary>
    public sealed class Snapshot
    {
        public int Tick;
        public float Time, Duration;
        public MatchPhase Phase;
        public float Circle;
        public int AliveCount, PlayerCount;
        public readonly PlayerState Self = new PlayerState();
        public readonly List<AvatarSnap> Avatars = new List<AvatarSnap>();
        public readonly List<ProjectileSnap> Projectiles = new List<ProjectileSnap>();
        public ZoneSnap[] Zones = new ZoneSnap[0];
        public readonly ObjectiveState SquadObjective = new ObjectiveState { IsSquad = true };
        public int SquadTotal;

        // extraction mode: own chain + public extraction state + every squad's stage (enemy progress is public info)
        public int Stage;
        public float StageProg;
        public Vec2 Site;
        public bool ExtractRevealed, ExtractContested;
        public Vec2 ExtractPos;
        public int ExtractController = -1, Winner = -1;
        public readonly byte[] SquadStage = new byte[GameConfig.SquadCount];
        public readonly float[] SquadExtract = new float[GameConfig.SquadCount];
        public ChainTask Task => MatchSim.TaskOf(Stage);
        public readonly List<HackNode> Nodes = new List<HackNode>();   // own squad's stabilization nodes
        public int Hackers;
        public bool HackContested;
        public readonly float[] SquadProg = new float[GameConfig.SquadCount];   // every squad's progress on its current step (public)
    }

    public sealed class RosterEntry
    {
        public int Id;
        public string Name;
        public Appearance Look;
        public bool IsBot;
        public int Squad;
        /// <summary>Backend profile id — only sent for your own squad (voice indicators, party mapping).</summary>
        public string ProfileId = "";
    }

    public static class SnapshotBuilder
    {
        /// <summary>Copies the fields a player needs about themself.</summary>
        public static void CopySelf(PlayerState s, PlayerState d)
        {
            d.Id = s.Id; d.Name = s.Name; d.IsBot = s.IsBot; d.Look = s.Look; d.Squad = s.Squad;
            d.CopyKinematicsFrom(s);
            d.Health = s.Health; d.Shield = s.Shield; d.RespawnT = s.RespawnT; d.SpawnProtT = s.SpawnProtT;
            d.FireCd = s.FireCd; d.PulseCd = s.PulseCd; d.DecoyCd = s.DecoyCd; d.BuyCd = s.BuyCd;
            d.FireSeq = s.FireSeq; d.CastSeq = s.CastSeq; d.HitSeq = s.HitSeq; d.JumpSeq = s.JumpSeq;
            d.TowerSightT = s.TowerSightT; d.PublicPingT = s.PublicPingT; d.NoiseT = s.NoiseT;
            d.Keys = s.Keys; d.CoresCollected = s.CoresCollected; d.ZoneId = s.ZoneId; d.VaultChannel = s.VaultChannel;
            d.CapturedMask = s.CapturedMask; d.TowerControlTime = s.TowerControlTime; d.Deaths = s.Deaths; d.Elims = s.Elims;
            d.BleedT = s.BleedT; d.ReviveProg = s.ReviveProg; d.Reviving = s.Reviving; d.Revives = s.Revives; d.Assists = s.Assists; d.DownedBy = s.DownedBy;
            d.Primary.CopyFrom(s.Primary); d.Secondary.CopyFrom(s.Secondary); d.Score.CopyFrom(s.Score);
            d.LastSeq = s.LastSeq; d.LastInput = s.LastInput;
        }

        public static void Build(MatchSim sim, PlayerState viewer, Snapshot snap)
        {
            snap.Tick = sim.Tick;
            snap.Time = sim.Time;
            snap.Duration = sim.Duration;
            snap.Phase = sim.Phase;
            snap.Circle = sim.CircleRadius;
            snap.PlayerCount = sim.Players.Count;
            int alive = 0;
            foreach (var p in sim.Players) if (p.Alive) alive++;
            snap.AliveCount = alive;
            CopySelf(viewer, snap.Self);
            var squad = sim.Squads[viewer.Squad];
            snap.SquadObjective.CopyFrom(squad.Objective);
            snap.SquadTotal = squad.Total;

            snap.Avatars.Clear();
            foreach (var p in sim.Players)
            {
                if (p == viewer) continue;
                byte vis = Visibility.OfPlayer(sim, viewer, p);
                if (vis == Visibility.None) continue;
                var a = new AvatarSnap
                {
                    AvatarId = p.Id, OwnerId = p.Id, Vis = vis, Pos = p.Pos, Vel = p.Vel, H = p.H, Yaw = p.Yaw,
                    Health01 = p.HealthFrac, FireSeq = p.FireSeq, CastSeq = p.CastSeq, HitSeq = p.HitSeq, JumpSeq = p.JumpSeq,
                };
                if (p.Sprinting) a.Flags |= AvatarFlags.Sprinting;
                if (p.DashT > 0) a.Flags |= AvatarFlags.Dashing;
                if (p.Grounded) a.Flags |= AvatarFlags.Grounded;
                if (p.Shield > 0) a.Flags |= AvatarFlags.Shield;
                if (p.SpawnProtT > 0) a.Flags |= AvatarFlags.SpawnProtected;
                if (p.ZoneId == sim.RuinsZone) a.Flags |= AvatarFlags.Stealthed;
                if (p.Squad == viewer.Squad) a.Flags |= AvatarFlags.Ally;
                if (p.Downed) { a.State |= AvatarState.Downed; a.ReviveProg = p.ReviveProg; }
                if (p.Reviving >= 0) a.State |= AvatarState.Reviving;
                snap.Avatars.Add(a);
            }
            foreach (var d in sim.Decoys)
            {
                byte vis = Visibility.OfDecoy(sim, viewer, d);
                if (vis == Visibility.None) continue;
                var owner = sim.Players[d.Owner];
                var a = new AvatarSnap
                {
                    AvatarId = d.AvatarId, OwnerId = d.Owner, Vis = vis, Pos = d.Pos, Vel = d.Vel, H = 0, Yaw = d.Yaw,
                    Health01 = owner.HealthFrac, Flags = AvatarFlags.Grounded,
                };
                if (owner.Shield > 0) a.Flags |= AvatarFlags.Shield;
                if (d.Owner == viewer.Id) a.Flags |= AvatarFlags.MyDecoy;
                if (owner.Squad == viewer.Squad) a.Flags |= AvatarFlags.Ally;
                if (sim.Map.ZoneAt(d.Pos) == sim.RuinsZone) a.Flags |= AvatarFlags.Stealthed;
                snap.Avatars.Add(a);
            }

            snap.Stage = squad.Stage; snap.StageProg = squad.StageProg; snap.Site = squad.Site;
            snap.Nodes.Clear();
            foreach (var n in squad.Nodes) snap.Nodes.Add(new HackNode { Pos = n.Pos, Kind = n.Kind, Prog = n.Prog, Hp = n.Hp, Contested = n.Contested });
            snap.Hackers = squad.Hackers; snap.HackContested = squad.Contested;
            for (int i = 0; i < GameConfig.SquadCount; i++) snap.SquadProg[i] = sim.Squads[i].StageProg;
            snap.ExtractRevealed = sim.ExtractRevealed; snap.ExtractContested = sim.ExtractContested; snap.ExtractPos = sim.ExtractPos;
            snap.ExtractController = sim.ExtractController; snap.Winner = sim.WinnerSquad;
            for (int i = 0; i < GameConfig.SquadCount; i++) { snap.SquadStage[i] = (byte)sim.Squads[i].Stage; snap.SquadExtract[i] = sim.Squads[i].ExtractProg; }

            snap.Projectiles.Clear();
            float pr = GameConfig.VisionRadius + 6f;
            foreach (var p in sim.Projectiles)
            {
                if (p.Owner != viewer.Id && !NearSquad(sim, viewer, p.Pos, pr)) continue;
                snap.Projectiles.Add(new ProjectileSnap { Id = p.Id, Owner = p.Owner, Pos = p.Pos, Vel = p.Vel });
            }

            if (snap.Zones.Length != sim.Zones.Length)
            {
                snap.Zones = new ZoneSnap[sim.Zones.Length];
                for (int i = 0; i < snap.Zones.Length; i++) snap.Zones[i] = new ZoneSnap();
            }
            for (int i = 0; i < sim.Zones.Length; i++)
            {
                var z = sim.Zones[i];
                var s = snap.Zones[i];
                s.Controller = z.Controller; s.Capturer = z.Capturer; s.Squad = z.Squad; s.CapturerSquad = z.CapturerSquad;
                s.Progress = z.Progress; s.Contested = z.Contested;
                s.Occupants = z.Occupants; s.Locked = z.Locked; s.Cooldown = z.Cooldown;
            }
        }

        /// <summary>Hidden-information filter for events. Returns false if this viewer must not learn about it.</summary>
        public static bool FilterEvent(MatchSim sim, PlayerState viewer, ref SimEvent e)
        {
            switch (e.Type)
            {
                case EventType.PhaseChanged:
                case EventType.ZoneCaptured:
                case EventType.VaultOpened:
                case EventType.Eliminated:
                case EventType.MatchEnded:
                case EventType.PickupSpawned:
                case EventType.PickupCollected:
                    return true;
                case EventType.ExtractRevealed:
                case EventType.ExtractControl:
                    return true;
                case EventType.HackGlitch:
                case EventType.HackContested:
                    return e.A == viewer.Squad;
                case EventType.HackActivity:
                    return e.A != viewer.Squad;     // everyone else hears "terminal activity detected"
                case EventType.NodeDestroyed:
                    return e.A >= 0 ? Ally(sim, viewer, e.A) : -1 - e.A == viewer.Squad;
                case EventType.StageComplete:
                    if (viewer.Squad != e.A) e.Pos = Vec2.Zero;   // rivals learn the progress, not where their site is
                    return true;
                case EventType.Downed:
                case EventType.Revived:
                    return Ally(sim, viewer, e.B) || e.A == viewer.Id || NearSquad(sim, viewer, e.Pos, GameConfig.VisionRadius + 5f);
                case EventType.ObjectiveComplete:
                    if (e.B == 2) return Ally(sim, viewer, e.A);   // squad objective: the whole squad
                    return e.A == viewer.Id;
                case EventType.AbilityPlay:
                case EventType.Purchase:
                    return e.A == viewer.Id;
                case EventType.Revealed:
                    return Ally(sim, viewer, e.A) || e.B == viewer.Id;
                case EventType.DecoySpawn:
                    if (Ally(sim, viewer, e.B)) return true;
                    if (!NearSquad(sim, viewer, e.Pos, GameConfig.VisionRadius + 5f)) return false;
                    e.A = -1; e.B = -1; // enemies only see a puff, not which avatar is fake
                    return true;
                case EventType.DecoyPop:
                    if (Ally(sim, viewer, e.B)) return true;
                    return NearSquad(sim, viewer, e.Pos, GameConfig.VisionRadius + 5f);
                default:
                    return e.A == viewer.Id || e.B == viewer.Id || NearSquad(sim, viewer, e.Pos, GameConfig.VisionRadius + 5f);
            }
        }

        private static bool Ally(MatchSim sim, PlayerState viewer, int playerId)
        {
            var p = sim.Player(playerId);
            return p != null && p.Squad == viewer.Squad;
        }

        /// <summary>Within range of the viewer or any squadmate (shared squad awareness).</summary>
        private static bool NearSquad(MatchSim sim, PlayerState viewer, Vec2 p, float r)
        {
            float r2 = r * r;
            foreach (var m in sim.Players)
                if (m.Squad == viewer.Squad && Vec2.DistSq(m.Pos, p) <= r2) return true;
            return false;
        }
    }
}
