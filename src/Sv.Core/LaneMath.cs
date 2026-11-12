using System.Runtime.CompilerServices;
#if NET
using System.Runtime.Intrinsics;
#endif

namespace Sv
{
    public static unsafe class Mask
    {
        public const ulong PackMultiplier = 0x0102040810204080ul;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong PackEight(ulong flagBytes) => (flagBytes * PackMultiplier) >> 56;

        public static ulong Pack(byte* flags)
        {
            var words = (ulong*)flags;
            var mask = 0ul;
            for (var group = 0; group < 8; group++)
            {
                mask |= PackEight(words[group]) << (group << 3);
            }

            return mask;
        }

        public static ulong Positive(int* lane)
        {
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                var mask = 0ul;
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    mask |= (ulong)Vector256.GreaterThan(Vector256.Load(lane + at), Vector256<int>.Zero).ExtractMostSignificantBits() << at;
                }

                return mask;
            }
#endif
            var flags = stackalloc byte[Lanes.Rows];
            for (var row = 0; row < Lanes.Rows; row++)
            {
                flags[row] = lane[row] > 0 ? (byte)1 : (byte)0;
            }

            return Pack(flags);
        }

        public static ulong NonZero(int* lane)
        {
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                var mask = 0ul;
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    mask |= (ulong)(~Vector256.Equals(Vector256.Load(lane + at), Vector256<int>.Zero)).ExtractMostSignificantBits() << at;
                }

                return mask;
            }
#endif
            var flags = stackalloc byte[Lanes.Rows];
            for (var row = 0; row < Lanes.Rows; row++)
            {
                flags[row] = lane[row] != 0 ? (byte)1 : (byte)0;
            }

            return Pack(flags);
        }

        public static ulong EqualTo(int* lane, int value)
        {
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                var mask = 0ul;
                var wanted = Vector256.Create(value);
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    mask |= (ulong)Vector256.Equals(Vector256.Load(lane + at), wanted).ExtractMostSignificantBits() << at;
                }

                return mask;
            }
#endif
            var flags = stackalloc byte[Lanes.Rows];
            for (var row = 0; row < Lanes.Rows; row++)
            {
                flags[row] = lane[row] == value ? (byte)1 : (byte)0;
            }

            return Pack(flags);
        }

        public static ulong Differ(int* first, int* second)
        {
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                var mask = 0ul;
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    mask |= (ulong)(~Vector256.Equals(Vector256.Load(first + at), Vector256.Load(second + at))).ExtractMostSignificantBits() << at;
                }

                return mask;
            }
#endif
            var flags = stackalloc byte[Lanes.Rows];
            for (var row = 0; row < Lanes.Rows; row++)
            {
                flags[row] = first[row] != second[row] ? (byte)1 : (byte)0;
            }

            return Pack(flags);
        }

        public static ulong Less(int* first, int* second)
        {
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                var mask = 0ul;
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    mask |= (ulong)Vector256.LessThan(Vector256.Load(first + at), Vector256.Load(second + at)).ExtractMostSignificantBits() << at;
                }

                return mask;
            }
#endif
            var flags = stackalloc byte[Lanes.Rows];
            for (var row = 0; row < Lanes.Rows; row++)
            {
                flags[row] = first[row] < second[row] ? (byte)1 : (byte)0;
            }

            return Pack(flags);
        }

        public static ulong Greater(int* first, int* second)
        {
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                var mask = 0ul;
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    mask |= (ulong)Vector256.GreaterThan(Vector256.Load(first + at), Vector256.Load(second + at)).ExtractMostSignificantBits() << at;
                }

                return mask;
            }
#endif
            var flags = stackalloc byte[Lanes.Rows];
            for (var row = 0; row < Lanes.Rows; row++)
            {
                flags[row] = first[row] > second[row] ? (byte)1 : (byte)0;
            }

            return Pack(flags);
        }

        public static ulong LessFloat(float* first, float* second)
        {
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                var mask = 0ul;
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    mask |= (ulong)Vector256.LessThan(Vector256.Load(first + at), Vector256.Load(second + at)).ExtractMostSignificantBits() << at;
                }

                return mask;
            }
