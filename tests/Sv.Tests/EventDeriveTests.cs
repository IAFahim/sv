using System.Runtime.InteropServices;
using Sv;
using Xunit;

public unsafe class EventDeriveTests
{
    const int Slots = 1 << 20;

    const int Words = Slots >> 6;

    public struct EdgeEvent
    {
        public int Slot;

        public int Entered;
    }

    struct EdgeMaker : IEventMaker<EdgeEvent>
    {
        public EdgeEvent Make(int row, int value) => new EdgeEvent { Slot = row, Entered = value };
    }

    sealed class Rig : IDisposable
    {
        public readonly EventStreams Streams = EventStreams.Create(1, 1 << 16);
        public readonly Channel<EdgeEvent> Events;
        public readonly Channel<DenseEvent> Dense;
        public readonly EventDerive Derive;
        public readonly ulong* Edges = (ulong*)NativeMemory.AllocZeroed(Words, sizeof(ulong));
        public readonly ulong* State = (ulong*)NativeMemory.AllocZeroed(Words, sizeof(ulong));

        public Rig(int capacity = EventDerive.DefaultCapacity)
        {
            Events = Streams.CreateChannel<EdgeEvent>();
            Dense = Streams.CreateChannel<DenseEvent>();
            Derive = new EventDerive(Events.Id, capacity);
        }

        public int RunWords(long tick)
        {
            var maker = new EdgeMaker();
            Derive.Begin();
            Derive.AddCount(EventDerive.CountEdges(Edges, Words));
            return Derive.Derive(Streams, Events, Dense, tick, Edges, State, Words, ref maker);
        }

        public (List<EdgeEvent> Events, List<DenseEvent> Dense, byte[] Bytes) Read()
        {
            Streams.Swap();
            var events = new List<EdgeEvent>();
            var dense = new List<DenseEvent>();
            var bytes = new List<byte>();
            while (Streams.TryReadNext(out var type, out var payload, out var size))
            {
                bytes.AddRange(BitConverter.GetBytes(type));
                bytes.AddRange(new ReadOnlySpan<byte>(payload, size).ToArray());
                if (type == Events.Id)
                {
                    events.Add(*(EdgeEvent*)payload);
                }
                else if (type == Dense.Id)
                {
                    dense.Add(*(DenseEvent*)payload);
                }
            }

            return (events, dense, bytes.ToArray());
        }

        public void Dispose()
        {
            Derive.Dispose();
            Streams.Dispose();
            NativeMemory.Free(Edges);
            NativeMemory.Free(State);
        }
    }

