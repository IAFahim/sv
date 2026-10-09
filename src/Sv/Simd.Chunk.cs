using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Sv;

internal static unsafe partial class Simd
{
    internal struct Width128
    {
    }

    internal struct Width256
    {
    }

    internal struct Masked256
    {
    }

    internal interface IBound
    {
        Vector128<int> Apply(Vector128<int> v, int slot);

        Vector256<int> Apply(Vector256<int> v, int slot);

        Vector256<int> ApplyMasked(Vector256<int> v, int slot, Vector256<int> mask);
    }

    internal interface IDelta
    {
        Vector128<int> Load128(int slot);

        Vector256<int> Load256(int slot);

        Vector256<int> LoadMasked256(int slot, Vector256<int> mask);

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> ApplyMasked(Vector256<int> v, int slot, Vector256<int> mask) => v;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> ApplyMasked(Vector256<int> v, int slot, Vector256<int> mask) => ClampBoth(v, Vector256.Create(min), Vector256.Create(max));
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> ApplyMasked(Vector256<int> v, int slot, Vector256<int> mask) => ClampBoth(v, Avx2.MaskLoad(min + slot, mask), Avx2.MaskLoad(max + slot, mask));
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> ApplyMasked(Vector256<int> v, int slot, Vector256<int> mask) => MinOnly(v, Avx2.MaskLoad(min + slot, mask));
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> ApplyMasked(Vector256<int> v, int slot, Vector256<int> mask) => MaxOnly(v, Avx2.MaskLoad(max + slot, mask));
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> ApplyMasked(Vector256<int> v, int slot, Vector256<int> mask) => ClampBoth(v, BoundsOf(Avx.MaskLoad(min + slot, mask.AsSingle())), BoundsOf(Avx.MaskLoad(max + slot, mask.AsSingle())));
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> ApplyMasked(Vector256<int> v, int slot, Vector256<int> mask) => MinOnly(v, BoundsOf(Avx.MaskLoad(min + slot, mask.AsSingle())));
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> ApplyMasked(Vector256<int> v, int slot, Vector256<int> mask) => MaxOnly(v, BoundsOf(Avx.MaskLoad(max + slot, mask.AsSingle())));
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
public Vector256<int> LoadMasked256(int slot, Vector256<int> mask) => Vector256.Create(value);

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
public Vector256<int> LoadMasked256(int slot, Vector256<int> mask) => Avx2.MaskLoad(delta + slot, mask);

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

    static void Dispatch<TW>(int* value, int* delta, int deltaValue, in Clamp clamp, int start, int count, int* applied, ulong word = 0)
        where TW : struct
    {
        switch (clamp.Kind)
        {
            case ClampKind.Constant:
                WithDelta<TW, ConstantBound>(value, delta, deltaValue, new ConstantBound(clamp.Min, clamp.Max), clamp, start, count, applied, word);
                return;
            case ClampKind.Columns:
                if (clamp.MinColumn == null)
                {
                    WithDelta<TW, ColumnMaxBound>(value, delta, deltaValue, new ColumnMaxBound(clamp.MaxColumn), clamp, start, count, applied, word);
                }
                else if (clamp.MaxColumn == null)
                {
                    WithDelta<TW, ColumnMinBound>(value, delta, deltaValue, new ColumnMinBound(clamp.MinColumn), clamp, start, count, applied, word);
                }
                else
                {
                    WithDelta<TW, ColumnsBound>(value, delta, deltaValue, new ColumnsBound(clamp.MinColumn, clamp.MaxColumn), clamp, start, count, applied, word);
                }

                return;
            case ClampKind.StatBounds:
                if (clamp.MinStatColumn == null)
                {
                    WithDelta<TW, StatMaxBound>(value, delta, deltaValue, new StatMaxBound(clamp.MaxStatColumn), clamp, start, count, applied, word);
                }
                else if (clamp.MaxStatColumn == null)
                {
                    WithDelta<TW, StatMinBound>(value, delta, deltaValue, new StatMinBound(clamp.MinStatColumn), clamp, start, count, applied, word);
                }
                else
                {
                    WithDelta<TW, StatsBound>(value, delta, deltaValue, new StatsBound(clamp.MinStatColumn, clamp.MaxStatColumn), clamp, start, count, applied, word);
                }

                return;
            default:
                WithDelta<TW, NoBound>(value, delta, deltaValue, default, clamp, start, count, applied, word);
                return;
        }
    }

