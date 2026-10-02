using System;
using System.Collections.Generic;

namespace Veil.Sim
{
    public enum Msg : byte
    {
        // client → server
        Hello = 1, Input = 2, Ready = 3, Leave = 4,
        // server → client
        Welcome = 10, Lobby = 11, MatchStart = 12, Snapshot = 13, Events = 14, MatchEnd = 15, Reject = 16,
    }

    public enum LobbyStatus : byte { Waiting, Countdown, InMatch, Results }

    public sealed class HelloMsg
    {
        public int Protocol = GameConfig.ProtocolVersion;
        public string Name;
        public Appearance Look;
        public string ProfileId = "";
        /// <summary>Signed match ticket from the Gateway (empty = legacy/LAN quick join).</summary>
        public string Ticket = "";
    }

    public sealed class LobbyEntry
    {
        public int ClientId;
        public string Name;
        public Appearance Look;
        public bool Ready;
        public bool IsHost;
    }

    public sealed class LobbyMsg
    {
        public LobbyStatus Status;
        public float Countdown;
        public int MatchSeconds;
        public int TotalPlayers;
        public string ServerName = "";
        public readonly List<LobbyEntry> Entries = new List<LobbyEntry>();
    }

    public sealed class MatchStartMsg
    {
        public int Seed;
        public int MatchSeconds;
        public int YourPlayerId;
        public readonly List<RosterEntry> Roster = new List<RosterEntry>();
    }

    /// <summary>Encoding/decoding for every message. Shared by client and server.</summary>
    public static class Protocol
    {
        // ---------------- helpers ----------------
        private static void Look(ByteWriter w, Appearance a) { w.U8(a.Outfit); w.U8(a.Hair); w.U8(a.HairColor); w.U8(a.Accessory); w.U8(a.Color); }
        private static Appearance Look(ByteReader r) => new Appearance { Outfit = r.U8(), Hair = r.U8(), HairColor = r.U8(), Accessory = r.U8(), Color = r.U8() };

        // ---------------- Hello / Welcome / Reject ----------------
        public static void WriteHello(ByteWriter w, HelloMsg m)
        {
            w.U8((byte)Msg.Hello); w.I32(m.Protocol); w.Str(m.Name); Look(w, m.Look); w.Str(m.ProfileId); w.Str(m.Ticket ?? "");
        }

        public static HelloMsg ReadHello(ByteReader r)
        {
            var m = new HelloMsg { Protocol = r.I32() };
            if (m.Protocol != GameConfig.ProtocolVersion) return m;   // old clients: stop before fields they don't send
            m.Name = r.Str(); m.Look = Look(r); m.ProfileId = r.Str(); m.Ticket = r.Str();
            return m;
        }

        public static void WriteWelcome(ByteWriter w, int clientId, string serverName) { w.U8((byte)Msg.Welcome); w.I32(clientId); w.Str(serverName); }
        public static void WriteReject(ByteWriter w, string reason) { w.U8((byte)Msg.Reject); w.Str(reason); }

        // ---------------- Ready ----------------
        public static void WriteReady(ByteWriter w, bool ready, int preferredSeconds)
        {
            w.U8((byte)Msg.Ready); w.Bool(ready); w.I32(preferredSeconds);
        }

        // ---------------- Lobby ----------------
        public static void WriteLobby(ByteWriter w, LobbyMsg m)
        {
            w.U8((byte)Msg.Lobby); w.U8((byte)m.Status); w.F32(m.Countdown); w.I32(m.MatchSeconds); w.U8((byte)m.TotalPlayers);
            w.Str(m.ServerName);
            w.U8((byte)m.Entries.Count);
            foreach (var e in m.Entries) { w.I32(e.ClientId); w.Str(e.Name); Look(w, e.Look); w.Bool(e.Ready); w.Bool(e.IsHost); }
        }

        public static LobbyMsg ReadLobby(ByteReader r)
        {
            var m = new LobbyMsg { Status = (LobbyStatus)r.U8(), Countdown = r.F32(), MatchSeconds = r.I32(), TotalPlayers = r.U8(), ServerName = r.Str() };
            int n = r.U8();
            for (int i = 0; i < n; i++)
                m.Entries.Add(new LobbyEntry { ClientId = r.I32(), Name = r.Str(), Look = Look(r), Ready = r.Bool(), IsHost = r.Bool() });
            return m;
        }

        // ---------------- Match start ----------------
        public static void WriteMatchStart(ByteWriter w, MatchStartMsg m)
        {
            w.U8((byte)Msg.MatchStart); w.I32(m.Seed); w.I32(m.MatchSeconds); w.U8((byte)m.YourPlayerId);
            w.U8((byte)m.Roster.Count);
            foreach (var e in m.Roster) { w.U8((byte)e.Id); w.Str(e.Name); Look(w, e.Look); w.Bool(e.IsBot); w.U8((byte)e.Squad); w.Str(e.ProfileId ?? ""); }
        }