    static int[] DistinctSlots(int count, int seed)
    {
        var random = new Random(seed);
        var all = Enumerable.Range(0, Slots).ToArray();
        for (var i = 0; i < count; i++)
        {
            var j = random.Next(i, Slots);
            (all[i], all[j]) = (all[j], all[i]);
        }

        return all.Take(count).ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1000)]
    [InlineData(65536)]
    [InlineData(65537)]
    public void Cap_EveryEventSortedBySlot_OrExactlyOneDenseRecord(int edges)
    {
        using var rig = new Rig();
        var slots = DistinctSlots(edges, edges + 7);
        var random = new Random(edges);
        foreach (var slot in slots)
        {
            rig.Edges[slot >> 6] |= 1ul << (slot & 63);
            if (random.Next(2) == 0)
            {
                rig.State[slot >> 6] |= 1ul << (slot & 63);
            }
        }

        var delivered = rig.RunWords(42);
        var read = rig.Read();
        if (edges <= EventDerive.DefaultCapacity)
        {
            Assert.False(rig.Derive.LastDense);
            Assert.Equal(edges, delivered);
            Assert.Empty(read.Dense);
            var expected = slots.OrderBy(s => s).ToArray();
            Assert.Equal(expected, read.Events.Select(e => e.Slot).ToArray());
            foreach (var e in read.Events)
            {
                Assert.Equal((int)((rig.State[e.Slot >> 6] >> (e.Slot & 63)) & 1ul), e.Entered);
            }
        }
        else
        {
            Assert.True(rig.Derive.LastDense);
            Assert.Equal(0, delivered);
            Assert.Empty(read.Events);
            var dense = Assert.Single(read.Dense);
            Assert.Equal(rig.Events.Id, dense.Channel);
            Assert.Equal(42, dense.Tick);
            Assert.Equal(edges, dense.Count);
        }

        Assert.Equal(edges, EventDerive.CountEdges(rig.Edges, Words));
    }

    [Fact]
    public void DeathWave_MillionEdges_TakesTheDenseBranch_AndAllocatesNothing()
    {
        using var rig = new Rig();
        for (var w = 0; w < Words; w++)
        {
            rig.Edges[w] = ulong.MaxValue;
        }

        rig.RunWords(1);
        rig.Read();
        rig.RunWords(2);
        rig.Read();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var tick = 3; tick < 40; tick++)
        {
            rig.RunWords(tick);
            rig.Streams.Swap();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        rig.RunWords(77);
        var read = rig.Read();
        Assert.Empty(read.Events);
        var dense = Assert.Single(read.Dense);
        Assert.Equal(Slots, dense.Count);
        Assert.Equal(77, dense.Tick);
        Assert.Equal(ulong.MaxValue, rig.Edges[Words - 1]);
    }

    [Fact]
    public void BelowCap_WarmDerive_AllocatesNothing()
    {
        using var rig = new Rig();
        foreach (var slot in DistinctSlots(1000, 3))
        {
            rig.Edges[slot >> 6] |= 1ul << (slot & 63);
        }

        var units = BuildUnits(rig, 9, out var slotsMemory);
        try
        {
            var maker = new EdgeMaker();
            for (var warm = 0; warm < 3; warm++)
            {
                rig.RunWords(warm);
                rig.Derive.Derive(rig.Streams, rig.Events, rig.Dense, warm, units, ref maker);
                rig.Streams.Swap();
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var tick = 3; tick < 30; tick++)
            {
                rig.RunWords(tick);
                rig.Derive.Begin();
                rig.Derive.AddCount(EventDerive.CountEdges(units));
                rig.Derive.Derive(rig.Streams, rig.Events, rig.Dense, tick, units, ref maker);
                rig.Streams.Swap();
            }

            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        finally
        {
            NativeMemory.Free(slotsMemory);
        }
    }

    static EdgeUnit[] BuildUnits(Rig rig, int seed, out int* slotsMemory)
    {
        var random = new Random(seed);
        var permutation = Enumerable.Range(0, Slots).ToArray();
        for (var i = Slots - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (permutation[i], permutation[j]) = (permutation[j], permutation[i]);
        }

        slotsMemory = (int*)NativeMemory.Alloc(Slots, sizeof(int));
        var units = new List<EdgeUnit>();
        var at = 0;
        while (at < Slots)
        {
            var rows = Math.Min(Slots - at, 1 + random.Next(64));
            var slots = slotsMemory + at;
            ulong edges = 0;
            ulong state = 0;
            for (var r = 0; r < rows; r++)
            {
                var slot = permutation[at + r];
                slots[r] = slot;
                if ((rig.Edges[slot >> 6] & (1ul << (slot & 63))) != 0)
                {
                    edges |= 1ul << r;
                }

                if ((rig.State[slot >> 6] & (1ul << (slot & 63))) != 0)
                {
                    state |= 1ul << r;
                }
            }

            units.Add(new EdgeUnit(slots, edges, state));
            at += rows;
        }

        return units.ToArray();
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(65536)]
    public void ChunkUnits_InAnyOrder_GiveTheWordStreamByteForByte(int edges)
    {
        using var rig = new Rig();
        var random = new Random(edges ^ 0x5EED);
        foreach (var slot in DistinctSlots(edges, 11))
        {
            rig.Edges[slot >> 6] |= 1ul << (slot & 63);
            if (random.Next(3) == 0)
            {
                rig.State[slot >> 6] |= 1ul << (slot & 63);
            }
        }

        rig.RunWords(5);
        var reference = rig.Read().Bytes;
        var units = BuildUnits(rig, 21, out var slotsMemory);
        try
        {
            var maker = new EdgeMaker();
            for (var shuffle = 0; shuffle < 12; shuffle++)
            {
                var order = units.OrderBy(_ => random.Next()).ToArray();
                rig.Derive.Begin();
                Parallel.For(0, order.Length / 512 + 1, part =>
                {
                    var from = part * 512;
                    var to = Math.Min(order.Length, from + 512);
                    if (from < to)
                    {
                        rig.Derive.AddCount(EventDerive.CountEdges(order.AsSpan(from, to - from)));
                    }
                });
                Assert.Equal(edges, rig.Derive.Edges);
                Assert.Equal(edges, rig.Derive.Derive(rig.Streams, rig.Events, rig.Dense, 5, order, ref maker));
                Assert.Equal(reference, rig.Read().Bytes);
            }
        }
        finally
        {
            NativeMemory.Free(slotsMemory);
        }
    }

    [Fact]
    public void ChunkUnits_AboveCap_DeliverOneDenseRecord_AndNoRows()
    {
        using var rig = new Rig(16);
        foreach (var slot in DistinctSlots(17, 4))
        {
            rig.Edges[slot >> 6] |= 1ul << (slot & 63);
        }

        var units = BuildUnits(rig, 8, out var slotsMemory);
        try
        {
            var maker = new EdgeMaker();
            rig.Derive.Begin();
            rig.Derive.AddCount(EventDerive.CountEdges(units));
            Assert.Equal(0, rig.Derive.Derive(rig.Streams, rig.Events, rig.Dense, 9, units, ref maker));
            var read = rig.Read();
            Assert.Empty(read.Events);
            Assert.Equal(17, Assert.Single(read.Dense).Count);
        }
        finally
        {
            NativeMemory.Free(slotsMemory);
        }
    }

    [Fact]
    public void Guards()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventDerive(0, 0));
        Assert.Equal(16, Marshal.SizeOf<DenseEvent>());
        var derive = new EventDerive(1);
        derive.Dispose();
        derive.Dispose();
    }
}
