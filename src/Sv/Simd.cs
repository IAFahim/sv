using System.Runtime.Intrinsics;

namespace Sv;

internal static unsafe partial class Simd
{
    internal static bool Has128 => Vector128.IsHardwareAccelerated;

    internal static bool Has256 => Vector256.IsHardwareAccelerated;

    internal static int SaturatingAdd(int a, int b)
    {
        var sum = a + b;
        return ((a ^ sum) & (b ^ sum)) < 0 ? (b < 0 ? int.MinValue : int.MaxValue) : sum;
    }

    internal static int SaturatingSubtract(int after, int before)
    {
        var diff = after - before;
        return ((after ^ before) & (after ^ diff)) < 0 ? (after < 0 ? int.MinValue : int.MaxValue) : diff;
    }

    internal static void Chunk(int* value, int* delta, int deltaValue, in Clamp clamp, int start, int count, int* applied)
    {
        clamp.CheckStatBounds();
        if (Vector256.IsHardwareAccelerated)
        {
            ChunkW256(value, delta, deltaValue, clamp, start, count, applied);
            return;
        }

        if (Vector128.IsHardwareAccelerated)
        {
            ChunkW128(value, delta, deltaValue, clamp, start, count, applied);
            return;
        }

        ChunkScalar(value, delta, deltaValue, clamp, start, count, applied);
    }

    internal static void ChunkScalar(int* value, int* delta, int deltaValue, in Clamp clamp, int start, int count, int* applied)
    {
        for (var i = 0; i < count; i++)
        {
            var slot = start + i;
            var before = value[slot];
            var sum = delta == null ? SaturatingAdd(before, deltaValue) : SaturatingAdd(before, delta[slot]);
            var after = clamp.Apply(sum, slot);
            value[slot] = after;
            if (applied != null)
            {
                applied[slot] = SaturatingSubtract(after, before);
            }
        }
    }

    internal static void ChunkW128(int* value, int* delta, int deltaValue, in Clamp clamp, int start, int count, int* applied)
    {
        Dispatch<Width128>(value, delta, deltaValue, clamp, start, count, applied);
    }

    internal static void ChunkW256(int* value, int* delta, int deltaValue, in Clamp clamp, int start, int count, int* applied)
    {
        Dispatch<Width256>(value, delta, deltaValue, clamp, start, count, applied);
    }

    static Vector128<int> BoundsOf(Vector128<float> v)
    {
        var converted = Vector128.ConvertToInt32Native(v);
        var low = Vector128.ConditionalSelect(Vector128.As<float, int>(Vector128.LessThan(v, Vector128.Create(-2147483648.0f))), Vector128.Create(int.MinValue), converted);
        var high = Vector128.ConditionalSelect(Vector128.As<float, int>(Vector128.GreaterThanOrEqual(v, Vector128.Create(2147483648.0f))), Vector128.Create(int.MaxValue), low);
        return Vector128.ConditionalSelect(Vector128.As<float, int>(Vector128.Equals(v, v)), high, Vector128<int>.Zero);
    }

    static Vector256<int> BoundsOf(Vector256<float> v)
    {
        var converted = Vector256.ConvertToInt32Native(v);
        var low = Vector256.ConditionalSelect(Vector256.As<float, int>(Vector256.LessThan(v, Vector256.Create(-2147483648.0f))), Vector256.Create(int.MinValue), converted);
        var high = Vector256.ConditionalSelect(Vector256.As<float, int>(Vector256.GreaterThanOrEqual(v, Vector256.Create(2147483648.0f))), Vector256.Create(int.MaxValue), low);
        return Vector256.ConditionalSelect(Vector256.As<float, int>(Vector256.Equals(v, v)), high, Vector256<int>.Zero);
    }

    static Vector128<int> ClampBoth(Vector128<int> v, Vector128<int> lo, Vector128<int> hi)
    {
        var floor = Vector128.Min(lo, hi);
        var above = Vector128.ConditionalSelect(Vector128.GreaterThan(v, hi), hi, v);
        return Vector128.ConditionalSelect(Vector128.LessThan(v, floor), floor, above);
    }

