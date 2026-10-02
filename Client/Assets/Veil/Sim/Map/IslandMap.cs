using System;
using System.Collections.Generic;
using static Veil.Sim.ArenaMap;

namespace Veil.Sim
{
    /// <summary>
    /// The Rilo island (400 x 400 m). Deterministic, shared by client and server.
    ///
    ///                 SNOW BASE (N)
    ///     OUTPOST (NW)     |      DOCKYARD (NE)
    ///             \     ~river~     /
    ///  HYDRO (W) --- RILO CITY + moat --- RUINS (E)
    ///             /     ~river~     \
    ///     CANYON (SW)      |      BEACH (SE)
    ///                  FOREST (S)
    ///
    /// Eight regions separated by rivers (water blocks movement, bridges are the chokepoints), a moat around
    /// Rilo City with 8 bridges, a ring road at 118 m. Squads spawn in the diagonal regions (Outpost, Dockyard,
    /// Beach, Canyon) at the same distance from the centre — every objective is placed by rotation, so distances
    /// are equal for all squads.
    /// </summary>
    public static class IslandMap
    {
        public const float CityRadius = 62f;
        public const float MoatIn = 64f, MoatOut = 70f;
        public const float RingRoad = 118f;
        public const float SpawnRadius = 150f;
        public const float RiverWidth = 7f;
        private static readonly (float r0, float r1)[] RiverBridges = { (113f, 123f), (157f, 165f) };

        public static readonly (string name, Biome biome)[] RegionNames =
        {
            ("SNOW BASE", Biome.Snow), ("DOCKYARD", Biome.Dock), ("RUINS", Biome.Ruins), ("BEACH", Biome.Beach),
            ("FOREST", Biome.Forest), ("CANYON", Biome.Canyon), ("HYDRO PLANT", Biome.Hydro), ("OUTPOST", Biome.Outpost),
        };

        /// <summary>Coastline distance from the centre for a bearing (degrees).</summary>
        public static float CoastRadius(float yaw)
        {
            float a = yaw * MathUtil.Deg2Rad;
            return 184f + 6f * MathF.Sin(3 * a + 0.4f) + 4f * MathF.Sin(7 * a + 1.3f) + 2.5f * MathF.Sin(13 * a);
        }

        public static float RiverYaw(int k, float r) => 22.5f + 45f * k + 2.6f * MathF.Sin(r * 0.05f + k * 1.7f);

        public static Biome BiomeAt(Vec2 p)
        {
            float r = p.Length;
            if (r > CoastRadius(p.Yaw)) return Biome.Sea;
            if (r < MoatIn) return Biome.City;
            int sector = ((int)MathF.Round(((p.Yaw % 360f) + 360f) % 360f / 45f)) % 8;
            return RegionNames[sector].biome;
        }

