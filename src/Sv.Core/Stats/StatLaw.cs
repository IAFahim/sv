using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if NET
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif

namespace Sv
{
    public enum ShapeKind : byte
    {
        Added = 0,
        Additive = 1,
        Multi = 2,
        Cap = 3,
    }

    public enum StatLayer : byte
    {
        Core = 0,
        External = 1,
    }

    [StructLayout(LayoutKind.Sequential, Size = 8)]
    public readonly struct ShapeTerm
    {
        public readonly int Value;
        public readonly ShapeKind Kind;

        public ShapeTerm(ShapeKind kind, int value)
        {
            Kind = kind;
            Value = value;
        }
    }

    public static unsafe class StatLaw
    {
        public const int Whole = 10000;
        public const int MinScale = 1;
        public const int MaxScale = 1000000;

        public static bool IsScale(int scale)
        {
            var at = MinScale;
            while (at < scale && at < MaxScale)
            {
                at *= 10;
            }

            return at == scale;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ScaleOrOne(int scale) => IsScale(scale) ? scale : MinScale;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Saturate(long value) => value > int.MaxValue ? int.MaxValue : value < int.MinValue ? int.MinValue : (int)value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Added(int value, long addedSum) =>
            addedSum > (long)int.MaxValue - value ? int.MaxValue : addedSum < (long)int.MinValue - value ? int.MinValue : (int)(value + addedSum);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Factor(long basisPointSum) => Added(Whole, basisPointSum);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Percent(int value, int factor) => Saturate((long)value * factor / Whole);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Multi(int value, int basisPoints) => Percent(value, Saturate((long)Whole + basisPoints));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Cap(int value, int cap) => value > cap ? cap : value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Precedes(in ShapeTerm first, in ShapeTerm second) =>
            first.Kind < second.Kind || (first.Kind == second.Kind && first.Value < second.Value);

        public static void Canonicalize(ShapeTerm* terms, int count)
        {
            for (var at = 1; at < count; at++)
            {
                var term = terms[at];
                var into = at - 1;
                while (into >= 0 && Precedes(term, terms[into]))
                {
                    terms[into + 1] = terms[into];
                    into--;
                }

                terms[into + 1] = term;
            }
        }

        public static int Fold(int start, ShapeTerm* terms, int count)
        {
            var addedSum = 0L;
            var additiveSum = 0L;
            for (var at = 0; at < count; at++)
            {
                var kind = terms[at].Kind;
                addedSum += kind == ShapeKind.Added ? terms[at].Value : 0;
                additiveSum += kind == ShapeKind.Additive ? terms[at].Value : 0;
            }

            var value = Percent(Added(start, addedSum), Factor(additiveSum));
            for (var at = 0; at < count; at++)
            {
                if (terms[at].Kind == ShapeKind.Multi)
                {
                    value = Multi(value, terms[at].Value);
                }
            }

            for (var at = 0; at < count; at++)
            {
                if (terms[at].Kind == ShapeKind.Cap)
                {
                    value = Cap(value, terms[at].Value);
                }
            }

            return value;
        }

        public static int Fold(int start, ShapeTerm* core, int coreCount, ShapeTerm* external, int externalCount)
        {
            var value = Fold(start, core, coreCount);
            return externalCount > 0 ? Fold(value, external, externalCount) : value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int Ratio(int fromScale, int toScale)
        {
            var from = ScaleOrOne(fromScale);
            var to = ScaleOrOne(toScale);
            return from >= to ? from / to : to / from;
        }

        public static int Rescale(int value, int fromScale, int toScale)
        {
            var ratio = Ratio(fromScale, toScale);
            return ScaleOrOne(fromScale) >= ScaleOrOne(toScale) ? value / ratio : Saturate((long)value * ratio);
        }

        public static int ScaleBy(int amount, int value, int scale) => Saturate((long)amount * value / ScaleOrOne(scale));

        public static int Derived(int source, int coefficient, int sourceScale, int targetScale)
        {
            var wide = (long)source * coefficient;
            var ratio = Ratio(sourceScale, targetScale);
            if (ScaleOrOne(sourceScale) >= ScaleOrOne(targetScale))
            {
                return Saturate(wide / ((long)Whole * ratio));
            }

            var limit = long.MaxValue / ratio;
            if (wide > limit || wide < -limit)
            {
                return wide < 0 ? int.MinValue : int.MaxValue;
            }

            return Saturate(wide * ratio / Whole);
        }

        public static int CurveBucket(int value, int min, int step, int length)
        {
            if (length < 1 || step < 1 || value <= min)
            {
                return 0;
            }

            var bucket = ((long)value - min) / step;
            return bucket >= length - 1 ? length - 1 : (int)bucket;
        }

        public static void Rescale(int* value, int fromScale, int toScale, int* output)
        {
            var ratio = Ratio(fromScale, toScale);
            var down = ScaleOrOne(fromScale) >= ScaleOrOne(toScale);
#if NET
            if (ratio == 1)
            {
                LaneMath.Copy(value, output);
                return;
            }

            if (!down && Vector256.IsHardwareAccelerated)
            {
                var high = Vector256.Create(int.MaxValue / ratio);
                var low = Vector256.Create(int.MinValue / ratio);
                var factor = Vector256.Create(ratio);
                var max = Vector256.Create(int.MaxValue);
                var min = Vector256.Create(int.MinValue);
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    var v = Vector256.Load(value + at);
                    var scaled = v * factor;
                    var above = Vector256.GreaterThan(v, high);
                    var below = Vector256.LessThan(v, low);
                    Vector256.ConditionalSelect(above, max, Vector256.ConditionalSelect(below, min, scaled)).Store(output + at);
                }

                return;
            }

            if (down && Avx.IsSupported)
            {
                var divisor = Vector256.Create((double)ratio);
                for (var at = 0; at < Lanes.Rows; at += 4)
                {
                    var quotient = Avx.Divide(Avx.ConvertToVector256Double(Vector128.Load(value + at)), divisor);
                    Avx.ConvertToVector128Int32WithTruncation(quotient).Store(output + at);
                }

                return;
            }
#endif
            for (var row = 0; row < Lanes.Rows; row++)
            {
                output[row] = down ? value[row] / ratio : Saturate((long)value[row] * ratio);
            }
        }

        public static void Cap(int* value, int* cap, int* output)
        {
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    var v = Vector256.Load(value + at);
                    var c = Vector256.Load(cap + at);
                    Vector256.ConditionalSelect(Vector256.GreaterThan(v, c), c, v).Store(output + at);
                }

                return;
            }
#endif
            for (var row = 0; row < Lanes.Rows; row++)
            {
                output[row] = Cap(value[row], cap[row]);
            }
        }

