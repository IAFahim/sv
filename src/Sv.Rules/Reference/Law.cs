using System.Collections.Generic;

namespace Sv.Rules.Reference
{
    public readonly struct ShapeEntry
    {
        public ShapeKind Kind { get; }

        public float Value { get; }

        public ShapeEntry(ShapeKind kind, float value)
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

        public static float StatFold(float start, List<ShapeEntry> entries)
        {
            var value = start;
            var additive = 1f;
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].Kind == ShapeKind.Added)
                {
                    value = FloatAdd(value, entries[i].Value);
                }
            }

            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].Kind == ShapeKind.Additive)
                {
                    additive = FloatAdd(additive, entries[i].Value);
                }
            }

            value = FloatMultiply(value, additive);
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].Kind == ShapeKind.Multi)
                {
                    value = FloatMultiply(value, FloatAdd(1f, entries[i].Value));
                }
            }

            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].Kind == ShapeKind.Cap)
                {
                    value = value > entries[i].Value ? entries[i].Value : value;
                }
            }

            return value;
        }

        public static int ShapeAmount(int amount, List<ShapeEntry> entries) =>
            entries.Count == 0 ? amount : BoundOf(StatFold(amount, entries));

        public static bool SameBits(float left, float right) =>
            System.BitConverter.SingleToInt32Bits(left) == System.BitConverter.SingleToInt32Bits(right);
    }
}