        public static MapData Build()
        {
            var m = new MapData { Island = true };
            var rng = new Rng(MapSeed);
            Paths.Clear();

            for (int k = 0; k < 8; k++)
            {
                float b = k * 45f;
                m.Regions.Add(new RegionDef { Name = RegionNames[k].name, Biome = RegionNames[k].biome, Bearing = b, Center = Vec2.FromYaw(b) * 140f });
            }
            m.Regions.Add(new RegionDef { Name = "RILO CITY", Biome = Biome.City, Bearing = 0, Center = Vec2.Zero });

            // ---------------- zones (old systems keep working) ----------------
            AddZone(m, ZoneType.Tower, "Rilo Plaza", Vec2.Zero, 10f);
            AddZone(m, ZoneType.Ruins, "Ruins", Vec2.FromYaw(90) * 125f, 13f);   // in front of the temple (RuinsLandmark)
            AddZone(m, ZoneType.Market, "Market", Vec2.FromYaw(0) * 42f, 9f);
            AddZone(m, ZoneType.Reactor, "Hydro Reactor", Vec2.FromYaw(270) * 138f, 9f);
            AddZone(m, ZoneType.Vault, "Snow Vault", Vec2.FromYaw(8) * 152f, 5.5f);
            foreach (var z in m.Zones)
                if (z.Type != ZoneType.Tower) m.Decals.Add(new GroundDecal { Kind = 2, Center = z.Center, Radius = z.Radius + 1.5f });

            // ---------------- sea around the coast ----------------
            for (float yaw = 0; yaw < 360; yaw += 3f)
            {
                float r = CoastRadius(yaw);
                var c = Vec2.FromYaw(yaw) * (r + 22f);
                WaterBox(m, c, new Vec2(r * 3f * MathUtil.Deg2Rad * 0.75f + 2f, 22f), yaw);
            }

            // ---------------- moat around Rilo City (8 bridges) ----------------
            for (float yaw = 0; yaw < 360; yaw += 5f)
            {
                float d = MathF.Abs(MathUtil.DeltaAngle(yaw, MathF.Round(yaw / 45f) * 45f));
                if (d < 6f) continue;
                float rm = (MoatIn + MoatOut) * 0.5f;
                WaterBox(m, Vec2.FromYaw(yaw) * rm, new Vec2(rm * 5f * MathUtil.Deg2Rad * 0.5f + 0.4f, (MoatOut - MoatIn) * 0.5f), yaw);
            }
            for (int i = 0; i < 8; i++)
            {
                float yaw = i * 45f;
                m.Decals.Add(new GroundDecal { Kind = 1, Center = Vec2.FromYaw(yaw) * ((MoatIn + MoatOut) * 0.5f), Half = new Vec2(3.6f, 5.2f), Rot = yaw });
            }

            // ---------------- rivers between regions (2 bridges each) ----------------
            // the river is built from 2.5 m segments; a bridge is exactly the run of skipped segments, so the wooden deck
            // and the walkable gap always match
            for (int k = 0; k < 8; k++)
            {
                float end = CoastRadius(22.5f + 45f * k) + 4f;
                float gapStart = -1f;
                for (float r = MoatOut - 1f; r < end; r += 2.5f)
                {
                    float mid = r + 1.25f;
                    bool bridge = false;
                    foreach (var (r0, r1) in RiverBridges) if (mid > r0 && mid < r1) bridge = true;
                    if (bridge) { if (gapStart < 0) gapStart = r; continue; }
                    if (gapStart >= 0) { AddRiverBridge(m, k, gapStart, r); gapStart = -1f; }
                    float yaw = RiverYaw(k, mid);
                    WaterBox(m, Vec2.FromYaw(yaw) * mid, new Vec2(RiverWidth * 0.5f, 1.65f), yaw);
                }
            }

            // ---------------- roads ----------------
            for (float yaw = 0; yaw < 360; yaw += 15f)
            {
                Path(m, Vec2.FromYaw(yaw) * RingRoad, Vec2.FromYaw(yaw + 15f) * RingRoad, 7f);
                m.Decals[m.Decals.Count - 1].Kind = 5;   // ring road segment: drawn as one smooth ring by WorldBuilder
            }
            for (int i = 0; i < 8; i++)
            {
                float yaw = i * 45f;
                Path(m, Vec2.FromYaw(yaw) * (MoatOut + 3f), Vec2.FromYaw(yaw) * (CoastRadius(yaw) - 16f), i % 2 == 0 ? 6f : 7f);
                Path(m, Vec2.FromYaw(yaw) * 14f, Vec2.FromYaw(yaw) * (MoatIn - 1f), 8f);      // city avenues
                m.Decals[m.Decals.Count - 1].Kind = 4;
            }

            // ---------------- Rilo City ----------------
            BuildCity(m, rng);

            m.Bake();

            // ---------------- spawns: 4 diagonal regions, same distance ----------------
            for (int s = 0; s < 4; s++)
            {
                float yaw = 45f + 90f * s;
                var home = FreeSpot(m, Vec2.FromYaw(yaw) * SpawnRadius, 1.5f);
                m.SpawnPoints.Add(home);
                m.Decals.Add(new GroundDecal { Kind = 2, Center = home, Radius = 7f });
            }
            // home terminals: a small compound on each spawn region's road towards the centre (same distance for everyone)
            for (int s = 0; s < 4; s++)
            {
                float yaw = 45f + 90f * s;
                var t = Vec2.FromYaw(yaw) * 128f;
                m.HomeTerminals.Add(t);
                m.Decals.Add(new GroundDecal { Kind = 2, Center = t, Radius = 6.5f });
                Box(m, ObstacleKind.Console, t + Vec2.FromYaw(yaw + 90f) * 1.6f, new Vec2(0.7f, 0.5f), yaw, 1.4f);
                for (int w = 0; w < 3; w++)
                {
                    float wy = yaw + 60f + w * 120f;   // three cover walls, three open entrances between them
                    Box(m, ObstacleKind.LowWall, t + Vec2.FromYaw(wy) * 7.5f, new Vec2(2.4f, 0.45f), wy + 90f, 1.0f);
                }
            }
            for (int s = 0; s < 4; s++)
                for (int j = 0; j < 3; j++)
                    m.SpawnPoints.Add(FreeSpot(m, m.SpawnPoints[s] + Vec2.FromYaw(j * 120f + 30f) * 4f, 1.2f));

            // ---------------- pickups: same pattern in every region ----------------
            for (int k = 0; k < 8; k++)
            {
                float b = k * 45f;
                m.CoreSpots.Add(FreeSpot(m, Vec2.FromYaw(b - 12f) * 100f, 1.2f));
                m.CoreSpots.Add(FreeSpot(m, Vec2.FromYaw(b + 12f) * 168f, 1.2f));
                m.KeySpots.Add(FreeSpot(m, Vec2.FromYaw(b + 10f) * 132f, 1.2f));
                m.KeySpots.Add(FreeSpot(m, Vec2.FromYaw(b - 14f) * 150f, 1.2f));
            }
            for (int i = 0; i < 4; i++) m.CoreSpots.Add(FreeSpot(m, Vec2.FromYaw(22.5f + 90f * i) * 40f, 1.2f));

            var reserved = new List<Vec2>();
            reserved.AddRange(m.SpawnPoints); reserved.AddRange(m.KeySpots); reserved.AddRange(m.CoreSpots);
            foreach (var t in m.HomeTerminals) for (int a = 0; a < 8; a++) reserved.Add(t + Vec2.FromYaw(a * 45f) * 5f);
            foreach (var t in m.HomeTerminals) reserved.Add(t);

            // ---------------- street furniture (solid, so nobody walks through lamps and benches) ----------------
            BuildFurniture(m, rng);

            // ---------------- regions ----------------
            BuildSnow(m, rng, reserved);
            BuildDock(m, rng, reserved);
            BuildRuins(m, rng, reserved);
            BuildBeach(m, rng, reserved);
            BuildForest(m, rng, reserved);
            BuildCanyon(m, rng, reserved);
            BuildHydro(m, rng, reserved);
            BuildOutpost(m, rng, reserved);

            m.Bake();
            return m;
        }

