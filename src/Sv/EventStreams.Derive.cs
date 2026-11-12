using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sv
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DenseEvent
    {
        public long Tick;

        public int Count;

        public ushort Channel;

        public DenseEvent(ushort channel, long tick, int count)
        {
            Channel = channel;
            Tick = tick;
            Count = count;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct EdgeUnit
    {
        public int* Slots;

        public ulong Edges;

        public ulong State;

        public EdgeUnit(int* slots, ulong edges, ulong state)
        {
            Slots = slots;
            Edges = edges;
            State = state;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct EdgeRecord
    {
        public int Slot;

        public int Value;
    }

    public sealed unsafe class EventDerive : IDisposable
    {
        public const int DefaultCapacity = 16384;

        readonly EdgeRecord* records;

        readonly EdgeRecord* scratch;

        readonly int* counts;

        long edges;

        bool disposed;

        public EventDerive(ushort channel, int capacity = DefaultCapacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "sv: event capacity must be >= 1");
            }

            Channel = channel;
            Capacity = capacity;
            records = (EdgeRecord*)Memory.Alloc((nuint)capacity, (nuint)sizeof(EdgeRecord));
            scratch = (EdgeRecord*)Memory.Alloc((nuint)capacity, (nuint)sizeof(EdgeRecord));
            counts = (int*)Memory.Alloc(256, sizeof(int));
        }

        public ushort Channel { get; }

        public int Capacity { get; }

        public long Edges => Volatile.Read(ref edges);

        public bool IsDense => Edges > Capacity;

        public int Delivered { get; private set; }

        public bool LastDense { get; private set; }

        public void Begin()
        {
            Volatile.Write(ref edges, 0);
        }

        public void AddCount(long count)
        {
            Interlocked.Add(ref edges, count);
        }

        public static long CountEdges(ulong* edgeWords, int words)
        {
            long total = 0;
            for (var w = 0; w < words; w++)
            {
                total += Bits.PopCount(edgeWords[w]);
            }

            return total;
        }

        public static long CountEdges(ReadOnlySpan<EdgeUnit> units)
        {
            long total = 0;
            for (var u = 0; u < units.Length; u++)
            {
                total += Bits.PopCount(units[u].Edges);
            }

            return total;
        }

        public int Derive<T, TMaker>(EventStreams streams, Channel<T> channel, Channel<DenseEvent> dense, long tick, ulong* edgeWords, ulong* stateWords, int words, ref TMaker maker)
            where T : unmanaged
            where TMaker : struct, IEventMaker<T>
        {
            Check(streams, channel, dense);
            if (Dense(streams, dense, tick))
            {
                return 0;
            }

            var batch = stackalloc T[64];
            var delivered = 0;
            for (var w = 0; w < words; w++)
            {
                var mask = edgeWords[w];
                if (mask == 0)
                {
                    continue;
                }

                var state = stateWords == null ? ulong.MaxValue : stateWords[w];
                var count = 0;
                var rowBase = w << 6;
                while (mask != 0)
                {
                    var bit = Bits.Tzcnt(mask);
                    mask &= mask - 1;
                    batch[count++] = maker.Make(rowBase + bit, (int)((state >> bit) & 1ul));
                }

                streams.AppendBatch(channel.Id, channel.Writer, batch, count);
                delivered += count;
            }

            return Finish(delivered);
        }

        public int Derive<T, TMaker>(EventStreams streams, Channel<T> channel, Channel<DenseEvent> dense, long tick, ReadOnlySpan<EdgeUnit> units, ref TMaker maker)
            where T : unmanaged
            where TMaker : struct, IEventMaker<T>
        {
            Check(streams, channel, dense);
            if (Dense(streams, dense, tick))
            {
                return 0;
            }

            var count = 0;
            var ordered = true;
            var last = -1;
            for (var u = 0; u < units.Length; u++)
            {
                var unit = units[u];
                var mask = unit.Edges;
                while (mask != 0)
                {
                    var bit = Bits.Tzcnt(mask);
                    mask &= mask - 1;
                    if (count == Capacity)
                    {
                        throw new InvalidOperationException("sv: edge count passed to the derive is below the edges present");
                    }

                    var slot = unit.Slots[bit];
                    ordered &= slot > last;
                    last = slot;
                    records[count].Slot = slot;
                    records[count].Value = (int)((unit.State >> bit) & 1ul);
                    count++;
                }
            }

            var sorted = ordered ? records : SortBySlot(count);
            var batch = stackalloc T[64];
            var at = 0;
            while (at < count)
            {
                var take = count - at < 64 ? count - at : 64;
                for (var i = 0; i < take; i++)
                {
                    var record = sorted[at + i];
                    batch[i] = maker.Make(record.Slot, record.Value);
                }

                streams.AppendBatch(channel.Id, channel.Writer, batch, take);
                at += take;
            }

            return Finish(count);
        }

        EdgeRecord* SortBySlot(int count)
        {
            var source = records;
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
                    counts[(int)(((uint)source[i].Slot >> shift) & 255u)]++;
                }

                if (counts[(int)(((uint)source[0].Slot >> shift) & 255u)] == count)
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
                    var record = source[i];
                    target[counts[(int)(((uint)record.Slot >> shift) & 255u)]++] = record;
                }

                var swap = source;
                source = target;
                target = swap;
            }

            return source;
        }

        bool Dense(EventStreams streams, Channel<DenseEvent> dense, long tick)
        {
            var total = Edges;
            if (total <= Capacity)
            {
                LastDense = false;
                return false;
            }

            var record = new DenseEvent(Channel, tick, total > int.MaxValue ? int.MaxValue : (int)total);
            streams.AppendBatch(dense.Id, dense.Writer, &record, 1);
            LastDense = true;
            Delivered = 0;
            return true;
        }

        int Finish(int delivered)
        {
            Delivered = delivered;
            return delivered;
        }

        void Check<T>(EventStreams streams, Channel<T> channel, Channel<DenseEvent> dense) where T : unmanaged
        {
            Guard.NotDisposed(disposed, this);
            if (streams == null)
            {
                throw new ArgumentNullException(nameof(streams));
            }

            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            if (dense == null)
            {
                throw new ArgumentNullException(nameof(dense));
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Memory.Free(records);
            Memory.Free(scratch);
            Memory.Free(counts);
        }
    }
}
