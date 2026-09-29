using System;
using System.Text;

namespace Veil.Sim
{
    /// <summary>Minimal little-endian writer used by the wire protocol (no transport dependency).</summary>
    public sealed class ByteWriter
    {
        private byte[] _buf;
        public int Length { get; private set; }

        public ByteWriter(int capacity = 1024) { _buf = new byte[capacity]; }

        public void Reset() => Length = 0;
        public byte[] Buffer => _buf;
        public byte[] ToArray() { var a = new byte[Length]; Array.Copy(_buf, a, Length); return a; }

        private void Ensure(int n)
        {
            if (Length + n <= _buf.Length) return;
            Array.Resize(ref _buf, Math.Max(_buf.Length * 2, Length + n));
        }

        public void U8(byte v) { Ensure(1); _buf[Length++] = v; }
        public void Bytes(byte[] src, int offset, int count) { Ensure(count); Array.Copy(src, offset, _buf, Length, count); Length += count; }
        public void Bool(bool v) => U8(v ? (byte)1 : (byte)0);
        public void I16(short v) { Ensure(2); _buf[Length++] = (byte)v; _buf[Length++] = (byte)(v >> 8); }
        public void U16(ushort v) { Ensure(2); _buf[Length++] = (byte)v; _buf[Length++] = (byte)(v >> 8); }
        public void I32(int v) { Ensure(4); for (int i = 0; i < 4; i++) _buf[Length++] = (byte)(v >> (8 * i)); }
        public void F32(float v) => I32(BitConverter.SingleToInt32Bits(v));

        /// <summary>Position quantised to 1 cm (range ±327 m).</summary>
        public void Pos(float v) => I16((short)MathF.Round(MathUtil.Clamp(v, -327f, 327f) * 100f));
        public void Vec(Vec2 v) { Pos(v.X); Pos(v.Y); }
        /// <summary>Velocity quantised to 1/100 m/s (±327 m/s).</summary>
        public void VelQ(Vec2 v) { Pos(v.X); Pos(v.Y); }
        public void Angle(float deg) => U16((ushort)(((deg % 360f) + 360f) % 360f / 360f * 65535f));
        /// <summary>0..255 quantisation of a value in [0, max].</summary>
        public void Unit(float v, float max) => U8((byte)MathF.Round(MathUtil.Clamp01(v / max) * 255f));
        /// <summary>Small time value with 1/100 s precision (0..655 s).</summary>
        public void Time(float t) => U16((ushort)MathF.Round(MathUtil.Clamp(t, 0, 655f) * 100f));

        public void Str(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s ?? "");
            int n = Math.Min(bytes.Length, 255);
            U8((byte)n);
            Ensure(n);
            Array.Copy(bytes, 0, _buf, Length, n);
            Length += n;
        }
    }

    public sealed class ByteReader
    {
        private readonly byte[] _buf;
        private int _pos;
        private readonly int _end;

        public ByteReader(byte[] buf, int offset, int length) { _buf = buf; _pos = offset; _end = offset + length; }
        public ByteReader(byte[] buf) : this(buf, 0, buf.Length) { }

        public int Remaining => _end - _pos;
        public int Position => _pos;

        private void Need(int n) { if (_pos + n > _end) throw new FormatException("Packet truncated"); }

        public byte U8() { Need(1); return _buf[_pos++]; }
        public bool Bool() => U8() != 0;
        public short I16() { Need(2); short v = (short)(_buf[_pos] | (_buf[_pos + 1] << 8)); _pos += 2; return v; }
        public ushort U16() { Need(2); ushort v = (ushort)(_buf[_pos] | (_buf[_pos + 1] << 8)); _pos += 2; return v; }
        public int I32() { Need(4); int v = _buf[_pos] | (_buf[_pos + 1] << 8) | (_buf[_pos + 2] << 16) | (_buf[_pos + 3] << 24); _pos += 4; return v; }
        public float F32() => BitConverter.Int32BitsToSingle(I32());
        public float Pos() => I16() / 100f;
        public Vec2 Vec() { float x = Pos(); return new Vec2(x, Pos()); }
        public Vec2 VelQ() => Vec();
        public float Angle() => U16() / 65535f * 360f;
        public float Unit(float max) => U8() / 255f * max;
        public float Time() => U16() / 100f;

        public string Str()
        {
            int n = U8();
            Need(n);
            var s = Encoding.UTF8.GetString(_buf, _pos, n);
            _pos += n;
            return s;
        }
    }
}