        // ------------------------------------------------------------------ helpers

        private static void AddRiverBridge(MapData m, int k, float g0, float g1)
        {
            float mid = (g0 + g1) * 0.5f, yaw = RiverYaw(k, mid);
            // Half.X = along the river bank (the walkable width), Half.Y = across the water (+1.5 m onto each bank)
            m.Decals.Add(new GroundDecal { Kind = 1, Center = Vec2.FromYaw(yaw) * mid, Half = new Vec2((g1 - g0) * 0.5f - 0.2f, RiverWidth * 0.5f + 1.5f), Rot = yaw + 90f });
        }

        /// <summary>Invisible collision for a big landmark model (footprint from LandmarkShapes, model space -> world).</summary>
        public static void LandmarkSolids(MapData m, (float x0, float x1, float y0, float y1, float h)[] rects, float width, Vec2 at, float yaw)
        {
            foreach (var r in rects)
            {
                // Blender model space (x, y) faces the game as (-x, -y), then the landmark's yaw
                var local = new Vec2(-(r.x0 + r.x1) * 0.5f, -(r.y0 + r.y1) * 0.5f) * width;
                var o = Box(m, ObstacleKind.Solid, at + Vec2.RotateYaw(local, yaw), new Vec2((r.x1 - r.x0) * 0.5f * width, (r.y1 - r.y0) * 0.5f * width), yaw, r.h);
                o.BlocksShots = r.h > 1.0f;
            }
        }

        public static readonly Vec2 RuinsLandmark = Vec2.FromYaw(90) * 138f;
        public static readonly Vec2 VaultLandmark = Vec2.FromYaw(8) * 152f + new Vec2(0, 10f);

        private static void WaterBox(MapData m, Vec2 c, Vec2 half, float rot)
        {
            var o = Box(m, ObstacleKind.Water, c, half, rot, 99f);
            o.BlocksShots = false;
        }

