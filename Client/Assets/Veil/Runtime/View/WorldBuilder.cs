using System.Collections.Generic;
using UnityEngine;
using Veil.Sim;

namespace Veil.View
{
    /// <summary>
    /// Builds the visual arena from <see cref="MapData"/>. Static geometry is batched; animated
    /// landmarks (tower crystal, reactor, vault door, waterfalls) stay as live objects.
    /// </summary>
    public sealed partial class WorldBuilder
    {
        public Transform Root { get; private set; }
        public Transform TowerCrystal { get; private set; }
        public Transform ReactorCore { get; private set; }
        public readonly List<Transform> ReactorRings = new List<Transform>();
        public Transform VaultDoor { get; private set; }
        public Material VaultDoorMat { get; private set; }

        private readonly MapData _map;
        private readonly StaticBatcher _batch = new StaticBatcher(40f);
        private System.Random _rnd;

        public WorldBuilder(MapData map) { _map = map; }

        public Transform BuildAll()
        {
            _rnd = new System.Random(ArenaMap.MapSeed);
            Root = new GameObject("Arena").transform;

            if (_map.Island) BuildIslandGround(); else BuildGround();
            foreach (var d in _map.Decals) BuildDecal(d);
            foreach (var o in _map.Obstacles) BuildObstacle(o);
            BuildScatter();
            if (_map.Island) BuildIslandSurroundings(); else BuildSurroundings();

            _batch.Bake(Build.Node(Root, "Static", Vector3.zero));
            Root.gameObject.AddComponent<WorldAnimator>().Init(this);
            return Root;
        }

        private float R(float a, float b) => a + (float)_rnd.NextDouble() * (b - a);

        private void Add(Mesh m, Material mat, Vector3 pos, Vector3 scale, Quaternion? rot = null, bool shadows = true)
            => _batch.Add(m, mat, pos, scale, rot ?? Quaternion.identity, shadows);

        // ------------------------------------------------------------------ ground

        private void BuildGround()
        {
            float size = _map.Half * 2 + 6;
            Add(MeshGen.GroundQuad, MaterialLib.Toon(Palette.Grass, 0f), new Vector3(0, 0, 0), new Vector3(size, 1, size), null, false);
            // grass colour variation patches
            for (int i = 0; i < 70; i++)
            {
                var p = new Vector3(R(-70, 70), 0.004f + i * 0.00005f, R(-70, 70));
                float s = R(5, 16);
                var c = i % 3 == 0 ? Palette.GrassLight : Palette.GrassDark;
                Add(MeshGen.Disc(20), MaterialLib.Toon(Color.Lerp(Palette.Grass, c, 0.55f), 0f), p, new Vector3(s, 1, s * R(0.6f, 1f)), Quaternion.Euler(0, R(0, 180), 0), false);
            }
            // plateau side walls (the arena is a floating island)
            var cliff = MaterialLib.Toon(Palette.CliffDark, 0.1f);
            float h = 18f, e = _map.Half + 3;
            Add(MeshGen.Box, cliff, new Vector3(0, -h / 2, e), new Vector3(e * 2, h, 1), null, false);
            Add(MeshGen.Box, cliff, new Vector3(0, -h / 2, -e), new Vector3(e * 2, h, 1), null, false);
            Add(MeshGen.Box, cliff, new Vector3(e, -h / 2, 0), new Vector3(1, h, e * 2), null, false);
            Add(MeshGen.Box, cliff, new Vector3(-e, -h / 2, 0), new Vector3(1, h, e * 2), null, false);
        }

