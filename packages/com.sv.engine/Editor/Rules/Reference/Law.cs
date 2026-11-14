using System.Collections.Generic;
using System.Numerics;

namespace Sv.Rules.Reference
{
    public readonly struct ShapeEntry
    {
        public ShapeKind Kind { get; }

        public int Value { get; }

        public ShapeEntry(ShapeKind kind, int value)
        {
            Kind = kind;
            Value = value;
        }
    }

    public static class ReferenceLaw
    {
        public const long TermLimit = 1L << 32;

        public static int Saturate(long value) => value > int.MaxValue ? int.MaxValue : value < int.MinValue ? int.MinValue : (int)value;

        public static int Step(int before, int delta, int floor, int ceiling)
        {
            var low = floor < ceiling ? floor : ceiling;
            var moved = (long)before + delta;
            return moved < low ? low : moved > ceiling ? ceiling : (int)moved;
        }

        public static int BoundOf(float value)
        {
            if (float.IsNaN(value))
            {
                return 0;
            }

            if (value >= 2147483648.0f)
            {
                return int.MaxValue;
            }

            if (value < -2147483648.0f)
            {
                return int.MinValue;
            }

            return unchecked((int)value);
        }

        public static bool Below(long value, long whole, long numerator, long denominator) =>
            Multiply(value, denominator) < Multiply(whole, numerator);

        public static bool NextLevel(bool state, bool enter, bool stay) => state ? stay : enter;

        public static long Term(int amount, int hits)
        {
            var product = (long)amount * hits;
            return product > TermLimit ? TermLimit : product < -TermLimit ? -TermLimit : product;
        }

        public static long Add(long left, long right)
        {
            var sum = unchecked(left + right);
            if (((left ^ sum) & (right ^ sum)) < 0)
            {
                return left < 0 ? long.MinValue : long.MaxValue;
            }

            return sum;
        }

        public static long Negate(long value) => value == long.MinValue ? long.MaxValue : -value;

        public static long Multiply(long left, long right)
        {
            if (left == 0 || right == 0)
            {
                return 0;
            }

            if (right == -1)
            {
                return Negate(left);
            }

            if (left == -1)
            {
                return Negate(right);
            }

            var negative = (left < 0) != (right < 0);
            if (left == long.MinValue || right == long.MinValue)
            {
                return negative ? long.MinValue : long.MaxValue;
            }

            var product = unchecked(left * right);
            if (product / right != left)
            {
                return negative ? long.MinValue : long.MaxValue;
            }

            return product;
        }

        public static float FloatAdd(float left, float right) => (float)(left + right);

        public static float FloatMultiply(float left, float right) => (float)(left * right);

        public const int Whole = 10000;

        static int Clamp32(BigInteger value) =>
            value > int.MaxValue ? int.MaxValue : value < int.MinValue ? int.MinValue : (int)value;

        static int Times(int value, int factor, int divisor) => Clamp32(BigInteger.Divide((BigInteger)value * factor, divisor));

        public static int StatFold(int start, List<ShapeEntry> entries)
        {
            var sorted = new List<ShapeEntry>(entries);
            sorted.Sort(ShapeOrder);
            var added = (BigInteger)start;
            var additive = (BigInteger)Whole;
            for (var i = 0; i < sorted.Count; i++)
            {
                added += sorted[i].Kind == ShapeKind.Added ? sorted[i].Value : 0;
                additive += sorted[i].Kind == ShapeKind.Additive ? sorted[i].Value : 0;
            }

            var value = Times(Clamp32(added), Clamp32(additive), Whole);
            for (var i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].Kind == ShapeKind.Multi)
                {
                    value = Times(value, Clamp32((BigInteger)Whole + sorted[i].Value), Whole);
                }
            }

            for (var i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].Kind == ShapeKind.Cap && value > sorted[i].Value)
                {
                    value = sorted[i].Value;
                }
            }

            return value;
        }

        public static int StatFold(int start, List<ShapeEntry> core, List<ShapeEntry> external)
        {
            var afterCore = StatFold(start, core);
            return external.Count == 0 ? afterCore : StatFold(afterCore, external);
        }

        public static int Derived(int source, int coefficient, int sourceScale, int targetScale)
        {
            var from = ScaleStep(sourceScale);
            var to = ScaleStep(targetScale);
            var wide = (BigInteger)source * coefficient;
            return from >= to
                ? Clamp32(BigInteger.Divide(wide, (BigInteger)Whole * (from / to)))
                : Clamp32(BigInteger.Divide(wide * (to / from), Whole));
        }

        public static int CurveBucket(int value, int min, int step, int length)
        {
            if (length <= 1 || step <= 0 || value <= min)
            {
                return 0;
            }

            var index = ((long)value - min) / step;
            return index >= length ? length - 1 : (int)index;
        }

        static int ShapeOrder(ShapeEntry first, ShapeEntry second) =>
            first.Kind != second.Kind ? first.Kind.CompareTo(second.Kind) : first.Value.CompareTo(second.Value);

        public static int ShapeAmount(int amount, List<ShapeEntry> entries) => StatFold(amount, entries);

        static int ScaleStep(int scale) =>
            scale == 1 || scale == 10 || scale == 100 || scale == 1000 || scale == 10000 || scale == 100000 || scale == 1000000 ? scale : 1;

        public static int Rescale(int value, int fromScale, int toScale)
        {
            var from = ScaleStep(fromScale);
            var to = ScaleStep(toScale);
            return from >= to ? Clamp32(BigInteger.Divide(value, from / to)) : Clamp32((BigInteger)value * (to / from));
        }

        public static int ScaleBy(int amount, int value, int scale) => Times(amount, value, ScaleStep(scale));

        public static int CardTerm(int value, int count, bool perCount) => perCount ? Clamp32((BigInteger)value * count) : value;

        const ulong Golden = 0x9E3779B97F4A7C15ul;
        const ulong FirstMultiplier = 0xBF58476D1CE4E5B9ul;
        const ulong SecondMultiplier = 0x94D049BB133111EBul;

        public static ulong SplitMix(ulong value)
        {
            var z = unchecked(value + Golden);
            z = unchecked((z ^ (z >> 30)) * FirstMultiplier);
            z = unchecked((z ^ (z >> 27)) * SecondMultiplier);
            return z ^ (z >> 31);
        }

        public static ulong RollHash(ulong seed, long frame, ulong key, int amount, int rule, int ordinal)
        {
            var inputs = new[] { unchecked((ulong)frame), key, (ulong)(uint)amount, (ulong)(uint)rule, (ulong)(uint)ordinal };
            var hash = seed;
            for (var i = 0; i < inputs.Length; i++)
            {
                hash = SplitMix(hash ^ inputs[i]);
            }

            return hash;
        }

        public static bool Rolls(ulong seed, long frame, ulong key, int amount, int rule, int ordinal, long basisPoints)
        {
            var share = (BigInteger)(RollHash(seed, frame, key, amount, rule, ordinal) >> 32) * 10000 / ((BigInteger)1 << 32);
            return share < basisPoints;
        }
    }
}