        /// <summary>Random point inside region k (between its rivers), at a radius range.</summary>
        private static Vec2 InRegion(Rng rng, int k, float r0, float r1, float edge = 6f)
        {
            float b = k * 45f;
            for (int i = 0; i < 20; i++)
            {
                float r = rng.Range(r0, r1);
                float half = 22.5f - (edge + RiverWidth) / MathF.Max(r, 1f) * MathUtil.Rad2Deg;
                float yaw = b + rng.Range(-half, half);
                if (r < CoastRadius(yaw) - 8f) return Vec2.FromYaw(yaw) * r;
            }
            return Vec2.FromYaw(b) * r0;
        }

        private static Vec2 Local(int k, float r, float dyaw) => Vec2.FromYaw(k * 45f + dyaw) * r;

        private static int Scatter(MapData m, Rng rng, List<Vec2> reserved, int k, int count, float r0, float r1,
                                   Func<Vec2, Obstacle> make, float spotClear = 3.5f, float obstacleClear = 2.6f)
        {
            int n = 0, tries = 0;
            while (n < count && tries++ < count * 30)
            {
                var p = InRegion(rng, k, r0, r1);
                if (!DecorAllowed(m, p, reserved, spotClear, obstacleClear)) continue;
                var o = make(p);
                o.Variant = rng.Int(10000);
                n++;
            }
            return n;
        }

        private static Obstacle Tree(MapData m, Vec2 p) => Circle(m, ObstacleKind.Tree, p, 0.55f, 7f);

        // ------------------------------------------------------------------ street furniture

        /// <summary>Decor obstacle categories (WorldBuilder draws Resources/Props/&lt;cat&gt;_N fitted to Height).</summary>
        public static readonly string[] DecorCats = { "streetlight", "planter", "bench", "parasol", "dumpster", "cone" };

        private static void Decor(MapData m, int cat, Vec2 p, float yaw, float r, float h, int v)
        {
            if (m.IsBlockedForStanding(p, r + 0.3f)) return;
            foreach (var q in m.SpawnPoints) if (Vec2.Dist(p, q) < 4f) return;
            foreach (var q in m.CoreSpots) if (Vec2.Dist(p, q) < 2.5f) return;
            foreach (var q in m.KeySpots) if (Vec2.Dist(p, q) < 2.5f) return;
            foreach (var q in m.HomeTerminals) if (Vec2.Dist(p, q) < 9f) return;
            var o = Circle(m, ObstacleKind.Decor, p, r, h);
            o.Rot = yaw; o.Variant = cat * 1000 + Math.Abs(v) % 1000;
            o.BlocksShots = cat != 0 && cat != 3;   // thin lamp posts / parasol poles don't stop bolts
        }

        private static void BuildFurniture(MapData m, Rng rng)
        {
            for (int i = 0; i < 8; i++)
            {
                float yaw = i * 45f;
                var dir = Vec2.FromYaw(yaw); var side = Vec2.FromYaw(yaw + 90f);
                for (float r = 20f; r < MoatIn - 3f; r += 9f)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Decor(m, 0, dir * r + side * (s * 5.4f), yaw + (s > 0 ? 180 : 0), 0.25f, 4.6f, i);
                        Decor(m, i % 2 == 0 ? 1 : 2, dir * (r + 4.5f) + side * (s * 5.6f), yaw + (s > 0 ? -90 : 90), 0.6f, 0.9f, i + (int)r);
                    }
            }
            for (float yaw = 7.5f; yaw < 360; yaw += 15f) Decor(m, 0, Vec2.FromYaw(yaw) * (RingRoad + 4.8f), yaw + 180, 0.25f, 4.6f, (int)yaw);
            for (int i = 0; i < 8; i++) Decor(m, 2, Vec2.FromYaw(i * 45f + 22.5f) * 17.5f, i * 45f + 22.5f, 0.6f, 0.9f, i);
            for (int i = 0; i < 14; i++)
            {
                var p = Vec2.FromYaw(135f + rng.Range(-17f, 17f)) * rng.Range(140f, 178f);
                if (BiomeAt(p) == Biome.Beach) Decor(m, 3, p, rng.Range(0, 360), 0.3f, 2.6f, i);
            }
            var blocks = new List<Obstacle>();
            foreach (var o in m.Obstacles) if (o.Kind == ObstacleKind.CityBlock) blocks.Add(o);
            foreach (var o in blocks)
            {
                if (o.Variant % 3 != 0) continue;
                var p = o.Center + Vec2.FromYaw(o.Variant % 360) * (Math.Max(o.Half.X, o.Half.Y) + 1.4f);
                bool bin = o.Variant % 2 == 0;
                Decor(m, bin ? 4 : 5, p, o.Variant % 90, bin ? 0.75f : 0.35f, bin ? 1.4f : 0.8f, o.Variant);
            }
            // consoles around the Rilo tower (central terminal)
            for (int i = 0; i < 4; i++)
            {
                float yaw = 45f + 90f * i;
                Box(m, ObstacleKind.Solid, Vec2.FromYaw(yaw) * 5.9f, new Vec2(0.6f, 0.5f), yaw, 1.4f);
            }
        }

