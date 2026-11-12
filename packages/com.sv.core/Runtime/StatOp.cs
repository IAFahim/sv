using System;
using System.Runtime.InteropServices;

namespace Sv
{
    public enum StatOpKind : byte
    {
        RemoveSource = 0,
        Add = 1,
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct StatOp
    {
        public readonly long ExpiresAt;
        public readonly int Slot;
        public readonly int Source;
        public readonly uint ValueBits;
        public readonly ushort Stat;
        public readonly ModifierKind Kind;
        public readonly StatOpKind Op;

        public StatOp(StatOpKind op, int slot, int source, ushort stat, ModifierKind kind, uint valueBits, long expiresAt)
        {
            Op = op;
            Slot = slot;
            Source = source;
            Stat = stat;
            Kind = kind;
            ValueBits = valueBits;
            ExpiresAt = expiresAt;
        }

        public static unsafe StatOp Add(int slot, StatId stat, ModifierKind kind, float value, int source, long expiresAt = long.MaxValue)
        {
            if ((uint)stat.Index > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(stat), $"sv: stat {stat.Index} does not fit a StatOp");
            }

            return new StatOp(StatOpKind.Add, slot, source, (ushort)stat.Index, kind, *(uint*)&value, expiresAt);
        }

        public static StatOp RemoveSource(int slot, int source) => new StatOp(StatOpKind.RemoveSource, slot, source, 0, ModifierKind.Added, 0u, 0L);
    }

    public static unsafe class StatOps
    {
        const int SmallRun = 24;

        public static void Canonicalize(Span<StatOp> ops, Span<StatOp> scratch)
        {
            var count = ops.Length;
            if (count < 2)
            {
                return;
            }

            if (scratch.Length < count)
            {
                throw new ArgumentException($"sv: scratch of {scratch.Length} ops cannot hold {count}", nameof(scratch));
            }

            fixed (StatOp* opsPtr = ops)
            fixed (StatOp* scratchPtr = scratch)
            {
                SortBySlot(opsPtr, scratchPtr, count);
                var at = 0;
                while (at < count)
                {
                    var slot = opsPtr[at].Slot;
                    var end = at + 1;
                    while (end < count && opsPtr[end].Slot == slot)
                    {
                        end++;
                    }

                    var length = end - at;
                    if (length <= SmallRun)
                    {
                        InsertionSort(opsPtr + at, length);
                    }
                    else
                    {
                        HeapSort(opsPtr + at, length);
                    }

                    at = end;
                }
            }
        }

        static void SortBySlot(StatOp* ops, StatOp* scratch, int count)
        {
            var counts = stackalloc int[256];
            var source = ops;
            var target = scratch;
            for (var pass = 0; pass < 4; pass++)
            {
                var shift = pass * 8;
                for (var b = 0; b < 256; b++)
                {
                    counts[b] = 0;
                }

                for (var i = 0; i < count; i++)
                {
                    counts[(int)((SlotKey(source[i].Slot) >> shift) & 255u)]++;
                }

                if (counts[(int)((SlotKey(source[0].Slot) >> shift) & 255u)] == count)
                {
                    continue;
                }

                var total = 0;
                for (var b = 0; b < 256; b++)
                {
                    var size = counts[b];
                    counts[b] = total;
                    total += size;
                }

                for (var i = 0; i < count; i++)
                {
                    var op = source[i];
                    target[counts[(int)((SlotKey(op.Slot) >> shift) & 255u)]++] = op;
                }

                var swap = source;
                source = target;
                target = swap;
            }

            if (source != ops)
            {
                Buffer.MemoryCopy(source, ops, (long)count * sizeof(StatOp), (long)count * sizeof(StatOp));
            }
        }

        static uint SlotKey(int slot) => (uint)slot ^ 0x80000000u;

        internal static int Compare(in StatOp a, in StatOp b)
        {
            if (a.Slot != b.Slot)
            {
                return a.Slot < b.Slot ? -1 : 1;
            }

            if (a.Op != b.Op)
            {
                return a.Op < b.Op ? -1 : 1;
            }

            if (a.Source != b.Source)
            {
                return a.Source < b.Source ? -1 : 1;
            }

            if (a.Stat != b.Stat)
            {
                return a.Stat < b.Stat ? -1 : 1;
            }

            if (a.Kind != b.Kind)
            {
                return a.Kind < b.Kind ? -1 : 1;
            }

            if (a.ValueBits != b.ValueBits)
            {
                return a.ValueBits < b.ValueBits ? -1 : 1;
            }

            if (a.ExpiresAt != b.ExpiresAt)
            {
                return a.ExpiresAt < b.ExpiresAt ? -1 : 1;
            }

            return 0;
        }

        static void InsertionSort(StatOp* ops, int count)
        {
            for (var i = 1; i < count; i++)
            {
                var item = ops[i];
                var j = i - 1;
                while (j >= 0 && Compare(ops[j], item) > 0)
                {
                    ops[j + 1] = ops[j];
                    j--;
                }

                ops[j + 1] = item;
            }
        }

        static void HeapSort(StatOp* ops, int count)
        {
            for (var start = count / 2 - 1; start >= 0; start--)
            {
                SiftDown(ops, start, count);
            }

            for (var end = count - 1; end > 0; end--)
            {
                var top = ops[0];
                ops[0] = ops[end];
                ops[end] = top;
                SiftDown(ops, 0, end);
            }
        }

        static void SiftDown(StatOp* ops, int root, int count)
        {
            while (true)
            {
                var child = root * 2 + 1;
                if (child >= count)
                {
                    return;
                }

                if (child + 1 < count && Compare(ops[child], ops[child + 1]) < 0)
                {
                    child++;
                }

                if (Compare(ops[root], ops[child]) >= 0)
                {
                    return;
                }

                var swap = ops[root];
                ops[root] = ops[child];
                ops[child] = swap;
                root = child;
            }
        }
    }
}
