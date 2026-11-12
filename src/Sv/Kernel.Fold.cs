using System;
using System.Runtime.CompilerServices;
#if NET
using System.Runtime.Intrinsics;
#endif

namespace Sv
{
    internal enum FoldWidth : byte
    {
        Auto = 0,
        Scalar = 1,
        Width128 = 2,
        Width256 = 3,
    }

    internal interface IFoldBound
    {
        int Floor(int slot);

        int Ceiling(int slot);
#if NET
        Vector128<int> Floor128(int slot);

        Vector128<int> Ceiling128(int slot);

        Vector256<int> Floor256(int slot);

        Vector256<int> Ceiling256(int slot);
#endif
    }

    internal readonly struct ConstantFold : IFoldBound
    {
        readonly int floor;

        readonly int ceiling;

        public ConstantFold(int floor, int ceiling)
        {
            this.floor = floor;
            this.ceiling = ceiling;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Floor(int slot) => floor;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Ceiling(int slot) => ceiling;
#if NET
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Floor128(int slot) => Vector128.Create(floor);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Ceiling128(int slot) => Vector128.Create(ceiling);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Floor256(int slot) => Vector256.Create(floor);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Ceiling256(int slot) => Vector256.Create(ceiling);
#endif
    }

    internal readonly unsafe struct RowsFold : IFoldBound
    {
        readonly int* floor;

        readonly int* ceiling;

        public RowsFold(int* floor, int* ceiling)
        {
            this.floor = floor;
            this.ceiling = ceiling;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Floor(int slot) => floor[slot];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Ceiling(int slot) => ceiling[slot];
#if NET
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Floor128(int slot) => Vector128.Load(floor + slot);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Ceiling128(int slot) => Vector128.Load(ceiling + slot);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Floor256(int slot) => Vector256.Load(floor + slot);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Ceiling256(int slot) => Vector256.Load(ceiling + slot);
#endif
    }

    internal readonly unsafe struct StatsFold : IFoldBound
    {
        readonly float* floor;

        readonly float* ceiling;

        public StatsFold(float* floor, float* ceiling)
        {
            this.floor = floor;
            this.ceiling = ceiling;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Floor(int slot) => Clamp.BoundOf(floor[slot]);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Ceiling(int slot) => Clamp.BoundOf(ceiling[slot]);
#if NET
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Floor128(int slot) => Simd.BoundOf128(Vector128.Load(floor + slot));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Ceiling128(int slot) => Simd.BoundOf128(Vector128.Load(ceiling + slot));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Floor256(int slot) => Simd.BoundOf256(Vector256.Load(floor + slot));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Ceiling256(int slot) => Simd.BoundOf256(Vector256.Load(ceiling + slot));
#endif
    }

    internal readonly unsafe struct StatCeilingFold : IFoldBound
    {
        readonly int floor;

        readonly float* ceiling;

        public StatCeilingFold(int floor, float* ceiling)
        {
            this.floor = floor;
            this.ceiling = ceiling;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Floor(int slot) => floor;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Ceiling(int slot) => Clamp.BoundOf(ceiling[slot]);
#if NET
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Floor128(int slot) => Vector128.Create(floor);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Ceiling128(int slot) => Simd.BoundOf128(Vector128.Load(ceiling + slot));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Floor256(int slot) => Vector256.Create(floor);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Ceiling256(int slot) => Simd.BoundOf256(Vector256.Load(ceiling + slot));
#endif
    }

    internal readonly unsafe struct AnyFold : IFoldBound
    {
        readonly int* floorRows;

        readonly int* ceilingRows;

        readonly float* floorStats;

        readonly float* ceilingStats;

        public AnyFold(int* floorRows, int* ceilingRows, float* floorStats, float* ceilingStats)
        {
            this.floorRows = floorRows;
            this.ceilingRows = ceilingRows;
            this.floorStats = floorStats;
            this.ceilingStats = ceilingStats;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Floor(int slot) => floorStats != null ? Clamp.BoundOf(floorStats[slot]) : floorRows != null ? floorRows[slot] : int.MinValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Ceiling(int slot) => ceilingStats != null ? Clamp.BoundOf(ceilingStats[slot]) : ceilingRows != null ? ceilingRows[slot] : int.MaxValue;
#if NET
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Floor128(int slot) => floorStats != null ? Simd.BoundOf128(Vector128.Load(floorStats + slot)) : floorRows != null ? Vector128.Load(floorRows + slot) : Vector128.Create(int.MinValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<int> Ceiling128(int slot) => ceilingStats != null ? Simd.BoundOf128(Vector128.Load(ceilingStats + slot)) : ceilingRows != null ? Vector128.Load(ceilingRows + slot) : Vector128.Create(int.MaxValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Floor256(int slot) => floorStats != null ? Simd.BoundOf256(Vector256.Load(floorStats + slot)) : floorRows != null ? Vector256.Load(floorRows + slot) : Vector256.Create(int.MinValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector256<int> Ceiling256(int slot) => ceilingStats != null ? Simd.BoundOf256(Vector256.Load(ceilingStats + slot)) : ceilingRows != null ? Vector256.Load(ceilingRows + slot) : Vector256.Create(int.MaxValue);
#endif
    }

    internal readonly unsafe struct FoldSpan
    {
        public readonly int* Value;
        public readonly int* Delta;
        public readonly long* Inbox;
        public readonly ulong* Words;
        public readonly ulong* Changed;
        public readonly int Start;
        public readonly int Count;
        public readonly int WordCount;

        public FoldSpan(int* value, int* delta, long* inbox, ulong* words, ulong* changed, int start, int count, int wordCount)
        {
            Value = value;
            Delta = delta;
            Inbox = inbox;
            Words = words;
            Changed = changed;
            Start = start;
            Count = count;
            WordCount = wordCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Rows(int w) => Words != null ? 64 : Math.Min(64, Count - (w << 6));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong Mask(int w, int rows) => Words != null ? Words[w] : rows == 64 ? ulong.MaxValue : (1ul << rows) - 1;
    }

    public static unsafe partial class Kernel
    {
        public static void Fold(Column<int> value, Window window, Column<int> delta, Bounds bounds, Column<ulong> changed = default)
        {
            FoldWith(FoldWidth.Auto, value, window, delta, bounds, changed, default);
        }

        public static void Fold(Column<int> value, Window window, Column<int> delta, Bounds bounds, Column<ulong> changed, Column<long> inbox)
        {
            FoldWith(FoldWidth.Auto, value, window, delta, bounds, changed, inbox);
        }

        internal static void FoldWith(FoldWidth width, Column<int> value, Window window, Column<int> delta, Bounds bounds, Column<ulong> changed, Column<long> inbox)
        {
            var masked = window.Words != null;
            var wordCount = masked ? window.WordCount : (window.Count + 63) >> 6;
            var reach = masked ? (long)window.Start + (long)window.WordCount * 64 : (long)window.Start + window.Count;
            if (masked)
            {
                Guard.EnabledRange(window.Start, window.WordCount, value.Length);
            }
            else
            {
                Guard.ChunkRange(window.Start, window.Count, value.Length);
            }

            if (delta.Length < reach)
            {
                throw new ArgumentException($"sv: delta column of {delta.Length} rows does not cover the window's {reach}");
            }

            if (inbox.Pointer != null && inbox.Length < reach)
            {
                throw new ArgumentException($"sv: inbox of {inbox.Length} cells does not cover the window's {reach}");
            }

            if (changed.Pointer != null && changed.Length < wordCount)
            {
                throw new ArgumentException($"sv: changed column of {changed.Length} words does not cover the window's {wordCount}");
            }

            if (wordCount == 0)
            {
                return;
            }

            var clamp = bounds.Clamp;
            clamp.CheckStatBounds();
            var span = new FoldSpan(value.Pointer, delta.Pointer, inbox.Pointer, window.Words, changed.Pointer, window.Start, window.Count, wordCount);
            var resolved = Resolve(width);
            switch (clamp.Kind)
            {
                case ClampKind.Constant:
                    FoldBound(resolved, span, new ConstantFold(clamp.Min, clamp.Max));
                    return;
                case ClampKind.Columns:
                    if (clamp.MinColumn != null && clamp.MaxColumn != null)
                    {
                        FoldBound(resolved, span, new RowsFold(clamp.MinColumn, clamp.MaxColumn));
                    }
                    else
                    {
                        FoldBound(resolved, span, new AnyFold(clamp.MinColumn, clamp.MaxColumn, null, null));
                    }

                    return;
                case ClampKind.StatBounds:
                    if (clamp.MinStatColumn != null && clamp.MaxStatColumn != null)
                    {
                        FoldBound(resolved, span, new StatsFold(clamp.MinStatColumn, clamp.MaxStatColumn));
                    }
                    else if (clamp.MaxStatColumn != null)
                    {
                        FoldBound(resolved, span, new StatCeilingFold(int.MinValue, clamp.MaxStatColumn));
                    }
                    else
                    {
                        FoldBound(resolved, span, new AnyFold(null, null, clamp.MinStatColumn, null));
                    }

                    return;
                default:
                    FoldBound(resolved, span, new ConstantFold(int.MinValue, int.MaxValue));
                    return;
            }
        }

        static FoldWidth Resolve(FoldWidth width)
        {
#if NET
            if (width == FoldWidth.Auto)
            {
                return Simd.Has256 ? FoldWidth.Width256 : Simd.Has128 ? FoldWidth.Width128 : FoldWidth.Scalar;
            }

            return width;
#else
            return FoldWidth.Scalar;
#endif
        }

        static void FoldBound<TB>(FoldWidth width, in FoldSpan span, TB bound)
            where TB : struct, IFoldBound
        {
#if NET
            if (width == FoldWidth.Width256)
            {
                Simd.FoldWindow256(span, bound);
                return;
            }

            if (width == FoldWidth.Width128)
            {
                Simd.FoldWindow128(span, bound);
                return;
            }
#endif
            var value = span.Value;
            var delta = span.Delta;
            var inbox = span.Inbox;
            var changed = span.Changed;
            for (var w = 0; w < span.WordCount; w++)
            {
                var rows = span.Rows(w);
                var bits = FoldScalar(value, delta, inbox, bound, span.Start + (w << 6), rows, span.Mask(w, rows));
                if (changed != null)
                {
                    changed[w] = bits;
                }
            }
        }

        internal static ulong FoldScalar<TB>(int* value, int* delta, long* inbox, TB bound, int start, int rows, ulong mask)
            where TB : struct, IFoldBound
        {
            var changed = 0ul;
            var live = rows >= 64 ? mask : mask & ((1ul << rows) - 1);
            while (live != 0)
            {
                var bit = CellDigest.Tzcnt(live);
                live &= live - 1;
                var slot = start + bit;
                var before = value[slot];
                var moved = inbox == null ? delta[slot] : Inbox.Take(inbox, slot, delta[slot]);
                var after = IntrinsicLaw.Step(before, moved, bound.Floor(slot), bound.Ceiling(slot));
                value[slot] = after;
                delta[slot] = 0;
                changed |= (after != before ? 1ul : 0ul) << bit;
            }

            return changed;
        }
    }
}
