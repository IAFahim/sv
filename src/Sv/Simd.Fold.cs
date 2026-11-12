#if NET
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Sv
{
    internal static unsafe partial class Simd
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> BoundOf128(Vector128<float> v) => BoundsOf(v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<int> BoundOf256(Vector256<float> v) => BoundsOf(v);

        internal static void FoldWindow256<TB>(in FoldSpan span, TB bound)
            where TB : struct, IFoldBound
        {
            var value = span.Value;
            var delta = span.Delta;
            var inbox = span.Inbox;
            var changedOut = span.Changed;
            for (var w = 0; w < span.WordCount; w++)
            {
                var start = span.Start + (w << 6);
                var rows = span.Rows(w);
                var mask = span.Mask(w, rows);
                var changed = 0ul;
                var group = 0;
                if (mask != 0)
                {
                    for (; group * 8 + 8 <= rows; group++)
                    {
                        var shift = group * 8;
                        var lanes = (mask >> shift) & 0xFFul;
                        if (lanes == 0)
                        {
                            continue;
                        }

                        var slot = start + shift;
                        if (lanes != 0xFFul)
                        {
                            changed |= Kernel.FoldScalar(value, delta, inbox, bound, slot, 8, lanes) << shift;
                            continue;
                        }

                        var before = Vector256.Load(value + slot);
                        var moved = Vector256.Load(delta + slot);
                        if (inbox != null)
                        {
                            moved = Take256(inbox + slot, moved);
                        }

                        var after = ClampBoth(SaturatingAdd(before, moved), bound.Floor256(slot), bound.Ceiling256(slot));
                        Vector256.Store(after, value + slot);
                        Vector256.Store(Vector256<int>.Zero, delta + slot);
                        changed |= (ulong)(~Vector256.Equals(after, before)).ExtractMostSignificantBits() << shift;
                    }

                    var tail = group * 8;
                    if (tail < rows)
                    {
                        changed |= Kernel.FoldScalar(value, delta, inbox, bound, start + tail, rows - tail, mask >> tail) << tail;
                    }
                }

                if (changedOut != null)
                {
                    changedOut[w] = changed;
                }
            }
        }

        internal static void FoldWindow128<TB>(in FoldSpan span, TB bound)
            where TB : struct, IFoldBound
        {
            var value = span.Value;
            var delta = span.Delta;
            var inbox = span.Inbox;
            var changedOut = span.Changed;
            for (var w = 0; w < span.WordCount; w++)
            {
                var start = span.Start + (w << 6);
                var rows = span.Rows(w);
                var mask = span.Mask(w, rows);
                var changed = 0ul;
                var group = 0;
                if (mask != 0)
                {
                    for (; group * 4 + 4 <= rows; group++)
                    {
                        var shift = group * 4;
                        var lanes = (mask >> shift) & 0xFul;
                        if (lanes == 0)
                        {
                            continue;
                        }

                        var slot = start + shift;
                        if (lanes != 0xFul)
                        {
                            changed |= Kernel.FoldScalar(value, delta, inbox, bound, slot, 4, lanes) << shift;
                            continue;
                        }

                        var before = Vector128.Load(value + slot);
                        var moved = Vector128.Load(delta + slot);
                        if (inbox != null)
                        {
                            moved = Take128(inbox + slot, moved);
                        }

                        var after = ClampBoth(SaturatingAdd(before, moved), bound.Floor128(slot), bound.Ceiling128(slot));
                        Vector128.Store(after, value + slot);
                        Vector128.Store(Vector128<int>.Zero, delta + slot);
                        changed |= (ulong)(~Vector128.Equals(after, before)).ExtractMostSignificantBits() << shift;
                    }

                    var tail = group * 4;
                    if (tail < rows)
                    {
                        changed |= Kernel.FoldScalar(value, delta, inbox, bound, start + tail, rows - tail, mask >> tail) << tail;
                    }
                }

                if (changedOut != null)
                {
                    changedOut[w] = changed;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector256<int> Take256(long* inbox, Vector256<int> local)
        {
            var (low, high) = Vector256.Widen(local);
            low = SaturateLanes(low + Vector256.Load(inbox));
            high = SaturateLanes(high + Vector256.Load(inbox + 4));
            Vector256.Store(Vector256<long>.Zero, inbox);
            Vector256.Store(Vector256<long>.Zero, inbox + 4);
            return Vector256.Narrow(low, high);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<int> Take128(long* inbox, Vector128<int> local)
        {
            var (low, high) = Vector128.Widen(local);
            low = SaturateLanes(low + Vector128.Load(inbox));
            high = SaturateLanes(high + Vector128.Load(inbox + 2));
            Vector128.Store(Vector128<long>.Zero, inbox);
            Vector128.Store(Vector128<long>.Zero, inbox + 2);
            return Vector128.Narrow(low, high);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector256<long> SaturateLanes(Vector256<long> v)
        {
            var max = Vector256.Create((long)int.MaxValue);
            var min = Vector256.Create((long)int.MinValue);
            var capped = Vector256.ConditionalSelect(Vector256.GreaterThan(v, max), max, v);
            return Vector256.ConditionalSelect(Vector256.LessThan(capped, min), min, capped);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<long> SaturateLanes(Vector128<long> v)
        {
            var max = Vector128.Create((long)int.MaxValue);
            var min = Vector128.Create((long)int.MinValue);
            var capped = Vector128.ConditionalSelect(Vector128.GreaterThan(v, max), max, v);
            return Vector128.ConditionalSelect(Vector128.LessThan(capped, min), min, capped);
        }
    }
}
#endif