#endif
            var flags = stackalloc byte[Lanes.Rows];
            for (var row = 0; row < Lanes.Rows; row++)
            {
                flags[row] = first[row] < second[row] ? (byte)1 : (byte)0;
            }

            return Pack(flags);
        }

        public static ulong GreaterFloat(float* first, float* second)
        {
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                var mask = 0ul;
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    mask |= (ulong)Vector256.GreaterThan(Vector256.Load(first + at), Vector256.Load(second + at)).ExtractMostSignificantBits() << at;
                }

                return mask;
            }
#endif
            var flags = stackalloc byte[Lanes.Rows];
            for (var row = 0; row < Lanes.Rows; row++)
            {
                flags[row] = first[row] > second[row] ? (byte)1 : (byte)0;
            }

            return Pack(flags);
        }
    }

    public static unsafe class LaneMath
    {
        public static void Fill(int* lane, int value)
        {
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                var broadcast = Vector256.Create(value);
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    broadcast.Store(lane + at);
                }

                return;
            }
#endif
            for (var row = 0; row < Lanes.Rows; row++)
            {
                lane[row] = value;
            }
        }

        public static void FillWords(ulong* lane, ulong value)
        {
            for (var word = 0; word < Lanes.Words; word++)
            {
                lane[word] = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Copy(void* source, void* destination) => Memory.Copy(source, destination, (nuint)Lanes.Bytes);

        public static void Select(ulong mask, int* whenSet, int* whenClear, int* output)
        {
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                var rowBits = Vector256.Create(1, 2, 4, 8, 16, 32, 64, 128);
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    var chosen = Vector256.Equals(Vector256.Create((int)(mask >> at) & 0xFF) & rowBits, rowBits);
                    Vector256.ConditionalSelect(chosen, Vector256.Load(whenSet + at), Vector256.Load(whenClear + at)).Store(output + at);
                }

                return;
            }
#endif
            for (var row = 0; row < Lanes.Rows; row++)
            {
                output[row] = ((mask >> row) & 1ul) != 0ul ? whenSet[row] : whenClear[row];
            }
        }

        public static void Add(int* first, int* second, int* output)
        {
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    (Vector256.Load(first + at) + Vector256.Load(second + at)).Store(output + at);
                }

                return;
            }
#endif
            for (var row = 0; row < Lanes.Rows; row++)
            {
                output[row] = unchecked(first[row] + second[row]);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int AddSaturate(int first, int second)
        {
            var sum = unchecked(first + second);
            return ((first ^ sum) & (second ^ sum)) < 0 ? (first >> 31) ^ int.MaxValue : sum;
        }

        public static void AddSaturate(int* first, int* second, int* output)
        {
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                var top = Vector256.Create(int.MaxValue);
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    var a = Vector256.Load(first + at);
                    var b = Vector256.Load(second + at);
                    var sum = a + b;
                    var overflow = Vector256.LessThan((a ^ sum) & (b ^ sum), Vector256<int>.Zero);
                    Vector256.ConditionalSelect(overflow, Vector256.ShiftRightArithmetic(a, 31) ^ top, sum).Store(output + at);
                }

                return;
            }
#endif
            for (var row = 0; row < Lanes.Rows; row++)
            {
                output[row] = AddSaturate(first[row], second[row]);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Clamp(int value, int floor, int ceiling)
        {
            var low = floor < ceiling ? floor : ceiling;
            return value < low ? low : value > ceiling ? ceiling : value;
        }

        public static void Clamp(int* value, int* floor, int* ceiling, int* output)
        {
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    var high = Vector256.Load(ceiling + at);
                    var low = Vector256.Min(Vector256.Load(floor + at), high);
                    Vector256.Max(Vector256.Min(Vector256.Load(value + at), high), low).Store(output + at);
                }

                return;
            }
