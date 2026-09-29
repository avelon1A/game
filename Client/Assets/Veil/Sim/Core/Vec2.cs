using System;

namespace Veil.Sim
{
    /// <summary>
    /// 2D vector on the ground plane. X = world X (east), Y = world Z (north).
    /// Yaw is in degrees and matches Unity: yaw 0 faces +Z, yaw 90 faces +X.
    /// </summary>
    [Serializable]
    public struct Vec2 : IEquatable<Vec2>
    {
        public float X, Y;

        public Vec2(float x, float y) { X = x; Y = y; }

        public static readonly Vec2 Zero = new Vec2(0, 0);

        public float LengthSq => X * X + Y * Y;
        public float Length => MathF.Sqrt(X * X + Y * Y);

        public Vec2 Normalized
        {
            get
            {
                float l = Length;
                return l > 1e-6f ? new Vec2(X / l, Y / l) : Zero;
            }
        }

        public Vec2 ClampLength(float max)
        {
            float l = Length;
            return l > max && l > 1e-6f ? this * (max / l) : this;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.X / s, a.Y / s);

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        public static float Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
        public static float Dist(Vec2 a, Vec2 b) => (a - b).Length;
        public static float DistSq(Vec2 a, Vec2 b) => (a - b).LengthSq;
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        public static Vec2 MoveTowards(Vec2 cur, Vec2 target, float maxDelta)
        {
            Vec2 d = target - cur;
            float l = d.Length;
            if (l <= maxDelta || l < 1e-6f) return target;
            return cur + d / l * maxDelta;
        }

        /// <summary>Unit direction for a yaw in degrees (Unity convention).</summary>
        public static Vec2 FromYaw(float yawDeg)
        {
            float r = yawDeg * MathUtil.Deg2Rad;
            return new Vec2(MathF.Sin(r), MathF.Cos(r));
        }

        /// <summary>Yaw in degrees for a direction (Unity convention).</summary>
        public float Yaw => MathF.Atan2(X, Y) * MathUtil.Rad2Deg;

        /// <summary>Local → world rotation, identical to Unity's Quaternion.Euler(0, yaw, 0) * (x, 0, y).</summary>
        public static Vec2 RotateYaw(Vec2 v, float yawDeg)
        {
            float r = yawDeg * MathUtil.Deg2Rad;
            float c = MathF.Cos(r), s = MathF.Sin(r);
            return new Vec2(v.X * c + v.Y * s, -v.X * s + v.Y * c);
        }

        /// <summary>World → local (inverse of <see cref="RotateYaw"/>).</summary>
        public static Vec2 InverseRotateYaw(Vec2 v, float yawDeg)
        {
            float r = yawDeg * MathUtil.Deg2Rad;
            float c = MathF.Cos(r), s = MathF.Sin(r);
            return new Vec2(v.X * c - v.Y * s, v.X * s + v.Y * c);
        }

        public bool Equals(Vec2 o) => X == o.X && Y == o.Y;
        public override bool Equals(object obj) => obj is Vec2 v && Equals(v);
        public override int GetHashCode() => X.GetHashCode() * 31 + Y.GetHashCode();
        public override string ToString() => $"({X:0.00}, {Y:0.00})";
    }

    public static class MathUtil
    {
        public const float Deg2Rad = MathF.PI / 180f;
        public const float Rad2Deg = 180f / MathF.PI;

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float MoveTowards(float cur, float target, float maxDelta)
        {
            if (MathF.Abs(target - cur) <= maxDelta) return target;
            return cur + MathF.Sign(target - cur) * maxDelta;
        }

        /// <summary>Signed smallest difference b - a in degrees, in [-180, 180].</summary>
        public static float DeltaAngle(float a, float b)
        {
            float d = (b - a) % 360f;
            if (d > 180f) d -= 360f;
            if (d < -180f) d += 360f;
            return d;
        }

        public static float LerpAngle(float a, float b, float t) => a + DeltaAngle(a, b) * t;
    }
}
