using System.Runtime.CompilerServices;

namespace Sv
{
    public static unsafe class Bits
    {
#if NET
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Tzcnt(ulong value) => System.Numerics.BitOperations.TrailingZeroCount(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Tzcnt(uint value) => System.Numerics.BitOperations.TrailingZeroCount(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Tzcnt(int value) => System.Numerics.BitOperations.TrailingZeroCount(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Lzcnt(ulong value) => System.Numerics.BitOperations.LeadingZeroCount(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Lzcnt(uint value) => System.Numerics.BitOperations.LeadingZeroCount(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PopCount(ulong value) => System.Numerics.BitOperations.PopCount(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PopCount(uint value) => System.Numerics.BitOperations.PopCount(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong RotateLeft(ulong value, int offset) => System.Numerics.BitOperations.RotateLeft(value, offset);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint OfSingle(float value) => System.BitConverter.SingleToUInt32Bits(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToSingle(uint bits) => System.BitConverter.UInt32BitsToSingle(bits);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ReadUnaligned64(byte* p) => Unsafe.ReadUnaligned<ulong>(p);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReadUnaligned32(byte* p) => Unsafe.ReadUnaligned<uint>(p);
#else
        public static int Tzcnt(ulong value)
        {
            if (value == 0)
            {
                return 64;
            }

            var n = 1;
            if ((value & 0xFFFFFFFFul) == 0)
            {
                n += 32;
                value >>= 32;
            }

            if ((value & 0xFFFFul) == 0)
            {
                n += 16;
                value >>= 16;
            }

            if ((value & 0xFFul) == 0)
            {
                n += 8;
                value >>= 8;
            }

            if ((value & 0xFul) == 0)
            {
                n += 4;
                value >>= 4;
            }

            if ((value & 0x3ul) == 0)
            {
                n += 2;
                value >>= 2;
            }

            return n - ((int)value & 1);
        }

        public static int Tzcnt(uint value) => value == 0 ? 32 : Tzcnt((ulong)value);

        public static int Tzcnt(int value) => Tzcnt((uint)value);

        public static int Lzcnt(ulong value)
        {
            if (value == 0)
            {
                return 64;
            }

            var n = 0;
            if ((value & 0xFFFFFFFF00000000ul) == 0)
            {
                n += 32;
                value <<= 32;
            }

            if ((value & 0xFFFF000000000000ul) == 0)
            {
                n += 16;
                value <<= 16;
            }

            if ((value & 0xFF00000000000000ul) == 0)
            {
                n += 8;
                value <<= 8;
            }

            if ((value & 0xF000000000000000ul) == 0)
            {
                n += 4;
                value <<= 4;
            }

            if ((value & 0xC000000000000000ul) == 0)
            {
                n += 2;
                value <<= 2;
            }

            return n + ((value & 0x8000000000000000ul) == 0 ? 1 : 0);
        }

        public static int Lzcnt(uint value) => Lzcnt((ulong)value) - 32;

        public static int PopCount(ulong value)
        {
            unchecked
            {
                value -= (value >> 1) & 0x5555555555555555ul;
                value = (value & 0x3333333333333333ul) + ((value >> 2) & 0x3333333333333333ul);
                value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0Ful;
                return (int)((value * 0x0101010101010101ul) >> 56);
            }
        }

        public static int PopCount(uint value) => PopCount((ulong)value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong RotateLeft(ulong value, int offset) => (value << offset) | (value >> (64 - offset));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint OfSingle(float value) => *(uint*)&value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToSingle(uint bits) => *(float*)&bits;

        public static ulong ReadUnaligned64(byte* p)
        {
            ulong value;
            System.Buffer.MemoryCopy(p, &value, sizeof(ulong), sizeof(ulong));
            return value;
        }

        public static uint ReadUnaligned32(byte* p)
        {
            uint value;
            System.Buffer.MemoryCopy(p, &value, sizeof(uint), sizeof(uint));
            return value;
        }
#endif
    }
}
