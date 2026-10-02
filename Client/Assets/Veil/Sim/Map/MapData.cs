using System;
using System.Collections.Generic;

namespace Veil.Sim
{
    public enum ObstacleKind : byte
    {
        Wall, LowWall, Pillar, Tree, Rock, Water, Building, Crate, Cliff, Stall, TowerCore, ReactorCore, VaultBuilding, Pylon,
        CityBlock, Container, Crane, Hut, Palm, Pine, Mesa, Tank, Watchtower
    }

    public enum ShapeKind : byte { Circle, Box }

    public enum ZoneType : byte { Tower, Vault, Reactor, Market, Ruins }

    /// <summary>Island regions (ground look + names on the map / HUD).</summary>
    public enum Biome : byte { City, Snow, Dock, Ruins, Beach, Forest, Canyon, Hydro, Outpost, Sea }

    public sealed class RegionDef
    {
        public string Name;
        public Biome Biome;
        public Vec2 Center;
        public float Bearing;     // degrees, 0 = north
    }

    /// <summary>A static collision/visual element of the arena.</summary>
    public sealed class Obstacle
    {
        public ObstacleKind Kind;
        public ShapeKind Shape;
        public Vec2 Center;
        public float Radius;      // circle
        public Vec2 Half;         // box half extents (local X, local Y)
        public float Rot;         // box yaw in degrees
        public float Height;
        public bool BlocksMove = true;
        public bool BlocksShots = true;
        public int Variant;       // visual variation seed

        public float BoundRadius => Shape == ShapeKind.Circle ? Radius : Half.Length;

        public bool BlocksVision => BlocksShots && Height >= GameConfig.VisionBlockHeight && Kind != ObstacleKind.Tree;

        /// <summary>Distance from point to the obstacle surface (negative = inside).</summary>
        public float SignedDistance(Vec2 p)
        {
            if (Shape == ShapeKind.Circle) return Vec2.Dist(p, Center) - Radius;
            Vec2 l = Vec2.InverseRotateYaw(p - Center, Rot);
            float dx = MathF.Abs(l.X) - Half.X, dy = MathF.Abs(l.Y) - Half.Y;
            float ox = MathF.Max(dx, 0), oy = MathF.Max(dy, 0);
            float outside = MathF.Sqrt(ox * ox + oy * oy);
            float inside = MathF.Min(MathF.Max(dx, dy), 0);
            return outside + inside;
        }

        /// <summary>Pushes a circle out of this obstacle. Returns true if it collided.</summary>
        public bool PushOut(ref Vec2 p, float r)
        {
            if (Shape == ShapeKind.Circle)
            {
                Vec2 d = p - Center;
                float min = r + Radius;
                float dsq = d.LengthSq;
                if (dsq >= min * min) return false;
                float dist = MathF.Sqrt(dsq);
                Vec2 n = dist > 1e-4f ? d / dist : new Vec2(1, 0);
                p = Center + n * min;
                return true;
            }

            Vec2 local = Vec2.InverseRotateYaw(p - Center, Rot);
            float cx = MathUtil.Clamp(local.X, -Half.X, Half.X);
            float cy = MathUtil.Clamp(local.Y, -Half.Y, Half.Y);
            Vec2 diff = new Vec2(local.X - cx, local.Y - cy);
            float ds = diff.LengthSq;
            if (ds >= r * r) return false;
            if (ds > 1e-8f)
            {
                float dist = MathF.Sqrt(ds);
                local = new Vec2(cx, cy) + diff / dist * r;
            }
            else
            {
                float px = Half.X - MathF.Abs(local.X);
                float py = Half.Y - MathF.Abs(local.Y);
                if (px < py) local.X = (local.X >= 0 ? 1 : -1) * (Half.X + r);
                else local.Y = (local.Y >= 0 ? 1 : -1) * (Half.Y + r);
            }
            p = Center + Vec2.RotateYaw(local, Rot);
            return true;
        }
    }

    public sealed class ZoneDef
    {
        public int Id;
        public ZoneType Type;
        public string Name;
        public Vec2 Center;
        public float Radius;
        public bool Capturable => Type != ZoneType.Vault;
    }

    /// <summary>Purely visual ground strip (paths, bridges, plazas) — no collision.</summary>
    public sealed class GroundDecal
    {
        public int Kind; // 0 path, 1 bridge, 2 plaza (circle), 3 grass patch
        public Vec2 Center;
        public Vec2 Half;
        public float Rot;
        public float Radius;
    }

    /// <summary>
    /// Static arena description shared by client and server. Built deterministically by
    /// <see cref="ArenaMap.Build"/> so both sides agree without transferring geometry.
    /// </summary>
    public sealed class MapData
    {
        public readonly List<Obstacle> Obstacles = new List<Obstacle>();
        public readonly List<ZoneDef> Zones = new List<ZoneDef>();
        public readonly List<Vec2> SpawnPoints = new List<Vec2>();
        public readonly List<Vec2> KeySpots = new List<Vec2>();
        public readonly List<Vec2> CoreSpots = new List<Vec2>();
        public readonly List<GroundDecal> Decals = new List<GroundDecal>();
        public readonly List<RegionDef> Regions = new List<RegionDef>();
        public bool Island;
        public NavGrid Nav;

        private const float CellSize = 8f;
        private int _cells;
        private List<int>[] _grid;
        private bool[] _visionBlocked; // 1m raster of vision-blocking obstacles
        private int _visionRes;

        public float Half => GameConfig.MapHalf;

