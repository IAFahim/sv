using System;
using System.Diagnostics;

namespace Sv
{
    internal static unsafe class Guard
    {
        public static void NotDisposed(bool disposed, object instance)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(instance.GetType().FullName);
            }
        }

        public static void SlotLive(int slot, ulong* live, int capacity)
        {
            if (slot < 0 || slot >= capacity || (live[slot >> 6] & (1ul << (slot & 63))) == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slot), $"sv: slot {slot} is not live");
            }
        }

        public static void Button(int button, int words)
        {
            if (button < 0 || button >= words * 64)
            {
                throw new ArgumentOutOfRangeException(nameof(button), $"sv: button {button} out of range (0..{words * 64 - 1})");
            }
        }

        public static void ButtonWord(int word, int words)
        {
            if (word < 0 || word >= words)
            {
                throw new ArgumentOutOfRangeException(nameof(word), $"sv: button word {word} out of range (0..{words - 1})");
            }
        }

        public static void Axis(int axis, int axes)
        {
            if (axis < 0 || axis >= axes)
            {
                throw new ArgumentOutOfRangeException(nameof(axis), $"sv: axis {axis} out of range (0..{axes - 1})");
            }
        }

        public static void ChunkRange(int start, int count, int capacity)
        {
            if (start < 0 || count < 0 || (long)start + count > capacity)
            {
                throw new ArgumentOutOfRangeException($"sv: chunk [{start}, {(long)start + count}) exceeds capacity {capacity}");
            }
        }

        public static void SpanReach(int length, int reach, string name)
        {
            if (length < reach)
            {
                throw new ArgumentException($"sv: span of {length} rows does not reach row {reach - 1}", name);
            }
        }

        public static void SpanWindow(int length, long window, string name)
        {
            if (length != 0 && length < window)
            {
                throw new ArgumentException($"sv: applied span of {length} rows does not cover the window end {window}", name);
            }
        }

        public static void EnabledRange(int offset, int capacity)
        {
            if (offset < 0 || (long)offset + 64 > capacity)
            {
                throw new ArgumentOutOfRangeException($"sv: enabled window [{offset}, {(long)offset + 64}) exceeds capacity {capacity}");
            }
        }

        public static void EnabledRange(int offset, int wordCount, int capacity)
        {
            if (offset < 0 || wordCount < 1 || (long)offset + (long)wordCount * 64 > capacity)
            {
                throw new ArgumentOutOfRangeException($"sv: enabled window [{offset}, {(long)offset + (long)wordCount * 64}) exceeds capacity {capacity}");
            }
        }

        public static void EnabledTail(ulong word, int validBits)
        {
            if (validBits < 1 || validBits > 64)
            {
                throw new ArgumentOutOfRangeException($"sv: enabled tail spans {validBits} valid rows (1..64)");
            }

            if (validBits < 64 && (word & ~((1ul << validBits) - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException($"sv: enabled word carries bits past its {validBits} valid rows");
            }
        }

        [Conditional("SV_CHECKED")]
        public static void BindingFresh(int generation)
        {
            if (generation < 0)
            {
                throw new InvalidOperationException("sv: stale binding — storage moved; rebind");
            }
        }

        [Conditional("SV_CHECKED")]
        public static void BoundColumnFit(int columnLength, int stride, int slot)
        {
            if (columnLength >= 0 && (long)slot * stride >= columnLength)
            {
                throw new InvalidOperationException($"sv: bound column length {columnLength} exceeded at slot {slot} (stride {stride}); rebind with the full column or pass columnLength: -1");
            }
        }
    }
}
