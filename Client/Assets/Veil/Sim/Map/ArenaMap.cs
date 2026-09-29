using System;
using System.Collections.Generic;

namespace Veil.Sim
{
    /// <summary>
    /// The first VEIL arena (~150m x 150m, GDD §9). Deterministic: the same seed produces
    /// the same arena on the client and the server.
    ///
    ///            N (+Z)
    ///   RUINS  ·········  MARKET
    ///     ·     ┌─moat─┐     ·
    ///  W  ·     │TOWER │     ·  E (+X)
    ///     ·     └──────┘     ·
    ///  REACTOR ·········  VAULT
    /// </summary>
    public static class ArenaMap
    {
        public const int MapSeed = 1337;

        private static readonly List<(Vec2 a, Vec2 b, float w)> Paths = new List<(Vec2, Vec2, float)>();

        public static MapData Build()
        {
            var m = new MapData();
            var rng = new Rng(MapSeed);
            Paths.Clear();

            // ---------------- Zones ----------------
            AddZone(m, ZoneType.Tower, "Tower", new Vec2(0, 0), 10f);
            AddZone(m, ZoneType.Ruins, "Ruins", new Vec2(-45, 40), 12f);
            AddZone(m, ZoneType.Market, "Market", new Vec2(45, 42), 10f);
            AddZone(m, ZoneType.Reactor, "Reactor", new Vec2(-44, -40), 9f);
            AddZone(m, ZoneType.Vault, "Vault", new Vec2(44.4f, -42.4f), 5.5f);

            // ---------------- Central plaza + moat ----------------
            m.Decals.Add(new GroundDecal { Kind = 2, Center = Vec2.Zero, Radius = 16f });
            Circle(m, ObstacleKind.TowerCore, new Vec2(0, 0), 2.8f, 22f);
            for (int i = 0; i < 4; i++)
            {
                float yaw = 45 + 90 * i;
                Box(m, ObstacleKind.LowWall, Vec2.FromYaw(yaw) * 6.8f, new Vec2(2.2f, 0.4f), yaw + 90, 1.0f);
            }
            for (int i = 0; i < 8; i++)
            {
                float yaw = 22.5f + 45 * i;
                Circle(m, ObstacleKind.Pillar, Vec2.FromYaw(yaw) * 13.5f, 0.6f, 4f);
            }
            // moat: square ring 17..21 with 7m bridge gaps on the 4 cardinal sides
            Water(m, new Vec2(-12.25f, 19), new Vec2(8.75f, 2));
            Water(m, new Vec2(12.25f, 19), new Vec2(8.75f, 2));
            Water(m, new Vec2(-12.25f, -19), new Vec2(8.75f, 2));
            Water(m, new Vec2(12.25f, -19), new Vec2(8.75f, 2));
            Water(m, new Vec2(19, 10.25f), new Vec2(2, 6.75f));
            Water(m, new Vec2(19, -10.25f), new Vec2(2, 6.75f));
            Water(m, new Vec2(-19, 10.25f), new Vec2(2, 6.75f));
            Water(m, new Vec2(-19, -10.25f), new Vec2(2, 6.75f));
            m.Decals.Add(new GroundDecal { Kind = 1, Center = new Vec2(0, 19), Half = new Vec2(3.5f, 2.6f) });
            m.Decals.Add(new GroundDecal { Kind = 1, Center = new Vec2(0, -19), Half = new Vec2(3.5f, 2.6f) });
            m.Decals.Add(new GroundDecal { Kind = 1, Center = new Vec2(19, 0), Half = new Vec2(2.6f, 3.5f) });
            m.Decals.Add(new GroundDecal { Kind = 1, Center = new Vec2(-19, 0), Half = new Vec2(2.6f, 3.5f) });

            // ---------------- Road ring ----------------
            Path(m, new Vec2(0, 21), new Vec2(0, 40));
            Path(m, new Vec2(0, -21), new Vec2(0, -40));
            Path(m, new Vec2(21, 0), new Vec2(40, 0));
            Path(m, new Vec2(-21, 0), new Vec2(-40, 0));
            Path(m, new Vec2(0, 40), new Vec2(-45, 40));
            Path(m, new Vec2(0, 40), new Vec2(45, 42));
            Path(m, new Vec2(0, -40), new Vec2(-44, -40));
            Path(m, new Vec2(0, -40), new Vec2(44, -42));
            Path(m, new Vec2(40, 0), new Vec2(45, 42));
            Path(m, new Vec2(40, 0), new Vec2(44, -42));
            Path(m, new Vec2(-40, 0), new Vec2(-45, 40));
            Path(m, new Vec2(-40, 0), new Vec2(-44, -40));
            foreach (var z in m.Zones)
                if (z.Type != ZoneType.Tower)
                    m.Decals.Add(new GroundDecal { Kind = 2, Center = z.Center, Radius = z.Radius + 1.5f });

            // ---------------- Ruins (cover & ambush) ----------------
            Box(m, ObstacleKind.Wall, new Vec2(-49, 49), new Vec2(4, 0.5f), 0, 3.2f);
            Box(m, ObstacleKind.LowWall, new Vec2(-39.5f, 49), new Vec2(2.5f, 0.5f), 0, 1.0f);
            Box(m, ObstacleKind.LowWall, new Vec2(-51, 31), new Vec2(3, 0.5f), 0, 1.0f);
            Box(m, ObstacleKind.Wall, new Vec2(-38.5f, 31), new Vec2(2.5f, 0.5f), 0, 3.2f);
            Box(m, ObstacleKind.Wall, new Vec2(-54, 43), new Vec2(0.5f, 4), 0, 3.2f);
            Box(m, ObstacleKind.LowWall, new Vec2(-54, 34), new Vec2(0.5f, 2), 0, 1.0f);
            Box(m, ObstacleKind.Wall, new Vec2(-36, 45), new Vec2(0.5f, 3), 0, 3.2f);
            Box(m, ObstacleKind.LowWall, new Vec2(-36, 35), new Vec2(0.5f, 2), 0, 1.0f);
            Box(m, ObstacleKind.Wall, new Vec2(-47.5f, 42.5f), new Vec2(2.2f, 0.45f), 90, 2.8f);
            Box(m, ObstacleKind.Wall, new Vec2(-41.5f, 36.5f), new Vec2(2f, 0.45f), 0, 2.8f);
            Circle(m, ObstacleKind.Pillar, new Vec2(-49.5f, 36f), 0.7f, 3.6f);
            Circle(m, ObstacleKind.Pillar, new Vec2(-40.5f, 44.5f), 0.7f, 3.6f);
            Circle(m, ObstacleKind.Pillar, new Vec2(-51f, 45.5f), 0.7f, 0.9f);
            Box(m, ObstacleKind.LowWall, new Vec2(-44f, 33.5f), new Vec2(2.4f, 0.6f), 30, 0.9f);

            // ---------------- Market (trade & utility) ----------------
            float[] stallYaws = { 0, 60, 120, 228, 310 };
            var market = m.Zone(ZoneType.Market);
            for (int i = 0; i < stallYaws.Length; i++)
            {
                var c = market.Center + Vec2.FromYaw(stallYaws[i]) * 6.8f;
                var o = Box(m, ObstacleKind.Stall, c, new Vec2(1.6f, 1.0f), stallYaws[i] + 90, 2.2f);
                o.Variant = i;
            }
            Box(m, ObstacleKind.Crate, market.Center + new Vec2(2.5f, 1.5f), new Vec2(0.6f, 0.6f), 15, 1.0f);
            Box(m, ObstacleKind.Crate, market.Center + new Vec2(-2.0f, -2.5f), new Vec2(0.6f, 0.6f), 40, 1.0f);

            // ---------------- Reactor (energy, high risk) ----------------
            var reactor = m.Zone(ZoneType.Reactor);
            Circle(m, ObstacleKind.ReactorCore, reactor.Center, 2.2f, 5f);
            for (int i = 0; i < 4; i++)
                Circle(m, ObstacleKind.Pylon, reactor.Center + Vec2.FromYaw(45 + 90 * i) * 6.2f, 0.5f, 4f);

            // ---------------- Vault (high value) ----------------
            Box(m, ObstacleKind.VaultBuilding, new Vec2(51, -49), new Vec2(6, 5), 45, 8f);

            // ---------------- Buildings ----------------
            Box(m, ObstacleKind.Building, new Vec2(27, 28), new Vec2(4, 3.5f), 45, 7f).Variant = 0;
            Box(m, ObstacleKind.Building, new Vec2(-27, 27), new Vec2(3.5f, 4f), -45, 7f).Variant = 1;
            Box(m, ObstacleKind.Building, new Vec2(28, -26), new Vec2(3.5f, 4f), -45, 7f).Variant = 2;
            Box(m, ObstacleKind.Building, new Vec2(-26, -28), new Vec2(4f, 3.5f), 45, 7f).Variant = 3;
            Box(m, ObstacleKind.Building, new Vec2(61, 8), new Vec2(3.5f, 5f), 0, 8f).Variant = 4;
            Box(m, ObstacleKind.Building, new Vec2(-61, -6), new Vec2(3.5f, 5f), 0, 8f).Variant = 5;
            Box(m, ObstacleKind.Building, new Vec2(12, 61), new Vec2(5f, 3.5f), 0, 8f).Variant = 6;
            Box(m, ObstacleKind.Building, new Vec2(-12, -61), new Vec2(5f, 3.5f), 0, 8f).Variant = 7;

            // cover walls between the ring and the plaza
            Box(m, ObstacleKind.LowWall, new Vec2(10, 30), new Vec2(3, 0.45f), 0, 1.0f);
            Box(m, ObstacleKind.LowWall, new Vec2(-10, -30), new Vec2(3, 0.45f), 0, 1.0f);
            Box(m, ObstacleKind.LowWall, new Vec2(30, -10), new Vec2(3, 0.45f), 90, 1.0f);
            Box(m, ObstacleKind.LowWall, new Vec2(-30, 10), new Vec2(3, 0.45f), 90, 1.0f);
            Box(m, ObstacleKind.Wall, new Vec2(-12, 31), new Vec2(3, 0.5f), 20, 3.2f);
            Box(m, ObstacleKind.Wall, new Vec2(12, -31), new Vec2(3, 0.5f), 20, 3.2f);
            Box(m, ObstacleKind.Wall, new Vec2(31, 12), new Vec2(0.5f, 3), 20, 3.2f);
            Box(m, ObstacleKind.Wall, new Vec2(-31, -12), new Vec2(0.5f, 3), 20, 3.2f);

            // ---------------- Boundary cliffs ----------------
            for (float t = -74; t <= 74; t += 7.5f)
            {
                float j = rng.Range(-1.5f, 1.5f);
                Circle(m, ObstacleKind.Cliff, new Vec2(t, 73.5f + j), rng.Range(3.5f, 5.5f), 14f).Variant = rng.Int(1000);
                Circle(m, ObstacleKind.Cliff, new Vec2(t, -73.5f - j), rng.Range(3.5f, 5.5f), 14f).Variant = rng.Int(1000);
                Circle(m, ObstacleKind.Cliff, new Vec2(73.5f + j, t), rng.Range(3.5f, 5.5f), 14f).Variant = rng.Int(1000);
                Circle(m, ObstacleKind.Cliff, new Vec2(-73.5f - j, t), rng.Range(3.5f, 5.5f), 14f).Variant = rng.Int(1000);
            }

            // Bake collision early so spot validation works, then re-bake after decoration.
            m.Bake();

            // ---------------- Gameplay spots ----------------
            for (int i = 0; i < GameConfig.MaxPlayers; i++)
            {
                float yaw = 12 + 360f / GameConfig.MaxPlayers * i;
                m.SpawnPoints.Add(FreeSpot(m, Vec2.FromYaw(yaw) * 57f, 1.5f));
            }
            Vec2[] keys =
            {
                new Vec2(-20, 46), new Vec2(20, 50), new Vec2(-62, 20), new Vec2(62, 24), new Vec2(-62, -24),
                new Vec2(60, -18), new Vec2(-22, -52), new Vec2(24, -58), new Vec2(-2, 63), new Vec2(3, -63),
                new Vec2(-46.5f, 38.5f), new Vec2(34, 12),
            };
            foreach (var k in keys) m.KeySpots.Add(FreeSpot(m, k, 1.2f));
            Vec2[] cores =
            {
                new Vec2(0, 19), new Vec2(0, -19), new Vec2(19, 0), new Vec2(-19, 0), new Vec2(-38, -46),
                new Vec2(-50, -34), new Vec2(51, 47), new Vec2(-40, 42), new Vec2(36, -35), new Vec2(58, 58),
                new Vec2(-58, -58), new Vec2(-58, 58),
            };
            foreach (var c in cores) m.CoreSpots.Add(FreeSpot(m, c, 1.2f));

            // ---------------- Decoration with collision ----------------
            var reserved = new List<Vec2>();
            reserved.AddRange(m.SpawnPoints);
            reserved.AddRange(m.KeySpots);
            reserved.AddRange(m.CoreSpots);

            int trees = 0, attempts = 0;
            while (trees < 150 && attempts++ < 4000)
            {
                var p = new Vec2(rng.Range(-68, 68), rng.Range(-68, 68));
                if (!DecorAllowed(m, p, reserved, 3.2f, 2.6f)) continue;
                var o = Circle(m, ObstacleKind.Tree, p, 0.55f, 7f);
                o.Variant = rng.Int(10000);
                trees++;
            }
            int rocks = 0; attempts = 0;
            while (rocks < 40 && attempts++ < 3000)
            {
                var p = new Vec2(rng.Range(-68, 68), rng.Range(-68, 68));
                if (!DecorAllowed(m, p, reserved, 4f, 3.2f)) continue;
                float r = rng.Range(0.9f, 2.0f);
                var o = Circle(m, ObstacleKind.Rock, p, r, rng.Range(1.3f, 3.2f));
                o.Variant = rng.Int(10000);
                rocks++;
            }
            int crates = 0; attempts = 0;
            while (crates < 26 && attempts++ < 2000)
            {
                var p = new Vec2(rng.Range(-66, 66), rng.Range(-66, 66));
                if (!DecorAllowed(m, p, reserved, 3.5f, 2.2f)) continue;
                float s = rng.Range(0.55f, 0.95f);
                var o = Box(m, ObstacleKind.Crate, p, new Vec2(s, s), rng.Range(0, 90), 1.0f);
                o.Variant = rng.Int(10000);
                crates++;
            }

            m.Bake();
            return m;
        }