        private void BuildDecal(GroundDecal d)
        {
            switch (d.Kind)
            {
                case 0: // path
                {
                    var rot = Quaternion.Euler(0, d.Rot, 0);
                    Add(MeshGen.GroundQuad, MaterialLib.Toon(Palette.PathDark, 0f), Build.V(d.Center, 0.012f), new Vector3(d.Half.X * 2 + 0.8f, 1, d.Half.Y * 2 + 0.4f), rot, false);
                    Add(MeshGen.GroundQuad, MaterialLib.Toon(Palette.Path, 0f), Build.V(d.Center, 0.02f), new Vector3(d.Half.X * 2, 1, d.Half.Y * 2), rot, false);
                    // paving stones
                    int n = (int)(d.Half.Y * 2 / 2.2f);
                    for (int i = 0; i < n; i++)
                    {
                        var local = new Vec2(R(-d.Half.X * 0.7f, d.Half.X * 0.7f), -d.Half.Y + (i + 0.5f) * (d.Half.Y * 2 / n));
                        var w = d.Center + Vec2.RotateYaw(local, d.Rot);
                        Add(MeshGen.Cylinder(6, true), MaterialLib.Toon(Palette.StoneLight, 0f), Build.V(w, 0.03f), new Vector3(R(0.6f, 1.1f), 0.03f, R(0.5f, 0.9f)), Quaternion.Euler(0, R(0, 90), 0), false);
                    }
                    break;
                }
                case 1: // bridge
                {
                    var wood = MaterialLib.Toon(Palette.Wood, 0.2f);
                    var woodD = MaterialLib.Toon(Palette.WoodDark, 0.2f);
                    bool alongZ = d.Half.Y > d.Half.X;
                    var br = Quaternion.Euler(0, d.Rot, 0);
                    Vector3 c = Build.V(d.Center, 0.18f);
                    Add(MeshGen.Box, wood, c, new Vector3(d.Half.X * 2, 0.3f, d.Half.Y * 2), br);
                    int planks = 9;
                    for (int i = 0; i < planks; i++)
                    {
                        float t = -1 + (i + 0.5f) * 2f / planks;
                        var off = alongZ ? new Vector3(0, 0.17f, t * d.Half.Y) : new Vector3(t * d.Half.X, 0.17f, 0);
                        var sc = alongZ ? new Vector3(d.Half.X * 2, 0.04f, 0.08f) : new Vector3(0.08f, 0.04f, d.Half.Y * 2);
                        Add(MeshGen.Box, woodD, c + br * off, sc, br, false);
                    }
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var railOff = alongZ ? new Vector3(s * (d.Half.X - 0.15f), 0.75f, 0) : new Vector3(0, 0.75f, s * (d.Half.Y - 0.15f));
                        var railSc = alongZ ? new Vector3(0.18f, 0.14f, d.Half.Y * 2) : new Vector3(d.Half.X * 2, 0.14f, 0.18f);
                        Add(MeshGen.Box, woodD, c + br * railOff, railSc, br);
                        for (int k = -1; k <= 1; k++)
                        {
                            var postOff = alongZ ? new Vector3(s * (d.Half.X - 0.15f), 0.45f, k * d.Half.Y * 0.9f) : new Vector3(k * d.Half.X * 0.9f, 0.45f, s * (d.Half.Y - 0.15f));
                            Add(MeshGen.Box, wood, c + br * postOff, new Vector3(0.22f, 0.8f, 0.22f), br);
                        }
                    }
                    break;
                }
                case 2: // plaza
                {
                    float r = d.Radius;
                    Add(MeshGen.Disc(48), MaterialLib.Toon(Palette.StoneDark, 0f), Build.V(d.Center, 0.03f), new Vector3(r * 2 + 1.2f, 1, r * 2 + 1.2f), null, false);
                    Add(MeshGen.Disc(48), MaterialLib.Toon(Palette.Stone, 0f), Build.V(d.Center, 0.035f), new Vector3(r * 2, 1, r * 2), null, false);
                    Add(MeshGen.Ring(0.36f, 0.4f, 48), MaterialLib.Toon(Palette.StoneLight, 0f), Build.V(d.Center, 0.04f), new Vector3(r * 2, 1, r * 2), null, false);
                    Add(MeshGen.Ring(0.2f, 0.23f, 40), MaterialLib.Toon(Palette.StoneLight, 0f), Build.V(d.Center, 0.04f), new Vector3(r * 2, 1, r * 2), null, false);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ obstacles

        private void BuildObstacle(Obstacle o)
        {
            var rot = Quaternion.Euler(0, o.Rot, 0);
            Vector3 c = Build.V(o.Center);
            var stone = MaterialLib.Toon(Palette.Stone, 0.25f);
            var stoneD = MaterialLib.Toon(Palette.StoneDark, 0.2f);
            var stoneL = MaterialLib.Toon(Palette.StoneLight, 0.25f);
            if (_map.Island && TryProp(o, c)) return;
            switch (o.Kind)
            {
                case ObstacleKind.Wall:
                case ObstacleKind.LowWall:
                {
                    Vector3 size = new Vector3(o.Half.X * 2, o.Height, o.Half.Y * 2);
                    Add(MeshGen.Box, o.Kind == ObstacleKind.LowWall ? stoneD : stone, c + Vector3.up * (o.Height / 2), size, rot);
                    Add(MeshGen.Box, stoneL, c + Vector3.up * (o.Height + 0.08f), new Vector3(size.x + 0.15f, 0.16f, size.z + 0.15f), rot);
                    if (o.Kind == ObstacleKind.Wall)
                    {
                        int bricks = Mathf.Max(1, (int)(Mathf.Max(size.x, size.z) / 1.4f));
                        for (int i = 0; i < bricks; i++)
                        {
                            if (_rnd.NextDouble() < 0.45) continue;
                            float t = -0.5f + (i + 0.5f) / bricks;
                            Vector3 local = size.x > size.z ? new Vector3(t * size.x, o.Height + 0.35f, 0) : new Vector3(0, o.Height + 0.35f, t * size.z);
                            Add(MeshGen.Box, stone, c + rot * local, new Vector3(0.7f, 0.5f, 0.7f) * R(0.8f, 1.1f), rot * Quaternion.Euler(0, R(-10, 10), 0));
                        }
                    }
                    break;
                }
                case ObstacleKind.Pillar:
                {
                    bool tower = o.Center.Length < 20f;
                    Add(MeshGen.Box, stoneD, c + Vector3.up * 0.2f, new Vector3(o.Radius * 2.6f, 0.4f, o.Radius * 2.6f));
                    Add(MeshGen.Cylinder(8, true), stone, c + Vector3.up * (o.Height / 2), new Vector3(o.Radius * 2, o.Height, o.Radius * 2));
                    if (o.Height > 2)
                    {
                        Add(MeshGen.Box, stoneL, c + Vector3.up * (o.Height + 0.15f), new Vector3(o.Radius * 2.5f, 0.3f, o.Radius * 2.5f));
                        if (tower) Add(MeshGen.Octahedron, MaterialLib.Glow(Palette.Tower, 2.5f), c + Vector3.up * (o.Height + 0.7f), new Vector3(0.5f, 0.8f, 0.5f), null, false);
                    }
                    break;
                }
                case ObstacleKind.Tree: BuildTree(o, c); break;
                case ObstacleKind.Rock:
                {
                    var m = MeshGen.Icosphere(1, 0.18f, o.Variant % 7);
                    var col = Color.Lerp(Palette.StoneDark, Palette.Stone, (o.Variant % 5) / 5f);
                    Add(m, MaterialLib.Toon(col, 0.2f), c + Vector3.up * (o.Height * 0.4f), new Vector3(o.Radius * 2.3f, o.Height * 1.25f, o.Radius * 2.1f), Quaternion.Euler(0, o.Variant % 360, 0));
                    Add(MeshGen.Icosphere(1, 0.2f, (o.Variant + 3) % 7), MaterialLib.Toon(Palette.StoneDark, 0.2f), c + new Vector3(o.Radius * 0.9f, 0.2f, o.Radius * 0.5f), Vector3.one * o.Radius * 0.9f);
                    break;
                }
                case ObstacleKind.Water when _map.Island: break;
                case ObstacleKind.Water:
                {
                    Vector3 size = new Vector3(o.Half.X * 2, 1, o.Half.Y * 2);
                    Add(MeshGen.GroundQuad, MaterialLib.Water(), c + Vector3.up * 0.06f, size, rot, false);
                    // stone curbs along the long sides
                    bool alongX = o.Half.X > o.Half.Y;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Vector3 off = alongX ? new Vector3(0, 0.15f, s * (o.Half.Y + 0.2f)) : new Vector3(s * (o.Half.X + 0.2f), 0.15f, 0);
                        Vector3 sc = alongX ? new Vector3(size.x + 0.8f, 0.3f, 0.4f) : new Vector3(0.4f, 0.3f, size.z + 0.8f);
                        Add(MeshGen.Box, stoneL, c + off, sc);
                    }
                    break;
                }
                case ObstacleKind.Building: BuildHouse(o, c, rot); break;
                case ObstacleKind.Crate:
                {
                    var wood = MaterialLib.Toon(Palette.Wood, 0.25f);
                    var woodD = MaterialLib.Toon(Palette.WoodDark, 0.2f);
                    Vector3 s = new Vector3(o.Half.X * 2, o.Height * 0.95f, o.Half.Y * 2);
                    Add(MeshGen.Box, wood, c + Vector3.up * (s.y / 2), s, rot);
                    Add(MeshGen.Box, woodD, c + Vector3.up * (s.y / 2), new Vector3(s.x * 1.02f, s.y * 0.18f, s.z * 1.02f), rot);
                    Add(MeshGen.Box, woodD, c + Vector3.up * (s.y / 2), new Vector3(s.x * 0.18f, s.y * 1.02f, s.z * 1.02f), rot);
                    break;
                }
                case ObstacleKind.Cliff:
                {
                    var m = MeshGen.Icosphere(1, 0.22f, o.Variant % 11);
                    var col = Color.Lerp(Palette.CliffDark, Palette.Cliff, (o.Variant % 7) / 7f);
                    Add(m, MaterialLib.Toon(col, 0.15f), c + Vector3.down * 3f, new Vector3(o.Radius * 2.4f, 20f, o.Radius * 2.4f), Quaternion.Euler(0, o.Variant % 360, 0));
                    Add(MeshGen.Icosphere(1, 0.25f, (o.Variant + 5) % 11), MaterialLib.Toon(Palette.GrassDark, 0.1f), c + Vector3.up * 6.4f, new Vector3(o.Radius * 1.6f, 1.2f, o.Radius * 1.6f), Quaternion.Euler(0, o.Variant % 90, 0));
                    break;
                }
                case ObstacleKind.Stall: BuildStall(o, c, rot); break;
                case ObstacleKind.TowerCore: if (_map.Island) BuildRiloTower(c); else BuildTower(c); break;
                case ObstacleKind.ReactorCore: BuildReactor(c); break;
                case ObstacleKind.VaultBuilding: BuildVault(o, c, rot); break;
                case ObstacleKind.CityBlock: BuildCityBlock(o, c); break;
                case ObstacleKind.Container: BuildContainer(o, c, rot); break;
                case ObstacleKind.Crane: BuildCrane(o, c); break;
                case ObstacleKind.Hut: BuildHouse(o, c, rot); break;
                case ObstacleKind.Palm: BuildPalm(o, c); break;
                case ObstacleKind.Pine: BuildPine(o, c); break;
                case ObstacleKind.Mesa: BuildMesa(o, c); break;
                case ObstacleKind.Tank: BuildTank(o, c); break;
                case ObstacleKind.Watchtower: BuildWatchtower(o, c); break;
                case ObstacleKind.Pylon:
                {
                    var metal = MaterialLib.Toon(Palette.Hex("#3b3752"), 0.3f, 0.6f);
                    Add(MeshGen.Cylinder(8, true), metal, c + Vector3.up * (o.Height / 2), new Vector3(o.Radius * 2, o.Height, o.Radius * 2));
                    Add(MeshGen.Octahedron, MaterialLib.Glow(Palette.Reactor, 3f), c + Vector3.up * (o.Height + 0.4f), new Vector3(0.5f, 0.9f, 0.5f), null, false);
                    Add(MeshGen.Torus(0.12f), MaterialLib.Glow(Palette.Reactor, 2f), c + Vector3.up * (o.Height * 0.6f), new Vector3(1.3f, 1f, 1.3f), null, false);
                    break;
                }
            }
        }

        private void BuildTree(Obstacle o, Vector3 c)
        {
            int v = o.Variant;
            var trunk = MaterialLib.Toon(Palette.Wood, 0.2f);
            float h = 2.2f + (v % 7) * 0.12f;
            Add(MeshGen.Cylinder(6, true), trunk, c + Vector3.up * (h / 2), new Vector3(0.45f, h, 0.45f), Quaternion.Euler(0, v % 60, 0));
            Color leaf;
            int k = v % 20;
            if (k < 2) leaf = Palette.Hex("#ff9fcf");       // blossom
            else if (k < 4) leaf = Palette.Hex("#ff9a3c");  // autumn
            else if (k < 11) leaf = Palette.Hex("#57b947");
            else leaf = Palette.Hex("#3f9e45");
            var leafMat = MaterialLib.Toon(leaf, 0.35f);
            var leafMatD = MaterialLib.Toon(leaf * 0.85f, 0.3f);
            if (v % 5 == 0)
            {
                // pine: stacked cones
                for (int i = 0; i < 3; i++)
                    Add(MeshGen.Cone(7), i % 2 == 0 ? leafMat : leafMatD, c + Vector3.up * (h + 0.3f + i * 1.1f), new Vector3(2.8f - i * 0.7f, 2.0f, 2.8f - i * 0.7f), Quaternion.Euler(0, v % 50 + i * 20, 0));
            }
            else
            {
                float s = 2.6f + (v % 9) * 0.12f;
                Add(MeshGen.Icosphere(1, 0.12f, v % 5), leafMat, c + Vector3.up * (h + s * 0.35f), new Vector3(s, s * 0.9f, s), Quaternion.Euler(0, v % 360, 0));
                Add(MeshGen.Icosphere(1, 0.14f, (v + 1) % 5), leafMatD, c + new Vector3(0.6f, h + s * 0.15f, -0.4f), Vector3.one * s * 0.6f, Quaternion.Euler(0, v % 180, 0));
                Add(MeshGen.Icosphere(1, 0.14f, (v + 2) % 5), leafMat, c + new Vector3(-0.5f, h + s * 0.2f, 0.5f), Vector3.one * s * 0.55f);
            }
        }

        private void BuildHouse(Obstacle o, Vector3 c, Quaternion rot)
        {
            Color[] roofs = { Palette.RoofBlue, Palette.RoofRed, Palette.RoofPurple, Palette.Hex("#3aa6a0") };
            var wall = MaterialLib.Toon(Palette.Hex("#efe6d6"), 0.2f);
            var stone = MaterialLib.Toon(Palette.Stone, 0.2f);
            var wood = MaterialLib.Toon(Palette.WoodDark, 0.2f);
            var roof = MaterialLib.Toon(roofs[o.Variant % roofs.Length], 0.35f, 0.3f);
            var window = MaterialLib.Glow(Palette.Hex("#ffd98a"), 1.4f);
            float w = o.Half.X * 2, d = o.Half.Y * 2, h = o.Height;
            float baseH = h * 0.32f, upperH = h * 0.3f;
            Add(MeshGen.Box, stone, c + rot * new Vector3(0, baseH / 2, 0), new Vector3(w, baseH, d), rot);
            Add(MeshGen.Box, wall, c + rot * new Vector3(0, baseH + upperH / 2, 0), new Vector3(w + 0.3f, upperH, d + 0.3f), rot);
            Add(MeshGen.Box, wood, c + rot * new Vector3(0, baseH + 0.08f, 0), new Vector3(w + 0.45f, 0.16f, d + 0.45f), rot);
            bool ridgeX = w >= d;
            var roofRot = rot * Quaternion.Euler(0, ridgeX ? 0 : 90, 0);
            float rw = (ridgeX ? w : d) + 0.9f, rd = (ridgeX ? d : w) + 1.2f;
            Add(MeshGen.Roof, roof, c + rot * new Vector3(0, baseH + upperH, 0), new Vector3(rw, h - baseH - upperH, rd), roofRot);
            Add(MeshGen.Box, stone, c + rot * new Vector3(w * 0.3f, h * 0.85f, 0), new Vector3(0.6f, h * 0.4f, 0.6f), rot);
            // door + windows on all faces
            for (int f = 0; f < 4; f++)
            {
                var faceRot = rot * Quaternion.Euler(0, f * 90, 0);
                float halfDepth = (f % 2 == 0 ? d : w) / 2 + 0.16f;
                float halfWidth = (f % 2 == 0 ? w : d) / 2;
                if (f == 0) Add(MeshGen.Box, wood, c + faceRot * new Vector3(0, 0.9f, halfDepth - 0.1f), new Vector3(1.1f, 1.8f, 0.12f), faceRot);
                for (int k = -1; k <= 1; k += 2)
                    Add(MeshGen.Box, window, c + faceRot * new Vector3(k * halfWidth * 0.5f, baseH + upperH * 0.5f, halfDepth), new Vector3(0.7f, 0.8f, 0.08f), faceRot, false);
            }
        }

        private void BuildStall(Obstacle o, Vector3 c, Quaternion rot)
        {
            Color[] awn = { Palette.RoofRed, Palette.Hex("#ffcf3f"), Palette.RoofBlue, Palette.Hex("#4dd68a"), Palette.RoofPurple };
            var wood = MaterialLib.Toon(Palette.Wood, 0.2f);
            var a1 = MaterialLib.Toon(awn[o.Variant % awn.Length], 0.3f);
            var a2 = MaterialLib.Toon(Color.white, 0.2f);
            float w = o.Half.X * 2, d = o.Half.Y * 2;
            Add(MeshGen.Box, wood, c + Vector3.up * 0.5f, new Vector3(w, 1f, d), rot);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Add(MeshGen.Box, wood, c + rot * new Vector3(sx * (w / 2 - 0.1f), 1.3f, sz * (d / 2 - 0.1f)), new Vector3(0.14f, 2.6f, 0.14f), rot);
            int stripes = 6;
            for (int i = 0; i < stripes; i++)
            {
                float t = -w / 2 + (i + 0.5f) * w / stripes;
                Add(MeshGen.Box, i % 2 == 0 ? a1 : a2, c + rot * new Vector3(t, 2.65f, 0), new Vector3(w / stripes, 0.12f, d + 0.6f), rot * Quaternion.Euler(12, 0, 0));
            }
            Color[] goods = { Palette.Energy, Palette.Gold, Palette.Danger, Palette.Ruins, Palette.Reactor };
            for (int i = 0; i < 4; i++)
                Add(MeshGen.SphereLow, MaterialLib.Toon(goods[(o.Variant + i) % goods.Length], 0.3f, 0.5f), c + rot * new Vector3(-w / 2 + 0.35f + i * (w - 0.7f) / 3f, 1.15f, 0), Vector3.one * 0.3f);
        }

        private void BuildTower(Vector3 c)
        {
            var stone = MaterialLib.Toon(Palette.Stone, 0.3f);
            var stoneL = MaterialLib.Toon(Palette.StoneLight, 0.3f);
            var trim = MaterialLib.Toon(Palette.Hex("#5b6bd6"), 0.3f, 0.3f);
            var glow = MaterialLib.Glow(Palette.Tower, 2.4f);
            Add(MeshGen.Cylinder(12, true), stoneL, c + Vector3.up * 0.6f, new Vector3(7.2f, 1.2f, 7.2f));
            Add(MeshGen.Cylinder(12, true), stone, c + Vector3.up * 3f, new Vector3(5.6f, 4.6f, 5.6f));
            Add(MeshGen.Torus(0.06f), glow, c + Vector3.up * 5.4f, new Vector3(5.8f, 3f, 5.8f), null, false);
            Add(MeshGen.Cylinder(10, true), stone, c + Vector3.up * 10f, new Vector3(4f, 10f, 4f));
            for (int i = 0; i < 3; i++)
                Add(MeshGen.Cylinder(10, true), trim, c + Vector3.up * (7f + i * 3f), new Vector3(4.25f, 0.35f, 4.25f));
            Add(MeshGen.Cylinder(12, true), stoneL, c + Vector3.up * 15.3f, new Vector3(6f, 0.6f, 6f));
            for (int i = 0; i < 8; i++)
            {
                var d = Quaternion.Euler(0, i * 45, 0) * Vector3.forward * 2.7f;
                Add(MeshGen.Box, stone, c + d + Vector3.up * 16f, new Vector3(0.7f, 0.9f, 0.7f), Quaternion.Euler(0, i * 45, 0));
            }
            Add(MeshGen.Cone(10), trim, c + Vector3.up * 18.5f, new Vector3(3.6f, 4f, 3.6f));
            // glowing windows
            for (int i = 0; i < 4; i++)
            {
                var rot = Quaternion.Euler(0, i * 90 + 45, 0);
                Add(MeshGen.Box, glow, c + rot * new Vector3(0, 11f, 2.02f), new Vector3(0.6f, 1.4f, 0.1f), rot, false);
            }
            // live crystal + beam
            var crystal = Build.Part(Root, MeshGen.Octahedron, MaterialLib.Glow(Palette.Tower, 3.5f), c + Vector3.up * 23f, new Vector3(2.2f, 3.4f, 2.2f), null, "TowerCrystal", false);
            TowerCrystal = crystal.transform;
            Build.Part(crystal.transform, MeshGen.Octahedron, MaterialLib.Unlit(new Color(0.4f, 0.9f, 1f, 0.25f), MaterialLib.Blend.Additive), Vector3.zero, Vector3.one * 1.6f, null, "Halo", false);
            Build.Part(Root, MeshGen.Cylinder(16), MaterialLib.Unlit(new Color(0.3f, 0.85f, 1f, 0.06f), MaterialLib.Blend.Additive), c + Vector3.up * 38f, new Vector3(0.7f, 30f, 0.7f), null, "TowerBeam", false);
        }

        private void BuildReactor(Vector3 c)
        {
            var metal = MaterialLib.Toon(Palette.Hex("#3b3752"), 0.3f, 0.6f);
            var metalL = MaterialLib.Toon(Palette.Hex("#6b6590"), 0.3f, 0.6f);
            Add(MeshGen.Cylinder(12, true), metalL, c + Vector3.up * 0.3f, new Vector3(6.4f, 0.6f, 6.4f));
            Add(MeshGen.Cylinder(12, true), metal, c + Vector3.up * 1.2f, new Vector3(4.4f, 1.2f, 4.4f));
            Add(MeshGen.Torus(0.08f), MaterialLib.Glow(Palette.Reactor, 2.5f), c + Vector3.up * 0.65f, new Vector3(6.6f, 2f, 6.6f), null, false);
            for (int i = 0; i < 6; i++)
            {
                var rot = Quaternion.Euler(0, i * 60, 0);
                Add(MeshGen.Box, metalL, c + rot * new Vector3(0, 2.2f, 1.9f), new Vector3(0.5f, 2.6f, 0.4f), rot);
            }
            var core = Build.Part(Root, MeshGen.Icosphere(1, 0f, 0), MaterialLib.Glow(Palette.Reactor, 4f), c + Vector3.up * 3.6f, Vector3.one * 2.4f, null, "ReactorCore", false);
            ReactorCore = core.transform;
            Build.Part(core.transform, MeshGen.Sphere, MaterialLib.Unlit(new Color(0.8f, 0.4f, 1f, 0.18f), MaterialLib.Blend.Additive), Vector3.zero, Vector3.one * 1.6f, null, "Halo", false);
            for (int i = 0; i < 3; i++)
            {
                var ring = Build.Part(Root, MeshGen.Torus(0.035f), MaterialLib.Glow(Palette.VeilLight, 3f), c + Vector3.up * 3.6f, Vector3.one * (4f + i * 0.9f), Quaternion.Euler(i * 50, 0, i * 30), "ReactorRing", false);
                ReactorRings.Add(ring.transform);
            }
        }

        private void BuildVault(Obstacle o, Vector3 c, Quaternion rot)
        {
            var stone = MaterialLib.Toon(Palette.Hex("#c9c3b3"), 0.25f);
            var stoneD = MaterialLib.Toon(Palette.Hex("#9d9585"), 0.2f);
            var gold = MaterialLib.Toon(Palette.Gold, 0.5f, 1f);
            float w = o.Half.X * 2, d = o.Half.Y * 2, h = o.Height;
            Add(MeshGen.Box, stoneD, c + Vector3.up * 0.4f, new Vector3(w + 1.2f, 0.8f, d + 1.2f), rot);
            Add(MeshGen.Box, stone, c + Vector3.up * (h * 0.4f), new Vector3(w, h * 0.8f, d), rot);
            Add(MeshGen.Box, gold, c + Vector3.up * (h * 0.8f + 0.15f), new Vector3(w + 0.4f, 0.3f, d + 0.4f), rot);
            Add(MeshGen.Box, stone, c + Vector3.up * (h * 0.8f + 0.9f), new Vector3(w * 0.75f, 1.2f, d * 0.75f), rot);
            Add(MeshGen.Box, gold, c + Vector3.up * (h * 0.8f + 1.6f), new Vector3(w * 0.5f, 0.4f, d * 0.5f), rot);
            // columns on the front face (local -X)
            for (int k = -1; k <= 1; k += 2)
                Add(MeshGen.Cylinder(10, true), stone, c + rot * new Vector3(-o.Half.X - 0.5f, h * 0.4f, k * o.Half.Y * 0.62f), new Vector3(0.9f, h * 0.8f, 0.9f), rot);
            // big round gold door (live, spins open)
            var doorPos = c + rot * new Vector3(-o.Half.X - 0.12f, 2.6f, 0);
            var door = Build.Part(Root, MeshGen.Cylinder(20), gold, doorPos, new Vector3(4.2f, 0.4f, 4.2f), rot * Quaternion.Euler(0, 0, 90), "VaultDoor");
            VaultDoor = door.transform;
            VaultDoorMat = MaterialLib.Glow(Palette.Vault, 0.2f);
            Build.Part(door.transform, MeshGen.Torus(0.08f), VaultDoorMat, new Vector3(0, 0.55f, 0), new Vector3(0.75f, 2f, 0.75f), null, "Rim", false);
            for (int i = 0; i < 6; i++)
                Build.Part(door.transform, MeshGen.Box, stoneD, Quaternion.Euler(0, i * 30, 0) * new Vector3(0, 0.55f, 0), new Vector3(0.7f, 0.25f, 0.06f), Quaternion.Euler(0, i * 30, 0), "Spoke", false);
        }

        // ------------------------------------------------------------------ scatter & surroundings

        private void BuildScatter()
        {
            // grass tufts and flowers (no collision)
            var tuft = MaterialLib.Toon(Palette.GrassDark, 0.2f);
            var tuftL = MaterialLib.Toon(Palette.GrassLight, 0.2f);
            Color[] flowers = { Palette.Hex("#ffffff"), Palette.Hex("#ffd24a"), Palette.Hex("#ff7ab8"), Palette.Hex("#8fb8ff") };
            float span = _map.Half - 8f;
            int tufts = _map.Island ? 3200 : 700;
            for (int i = 0; i < tufts; i++)
            {
                var p = new Vec2(R(-span, span), R(-span, span));
                if (_map.IsBlockedForStanding(p, 0.5f) || _map.ZoneAt(p) >= 0 || p.Length < 17) continue;
                if (_map.Island && !Grassy(IslandMap.BiomeAt(p))) continue;
                bool onPath = false;
                foreach (var d in _map.Decals)
                    if (d.Kind == 0 && Mathf.Abs(Vec2.InverseRotateYaw(p - d.Center, d.Rot).X) < d.Half.X + 0.5f && Mathf.Abs(Vec2.InverseRotateYaw(p - d.Center, d.Rot).Y) < d.Half.Y) { onPath = true; break; }
                if (onPath) continue;
                if (i % 5 == 0)
                    Add(MeshGen.SphereLow, MaterialLib.Toon(flowers[i % flowers.Length], 0.2f), Build.V(p, 0.25f), Vector3.one * 0.22f, null, false);
                else
                    for (int k = 0; k < 3; k++)
                        Add(MeshGen.Cone(4), k == 1 ? tuftL : tuft, Build.V(p + new Vec2(R(-0.2f, 0.2f), R(-0.2f, 0.2f)), 0.22f), new Vector3(0.18f, R(0.35f, 0.55f), 0.18f), Quaternion.Euler(R(-20, 20), R(0, 90), R(-20, 20)), false);
            }
            // bushes along roads
            var bush = MaterialLib.Toon(Palette.Hex("#4aa843"), 0.3f);
            int bushes = _map.Island ? 600 : 120;
            for (int i = 0; i < bushes; i++)
            {
                var p = new Vec2(R(-span, span), R(-span, span));
                if (_map.IsBlockedForStanding(p, 1.2f) || _map.ZoneAt(p) >= 0 || p.Length < 22) continue;
                if (_map.Island && !Grassy(IslandMap.BiomeAt(p))) continue;
                Add(MeshGen.Icosphere(1, 0.15f, i % 5), bush, Build.V(p, 0.3f), new Vector3(R(0.9f, 1.5f), R(0.7f, 1.1f), R(0.9f, 1.5f)), Quaternion.Euler(0, R(0, 360), 0));
            }
        }

        private void BuildSurroundings()
        {
            // sea far below, distant floating islands and mountains
            Add(MeshGen.GroundQuad, MaterialLib.Water(), new Vector3(0, -18f, 0), new Vector3(900, 1, 900), null, false);
            var rock = MaterialLib.Toon(Palette.Cliff, 0.15f);
            var grass = MaterialLib.Toon(Palette.GrassDark, 0.1f);
            for (int i = 0; i < 26; i++)
            {
                float a = i / 26f * 360f + R(-5, 5);
                float dist = R(130, 230);
                var p = Quaternion.Euler(0, a, 0) * Vector3.forward * dist;
                float s = R(18, 45);
                float y = R(-20, 5);
                Add(MeshGen.Icosphere(1, 0.25f, i % 9), rock, p + Vector3.up * (y - s * 0.3f), new Vector3(s, s * 1.4f, s), Quaternion.Euler(0, R(0, 360), 0), false);
                Add(MeshGen.Icosphere(1, 0.2f, (i + 4) % 9), grass, p + Vector3.up * (y + s * 0.35f), new Vector3(s * 0.9f, s * 0.25f, s * 0.9f), null, false);
                if (i % 3 == 0)
                    Add(MeshGen.Icosphere(1, 0.12f, i % 5), MaterialLib.Toon(Palette.Hex("#57b947"), 0.3f), p + Vector3.up * (y + s * 0.6f), Vector3.one * s * 0.3f, null, false);
            }
            // waterfalls pouring off the plateau
            var fallTex = WaterfallTexture();
            var fallMat = MaterialLib.Unlit(new Color(0.75f, 0.95f, 1f, 0.85f), MaterialLib.Blend.Alpha, fallTex);
            fallMat.SetVector("_Scroll", new Vector4(0, 1.2f, 0, 0));
            Vector3[] falls = { new Vector3(-30, 0, 78.3f), new Vector3(35, 0, 78.3f), new Vector3(78.3f, 0, -20), new Vector3(-78.3f, 0, 25), new Vector3(10, 0, -78.3f) };
            foreach (var f in falls)
            {
                var look = Quaternion.LookRotation(new Vector3(f.x, 0, f.z).normalized);
                var go = Build.Part(Root, MeshGen.Quad, fallMat, f + Vector3.down * 9f + look * Vector3.forward * 0.5f, new Vector3(7f, 18f, 1f), look * Quaternion.Euler(0, 180, 0), "Waterfall", false);
                go.GetComponent<MeshRenderer>().sharedMaterial.mainTextureScale = new Vector2(1, 2);
                Build.Part(Root, MeshGen.Disc(16), MaterialLib.Unlit(new Color(1, 1, 1, 0.5f), MaterialLib.Blend.Alpha), f + Vector3.down * 17.8f + look * Vector3.forward * 2f, new Vector3(9, 1, 5), look, "Foam", false);
            }
        }

        private static Texture2D WaterfallTexture()
        {
            var t = new Texture2D(32, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var rnd = new System.Random(3);
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 32; x++)
                {
                    float stripe = Mathf.PerlinNoise(x * 0.35f, y * 0.06f);
                    float v = 0.55f + stripe * 0.45f;
                    float edge = Mathf.Clamp01(Mathf.Min(x, 31 - x) / 5f);
                    t.SetPixel(x, y, new Color(v, v, 1, edge * (0.6f + stripe * 0.4f)));
                }
            t.Apply();
            return t;
        }
    }