        public static void Percent(int* value, int factor, int* output)
        {
            for (var row = 0; row < Lanes.Rows; row++)
            {
                output[row] = Percent(value[row], factor);
            }
        }

        public static void Added(int* value, int added, int* output)
        {
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                if (added == 0)
                {
                    if (value != output)
                    {
                        for (var at = 0; at < Lanes.Rows; at += 8)
                        {
                            Vector256.Load(value + at).Store(output + at);
                        }
                    }

                    return;
                }

                var delta = Vector256.Create(added);
                var top = Vector256.Create(int.MaxValue);
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    var v = Vector256.Load(value + at);
                    var sum = v + delta;
                    var overflow = Vector256.LessThan((v ^ sum) & (delta ^ sum), Vector256<int>.Zero);
                    Vector256.ConditionalSelect(overflow, Vector256.ShiftRightArithmetic(v, 31) ^ top, sum).Store(output + at);
                }

                return;
            }
#endif
            for (var row = 0; row < Lanes.Rows; row++)
            {
                output[row] = Added(value[row], added);
            }
        }

        public static void Cap(int* value, int cap, int* output)
        {
#if NET
            if (Vector256.IsHardwareAccelerated)
            {
                var limit = Vector256.Create(cap);
                for (var at = 0; at < Lanes.Rows; at += 8)
                {
                    var v = Vector256.Load(value + at);
                    Vector256.ConditionalSelect(Vector256.GreaterThan(v, limit), limit, v).Store(output + at);
                }

                return;
            }
#endif
            for (var row = 0; row < Lanes.Rows; row++)
            {
                output[row] = Cap(value[row], cap);
            }
        }

        public static void Fold(int* start, ShapeTerm* terms, int count, int* output)
        {
            var addedSum = 0L;
            var additiveSum = 0L;
            var hasCap = false;
            var minCap = int.MaxValue;
            for (var at = 0; at < count; at++)
            {
                var kind = terms[at].Kind;
                var v = terms[at].Value;
                if (kind == ShapeKind.Added)
                {
                    addedSum += v;
                }
                else if (kind == ShapeKind.Additive)
                {
                    additiveSum += v;
                }
                else if (kind == ShapeKind.Cap && (!hasCap || v < minCap))
                {
                    hasCap = true;
                    minCap = v;
                }
            }

            if (addedSum >= int.MinValue && addedSum <= int.MaxValue)
            {
                Added(start, (int)addedSum, output);
            }
            else
            {
                for (var row = 0; row < Lanes.Rows; row++)
                {
                    output[row] = Added(start[row], addedSum);
                }
            }

            var factor = Factor(additiveSum);
            if (factor != Whole)
            {
                Percent(output, factor, output);
            }

            for (var at = 0; at < count; at++)
            {
                if (terms[at].Kind == ShapeKind.Multi)
                {
                    Percent(output, Saturate((long)Whole + terms[at].Value), output);
                }
            }

            if (hasCap)
            {
                Cap(output, minCap, output);
            }
        }

        public static void Fold(int* start, ShapeTerm* core, int coreCount, ShapeTerm* external, int externalCount, int* output)
        {
            Fold(start, core, coreCount, output);
            if (externalCount > 0)
            {
                Fold(output, external, externalCount, output);
            }
        }

        public static ulong Below(int* value, int* whole, int numerator, int denominator)
        {
            var flags = stackalloc byte[Lanes.Rows];
            for (var row = 0; row < Lanes.Rows; row++)
            {
                flags[row] = Below(value[row], whole[row], numerator, denominator) ? (byte)1 : (byte)0;
            }

            return Mask.Pack(flags);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Below(int value, int whole, int numerator, int denominator) =>
            (long)value * denominator < (long)whole * numerator;
    }
}
