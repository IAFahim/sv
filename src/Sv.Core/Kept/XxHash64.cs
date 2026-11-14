using System;
using System.Runtime.CompilerServices;

namespace Sv
{
    internal static unsafe class XxHash64
    {
        const ulong P1 = 0x9E3779B185EBCA87ul;

        const ulong P2 = 0xC2B2AE3D27D4EB4Ful;

        const ulong P3 = 0x165667B19E3779F9ul;

        const ulong P4 = 0x85EBCA77C2B2AE63ul;

        const ulong P5 = 0x27D4EB2F165667C5ul;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong Read64(byte* p)
        {
            var v = Bits.ReadUnaligned64(p);
            return BitConverter.IsLittleEndian ? v : System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint Read32(byte* p)
        {
            var v = Bits.ReadUnaligned32(p);
            return BitConverter.IsLittleEndian ? v : System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong Round(ulong acc, ulong input)
        {
            unchecked
            {
                acc += input * P2;
                acc = Bits.RotateLeft(acc, 31);
                return acc * P1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong Merge(ulong acc, ulong value)
        {
            unchecked
            {
                acc ^= Round(0, value);
                return acc * P1 + P4;
            }
        }

        internal static ulong Hash(byte* data, int length, ulong seed = 0)
        {
            unchecked
            {
                var p = data;
                var end = data + length;
                ulong h;
                if (length >= 32)
                {
                    var v1 = seed + P1 + P2;
                    var v2 = seed + P2;
                    var v3 = seed;
                    var v4 = seed - P1;
                    var limit = end - 32;
                    while (p <= limit)
                    {
                        v1 = Round(v1, Read64(p));
                        v2 = Round(v2, Read64(p + 8));
                        v3 = Round(v3, Read64(p + 16));
                        v4 = Round(v4, Read64(p + 24));
                        p += 32;
                    }

                    h = Bits.RotateLeft(v1, 1) + Bits.RotateLeft(v2, 7) + Bits.RotateLeft(v3, 12) + Bits.RotateLeft(v4, 18);
                    h = Merge(h, v1);
                    h = Merge(h, v2);
                    h = Merge(h, v3);
                    h = Merge(h, v4);
                }
                else
                {
                    h = seed + P5;
                }

                h += (ulong)length;
                while (p + 8 <= end)
                {
                    h ^= Round(0, Read64(p));
                    h = Bits.RotateLeft(h, 27) * P1 + P4;
                    p += 8;
                }

                if (p + 4 <= end)
                {
                    h ^= Read32(p) * P1;
                    h = Bits.RotateLeft(h, 23) * P2 + P3;
                    p += 4;
                }

                while (p < end)
                {
                    h ^= *p * P5;
                    h = Bits.RotateLeft(h, 11) * P1;
                    p++;
                }

                h ^= h >> 33;
                h *= P2;
                h ^= h >> 29;
                h *= P3;
                h ^= h >> 32;
                return h;
            }
        }
    }
}