#endif
            for (var row = 0; row < Lanes.Rows; row++)
            {
                output[row] = Clamp(value[row], floor[row], ceiling[row]);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BoundOf(float value) =>
            float.IsNaN(value) ? 0 : value >= 2147483648.0f ? int.MaxValue : value < -2147483648.0f ? int.MinValue : unchecked((int)value);

        public static void BoundOf(float* value, int* output)
        {
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                var top = Vector256.Create(2147483648.0f);
                var bottom = Vector256.Create(-2147483648.0f);
                var max = Vector256.Create(int.MaxValue);
                var min = Vector256.Create(int.MinValue);
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    var v = Vector256.Load(value + at);
                    var truncated = Vector256.ConvertToInt32(v);
                    var number = Vector256.Equals(v, v).AsInt32();
                    var above = Vector256.GreaterThanOrEqual(v, top).AsInt32();
                    var below = Vector256.LessThan(v, bottom).AsInt32();
                    var bounded = Vector256.ConditionalSelect(above, max, Vector256.ConditionalSelect(below, min, truncated));
                    (bounded & number).Store(output + at);
                }

                return;
            }
#endif
            for (var row = 0; row < Lanes.Rows; row++)
            {
                output[row] = BoundOf(value[row]);
            }
        }

        public static void ToFloat(int* value, float* output)
        {
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    Vector256.ConvertToSingle(Vector256.Load(value + at)).Store(output + at);
                }

                return;
            }
#endif
            for (var row = 0; row < Lanes.Rows; row++)
            {
                output[row] = value[row];
            }
        }

        public static void AddFloat(float* first, float* second, float* output)
        {
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    (Vector256.Load(first + at) + Vector256.Load(second + at)).Store(output + at);
                }

                return;
            }
#endif
            for (var row = 0; row < Lanes.Rows; row++)
            {
                output[row] = (float)(first[row] + second[row]);
            }
        }

        public static void SubtractFloat(float* first, float* second, float* output)
        {
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    (Vector256.Load(first + at) - Vector256.Load(second + at)).Store(output + at);
                }

                return;
            }
#endif
            for (var row = 0; row < Lanes.Rows; row++)
            {
                output[row] = (float)(first[row] - second[row]);
            }
        }

        public static void MultiplyFloat(float* first, float* second, float* output)
        {
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    (Vector256.Load(first + at) * Vector256.Load(second + at)).Store(output + at);
                }

                return;
            }
#endif
            for (var row = 0; row < Lanes.Rows; row++)
            {
                output[row] = (float)(first[row] * second[row]);
            }
        }

        public static uint RowLevels(ulong* eventLane, int row)
        {
            var levels = 0u;
            for (var word = 0; word < Lanes.Words; word++)
            {
                levels |= (uint)((eventLane[word] >> row) & 1ul) << word;
            }

            return levels;
        }

        public static void SetRowLevels(ulong* eventLane, int row, uint levels)
        {
            var bit = 1ul << row;
            for (var word = 0; word < Lanes.Words; word++)
            {
                var set = ((levels >> word) & 1u) != 0u;
                eventLane[word] = set ? eventLane[word] | bit : eventLane[word] & ~bit;
            }
        }

        public static void BlendWords(ulong mask, ulong* whenSet, ulong* whenClear, ulong* output)
        {
            for (var word = 0; word < Lanes.Words; word++)
            {
                output[word] = (whenSet[word] & mask) | (whenClear[word] & ~mask);
            }
        }

        public static ulong DifferWords(ulong* first, ulong* second)
        {
            var differ = 0ul;
            for (var word = 0; word < Lanes.Words; word++)
            {
                differ |= first[word] ^ second[word];
            }

            return differ;
        }
    }
}