    static Vector128<int> MinOnly(Vector128<int> v, Vector128<int> lo)
    {
        return Vector128.ConditionalSelect(Vector128.LessThan(v, lo), lo, v);
    }

    static Vector128<int> MaxOnly(Vector128<int> v, Vector128<int> hi)
    {
        return Vector128.ConditionalSelect(Vector128.GreaterThan(v, hi), hi, v);
    }

    static Vector256<int> ClampBoth(Vector256<int> v, Vector256<int> lo, Vector256<int> hi)
    {
        var floor = Vector256.Min(lo, hi);
        var above = Vector256.ConditionalSelect(Vector256.GreaterThan(v, hi), hi, v);
        return Vector256.ConditionalSelect(Vector256.LessThan(v, floor), floor, above);
    }

    static Vector256<int> MinOnly(Vector256<int> v, Vector256<int> lo)
    {
        return Vector256.ConditionalSelect(Vector256.LessThan(v, lo), lo, v);
    }

    static Vector256<int> MaxOnly(Vector256<int> v, Vector256<int> hi)
    {
        return Vector256.ConditionalSelect(Vector256.GreaterThan(v, hi), hi, v);
    }

    internal static ulong BoolNonZeroWord(byte* cond, int row)
    {
        if (Vector256.IsHardwareAccelerated)
        {
            return BoolWord256(cond, row);
        }

        if (Vector128.IsHardwareAccelerated)
        {
            return BoolWord128(cond, row);
        }

        return BoolWordScalar(cond, row);
    }

    internal static ulong BoolWordScalar(byte* cond, int row)
    {
        ulong word = 0;
        for (var i = 0; i < 64; i++)
        {
            if (cond[row + i] != 0)
            {
                word |= 1ul << i;
            }
        }

        return word;
    }

    internal static ulong BoolWord128(byte* cond, int row)
    {
        var zero = Vector128<byte>.Zero;
        ulong word = 0;
        for (var part = 0; part < 4; part++)
        {
            var v = Vector128.Load(cond + row + part * 16);
            var bits = ~Vector128.ExtractMostSignificantBits(Vector128.Equals(v, zero)) & 0xFFFFu;
            word |= (ulong)bits << (part * 16);
        }

        return word;
    }

    internal static ulong BoolWord256(byte* cond, int row)
    {
        var zero = Vector256<byte>.Zero;
        ulong word = 0;
        for (var part = 0; part < 2; part++)
        {
            var v = Vector256.Load(cond + row + part * 32);
            var bits = ~Vector256.ExtractMostSignificantBits(Vector256.Equals(v, zero)) & 0xFFFFFFFFu;
            word |= (ulong)bits << (part * 32);
        }

        return word;
    }

    internal static ulong IntNonZeroWord(int* values, int row)
    {
        if (Vector256.IsHardwareAccelerated)
        {
            return IntWord256(values, row);
        }

        if (Vector128.IsHardwareAccelerated)
        {
            return IntWord128(values, row);
        }

        return IntWordScalar(values, row);
    }

    internal static ulong IntWordScalar(int* values, int row)
    {
        ulong word = 0;
        for (var i = 0; i < 64; i++)
        {
            if (values[row + i] != 0)
            {
                word |= 1ul << i;
            }
        }

        return word;
    }

    internal static ulong IntWord128(int* values, int row)
    {
        var zero = Vector128<int>.Zero;
        ulong word = 0;
        for (var part = 0; part < 16; part++)
        {
            var v = Vector128.Load(values + row + part * 4);
            var bits = ~Vector128.ExtractMostSignificantBits(Vector128.Equals(v, zero)) & 0xFu;
            word |= (ulong)bits << (part * 4);
        }

        return word;
    }

