using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sv;
using Xunit;

public unsafe class StructMakerTests
{
    public struct Hit
    {
        public int Row;

        public int Value;

        public int Order;
    }

    [UnmanagedCallersOnly]
    static Hit MakeHit(int row, int value) => new() { Row = row, Value = value };

    struct HitMaker : IEventMaker<Hit>
    {
        public Hit Make(int row, int value) => new() { Row = row, Value = value };
    }

    struct CountingMaker : IEventMaker<Hit>
    {
        public int Made;

        public Hit Make(int row, int value) => new() { Row = row, Value = value, Order = Made++ };
    }

    static List<Hit> Drain(EventStreams streams)
    {
        streams.Swap();
        var hits = new List<Hit>();
        while (streams.TryReadNext(out _, out var payload, out _))
        {
            hits.Add(*(Hit*)payload);
        }

        return hits;
    }

    [Fact]
    public void StructMaker_MatchesPointerMaker_EveryEmitter()
    {
        const int n = 517;
        var random = new Random(77);
        var cond = stackalloc byte[n];
        var values = stackalloc int[n];
        for (var i = 0; i < n; i++)
        {
            cond[i] = (byte)(random.Next(4) == 0 ? random.Next(1, 256) : 0);
            values[i] = random.Next(5) == 0 ? random.Next(-9, 10) : 0;
        }

        using var a = EventStreams.Create(1, 64);
        using var b = EventStreams.Create(1, 64);
        var ca = a.CreateChannel<Hit>();
        var cb = b.CreateChannel<Hit>();
        var maker = new HitMaker();
        foreach (var (start, count) in new[] { (0, n), (3, 64), (5, 130), (0, 0), (100, 7) })
        {
            a.EmitFromBools(ca, cond, start, count, &MakeHit);
            b.EmitFromBools(cb, cond, start, count, ref maker);
            a.EmitFromNonZero(ca, values, start, count, &MakeHit);
            b.EmitFromNonZero(cb, values, start, count, ref maker);
        }

        foreach (var word in new[] { 0ul, ulong.MaxValue, 0x8000_0000_0000_0001ul, 0x0F0F_0F0F_0F0F_0F0Ful })
        {
            foreach (var offset in new[] { 0, 64, 200 })
            {
                a.EmitFromMask(ca, word, offset, &MakeHit);
                b.EmitFromMask(cb, word, offset, ref maker);
                a.EmitFromBools(ca, cond, word, offset, &MakeHit);
                b.EmitFromBools(cb, cond, word, offset, ref maker);
                a.EmitFromNonZero(ca, values, word, offset, &MakeHit);
                b.EmitFromNonZero(cb, values, word, offset, ref maker);
            }
        }

        var expected = Drain(a);
        var actual = Drain(b);
        Assert.True(expected.Count > 500);
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Row, actual[i].Row);
            Assert.Equal(expected[i].Value, actual[i].Value);
        }
    }

    [Fact]
    public void StructMaker_StateIsPassedByRef_InEmitOrder()
    {
        var values = stackalloc int[200];
        for (var i = 0; i < 200; i++)
        {
            values[i] = i % 3 == 0 ? 1 : 0;
        }

        using var streams = EventStreams.Create(1, 64);
        var channel = streams.CreateChannel<Hit>();
        var maker = new CountingMaker();
        streams.EmitFromNonZero(channel, values, 0, 200, ref maker);
        var hits = Drain(streams);
        Assert.Equal(67, maker.Made);
        for (var i = 0; i < hits.Count; i++)
        {
            Assert.Equal(i, hits[i].Order);
            Assert.Equal(i * 3, hits[i].Row);
        }
    }

    [Fact]
    public void StructMaker_WarmEmitAllocatesZero()
    {
        var values = stackalloc int[256];
        for (var i = 0; i < 256; i++)
        {
            values[i] = i % 5;
        }

        using var streams = EventStreams.Create(1, 1 << 16);
        var channel = streams.CreateChannel<Hit>();
        var maker = new HitMaker();
        streams.EmitFromNonZero(channel, values, 0, 256, ref maker);
        Drain(streams);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var it = 0; it < 1000; it++)
        {
            streams.EmitFromNonZero(channel, values, 0, 256, ref maker);
            streams.Swap();
            while (streams.TryReadNext(out _, out _, out _))
            {
            }
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
