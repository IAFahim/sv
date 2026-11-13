using System;
using System.Runtime.InteropServices;

namespace Sv
{
    public enum StatOpKind : byte
    {
        RemoveSource = 0,
        Add = 1,
    }

    [StructLayout(LayoutKind.Sequential, Size = 24)]
    public readonly struct StatOp
    {
        public const ushort NoStat = ushort.MaxValue;

        public readonly long ExpiresAt;
        public readonly int Slot;
        public readonly int Source;
        public readonly int Value;
        public readonly ushort Stat;
        public readonly ShapeKind Kind;
        readonly byte packedOp;

        public StatOpKind Op
        {
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
            get => (StatOpKind)(packedOp & 0x0F);
        }

        public StatLayer Layer
        {
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
            get => (StatLayer)(packedOp >> 4);
        }

        public StatOp(StatOpKind op, int slot, int source, int stat, ShapeKind kind, int value, long expiresAt)
            : this(op, slot, source, stat, kind, value, expiresAt, StatLayer.Core)
        {
        }

        public StatOp(StatOpKind op, int slot, int source, int stat, ShapeKind kind, int value, long expiresAt, StatLayer layer)
        {
            var rawOp = (byte)op > 15 ? 15 : (byte)op;
            var rawLayer = (byte)layer > 15 ? 15 : (byte)layer;
            packedOp = (byte)(rawOp | (rawLayer << 4));
            Slot = slot;
            Source = source;
            Stat = stat < 0 || stat >= NoStat ? NoStat : (ushort)stat;
            Kind = kind;
            Value = value;
            ExpiresAt = expiresAt;
        }

        public static StatOp Add(int slot, int stat, ShapeKind kind, int value, int source, long expiresAt = long.MaxValue, StatLayer layer = StatLayer.Core) =>
            new StatOp(StatOpKind.Add, slot, source, stat, kind, value, expiresAt, layer);

        public static StatOp RemoveSource(int slot, int source) =>
            new StatOp(StatOpKind.RemoveSource, slot, source, NoStat, ShapeKind.Added, 0, 0L, StatLayer.Core);
    }

    public readonly struct ModifierHandle
    {
        public readonly long Id;

        public ModifierHandle(long id)
        {
            Id = id;
        }
    }

    public static unsafe class StatOps
    {
        const int SmallRun = 24;

        public static int Compare(in StatOp first, in StatOp second)
        {
            if (first.Slot != second.Slot)
            {
                return first.Slot < second.Slot ? -1 : 1;
            }

            if (first.Op != second.Op)
            {
                return first.Op < second.Op ? -1 : 1;
            }

            if (first.Source != second.Source)
            {
                return first.Source < second.Source ? -1 : 1;
            }

            if (first.Stat != second.Stat)
            {
                return first.Stat < second.Stat ? -1 : 1;
            }

            if (first.Layer != second.Layer)
            {
                return first.Layer < second.Layer ? -1 : 1;
            }

            if (first.Kind != second.Kind)
            {
                return first.Kind < second.Kind ? -1 : 1;
            }

            if (first.Value != second.Value)
            {
                return first.Value < second.Value ? -1 : 1;
            }

            if (first.ExpiresAt != second.ExpiresAt)
            {
                return first.ExpiresAt < second.ExpiresAt ? -1 : 1;
            }

            return 0;
        }

        public static void Canonicalize(Span<StatOp> ops, Span<StatOp> scratch)
        {
            if (scratch.Length < ops.Length)
            {
                throw new ArgumentException("sv: scratch is shorter than the ops", nameof(scratch));
            }

            fixed (StatOp* opsAt = ops)
            fixed (StatOp* scratchAt = scratch)
            {
                Canonicalize(opsAt, ops.Length, scratchAt);
            }
        }

        public static void Canonicalize(StatOp* ops, int count, StatOp* scratch)
        {
            if (count < 2)
            {
                return;
            }

            SortBySlot(ops, scratch, count);
            var at = 0;
            while (at < count)
            {
                var slot = ops[at].Slot;
                var end = at + 1;
                while (end < count && ops[end].Slot == slot)
                {
                    end++;
                }

                if (end - at <= SmallRun)
                {
                    InsertionSort(ops + at, end - at);
                }
                else
                {
                    HeapSort(ops + at, end - at);
                }

                at = end;
            }
        }

        static uint SlotKey(int slot) => (uint)slot ^ 0x80000000u;

        static void SortBySlot(StatOp* ops, StatOp* scratch, int count)
        {
            var counts = stackalloc int[256];
            var from = ops;
            var into = scratch;
            for (var pass = 0; pass < 4; pass++)
            {
                var shift = pass * 8;
                for (var bucket = 0; bucket < 256; bucket++)
                {
                    counts[bucket] = 0;
                }

                for (var at = 0; at < count; at++)
                {
                    counts[(int)((SlotKey(from[at].Slot) >> shift) & 255u)]++;
                }

                if (counts[(int)((SlotKey(from[0].Slot) >> shift) & 255u)] == count)
                {
                    continue;
                }

                var total = 0;
                for (var bucket = 0; bucket < 256; bucket++)
                {
                    var size = counts[bucket];
                    counts[bucket] = total;
                    total += size;
                }

                for (var at = 0; at < count; at++)
                {
                    var op = from[at];
                    into[counts[(int)((SlotKey(op.Slot) >> shift) & 255u)]++] = op;
                }

                var swap = from;
                from = into;
                into = swap;
            }

            if (from != ops)
            {
                Memory.Copy(from, ops, (nuint)count * (nuint)sizeof(StatOp));
            }
        }

        static void InsertionSort(StatOp* ops, int count)
        {
            for (var at = 1; at < count; at++)
            {
                var op = ops[at];
                var into = at - 1;
                while (into >= 0 && Compare(ops[into], op) > 0)
                {
                    ops[into + 1] = ops[into];
                    into--;
                }

                ops[into + 1] = op;
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