    internal static ulong IntWord256(int* values, int row)
    {
        var zero = Vector256<int>.Zero;
        ulong word = 0;
        for (var part = 0; part < 8; part++)
        {
            var v = Vector256.Load(values + row + part * 8);
            var bits = ~Vector256.ExtractMostSignificantBits(Vector256.Equals(v, zero)) & 0xFFu;
            word |= (ulong)bits << (part * 8);
        }

        return word;
    }

    internal static void NormalizeBoolsScalar(byte* src, byte* dst, int count)
    {
        for (var i = 0; i < count; i++)
        {
            dst[i] = (byte)(src[i] != 0 ? 1 : 0);
        }
    }

    internal static void NormalizeBools128(byte* src, byte* dst, int count)
    {
        int i;
        for (i = 0; i + 16 <= count; i += 16)
        {
            var v = Vector128.Load(src + i);
            var isZero = Vector128.Equals(v, Vector128<byte>.Zero);
            Vector128.Store(Vector128.ConditionalSelect(isZero, Vector128<byte>.Zero, Vector128<byte>.One), dst + i);
        }

        for (; i < count; i++)
        {
            dst[i] = (byte)(src[i] != 0 ? 1 : 0);
        }
    }

    internal static void NormalizeBools256(byte* src, byte* dst, int count)
    {
        int i;
        for (i = 0; i + 32 <= count; i += 32)
        {
            var v = Vector256.Load(src + i);
            var isZero = Vector256.Equals(v, Vector256<byte>.Zero);
            Vector256.Store(Vector256.ConditionalSelect(isZero, Vector256<byte>.Zero, Vector256<byte>.One), dst + i);
        }

        for (; i < count; i++)
        {
            dst[i] = (byte)(src[i] != 0 ? 1 : 0);
        }
    }

    internal static void ApplyButtonWords(ulong* words, ulong* buttons, ulong* previous, ulong* pressed, ulong* released, int count)
    {
        if (Vector256.IsHardwareAccelerated)
        {
            ApplyButtonWords256(words, buttons, previous, pressed, released, count);
            return;
        }

        if (Vector128.IsHardwareAccelerated)
        {
            ApplyButtonWords128(words, buttons, previous, pressed, released, count);
            return;
        }

        ApplyButtonWordsScalar(words, buttons, previous, pressed, released, count);
    }

    internal static void ApplyButtonWordsScalar(ulong* words, ulong* buttons, ulong* previous, ulong* pressed, ulong* released, int count)
    {
        for (var w = 0; w < count; w++)
        {
            previous[w] = buttons[w];
            var word = words[w];
            pressed[w] = word & ~previous[w];
            released[w] = ~word & previous[w];
            buttons[w] = word;
        }
    }

    internal static void ApplyButtonWords128(ulong* words, ulong* buttons, ulong* previous, ulong* pressed, ulong* released, int count)
    {
        int w;
        for (w = 0; w + 2 <= count; w += 2)
        {
            var old = Vector128.Load(buttons + w);
            var cur = Vector128.Load(words + w);
            Vector128.Store(old, previous + w);
            Vector128.Store(cur & ~old, pressed + w);
            Vector128.Store(old & ~cur, released + w);
            Vector128.Store(cur, buttons + w);
        }

        for (; w < count; w++)
        {
            previous[w] = buttons[w];
            var word = words[w];
            pressed[w] = word & ~previous[w];
            released[w] = ~word & previous[w];
            buttons[w] = word;
        }
    }

    internal static void ApplyButtonWords256(ulong* words, ulong* buttons, ulong* previous, ulong* pressed, ulong* released, int count)
    {
        int w;
        for (w = 0; w + 4 <= count; w += 4)
        {
            var old = Vector256.Load(buttons + w);
            var cur = Vector256.Load(words + w);
            Vector256.Store(old, previous + w);
            Vector256.Store(cur & ~old, pressed + w);
            Vector256.Store(old & ~cur, released + w);
            Vector256.Store(cur, buttons + w);
        }

        for (; w < count; w++)
        {
            previous[w] = buttons[w];
            var word = words[w];
            pressed[w] = word & ~previous[w];
            released[w] = ~word & previous[w];
            buttons[w] = word;
        }
    }
}