        // ------------------------------------------------------------------ Rilo City

        private static void BuildCity(MapData m, Rng rng)
        {
            // central plaza: tower core, low walls (cover), pillars — the Hack Terminal and the Vault
            m.Decals.Add(new GroundDecal { Kind = 2, Center = Vec2.Zero, Radius = 18f });
            Circle(m, ObstacleKind.TowerCore, Vec2.Zero, 4.6f, 40f);   // the Meshy Rilo tower base
            Box(m, ObstacleKind.Solid, Vec2.Zero, new Vec2(5.3f, 5.3f), 45f, 1.6f);   // its square plinth
            for (int i = 0; i < 4; i++)
            {
                float yaw = 45 + 90 * i;
                Box(m, ObstacleKind.LowWall, Vec2.FromYaw(yaw) * 6.8f, new Vec2(2.2f, 0.4f), yaw + 90, 1.0f);
            }
            for (int i = 0; i < 8; i++)
                Circle(m, ObstacleKind.Pillar, Vec2.FromYaw(22.5f + 45 * i) * 13.5f, 0.6f, 4f);

            // city blocks on a grid, avenues kept clear along the 8 bridge directions
            for (float x = -56; x <= 56; x += 15f)
                for (float y = -56; y <= 56; y += 15f)
                {
                    var c = new Vec2(x + rng.Range(-1.5f, 1.5f), y + rng.Range(-1.5f, 1.5f));
                    float r = c.Length;
                    if (r < 24f || r > CityRadius - 7f) continue;
                    float bearing = ((c.Yaw % 45f) + 45f) % 45f;
                    if (MathF.Min(bearing, 45f - bearing) * MathUtil.Deg2Rad * r < 7.5f) continue;   // avenue
                    if (Vec2.Dist(c, Vec2.FromYaw(0) * 42f) < 17f) continue;                         // market square (big market landmark)
                    var o = Box(m, ObstacleKind.CityBlock, c, new Vec2(rng.Range(4.2f, 5.6f), rng.Range(4.2f, 5.6f)), 0, rng.Range(10f, 26f));
                    o.Variant = rng.Int(10000);
                }
            // market hall (big Meshy landmark): roof posts + stall counters, open in the middle and at both ends
            var mk = Vec2.FromYaw(0) * 42f;
            foreach (float x in new[] { -10.45f, -5.2f, 0f, 5.2f, 10.45f })
                foreach (float y in new[] { -9.35f, 9.35f })
                    Circle(m, ObstacleKind.Solid, mk + new Vec2(x, y), 0.35f, 6f);
            foreach (float y in new[] { -7.4f, 7.4f })
                foreach (var (x0, x1) in new[] { (-9.6f, -5.8f), (-4.1f, -1.4f), (1.4f, 4.1f), (5.8f, 9.6f) })
                    Box(m, ObstacleKind.Solid, mk + new Vec2((x0 + x1) * 0.5f, y), new Vec2((x1 - x0) * 0.5f, 0.6f), 0, 1.0f);
            // street cover: low walls and crates on the avenues
            for (int i = 0; i < 8; i++)
            {
                float yaw = i * 45f;
                Box(m, ObstacleKind.LowWall, Vec2.FromYaw(yaw + 9f) * 34f, new Vec2(2.4f, 0.45f), yaw, 1.0f);
                Box(m, ObstacleKind.Crate, Vec2.FromYaw(yaw - 7f) * 50f, new Vec2(0.8f, 0.8f), yaw + 20, 1.0f);
            }
        }

        // ------------------------------------------------------------------ regions

