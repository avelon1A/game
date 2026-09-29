using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Veil.Sim;

namespace Veil.Server
{
    /// <summary>
    /// Runs complete headless matches with 15 bots to validate rules, balance and performance
    /// (GDD Prototype 5 "measure CPU"). Usage: dotnet run -- --selftest [seconds] [matches]
    /// </summary>
    public static class SelfTest
    {
        public static int Run(int matchSeconds, int matches)
        {
            var sw = Stopwatch.StartNew();
            var map = ArenaMap.Build();
            Console.WriteLine($"Map built in {sw.ElapsedMilliseconds} ms: {map.Obstacles.Count} obstacles, {map.Zones.Count} zones, " +
                              $"{map.SpawnPoints.Count} spawns, {map.KeySpots.Count} key spots, {map.CoreSpots.Count} core spots");

            // sanity: spawn points must be walkable & paths between zones must exist
            int bad = 0;
            foreach (var s in map.SpawnPoints) if (!map.Nav.Walkable(s)) { bad++; Console.WriteLine($"  !! spawn not walkable {s}"); }
            var path = new List<Vec2>();
            foreach (var a in map.Zones)
                foreach (var b in map.Zones)
                {
                    if (a == b) continue;
                    if (!map.Nav.FindPath(a.Center + new Vec2(0, a.Type == ZoneType.Tower ? 5 : 0), b.Center + new Vec2(0, b.Type == ZoneType.Tower ? 5 : 0), path))
                    { bad++; Console.WriteLine($"  !! no path {a.Name} -> {b.Name}"); }
                }
            Console.WriteLine(bad == 0 ? "Map validation OK" : $"Map validation: {bad} problems");

            int failures = 0;
            for (int m = 0; m < matches; m++)
            {
                var settings = new MatchSettings { MatchSeconds = matchSeconds, Seed = 100 + m, TotalPlayers = 15 };
                var sim = new MatchSim(map, settings);
                sim.FillBots();
                sim.Start();
                var counts = new Dictionary<EventType, int>();
                var snap = new Snapshot();
                var w = new ByteWriter(4096);
                int maxSnap = 0;
                long tickTicks = 0;
                int ticks = 0;
                var tsw = new Stopwatch();
                while (!sim.Ended)
                {
                    tsw.Restart();
                    sim.Step();
                    tickTicks += tsw.ElapsedTicks;
                    ticks++;
                    foreach (var e in sim.Events) counts[e.Type] = counts.TryGetValue(e.Type, out var c) ? c + 1 : 1;
                    if (sim.Tick % 15 == 0)
                    {
                        foreach (var p in sim.Players)
                        {
                            SnapshotBuilder.Build(sim, p, snap);
                            w.Reset();
                            Protocol.WriteSnapshot(w, snap);
                            maxSnap = Math.Max(maxSnap, w.Length);
                            // round-trip check
                            var back = Protocol.ReadSnapshot(new ByteReader(w.Buffer, 1, w.Length - 1));
                            if (back.Avatars.Count != snap.Avatars.Count || Math.Abs(back.Self.Pos.X - snap.Self.Pos.X) > 0.001f)
                            { failures++; Console.WriteLine("  !! snapshot round-trip mismatch"); }
                        }
                    }
                    if (ticks > matchSeconds * GameConfig.TickRate + 100) { failures++; Console.WriteLine("  !! match did not end"); break; }
                }
                double avgMs = tickTicks * 1000.0 / Stopwatch.Frequency / Math.Max(1, ticks);
                Console.WriteLine($"\nMatch {m + 1}: {ticks} ticks, avg tick {avgMs:0.000} ms (budget {1000.0 / GameConfig.TickRate:0.0} ms), max snapshot {maxSnap} bytes");
                Console.WriteLine("  events: " + string.Join(", ", counts.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));
                Console.WriteLine("  rank name        kind         total  prim sec  res terr elim surv bonus  K/D  objectives");
                foreach (var r in sim.Results)
                {
                    var p = sim.Players[r.PlayerId];
                    Console.WriteLine($"  #{r.Rank,-3} {r.Name,-11} {p.BotKind,-12} {r.Total,5} {r.Primary,5} {r.Secondary,3} {r.Resources,4} {r.Territory,4} {r.Eliminations,4} {r.Survival,4} {r.Bonus,5}  {r.Elims}/{r.Deaths}  " +
                                      $"{p.Primary.Type}{(p.Primary.Done ? "✓" : $"({p.Primary.Progress:0.#}/{p.Primary.Target})")} + {p.Secondary.Type}{(p.Secondary.Done ? "✓" : $"({p.Secondary.Progress:0.#}/{p.Secondary.Target})")}");
                }
                if (avgMs > 1000.0 / GameConfig.TickRate) { failures++; Console.WriteLine("  !! tick over budget"); }
                var totals = sim.Results.Select(r => r.Total).ToList();
                if (totals.All(t => t == totals[0])) { failures++; Console.WriteLine("  !! scores identical — nothing happened"); }
            }
            Console.WriteLine(failures == 0 && bad == 0 ? "\nSELFTEST PASS" : $"\nSELFTEST FAIL ({failures + bad})");
            return failures == 0 && bad == 0 ? 0 : 1;
        }
    }
}
