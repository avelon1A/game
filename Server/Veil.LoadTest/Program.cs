using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using LiteNetLib;
using Veil.Sim;

namespace Veil.LoadTest
{
    /// <summary>
    /// GDD Prototype 5: connects N headless "human" clients to a VEIL server, readies them,
    /// plays one match with wandering inputs and reports snapshot rate, bandwidth and latency.
    /// Usage: dotnet run -- [clients=15] [host=127.0.0.1] [port=7777]
    /// </summary>
    public static class Program
    {
        private sealed class Bot
        {
            public int Index;
            public NetManager Net;
            public NetPeer Peer;
            public int PlayerId = -1;
            public bool InMatch, Ended, Welcomed;
            public int Snapshots, Events;
            public long Bytes;
            public int Seq;
            public Vec2 Pos;
            public float Yaw;
            public float TurnT;
            public readonly List<InputCmd> Recent = new List<InputCmd>();
            public readonly ByteWriter W = new ByteWriter(256);
            public int Visible, MaxVisible;
            public int Score;
        }

        public static int Main(string[] args)
        {
            int n = args.Length > 0 ? int.Parse(args[0]) : 15;
            string host = args.Length > 1 ? args[1] : "127.0.0.1";
            int port = args.Length > 2 ? int.Parse(args[2]) : 7777;
            Console.WriteLine($"VEIL load test: {n} clients → {host}:{port}");

            var bots = new List<Bot>();
            var rng = new Random(7);
            for (int i = 0; i < n; i++)
            {
                var b = new Bot { Index = i, Yaw = rng.Next(360) };
                var l = new EventBasedNetListener();
                b.Net = new NetManager(l) { AutoRecycle = true, ChannelsCount = 2, UpdateTime = 10 };
                l.PeerConnectedEvent += p =>
                {
                    b.Peer = p;
                    b.W.Reset();
                    Protocol.WriteHello(b.W, new HelloMsg { Name = $"Load{i:00}", Look = Appearance.Preset(i) });
                    p.Send(b.W.Buffer, 0, b.W.Length, 1, DeliveryMethod.ReliableOrdered);
                };
                l.NetworkReceiveEvent += (p, r, ch, m) =>
                {
                    var data = r.GetRemainingBytes();
                    b.Bytes += data.Length;
                    var br = new ByteReader(data, 1, data.Length - 1);
                    switch ((Msg)data[0])
                    {
                        case Msg.Welcome:
                            b.Welcomed = true;
                            b.W.Reset(); Protocol.WriteReady(b.W, true, 60);
                            p.Send(b.W.Buffer, 0, b.W.Length, 1, DeliveryMethod.ReliableOrdered);
                            break;
                        case Msg.MatchStart:
                            b.PlayerId = Protocol.ReadMatchStart(br).YourPlayerId;
                            b.InMatch = true;
                            break;
                        case Msg.Snapshot:
                        {
                            var s = Protocol.ReadSnapshot(br);
                            b.Snapshots++;
                            b.Pos = s.Self.Pos;
                            b.Visible = s.Avatars.Count;
                            b.MaxVisible = Math.Max(b.MaxVisible, s.Avatars.Count);
                            b.Score = s.Self.Score.Total;
                            break;
                        }
                        case Msg.Events: b.Events++; break;
                        case Msg.MatchEnd:
                        {
                            var res = Protocol.ReadMatchEnd(br);
                            var me = res.FirstOrDefault(x => x.PlayerId == b.PlayerId);
                            if (me != null) b.Score = me.Total;
                            b.Ended = true;
                            break;
                        }
                    }
                };
                b.Net.Start();
                b.Net.Connect(host, port, "VEIL");
                bots.Add(b);
            }

            var sw = Stopwatch.StartNew();
            double lastTick = 0, lastReport = 0, matchStart = -1;
            while (sw.Elapsed.TotalSeconds < 240)
            {
                foreach (var b in bots) b.Net.PollEvents();
                double t = sw.Elapsed.TotalSeconds;
                if (t - lastTick >= GameConfig.Dt)
                {
                    lastTick = t;
                    foreach (var b in bots)
                    {
                        if (!b.InMatch || b.Ended || b.Peer == null) continue;
                        b.TurnT -= GameConfig.Dt;
                        if (b.TurnT <= 0) { b.Yaw = (float)((-b.Pos).Yaw + rng.Next(-70, 70)); b.TurnT = (float)rng.NextDouble() * 3 + 1; }
                        var dir = Vec2.FromYaw(b.Yaw);
                        var cmd = new InputCmd { Seq = ++b.Seq, MoveX = dir.X, MoveY = dir.Y, Yaw = b.Yaw };
                        if (rng.NextDouble() < 0.3) cmd.Buttons |= Buttons.Fire;
                        if (rng.NextDouble() < 0.01) cmd.Buttons |= Buttons.Jump;
                        if (rng.NextDouble() < 0.004) cmd.Buttons |= Buttons.Pulse;
                        if (rng.NextDouble() < 0.004) cmd.Buttons |= Buttons.Dash;
                        b.Recent.Add(cmd);
                        if (b.Recent.Count > 3) b.Recent.RemoveAt(0);
                        b.W.Reset();
                        Protocol.WriteInputs(b.W, b.Recent, 0, b.Recent.Count);
                        b.Peer.Send(b.W.Buffer, 0, b.W.Length, 0, DeliveryMethod.Unreliable);
                    }
                }
                if (matchStart < 0 && bots.All(b => b.InMatch)) { matchStart = t; Console.WriteLine($"[{t:0.0}s] all {n} clients in match"); }
                if (t - lastReport >= 5)
                {
                    lastReport = t;
                    int connected = bots.Count(b => b.Peer != null && b.Peer.ConnectionState == ConnectionState.Connected);
                    double mt = matchStart > 0 ? t - matchStart : 0;
                    double rate = mt > 0 ? bots.Average(b => b.Snapshots) / mt : 0;
                    double kbps = mt > 0 ? bots.Average(b => b.Bytes) / mt / 1024.0 : 0;
                    int ping = connected > 0 ? (int)bots.Where(b => b.Peer != null).Average(b => b.Peer.Ping) : 0;
                    Console.WriteLine($"[{t:0.0}s] connected {connected}/{n}  inMatch {bots.Count(b => b.InMatch)}  snapshots/s per client {rate:0.0}  downstream {kbps:0.0} KB/s per client  ping {ping} ms  avg visible {bots.Average(b => b.Visible):0.0}");
                }
                if (matchStart > 0 && bots.All(b => b.Ended)) break;
                Thread.Sleep(1);
            }

            bool ok = bots.All(b => b.Ended);
            Console.WriteLine();
            Console.WriteLine(ok ? $"LOADTEST PASS: {n} clients completed a match" : "LOADTEST INCOMPLETE");
            foreach (var b in bots.OrderByDescending(b => b.Score).Take(5))
                Console.WriteLine($"  Load{b.Index:00}: score {b.Score}, snapshots {b.Snapshots}, events {b.Events}, max visible {b.MaxVisible}, {b.Bytes / 1024} KB");
            foreach (var b in bots) b.Net.Stop();
            return ok ? 0 : 1;
        }
    }
}