        private static void BuildSnow(MapData m, Rng rng, List<Vec2> reserved)
        {
            const int k = 0;
            LandmarkSolids(m, LandmarkShapes.Vault, LandmarkShapes.VaultWidth, VaultLandmark, 180f);   // big vault landmark, door facing the zone
            for (int i = 0; i < 5; i++)
            {
                var hp = Local(k, 125f + i % 2 * 22f, -14f + i * 7f);
                if (Vec2.Dist(hp, m.Zone(ZoneType.Vault).Center) < 16f) continue;   // keep the vault square clear
                Box(m, ObstacleKind.Hut, hp, new Vec2(3.2f, 2.4f), rng.Range(0, 90), 4f).Variant = i;
            }
            Circle(m, ObstacleKind.Watchtower, Local(k, 108f, 10f), 1.6f, 12f);
            Box(m, ObstacleKind.LowWall, Local(k, 140f, 0f), new Vec2(3f, 0.5f), 90, 1.1f);
            Scatter(m, rng, reserved, k, 55, 80f, 182f, p => Circle(m, ObstacleKind.Pine, p, 0.6f, 8f));
            Scatter(m, rng, reserved, k, 10, 80f, 180f, p => Circle(m, ObstacleKind.Rock, p, rng.Range(1f, 2.2f), rng.Range(1.4f, 3f)));
        }

