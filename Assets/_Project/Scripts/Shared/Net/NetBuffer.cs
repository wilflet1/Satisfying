using System;
using System.Text;

namespace Satisfying.Shared
{
    /// <summary>
    /// Bit-level packet writer/reader. Everything on the wire goes through this so packets stay
    /// small enough that a full input + snapshot exchange fits well inside one MTU at 64Hz.
    /// </summary>
    public sealed class NetBuffer
    {
        public byte[] Data;
        int _bitPos;
        bool _overflow;

        public NetBuffer(int capacityBytes)
        {
            Data = new byte[capacityBytes];
        }

        public int BitPosition { get { return _bitPos; } }
        public int BytePosition { get { return (_bitPos + 7) >> 3; } }
        public bool Overflowed { get { return _overflow; } }
        public int CapacityBits { get { return Data.Length << 3; } }

        public void ResetWrite()
        {
            _bitPos = 0;
            _overflow = false;
            Array.Clear(Data, 0, Data.Length);
        }

        public void ResetRead(byte[] source, int length)
        {
            if (Data.Length < length) Data = new byte[length];
            Buffer.BlockCopy(source, 0, Data, 0, length);
            _bitPos = 0;
            _overflow = false;
        }

        public void SeekBits(int bitPos) { _bitPos = bitPos; }

        // ------------------------------------------------------------------ raw bits
        public void WriteBits(uint value, int bits)
        {
            if (bits <= 0 || bits > 32) throw new ArgumentOutOfRangeException("bits");
            if (_bitPos + bits > CapacityBits) { _overflow = true; return; }
            if (bits < 32) value &= (1u << bits) - 1u;
            for (int i = 0; i < bits; i++)
            {
                int bit = _bitPos + i;
                if ((value & (1u << i)) != 0u) Data[bit >> 3] |= (byte)(1 << (bit & 7));
                else Data[bit >> 3] &= (byte)~(1 << (bit & 7));
            }
            _bitPos += bits;
        }

        public uint ReadBits(int bits)
        {
            if (bits <= 0 || bits > 32) throw new ArgumentOutOfRangeException("bits");
            if (_bitPos + bits > CapacityBits) { _overflow = true; return 0u; }
            uint value = 0u;
            for (int i = 0; i < bits; i++)
            {
                int bit = _bitPos + i;
                if ((Data[bit >> 3] & (1 << (bit & 7))) != 0) value |= 1u << i;
            }
            _bitPos += bits;
            return value;
        }

        // ------------------------------------------------------------------ primitives
        public void WriteBool(bool v) { WriteBits(v ? 1u : 0u, 1); }
        public bool ReadBool() { return ReadBits(1) != 0u; }

        public void WriteByte(byte v) { WriteBits(v, 8); }
        public byte ReadByte() { return (byte)ReadBits(8); }

        public void WriteUShort(ushort v) { WriteBits(v, 16); }
        public ushort ReadUShort() { return (ushort)ReadBits(16); }

        public void WriteUInt(uint v) { WriteBits(v, 32); }
        public uint ReadUInt() { return ReadBits(32); }

        public void WriteInt(int v) { WriteBits(unchecked((uint)v), 32); }
        public int ReadInt() { return unchecked((int)ReadBits(32)); }

        public void WriteFloat(float v)
        {
            byte[] b = BitConverter.GetBytes(v);
            WriteBits(BitConverter.ToUInt32(b, 0), 32);
        }

        public float ReadFloat()
        {
            uint u = ReadBits(32);
            return BitConverter.ToSingle(BitConverter.GetBytes(u), 0);
        }

        public void WriteVec3(Vec3 v) { WriteFloat(v.x); WriteFloat(v.y); WriteFloat(v.z); }
        public Vec3 ReadVec3() { Vec3 v; v.x = ReadFloat(); v.y = ReadFloat(); v.z = ReadFloat(); return v; }

        /// <summary>Signed 0..N tick delta, used to reference recent ticks compactly.</summary>
        public void WriteTick(uint tick) { WriteUInt(tick); }
        public uint ReadTick() { return ReadUInt(); }

        // ------------------------------------------------------------------ quantised
        public void WriteQ(float value, float min, float max, int bits)
        {
            float range = max - min;
            float t = range <= 0f ? 0f : MathK.Clamp01((value - min) / range);
            uint maxV = bits >= 32 ? uint.MaxValue : (1u << bits) - 1u;
            WriteBits((uint)(t * maxV + 0.5f), bits);
        }

        public float ReadQ(float min, float max, int bits)
        {
            uint maxV = bits >= 32 ? uint.MaxValue : (1u << bits) - 1u;
            uint raw = ReadBits(bits);
            return min + (max - min) * (raw / (float)maxV);
        }

        /// <summary>Positions: 1cm precision over a +/-512m world costs 17 bits per axis.</summary>
        public void WriteQVec3(Vec3 v, float min, float max, int bits)
        {
            WriteQ(v.x, min, max, bits);
            WriteQ(v.y, min, max, bits);
            WriteQ(v.z, min, max, bits);
        }

        public Vec3 ReadQVec3(float min, float max, int bits)
        {
            Vec3 v;
            v.x = ReadQ(min, max, bits);
            v.y = ReadQ(min, max, bits);
            v.z = ReadQ(min, max, bits);
            return v;
        }

        public void WriteString(string s)
        {
            if (s == null) s = "";
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            int len = bytes.Length > 255 ? 255 : bytes.Length;
            WriteByte((byte)len);
            for (int i = 0; i < len; i++) WriteByte(bytes[i]);
        }

        public string ReadString()
        {
            int len = ReadByte();
            byte[] bytes = new byte[len];
            for (int i = 0; i < len; i++) bytes[i] = ReadByte();
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>Long string (up to 64KB) - used for the tuning payload.</summary>
        public void WriteString2(string s)
        {
            if (s == null) s = "";
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            int len = bytes.Length > 65535 ? 65535 : bytes.Length;
            WriteUShort((ushort)len);
            for (int i = 0; i < len; i++) WriteByte(bytes[i]);
        }

        public string ReadString2()
        {
            int len = ReadUShort();
            byte[] bytes = new byte[len];
            for (int i = 0; i < len; i++) bytes[i] = ReadByte();
            return Encoding.UTF8.GetString(bytes);
        }

        public void WriteBytes(byte[] src, int offset, int count)
        {
            WriteUShort((ushort)count);
            for (int i = 0; i < count; i++) WriteByte(src[offset + i]);
        }

        public byte[] ReadBytes()
        {
            int count = ReadUShort();
            byte[] dst = new byte[count];
            for (int i = 0; i < count; i++) dst[i] = ReadByte();
            return dst;
        }

        public byte[] ToArray()
        {
            byte[] result = new byte[BytePosition];
            Buffer.BlockCopy(Data, 0, result, 0, result.Length);
            return result;
        }
    }
}
