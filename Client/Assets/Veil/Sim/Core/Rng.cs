namespace Veil.Sim
{
    /// <summary>Small deterministic PRNG (xorshift32). Same sequence on client and server.</summary>
    public sealed class Rng
    {
        private uint _state;

        public Rng(int seed)
        {
            _state = (uint)seed * 2654435761u + 0x9E3779B9u;
            if (_state == 0) _state = 0x12345678u;
            for (int i = 0; i < 4; i++) NextUInt();
        }

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>[0, 1)</summary>
        public float Next() => (NextUInt() >> 8) * (1f / 16777216f);

        public float Range(float min, float max) => min + (max - min) * Next();

        /// <summary>[0, maxExclusive)</summary>
        public int Int(int maxExclusive) => maxExclusive <= 0 ? 0 : (int)(NextUInt() % (uint)maxExclusive);

        public int Range(int minInclusive, int maxExclusive) => minInclusive + Int(maxExclusive - minInclusive);

        public bool Chance(float p) => Next() < p;

        public Vec2 InsideCircle(float radius)
        {
            float a = Range(0, 360f);
            float r = radius * System.MathF.Sqrt(Next());
            return Vec2.FromYaw(a) * r;
        }
    }
}