        private static bool DecorAllowed(MapData m, Vec2 p, List<Vec2> reserved, float spotClear, float obstacleClear)
        {
            if (p.Length < 25f) return false;                 // plaza + moat
            foreach (var z in m.Zones)
                if (Vec2.Dist(p, z.Center) < z.Radius + 3.5f) return false;
            foreach (var (a, b, w) in Paths)
                if (DistToSegment(p, a, b) < w * 0.5f + 1.8f) return false;
            foreach (var r in reserved)
                if (Vec2.Dist(p, r) < spotClear) return false;
            foreach (var o in m.Obstacles)
                if (o.SignedDistance(p) < obstacleClear) return false;
            return true;
        }

        public static float DistToSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            Vec2 ab = b - a;
            float t = MathUtil.Clamp01(Vec2.Dot(p - a, ab) / MathF.Max(ab.LengthSq, 1e-6f));
            return Vec2.Dist(p, a + ab * t);
        }

        private static Vec2 FreeSpot(MapData m, Vec2 p, float r)
        {
            if (!m.IsBlockedForStanding(p, r)) return p;
            for (float rad = 0.5f; rad < 12f; rad += 0.5f)
                for (int a = 0; a < 16; a++)
                {
                    var q = p + Vec2.FromYaw(a * 22.5f) * rad;
                    if (!m.IsBlockedForStanding(q, r)) return q;
                }
            return p;
        }

