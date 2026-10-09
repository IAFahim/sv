using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Sv;

internal static unsafe partial class Simd
{
    internal struct Width128
    {
    }

    internal struct Width256
    {
    }

    internal interface IBound
    {
        Vector128<int> Apply(Vector128<int> v, int slot);

        Vector256<int> Apply(Vector256<int> v, int slot);
    }

    internal interface IDelta
    {
        Vector128<int> Load128(int slot);

        Vector256<int> Load256(int slot);

        int Load(int slot);
    }

    internal interface IApplied
    {
        void Store(Vector128<int> v, int slot);

        void Store(Vector256<int> v, int slot);

        void Store(int v, int slot);
    }

    internal readonly struct NoBound : IBound
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Apply(Vector128<int> v, int slot) => v;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Apply(Vector256<int> v, int slot) => v;
    }

    internal readonly struct ConstantBound : IBound
    {
        readonly int min;

        readonly int max;

        public ConstantBound(int min, int max)
        {
            this.min = min;
            this.max = max;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Apply(Vector128<int> v, int slot) => ClampBoth(v, Vector128.Create(min), Vector128.Create(max));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Apply(Vector256<int> v, int slot) => ClampBoth(v, Vector256.Create(min), Vector256.Create(max));
    }

    internal readonly struct ColumnsBound : IBound
    {
        readonly int* min;

        readonly int* max;

        public ColumnsBound(int* min, int* max)
        {
            this.min = min;
            this.max = max;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Apply(Vector128<int> v, int slot) => ClampBoth(v, Vector128.Load(min + slot), Vector128.Load(max + slot));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Apply(Vector256<int> v, int slot) => ClampBoth(v, Vector256.Load(min + slot), Vector256.Load(max + slot));
    }

    internal readonly struct ColumnMinBound : IBound
    {
        readonly int* min;

        public ColumnMinBound(int* min)
        {
            this.min = min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Apply(Vector128<int> v, int slot) => MinOnly(v, Vector128.Load(min + slot));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Apply(Vector256<int> v, int slot) => MinOnly(v, Vector256.Load(min + slot));
    }

    internal readonly struct ColumnMaxBound : IBound
    {
        readonly int* max;

        public ColumnMaxBound(int* max)
        {
            this.max = max;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Apply(Vector128<int> v, int slot) => MaxOnly(v, Vector128.Load(max + slot));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Apply(Vector256<int> v, int slot) => MaxOnly(v, Vector256.Load(max + slot));
    }

    internal readonly struct StatsBound : IBound
    {
        readonly float* min;

        readonly float* max;

        public StatsBound(float* min, float* max)
        {
            this.min = min;
            this.max = max;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Apply(Vector128<int> v, int slot) => ClampBoth(v, BoundsOf(Vector128.Load(min + slot)), BoundsOf(Vector128.Load(max + slot)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Apply(Vector256<int> v, int slot) => ClampBoth(v, BoundsOf(Vector256.Load(min + slot)), BoundsOf(Vector256.Load(max + slot)));
    }

    internal readonly struct StatMinBound : IBound
    {
        readonly float* min;

        public StatMinBound(float* min)
        {
            this.min = min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Apply(Vector128<int> v, int slot) => MinOnly(v, BoundsOf(Vector128.Load(min + slot)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Apply(Vector256<int> v, int slot) => MinOnly(v, BoundsOf(Vector256.Load(min + slot)));
    }

    internal readonly struct StatMaxBound : IBound
    {
        readonly float* max;

        public StatMaxBound(float* max)
        {
            this.max = max;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Apply(Vector128<int> v, int slot) => MaxOnly(v, BoundsOf(Vector128.Load(max + slot)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Apply(Vector256<int> v, int slot) => MaxOnly(v, BoundsOf(Vector256.Load(max + slot)));
    }

    internal readonly struct UniformDelta : IDelta
    {
        readonly int value;

        public UniformDelta(int value)
        {
            this.value = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Load128(int slot) => Vector128.Create(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Load256(int slot) => Vector256.Create(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Load(int slot) => value;
    }

    internal readonly struct ColumnDelta : IDelta
    {
        readonly int* delta;

        public ColumnDelta(int* delta)
        {
            this.delta = delta;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Load128(int slot) => Vector128.Load(delta + slot);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Load256(int slot) => Vector256.Load(delta + slot);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Load(int slot) => delta[slot];
    }

    internal readonly struct NoApplied : IApplied
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Store(Vector128<int> v, int slot)
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Store(Vector256<int> v, int slot)
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Store(int v, int slot)
        {
        }
    }

    internal readonly struct ColumnApplied : IApplied
    {
        readonly int* applied;

        public ColumnApplied(int* applied)
        {
            this.applied = applied;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Store(Vector128<int> v, int slot) => Vector128.Store(v, applied + slot);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Store(Vector256<int> v, int slot) => Vector256.Store(v, applied + slot);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Store(int v, int slot) => applied[slot] = v;
    }

    static void Dispatch<TW>(int* value, int* delta, int deltaValue, in Clamp clamp, int start, int count, int* applied)
        where TW : struct
    {
        switch (clamp.Kind)
        {
            case ClampKind.Constant:
                WithDelta<TW, ConstantBound>(value, delta, deltaValue, new ConstantBound(clamp.Min, clamp.Max), clamp, start, count, applied);
                return;
            case ClampKind.Columns:
                if (clamp.MinColumn == null)
                {
                    WithDelta<TW, ColumnMaxBound>(value, delta, deltaValue, new ColumnMaxBound(clamp.MaxColumn), clamp, start, count, applied);
                }
                else if (clamp.MaxColumn == null)
                {
                    WithDelta<TW, ColumnMinBound>(value, delta, deltaValue, new ColumnMinBound(clamp.MinColumn), clamp, start, count, applied);
                }
                else
                {
                    WithDelta<TW, ColumnsBound>(value, delta, deltaValue, new ColumnsBound(clamp.MinColumn, clamp.MaxColumn), clamp, start, count, applied);
                }

                return;
            case ClampKind.StatBounds:
                if (clamp.MinStatColumn == null)
                {
                    WithDelta<TW, StatMaxBound>(value, delta, deltaValue, new StatMaxBound(clamp.MaxStatColumn), clamp, start, count, applied);
                }
                else if (clamp.MaxStatColumn == null)
                {
                    WithDelta<TW, StatMinBound>(value, delta, deltaValue, new StatMinBound(clamp.MinStatColumn), clamp, start, count, applied);
                }
                else
                {
                    WithDelta<TW, StatsBound>(value, delta, deltaValue, new StatsBound(clamp.MinStatColumn, clamp.MaxStatColumn), clamp, start, count, applied);
                }

                return;
            default:
                WithDelta<TW, NoBound>(value, delta, deltaValue, default, clamp, start, count, applied);
                return;
        }
    }

    static void WithDelta<TW, TB>(int* value, int* delta, int deltaValue, TB bound, in Clamp clamp, int start, int count, int* applied)
        where TW : struct
        where TB : struct, IBound
    {
        if (delta == null)
        {
            WithApplied<TW, TB, UniformDelta>(value, new UniformDelta(deltaValue), bound, clamp, start, count, applied);
        }
        else
        {
            WithApplied<TW, TB, ColumnDelta>(value, new ColumnDelta(delta), bound, clamp, start, count, applied);
        }
    }

    static void WithApplied<TW, TB, TD>(int* value, TD delta, TB bound, in Clamp clamp, int start, int count, int* applied)
        where TW : struct
        where TB : struct, IBound
        where TD : struct, IDelta
    {
        if (applied == null)
        {
            Loop<TW, TB, TD, NoApplied>(value, delta, bound, default, clamp, start, count);
        }
        else
        {
            Loop<TW, TB, TD, ColumnApplied>(value, delta, bound, new ColumnApplied(applied), clamp, start, count);
        }
    }

    static void Loop<TW, TB, TD, TA>(int* value, TD delta, TB bound, TA applied, in Clamp clamp, int start, int count)
        where TW : struct
        where TB : struct, IBound
        where TD : struct, IDelta
        where TA : struct, IApplied
    {
        var i = 0;
        if (typeof(TW) == typeof(Width256))
        {
            for (; i + 8 <= count; i += 8)
            {
                var slot = start + i;
                var before = Vector256.Load(value + slot);
                var after = bound.Apply(before + delta.Load256(slot), slot);
                Vector256.Store(after, value + slot);
                applied.Store(after - before, slot);
            }
        }
        else
        {
            for (; i + 4 <= count; i += 4)
            {
                var slot = start + i;
                var before = Vector128.Load(value + slot);
                var after = bound.Apply(before + delta.Load128(slot), slot);
                Vector128.Store(after, value + slot);
                applied.Store(after - before, slot);
            }
        }

        for (; i < count; i++)
        {
            var slot = start + i;
            var before = value[slot];
            var after = clamp.Apply(before + delta.Load(slot), slot);
            value[slot] = after;
            applied.Store(after - before, slot);
        }
    }
}