        private static void BuildDock(MapData m, Rng rng, List<Vec2> reserved)
        {
            const int k = 1;
            // container yard: rows of stacks (cover + lanes)
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 4; col++)
                {
                    float r = 95f + row * 9f, dy = -13f + col * 8.5f;
                    if ((row + col) % 3 == 0) continue;
                    var c = Local(k, r, dy);
                    var o = Box(m, ObstacleKind.Container, c, new Vec2(1.3f, 3.1f), k * 45f + 90f, (row * 7 + col) % 2 == 0 ? 5.2f : 2.6f);
                    o.Variant = row * 4 + col;
                }
            Circle(m, ObstacleKind.Crane, Local(k, 168f, -9f), 4.8f, 22f);   // radius covers the crane leg frame
            Circle(m, ObstacleKind.Crane, Local(k, 168f, 9f), 4.8f, 22f);
            Box(m, ObstacleKind.Building, Local(k, 136f, -13f), new Vec2(6f, 4.5f), k * 45f, 8f).Variant = 4;
            Box(m, ObstacleKind.Building, Local(k, 136f, 13f), new Vec2(6f, 4.5f), k * 45f, 8f).Variant = 6;
            Scatter(m, rng, reserved, k, 14, 80f, 180f, p => Box(m, ObstacleKind.Crate, p, new Vec2(0.8f, 0.8f), rng.Range(0, 90), 1.0f));
        }

        private static void BuildRuins(MapData m, Rng rng, List<Vec2> reserved)
        {
            const int k = 2;
            var c = m.Zone(ZoneType.Ruins).Center;
            // temple ruins landmark: the raised temple + colonnade block, the lower stairs stay walkable
            LandmarkSolids(m, LandmarkShapes.Ruins, LandmarkShapes.RuinsWidth, RuinsLandmark, 90f);
            Scatter(m, rng, reserved, k, 30, 80f, 182f, p => Tree(m, p));
            Scatter(m, rng, reserved, k, 14, 80f, 180f, p => Circle(m, ObstacleKind.Rock, p, rng.Range(1f, 2.2f), rng.Range(1.4f, 3f)));
            Scatter(m, rng, reserved, k, 8, 90f, 175f, p => Circle(m, ObstacleKind.Pillar, p, 0.7f, rng.Range(1f, 3.6f)));
        }

        private static void BuildBeach(MapData m, Rng rng, List<Vec2> reserved)
        {
            const int k = 3;
            for (int i = 0; i < 4; i++) Box(m, ObstacleKind.Hut, Local(k, 120f + (i % 2) * 30f, -12f + i * 8f), new Vec2(2.6f, 2.2f), rng.Range(0, 90), 3.6f).Variant = 10 + i;
            Circle(m, ObstacleKind.Watchtower, Local(k, 172f, 0f), 1.4f, 9f);
            Scatter(m, rng, reserved, k, 34, 80f, 182f, p => Circle(m, ObstacleKind.Palm, p, 0.5f, 8f));
            Scatter(m, rng, reserved, k, 10, 90f, 180f, p => Circle(m, ObstacleKind.Rock, p, rng.Range(1f, 2f), rng.Range(1.2f, 2.6f)));
        }

        private static void BuildForest(MapData m, Rng rng, List<Vec2> reserved)
        {
            const int k = 4;
            for (int i = 0; i < 3; i++) Box(m, ObstacleKind.Hut, Local(k, 120f + i * 18f, -8f + i * 8f), new Vec2(3f, 2.4f), rng.Range(0, 90), 4f).Variant = 20 + i;
            Scatter(m, rng, reserved, k, 95, 80f, 182f, p => Tree(m, p), 3.2f, 2.2f);
            Scatter(m, rng, reserved, k, 14, 80f, 180f, p => Circle(m, ObstacleKind.Rock, p, rng.Range(1f, 2.2f), rng.Range(1.4f, 3f)));
        }

        private static void BuildCanyon(MapData m, Rng rng, List<Vec2> reserved)
        {
            const int k = 5;
            Scatter(m, rng, reserved, k, 16, 85f, 178f, p => Circle(m, ObstacleKind.Mesa, p, rng.Range(4f, 8f), rng.Range(9f, 16f)), 14f, 4f);   // spires stay well clear of spawns and pickups
            Scatter(m, rng, reserved, k, 24, 80f, 180f, p => Circle(m, ObstacleKind.Rock, p, rng.Range(1.2f, 2.6f), rng.Range(1.6f, 3.4f)));
            for (int i = 0; i < 2; i++) Box(m, ObstacleKind.Hut, Local(k, 112f + i * 30f, 14f - i * 6f), new Vec2(3f, 2.4f), rng.Range(0, 90), 4f).Variant = 30 + i;
        }

        private static void BuildHydro(MapData m, Rng rng, List<Vec2> reserved)
        {
            const int k = 6;
            var rc = m.Zone(ZoneType.Reactor).Center;
            // reactor landmark: the energy ball and its 4 pylons are solid; the deck is walkable
            Circle(m, ObstacleKind.Solid, rc, 3.6f, 7f);
            for (int i = 0; i < 4; i++)
            {
                Circle(m, ObstacleKind.Solid, rc + Vec2.FromYaw(45 + 90 * i) * 7.4f, 0.6f, 9f);
            }
            for (int i = 0; i < 4; i++) Circle(m, ObstacleKind.Tank, Local(k, 108f + (i % 2) * 14f, i < 2 ? -12f : 12f), 3.2f, 7f);
            // the dam: a long wall towards the coast with gaps
            Box(m, ObstacleKind.Wall, Local(k, 170f, -8f), new Vec2(9f, 1.2f), k * 45f + 90f, 9f);
            Box(m, ObstacleKind.Wall, Local(k, 170f, 9f), new Vec2(7f, 1.2f), k * 45f + 90f, 9f);
            Scatter(m, rng, reserved, k, 30, 80f, 182f, p => Tree(m, p));
            Scatter(m, rng, reserved, k, 8, 80f, 180f, p => Box(m, ObstacleKind.Crate, p, new Vec2(0.8f, 0.8f), rng.Range(0, 90), 1.0f));
        }

        private static void BuildOutpost(MapData m, Rng rng, List<Vec2> reserved)
        {
            const int k = 7;
            Circle(m, ObstacleKind.Watchtower, Local(k, 112f, -10f), 1.6f, 12f);
            Circle(m, ObstacleKind.Watchtower, Local(k, 172f, 6f), 1.6f, 12f);
            for (int i = 0; i < 4; i++) Box(m, ObstacleKind.Hut, Local(k, 122f + (i / 2) * 22f, -9f + (i % 2) * 18f), new Vec2(3f, 2.4f), k * 45f, 4f).Variant = 40 + i;
            for (int i = 0; i < 6; i++) Box(m, ObstacleKind.LowWall, Local(k, 100f + i * 12f, i % 2 == 0 ? -4f : 5f), new Vec2(2.2f, 0.5f), k * 45f + 90f, 1.0f);
            Scatter(m, rng, reserved, k, 40, 80f, 182f, p => Tree(m, p));
            Scatter(m, rng, reserved, k, 10, 80f, 180f, p => Circle(m, ObstacleKind.Rock, p, rng.Range(1f, 2.2f), rng.Range(1.4f, 3f)));
        }
    }
}