    static void WithDelta<TW, TB>(int* value, int* delta, int deltaValue, TB bound, in Clamp clamp, int start, int count, int* applied, ulong word)
        where TW : struct
        where TB : struct, IBound
    {
        if (delta == null)
        {
            WithApplied<TW, TB, UniformDelta>(value, new UniformDelta(deltaValue), bound, clamp, start, count, applied, word);
        }
        else
        {
            WithApplied<TW, TB, ColumnDelta>(value, new ColumnDelta(delta), bound, clamp, start, count, applied, word);
        }
    }

    static void WithApplied<TW, TB, TD>(int* value, TD delta, TB bound, in Clamp clamp, int start, int count, int* applied, ulong word)
        where TW : struct
        where TB : struct, IBound
        where TD : struct, IDelta
    {
        if (applied == null)
        {
            Loop<TW, TB, TD, NoApplied>(value, delta, bound, default, clamp, start, count, word);
        }
        else
        {
            Loop<TW, TB, TD, ColumnApplied>(value, delta, bound, new ColumnApplied(applied), clamp, start, count, word);
        }
    }

    static void Loop<TW, TB, TD, TA>(int* value, TD delta, TB bound, TA applied, in Clamp clamp, int start, int count, ulong word)
        where TW : struct
        where TB : struct, IBound
        where TD : struct, IDelta
        where TA : struct, IApplied
    {
        if (typeof(TW) == typeof(Masked256))
        {
            MaskedWord(value, delta, bound, applied, start, word);
            return;
        }

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

    internal static void EnabledWord(int* value, int* delta, int deltaValue, in Clamp clamp, ulong word, int offset, int* applied)
    {
        clamp.CheckStatBounds();
        if (word == ulong.MaxValue && Vector256.IsHardwareAccelerated)
        {
            Dispatch<Width256>(value, delta, deltaValue, clamp, offset, 64, applied);
            return;
        }

        if (word == ulong.MaxValue && Vector128.IsHardwareAccelerated)
        {
            Dispatch<Width128>(value, delta, deltaValue, clamp, offset, 64, applied);
            return;
        }

        if (Avx2.IsSupported)
        {
            Dispatch<Masked256>(value, delta, deltaValue, clamp, offset, 64, applied, word);
            return;
        }

        EnabledWordScalar(value, delta, deltaValue, clamp, word, offset, applied);
    }

    internal static void EnabledWordScalar(int* value, int* delta, int deltaValue, in Clamp clamp, ulong word, int offset, int* applied)
    {
        var mask = word;
        while (mask != 0)
        {
            var bit = System.Numerics.BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;
            var slot = offset + bit;
            var before = value[slot];
            var after = clamp.Apply(before + (delta == null ? deltaValue : delta[slot]), slot);
            value[slot] = after;
            if (applied != null)
            {
                applied[slot] = after - before;
            }
        }

        if (applied == null)
        {
            return;
        }

        var skipped = ~word;
        while (skipped != 0)
        {
            var bit = System.Numerics.BitOperations.TrailingZeroCount(skipped);
            skipped &= skipped - 1;
            applied[offset + bit] = 0;
        }
    }

    static void MaskedWord<TB, TD, TA>(int* value, TD delta, TB bound, TA applied, int offset, ulong word)
        where TB : struct, IBound
        where TD : struct, IDelta
        where TA : struct, IApplied
    {
        var laneBits = Vector256.Create(1, 2, 4, 8, 16, 32, 64, 128);
        for (var group = 0; group < 8; group++)
        {
            var slot = offset + group * 8;
            var lanes = (int)(word >> (group * 8)) & 0xFF;
            if (lanes == 0)
            {
                applied.Store(Vector256<int>.Zero, slot);
                continue;
            }

            if (lanes == 0xFF)
            {
                var full = Vector256.Load(value + slot);
                var fullAfter = bound.Apply(full + delta.Load256(slot), slot);
                Vector256.Store(fullAfter, value + slot);
                applied.Store(fullAfter - full, slot);
                continue;
            }

            var mask = ~Vector256.Equals(Vector256.Create(lanes) & laneBits, Vector256<int>.Zero);
            var before = Avx2.MaskLoad(value + slot, mask);
            var after = bound.ApplyMasked(before + delta.LoadMasked256(slot, mask), slot, mask);
            Avx2.MaskStore(value + slot, mask, after);
            applied.Store((after - before) & mask, slot);
        }
    }
}