        public static MatchStartMsg ReadMatchStart(ByteReader r)
        {
            var m = new MatchStartMsg { Seed = r.I32(), MatchSeconds = r.I32(), YourPlayerId = r.U8() };
            int n = r.U8();
            for (int i = 0; i < n; i++) m.Roster.Add(new RosterEntry { Id = r.U8(), Name = r.Str(), Look = Look(r), IsBot = r.Bool(), Squad = r.U8(), ProfileId = r.Str() });
            return m;
        }

        // ---------------- Input (redundant: last N commands) ----------------
        public static void WriteInputs(ByteWriter w, IList<InputCmd> cmds, int start, int count)
        {
            w.U8((byte)Msg.Input);
            w.U8((byte)count);
            for (int i = start; i < start + count; i++)
            {
                var c = cmds[i];
                w.I32(c.Seq);
                w.U8((byte)(sbyte)MathF.Round(MathUtil.Clamp(c.MoveX, -1, 1) * 127f));
                w.U8((byte)(sbyte)MathF.Round(MathUtil.Clamp(c.MoveY, -1, 1) * 127f));
                w.Angle(c.Yaw);
                w.U16((ushort)c.Buttons);
            }
        }

        public static void ReadInputs(ByteReader r, List<InputCmd> output)
        {
            output.Clear();
            int n = r.U8();
            for (int i = 0; i < n; i++)
            {
                var c = new InputCmd { Seq = r.I32() };
                c.MoveX = (sbyte)r.U8() / 127f;
                c.MoveY = (sbyte)r.U8() / 127f;
                c.Yaw = r.Angle();
                c.Buttons = (Buttons)r.U16();
                output.Add(c);
            }
        }

        /// <summary>Quantises an input exactly like the wire does, so prediction uses the same values as the server.</summary>
        public static InputCmd Quantize(InputCmd c)
        {
            c.MoveX = (sbyte)MathF.Round(MathUtil.Clamp(c.MoveX, -1, 1) * 127f) / 127f;
            c.MoveY = (sbyte)MathF.Round(MathUtil.Clamp(c.MoveY, -1, 1) * 127f) / 127f;
            c.Yaw = (ushort)(((c.Yaw % 360f) + 360f) % 360f / 360f * 65535f) / 65535f * 360f;
            return c;
        }

        // ---------------- Snapshot ----------------
        public static void WriteSnapshot(ByteWriter w, Snapshot s)
        {
            w.U8((byte)Msg.Snapshot);
            w.I32(s.Tick); w.F32(s.Time); w.F32(s.Duration); w.U8((byte)s.Phase); w.F32(s.Circle);
            w.U8((byte)s.AliveCount); w.U8((byte)s.PlayerCount);

            var p = s.Self;
            w.U8((byte)p.Id); w.U8((byte)p.Squad); w.I32(p.LastSeq);
            w.F32(p.Pos.X); w.F32(p.Pos.Y); w.F32(p.Vel.X); w.F32(p.Vel.Y); w.F32(p.Knock.X); w.F32(p.Knock.Y);
            w.F32(p.DashDir.X); w.F32(p.DashDir.Y); w.F32(p.H); w.F32(p.VH); w.F32(p.Yaw);
            w.Bool(p.Grounded); w.F32(p.DashT); w.F32(p.DashCd); w.F32(p.SpeedBuffT);
            w.F32(p.Health); w.F32(p.Shield); w.F32(p.Energy); w.Bool(p.Alive); w.Time(p.RespawnT); w.Time(p.SpawnProtT);
            w.Time(p.FireCd); w.Time(p.PulseCd); w.Time(p.DecoyCd); w.Time(p.BuyCd);
            w.U8(p.FireSeq); w.U8(p.CastSeq); w.U8(p.HitSeq); w.U8(p.JumpSeq);
            w.Time(p.TowerSightT); w.Time(p.PublicPingT); w.Time(p.NoiseT);
            w.U8((byte)p.Keys); w.U8((byte)p.CoresCollected); w.U8((byte)(p.ZoneId + 1)); w.Time(p.VaultChannel);
            w.I32(p.CapturedMask); w.F32(p.TowerControlTime); w.U8((byte)p.Deaths); w.U8((byte)p.Elims);
            w.Bool(p.Downed); w.Time(p.BleedT); w.Unit(p.ReviveProg, 1f); w.U8((byte)(p.Reviving + 1)); w.U8((byte)p.Revives); w.U8((byte)p.Assists); w.U8((byte)(p.DownedBy + 1));
            WriteObjective(w, p.Primary); WriteObjective(w, p.Secondary);
            var sc = p.Score;
            w.I32(sc.Primary); w.I32(sc.Secondary); w.I32(sc.Resources); w.I32(sc.Territory); w.I32(sc.Eliminations); w.I32(sc.Survival); w.I32(sc.Bonus); w.I32(sc.Squad);
            WriteObjective(w, s.SquadObjective); w.I32(s.SquadTotal);

            w.U8((byte)s.Avatars.Count);
            foreach (var a in s.Avatars)
            {
                w.I16((short)a.AvatarId); w.U8((byte)a.OwnerId); w.U8(a.Vis);
                w.Vec(a.Pos); w.VelQ(a.Vel); w.Pos(a.H); w.Angle(a.Yaw); w.Unit(a.Health01, 1f); w.U8((byte)a.Flags);
                w.U8(a.FireSeq); w.U8(a.CastSeq); w.U8(a.HitSeq); w.U8(a.JumpSeq);
                w.U8(a.State); w.Unit(a.ReviveProg, 1f);
            }

            w.U8((byte)Math.Min(s.Projectiles.Count, 255));
            for (int i = 0; i < Math.Min(s.Projectiles.Count, 255); i++)
            {
                var pr = s.Projectiles[i];
                w.I32(pr.Id); w.U8((byte)pr.Owner); w.Vec(pr.Pos); w.VelQ(pr.Vel);
            }

            w.U8((byte)s.Zones.Length);
            foreach (var z in s.Zones)
            {
                w.U8((byte)(z.Controller + 1)); w.U8((byte)(z.Capturer + 1)); w.U8((byte)(z.Squad + 1)); w.U8((byte)(z.CapturerSquad + 1)); w.Unit(z.Progress, 1f);
                w.Bool(z.Contested); w.Bool(z.Locked); w.U8((byte)z.Occupants); w.Time(z.Cooldown);
            }
        }

