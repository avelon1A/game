using System;
using System.Collections.Generic;

namespace Veil.Sim
{
    /// <summary>1m walkability grid with A* + line-of-sight path smoothing, used by bots.</summary>
    public sealed class NavGrid
    {
        public readonly int Size;
        private readonly float _half;
        private readonly bool[] _walkable;
        private readonly float[] _g;
        private readonly int[] _parent;
        private readonly int[] _stamp;
        private readonly bool[] _closed;
        private int _gen;
        private readonly List<int> _walkableCells = new List<int>();

        private static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] Dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private const float Diag = 1.41421356f;

        public NavGrid(MapData map)
        {
            _half = map.Half;
            Size = (int)(_half * 2);
            int n = Size * Size;
            _walkable = new bool[n];
            _g = new float[n];
            _parent = new int[n];
            _stamp = new int[n];
            _closed = new bool[n];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    var p = CellCenter(x, y);
                    bool w = !map.IsBlockedForStanding(p, 0.6f);
                    _walkable[y * Size + x] = w;
                    if (w) _walkableCells.Add(y * Size + x);
                }
        }

        public Vec2 CellCenter(int x, int y) => new Vec2(x - _half + 0.5f, y - _half + 0.5f);
        public Vec2 CellCenter(int idx) => CellCenter(idx % Size, idx / Size);

        public int CellOf(Vec2 p)
        {
            int x = MathUtil.Clamp((int)(p.X + _half), 0, Size - 1);
            int y = MathUtil.Clamp((int)(p.Y + _half), 0, Size - 1);
            return y * Size + x;
        }

        public bool Walkable(int idx) => idx >= 0 && idx < _walkable.Length && _walkable[idx];
        public bool Walkable(Vec2 p) => Walkable(CellOf(p));

        public Vec2 RandomWalkable(Rng rng) => CellCenter(_walkableCells[rng.Int(_walkableCells.Count)]);

        public int NearestWalkable(int idx)
        {
            if (Walkable(idx)) return idx;
            int cx = idx % Size, cy = idx / Size;
            for (int r = 1; r < 20; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= Size || y >= Size) continue;
                        int i = y * Size + x;
                        if (_walkable[i]) return i;
                    }
            return idx;
        }

        // ---------- A* ----------
        private readonly List<(float f, int idx)> _heap = new List<(float, int)>();

        private void Push(float f, int idx)
        {
            _heap.Add((f, idx));
            int i = _heap.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (_heap[p].f <= _heap[i].f) break;
                (_heap[p], _heap[i]) = (_heap[i], _heap[p]);
                i = p;
            }
        }

        private int Pop()
        {
            int top = _heap[0].idx;
            int last = _heap.Count - 1;
            _heap[0] = _heap[last];
            _heap.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, s = i;
                if (l < _heap.Count && _heap[l].f < _heap[s].f) s = l;
                if (r < _heap.Count && _heap[r].f < _heap[s].f) s = r;
                if (s == i) break;
                (_heap[s], _heap[i]) = (_heap[i], _heap[s]);
                i = s;
            }
            return top;
        }

        private static float Octile(int ax, int ay, int bx, int by)
        {
            int dx = Math.Abs(ax - bx), dy = Math.Abs(ay - by);
            return (dx + dy) + (Diag - 2) * Math.Min(dx, dy);
        }

        /// <summary>Finds a smoothed path. Returns false if unreachable (then output is a straight line).</summary>
        public bool FindPath(Vec2 from, Vec2 to, List<Vec2> output, int maxExpand = 12000)
        {
            output.Clear();
            int start = NearestWalkable(CellOf(from));
            int goal = NearestWalkable(CellOf(to));
            if (start == goal) { output.Add(to); return true; }

            _gen++;
            _heap.Clear();
            int gx = goal % Size, gy = goal / Size;
            _stamp[start] = _gen; _g[start] = 0; _parent[start] = -1; _closed[start] = false;
            Push(Octile(start % Size, start / Size, gx, gy), start);
            int expanded = 0;
            bool found = false;
            int best = start;
            float bestH = float.MaxValue;

            while (_heap.Count > 0 && expanded++ < maxExpand)
            {
                int cur = Pop();
                if (_closed[cur] && _stamp[cur] == _gen) continue;
                _closed[cur] = true;
                if (cur == goal) { found = true; break; }
                int cx = cur % Size, cy = cur / Size;
                float h = Octile(cx, cy, gx, gy);
                if (h < bestH) { bestH = h; best = cur; }

                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + Dx[d], ny = cy + Dy[d];
                    if (nx < 0 || ny < 0 || nx >= Size || ny >= Size) continue;
                    int ni = ny * Size + nx;
                    if (!_walkable[ni]) continue;
                    if (d >= 4 && (!_walkable[cy * Size + nx] || !_walkable[ny * Size + cx])) continue;
                    float ng = _g[cur] + (d >= 4 ? Diag : 1f);
                    if (_stamp[ni] != _gen)
                    {
                        _stamp[ni] = _gen; _closed[ni] = false; _g[ni] = float.MaxValue;
                    }
                    else if (_closed[ni]) continue;
                    if (ng < _g[ni])
                    {
                        _g[ni] = ng;
                        _parent[ni] = cur;
                        Push(ng + Octile(nx, ny, gx, gy), ni);
                    }
                }
            }

            int end = found ? goal : best;
            var raw = new List<int>();
            for (int c = end; c != -1 && raw.Count < 5000; c = _parent[c]) raw.Add(c);
            raw.Reverse();

            // String pulling: skip waypoints while a straight walkable line exists.
            Vec2 anchor = from;
            int i = 0;
            while (i < raw.Count)
            {
                int far = i;
                for (int j = Math.Min(raw.Count - 1, i + 24); j > i; j--)
                {
                    if (LineWalkable(anchor, CellCenter(raw[j]))) { far = j; break; }
                }
                var wp = CellCenter(raw[far]);
                output.Add(wp);
                anchor = wp;
                i = far + 1;
            }
            if (found && LineWalkable(anchor, to)) output[output.Count - 1] = to;
            return found;
        }

        public bool LineWalkable(Vec2 a, Vec2 b)
        {
            float len = Vec2.Dist(a, b);
            int steps = Math.Max(1, (int)(len / 0.4f));
            for (int s = 1; s <= steps; s++)
            {
                if (!Walkable(Vec2.Lerp(a, b, s / (float)steps))) return false;
            }
            return true;
        }
    }
}