        private static void AddZone(MapData m, ZoneType t, string name, Vec2 c, float r)
        {
            m.Zones.Add(new ZoneDef { Id = m.Zones.Count, Type = t, Name = name, Center = c, Radius = r });
        }

        private static void Path(MapData m, Vec2 a, Vec2 b, float width = 5f)
        {
            Vec2 d = b - a;
            m.Decals.Add(new GroundDecal
            {
                Kind = 0,
                Center = (a + b) * 0.5f,
                Half = new Vec2(width * 0.5f, d.Length * 0.5f + width * 0.5f),
                Rot = d.Yaw,
            });
            Paths.Add((a, b, width));
        }

        private static Obstacle Circle(MapData m, ObstacleKind k, Vec2 c, float r, float h)
        {
            var o = new Obstacle { Kind = k, Shape = ShapeKind.Circle, Center = c, Radius = r, Height = h };
            m.Obstacles.Add(o);
            return o;
        }

        private static Obstacle Box(MapData m, ObstacleKind k, Vec2 c, Vec2 half, float rot, float h)
        {
            var o = new Obstacle { Kind = k, Shape = ShapeKind.Box, Center = c, Half = half, Rot = rot, Height = h };
            m.Obstacles.Add(o);
            return o;
        }

        private static void Water(MapData m, Vec2 c, Vec2 half)
        {
            var o = Box(m, ObstacleKind.Water, c, half, 0, 99f);
            o.BlocksShots = false;
        }
    }
}