        private static void WriteObjective(ByteWriter w, ObjectiveState o)
        {
            w.U8((byte)o.Type); w.Bool(o.IsPrimary); w.Bool(o.IsSquad); w.F32(o.Progress); w.F32(o.Target); w.Bool(o.Done); w.U8((byte)(o.TargetPlayer + 1));
        }

        private static void ReadObjective(ByteReader r, ObjectiveState o)
        {
            o.Type = (ObjectiveType)r.U8(); o.IsPrimary = r.Bool(); o.IsSquad = r.Bool(); o.Progress = r.F32(); o.Target = r.F32(); o.Done = r.Bool(); o.TargetPlayer = r.U8() - 1;
        }

        public static Snapshot ReadSnapshot(ByteReader r)
        {
            var s = new Snapshot
            {
                Tick = r.I32(), Time = r.F32(), Duration = r.F32(), Phase = (MatchPhase)r.U8(), Circle = r.F32(),
                AliveCount = r.U8(), PlayerCount = r.U8(),
            };
            var p = s.Self;
            p.Id = r.U8(); p.Squad = r.U8(); p.LastSeq = r.I32();
            p.Pos = new Vec2(r.F32(), r.F32()); p.Vel = new Vec2(r.F32(), r.F32()); p.Knock = new Vec2(r.F32(), r.F32());
            p.DashDir = new Vec2(r.F32(), r.F32()); p.H = r.F32(); p.VH = r.F32(); p.Yaw = r.F32();
            p.Grounded = r.Bool(); p.DashT = r.F32(); p.DashCd = r.F32(); p.SpeedBuffT = r.F32();
            p.Health = r.F32(); p.Shield = r.F32(); p.Energy = r.F32(); p.Alive = r.Bool(); p.RespawnT = r.Time(); p.SpawnProtT = r.Time();
            p.FireCd = r.Time(); p.PulseCd = r.Time(); p.DecoyCd = r.Time(); p.BuyCd = r.Time();
            p.FireSeq = r.U8(); p.CastSeq = r.U8(); p.HitSeq = r.U8(); p.JumpSeq = r.U8();
            p.TowerSightT = r.Time(); p.PublicPingT = r.Time(); p.NoiseT = r.Time();
            p.Keys = r.U8(); p.CoresCollected = r.U8(); p.ZoneId = r.U8() - 1; p.VaultChannel = r.Time();
            p.CapturedMask = r.I32(); p.TowerControlTime = r.F32(); p.Deaths = r.U8(); p.Elims = r.U8();
            p.Downed = r.Bool(); p.BleedT = r.Time(); p.ReviveProg = r.Unit(1f); p.Reviving = r.U8() - 1; p.Revives = r.U8(); p.Assists = r.U8(); p.DownedBy = r.U8() - 1;
            ReadObjective(r, p.Primary); ReadObjective(r, p.Secondary);
            var sc = p.Score;
            sc.Primary = r.I32(); sc.Secondary = r.I32(); sc.Resources = r.I32(); sc.Territory = r.I32(); sc.Eliminations = r.I32(); sc.Survival = r.I32(); sc.Bonus = r.I32(); sc.Squad = r.I32();
            ReadObjective(r, s.SquadObjective); s.SquadTotal = r.I32();

            int na = r.U8();
            for (int i = 0; i < na; i++)
            {
                var a = new AvatarSnap { AvatarId = r.I16(), OwnerId = r.U8(), Vis = r.U8() };
                a.Pos = r.Vec(); a.Vel = r.VelQ(); a.H = r.Pos(); a.Yaw = r.Angle(); a.Health01 = r.Unit(1f); a.Flags = (AvatarFlags)r.U8();
                a.FireSeq = r.U8(); a.CastSeq = r.U8(); a.HitSeq = r.U8(); a.JumpSeq = r.U8();
                a.State = r.U8(); a.ReviveProg = r.Unit(1f);
                s.Avatars.Add(a);
            }
            int np = r.U8();
            for (int i = 0; i < np; i++)
                s.Projectiles.Add(new ProjectileSnap { Id = r.I32(), Owner = r.U8(), Pos = r.Vec(), Vel = r.VelQ() });
            int nz = r.U8();
            s.Zones = new ZoneSnap[nz];
            for (int i = 0; i < nz; i++)
            {
                s.Zones[i] = new ZoneSnap
                {
                    Controller = r.U8() - 1, Capturer = r.U8() - 1, Squad = r.U8() - 1, CapturerSquad = r.U8() - 1, Progress = r.Unit(1f), Contested = r.Bool(), Locked = r.Bool(),
                    Occupants = r.U8(), Cooldown = r.Time(),
                };
            }
            return s;
        }