    /// <summary>Animates the live landmark pieces.</summary>
    public sealed class WorldAnimator : MonoBehaviour
    {
        private WorldBuilder _w;
        private float _vaultOpenT;

        public void Init(WorldBuilder w) { _w = w; }

        public void PlayVaultOpen() { _vaultOpenT = 3f; }

        private void Update()
        {
            if (_w == null) return;
            float t = Time.time;
            if (_w.TowerCrystal)
            {
                _w.TowerCrystal.localRotation = Quaternion.Euler(0, t * 30f, 0);
                _w.TowerCrystal.localPosition = new Vector3(0, 23f + Mathf.Sin(t * 1.2f) * 0.6f, 0);
            }
            if (_w.ReactorCore)
            {
                float pulse = 1f + Mathf.Sin(t * 3f) * 0.08f;
                _w.ReactorCore.localScale = Vector3.one * 2.4f * pulse;
                _w.ReactorCore.Rotate(0, 40f * Time.deltaTime, 0, Space.World);
                for (int i = 0; i < _w.ReactorRings.Count; i++)
                    _w.ReactorRings[i].Rotate(new Vector3(30 + i * 15, 60 - i * 20, 10 * i) * Time.deltaTime, Space.Self);
            }
            if (_w.VaultDoor)
            {
                if (_vaultOpenT > 0)
                {
                    _vaultOpenT -= Time.deltaTime;
                    _w.VaultDoor.Rotate(0, 360f * Time.deltaTime, 0, Space.Self);
                    _w.VaultDoorMat.SetColor("_EmissionColor", Palette.Vault * (1.5f + Mathf.Sin(t * 20) * 0.5f));
                }
                else _w.VaultDoorMat.SetColor("_EmissionColor", Palette.Vault * 0.3f);
            }
        }
    }
}
