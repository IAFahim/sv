using System.Runtime.InteropServices;
using Sv;
using Xunit;

[Collection(HeavyAllocationCollection.Name)]
public unsafe class EventCapConfigTests
{
    const int Slots = 1 << 18;

    const int Words = Slots >> 6;

    struct Hit
    {
        public int Slot;

        public int Entered;
    }

    struct HitMaker : IEventMaker<Hit>
    {
        public Hit Make(int row, int value) => new Hit { Slot = row, Entered = value };
    }

    static void Mark(ulong* edges, int count, int stride)
    {
        Memory.Clear(edges, Words * sizeof(ulong));
        for (var i = 0; i < count; i++)
        {
            var slot = (int)((long)i * stride % Slots);
            edges[slot >> 6] |= 1ul << (slot & 63);
        }
    }

    static (int Events, List<DenseEvent> Dense, bool Sorted) Drain(EventStreams streams, ushort events, ushort dense)
    {
        streams.Swap();
        var count = 0;
        var last = -1;
        var sorted = true;
        var denseRecords = new List<DenseEvent>();
        while (streams.TryReadNext(out var type, out var payload, out _))
        {
            if (type == events)
            {
                var slot = ((Hit*)payload)->Slot;
                sorted &= slot > last;
                last = slot;
                count++;
            }
            else if (type == dense)
            {
                denseRecords.Add(*(DenseEvent*)payload);
            }
        }

        return (count, denseRecords, sorted);
    }

    [Fact]
    public void PerChannelCaps_SetAtHostBuild_DecideIndependently_OnTheSameEdges()
    {
        using var streams = EventStreams.Create(1, 1 << 16);
        var big = streams.CreateChannel<Hit>();
        var small = streams.CreateChannel<Hit>();
        var dense = streams.CreateChannel<DenseEvent>();
        using var askedBig = new EventDerive(big.Id, 65_536);
        using var byDefault = new EventDerive(small.Id);
        Assert.Equal(65_536, askedBig.Capacity);
        Assert.Equal(EventDerive.DefaultCapacity, byDefault.Capacity);
        Assert.Equal(16_384, byDefault.Capacity);
        var edges = (ulong*)NativeMemory.AllocZeroed(Words, sizeof(ulong));
        try
        {
            foreach (var count in new[] { 16_384, 16_385, 65_536, 65_537, 0 })
            {
                Mark(edges, count, 3);
                var total = EventDerive.CountEdges(edges, Words);
                Assert.Equal(count, total);
                var maker = new HitMaker();
                askedBig.Begin();
                askedBig.AddCount(total);
                byDefault.Begin();
                byDefault.AddCount(total);
                var bigDelivered = askedBig.Derive(streams, big, dense, count, edges, null, Words, ref maker);
                var smallDelivered = byDefault.Derive(streams, small, dense, count, edges, null, Words, ref maker);
                var bigRead = Drain(streams, big.Id, dense.Id);
                Assert.Equal(count <= 65_536 ? count : 0, bigDelivered);
                Assert.Equal(count <= 16_384 ? count : 0, smallDelivered);
                Assert.Equal(count > 65_536, askedBig.LastDense);
                Assert.Equal(count > 16_384, byDefault.LastDense);
                var expectedDense = (count > 65_536 ? 1 : 0) + (count > 16_384 ? 1 : 0);
                Assert.Equal(expectedDense, bigRead.Dense.Count);
                foreach (var record in bigRead.Dense)
                {
                    Assert.Equal(count, record.Count);
                    Assert.Equal(count, record.Tick);
                }

                Assert.True(bigRead.Sorted);
            }
        }
        finally
        {
            NativeMemory.Free(edges);
        }
    }

    [Fact]
    public void AskedCap65536_DeliversWarmWithoutAllocating_AndBadCapsThrow()
    {
        using var streams = EventStreams.Create(1, 1 << 20);
        var hits = streams.CreateChannel<Hit>();
        var dense = streams.CreateChannel<DenseEvent>();
        using var derive = new EventDerive(hits.Id, 65_536);
        var edges = (ulong*)NativeMemory.AllocZeroed(Words, sizeof(ulong));
        try
        {
            Mark(edges, 65_536, 4);
            var maker = new HitMaker();
            for (var tick = 0; tick < 3; tick++)
            {
                derive.Begin();
                derive.AddCount(EventDerive.CountEdges(edges, Words));
                Assert.Equal(65_536, derive.Derive(streams, hits, dense, tick, edges, null, Words, ref maker));
                streams.Swap();
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var tick = 3; tick < 8; tick++)
            {
                derive.Begin();
                derive.AddCount(EventDerive.CountEdges(edges, Words));
                derive.Derive(streams, hits, dense, tick, edges, null, Words, ref maker);
                streams.Swap();
            }

            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        finally
        {
            NativeMemory.Free(edges);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new EventDerive(hits.Id, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventDerive(hits.Id, EventDerive.MaxCapacity + 1));
    }
}