        // ---------------- Events ----------------
        public static void WriteEvents(ByteWriter w, List<SimEvent> events)
        {
            w.U8((byte)Msg.Events);
            w.U16((ushort)events.Count);
            foreach (var e in events)
            {
                w.U8((byte)e.Type); w.I16((short)e.A); w.I16((short)e.B); w.I16((short)MathUtil.Clamp(e.Value, short.MinValue, short.MaxValue)); w.Vec(e.Pos);
            }
        }

        public static void ReadEvents(ByteReader r, List<SimEvent> output)
        {
            output.Clear();
            int n = r.U16();
            for (int i = 0; i < n; i++)
                output.Add(new SimEvent((EventType)r.U8(), r.I16(), r.I16(), r.I16(), r.Vec()));
        }

        // ---------------- Match end ----------------
        public static void WriteMatchEnd(ByteWriter w, List<PlayerResult> results)
        {
            w.U8((byte)Msg.MatchEnd);
            w.U8((byte)results.Count);
            foreach (var x in results)
            {
                w.U8((byte)x.PlayerId); w.Str(x.Name); w.Bool(x.IsBot); Look(w, x.Look); w.U8((byte)x.Rank); w.I32(x.Total);
                w.I32(x.Primary); w.I32(x.Secondary); w.I32(x.Resources); w.I32(x.Territory); w.I32(x.Eliminations); w.I32(x.Survival); w.I32(x.Bonus);
                w.U8((byte)x.Elims); w.U8((byte)x.Deaths); w.U8((byte)x.Assists); w.U8((byte)x.Revives); w.Bool(x.PrimaryDone); w.Bool(x.SecondaryDone); w.U8((byte)x.PrimaryType); w.U8((byte)x.SecondaryType);
                w.U8((byte)x.Squad); w.U8((byte)x.SquadRank); w.I32(x.SquadTotal); w.I32(x.SquadPoints);
            }
        }

        public static List<PlayerResult> ReadMatchEnd(ByteReader r)
        {
            var list = new List<PlayerResult>();
            int n = r.U8();
            for (int i = 0; i < n; i++)
            {
                var x = new PlayerResult { PlayerId = r.U8(), Name = r.Str(), IsBot = r.Bool(), Look = Look(r), Rank = r.U8(), Total = r.I32() };
                x.Primary = r.I32(); x.Secondary = r.I32(); x.Resources = r.I32(); x.Territory = r.I32(); x.Eliminations = r.I32(); x.Survival = r.I32(); x.Bonus = r.I32();
                x.Elims = r.U8(); x.Deaths = r.U8(); x.Assists = r.U8(); x.Revives = r.U8(); x.PrimaryDone = r.Bool(); x.SecondaryDone = r.Bool(); x.PrimaryType = (ObjectiveType)r.U8(); x.SecondaryType = (ObjectiveType)r.U8();
                x.Squad = r.U8(); x.SquadRank = r.U8(); x.SquadTotal = r.I32(); x.SquadPoints = r.I32();
                list.Add(x);
            }
            return list;
        }
    }
}