        public ZoneDef Zone(ZoneType t)
        {
            foreach (var z in Zones) if (z.Type == t) return z;
            return null;
        }

        /// <summary>Call after all obstacles are added.</summary>
        public void Bake()
        {
            _cells = (int)MathF.Ceiling(Half * 2 / CellSize) + 2;
            _grid = new List<int>[_cells * _cells];
            for (int i = 0; i < Obstacles.Count; i++)
            {
                var o = Obstacles[i];
                float br = o.BoundRadius;
                int x0 = CellX(o.Center.X - br), x1 = CellX(o.Center.X + br);
                int y0 = CellX(o.Center.Y - br), y1 = CellX(o.Center.Y + br);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int idx = y * _cells + x;
                        (_grid[idx] ??= new List<int>()).Add(i);
                    }
            }

            _visionRes = (int)(Half * 2);
            _visionBlocked = new bool[_visionRes * _visionRes];
            for (int y = 0; y < _visionRes; y++)
                for (int x = 0; x < _visionRes; x++)
                {
                    var p = new Vec2(x - Half + 0.5f, y - Half + 0.5f);
                    foreach (int oi in Query(p))
                    {
                        var o = Obstacles[oi];
                        if (o.BlocksVision && o.SignedDistance(p) < 0) { _visionBlocked[y * _visionRes + x] = true; break; }
                    }
                }

            Nav = new NavGrid(this);
        }

        private int CellX(float v) => MathUtil.Clamp((int)((v + Half) / CellSize) + 1, 0, _cells - 1);

        private static readonly List<int> Empty = new List<int>();

        /// <summary>Obstacles whose bounds overlap the cell containing p.</summary>
        public List<int> Query(Vec2 p)
        {
            var l = _grid[CellX(p.Y) * _cells + CellX(p.X)];
            return l ?? Empty;
        }

        private readonly HashSet<int> _scratch = new HashSet<int>();

        /// <summary>Resolves a moving circle against the map. height = feet height (jumping clears low obstacles).</summary>
        public bool ResolveCircle(ref Vec2 p, float r, float height)
        {
            bool hit = false;
            for (int iter = 0; iter < 3; iter++)
            {
                bool any = false;
                _scratch.Clear();
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var q = new Vec2(p.X + dx * r, p.Y + dy * r);
                        foreach (int oi in Query(q))
                        {
                            if (!_scratch.Add(oi)) continue;
                            var o = Obstacles[oi];
                            if (!o.BlocksMove || height >= o.Height) continue;
                            if (o.PushOut(ref p, r)) any = true;
                        }
                    }
                if (!any) break;
                hit = true;
            }
            float lim = Half - 1.2f;
            p.X = MathUtil.Clamp(p.X, -lim, lim);
            p.Y = MathUtil.Clamp(p.Y, -lim, lim);
            return hit;
        }

        /// <summary>True if a point at the given height is inside something that stops projectiles.</summary>
        public bool BlocksShotAt(Vec2 p, float height, float r)
        {
            if (MathF.Abs(p.X) > Half || MathF.Abs(p.Y) > Half) return true;
            foreach (int oi in Query(p))
            {
                var o = Obstacles[oi];
                if (!o.BlocksShots || height >= o.Height) continue;
                if (o.SignedDistance(p) < r) return true;
            }
            return false;
        }

        public bool IsBlockedForStanding(Vec2 p, float r)
        {
            if (MathF.Abs(p.X) > Half - 2 || MathF.Abs(p.Y) > Half - 2) return true;
            foreach (int oi in Query(p))
            {
                var o = Obstacles[oi];
                if (o.BlocksMove && o.SignedDistance(p) < r) return true;
            }
            return false;
        }

        /// <summary>Line-of-sight test against tall obstacles using the 1m vision raster (DDA).</summary>
        public bool HasLineOfSight(Vec2 a, Vec2 b)
        {
            float ax = a.X + Half, ay = a.Y + Half, bx = b.X + Half, by = b.Y + Half;
            int x = (int)ax, y = (int)ay;
            int ex = (int)bx, ey = (int)by;
            float dx = bx - ax, dy = by - ay;
            int sx = dx > 0 ? 1 : -1, sy = dy > 0 ? 1 : -1;
            float tdx = dx != 0 ? MathF.Abs(1f / dx) : float.MaxValue;
            float tdy = dy != 0 ? MathF.Abs(1f / dy) : float.MaxValue;
            float tmx = dx != 0 ? ((sx > 0 ? (x + 1 - ax) : (ax - x)) * tdx) : float.MaxValue;
            float tmy = dy != 0 ? ((sy > 0 ? (y + 1 - ay) : (ay - y)) * tdy) : float.MaxValue;
            int guard = 0;
            while (guard++ < 1200)
            {
                if (x < 0 || y < 0 || x >= _visionRes || y >= _visionRes) return false;
                if (!(x == (int)ax && y == (int)ay) && _visionBlocked[y * _visionRes + x])
                {
                    if (x == ex && y == ey) return true; // target stands on edge of obstacle cell
                    return false;
                }
                if (x == ex && y == ey) return true;
                if (tmx < tmy) { tmx += tdx; x += sx; }
                else { tmy += tdy; y += sy; }
            }
            return true;
        }

        public int ZoneAt(Vec2 p)
        {
            foreach (var z in Zones)
                if (Vec2.DistSq(p, z.Center) <= z.Radius * z.Radius) return z.Id;
            return -1;
        }
    }
}
