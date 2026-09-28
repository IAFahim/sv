using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sv;
using Xunit;

public unsafe struct EventsSandbox
{
    public struct ManaGained
    {
        public int Slot;

        public int Amount;
    }

    public struct Landed
    {
        public int Slot;
    }

[UnmanagedCallersOnly]
    public static ManaGained MakeMana(int slot, int value) => new() { Slot = slot, Amount = value };

[UnmanagedCallersOnly]
    public static Landed MakeLanded(int slot, int value) => new() { Slot = slot };
}

public unsafe class EventsTests
{
    static List<(int slot, int amount)> DrainMana(EventStreams streams)
    {
        var result = new List<(int, int)>();
        while (streams.TryReadNext(out _, out var payload, out _))
        {
            var evt = *(EventsSandbox.ManaGained*)payload;
            result.Add((evt.Slot, evt.Amount));
        }

        return result;
    }

    [Fact]
    public void Channel_WriteRead_Roundtrip()
    {
        using var streams = EventStreams.Create();
        var mana = streams.CreateChannel<EventsSandbox.ManaGained>();
        mana.Write(new EventsSandbox.ManaGained { Slot = 3, Amount = 12 });
        mana.Write(new EventsSandbox.ManaGained { Slot = 7, Amount = -4 });
        streams.Swap();

        var drained = DrainMana(streams);
        Assert.Equal(new[] { (3, 12), (7, -4) }, drained);
        Assert.False(streams.TryReadNext(out _, out _, out _));
    }

    [Fact]
    public void Swap_DoubleBuffers_WritesAfterSwapAreNextFrame()
    {
        using var streams = EventStreams.Create();
        var mana = streams.CreateChannel<EventsSandbox.ManaGained>();
        mana.Write(new EventsSandbox.ManaGained { Slot = 1, Amount = 10 });
        streams.Swap();
        var frame1 = DrainMana(streams);
        Assert.Equal(new[] { (1, 10) }, frame1);

        mana.Write(new EventsSandbox.ManaGained { Slot = 2, Amount = 20 });
        streams.Swap();
        var frame2 = DrainMana(streams);
        Assert.Equal(new[] { (2, 20) }, frame2);
    }

    [Fact]
    public void Writers_DrainInIndexOrder()
    {
        using var streams = EventStreams.Create(writerCount: 2);
        var w1 = streams.CreateChannel<EventsSandbox.ManaGained>(writer: 1);
        var w0 = streams.CreateChannel<EventsSandbox.ManaGained>(writer: 0);
        w1.Write(new EventsSandbox.ManaGained { Slot = 100, Amount = 1 });
        w0.Write(new EventsSandbox.ManaGained { Slot = 0, Amount = 1 });
        w1.Write(new EventsSandbox.ManaGained { Slot = 101, Amount = 1 });
        w0.Write(new EventsSandbox.ManaGained { Slot = 1, Amount = 1 });
        streams.Swap();

        Assert.Equal(new[] { (0, 1), (1, 1), (100, 1), (101, 1) }, DrainMana(streams));
    }

    [Fact]
    public void EmitFromMask_ConformsToBusFixtures()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "mask-fixtures.json"));
        using var doc = JsonDocument.Parse(json);
        using var streams = EventStreams.Create();
        var landed = streams.CreateChannel<EventsSandbox.Landed>();

        foreach (var pattern in doc.RootElement.GetProperty("patterns").EnumerateArray())
        {
            var low = Convert.ToUInt64(pattern.GetProperty("low").GetString(), 16);
            var high = Convert.ToUInt64(pattern.GetProperty("high").GetString(), 16);
            var expected = new List<(int, int)>();
            foreach (var run in pattern.GetProperty("enabledRuns").EnumerateArray())
            {
                var start = run[0].GetInt32();
                var len = run[1].GetInt32();
                for (var i = 0; i < len; i++)
                {
                    expected.Add((start + i, 1));
                }
            }

            streams.EmitFromMask(landed, low, 0, &EventsSandbox.MakeLanded);
            streams.EmitFromMask(landed, high, 64, &EventsSandbox.MakeLanded);
            streams.Swap();

            var drained = new List<(int, int)>();
            while (streams.TryReadNext(out _, out var payload, out _))
            {
                var evt = *(EventsSandbox.Landed*)payload;
                drained.Add((evt.Slot, 1));
            }

            Assert.Equal(expected, drained);
        }
    }

    [Fact]
    public void EmitFromBools_NormalizesNonZeroBytes()
    {
        using var streams = EventStreams.Create();
        var landed = streams.CreateChannel<EventsSandbox.Landed>();
        var cond = stackalloc byte[8];
        cond[0] = 1;
        cond[1] = 5;
        cond[2] = 0;
        cond[3] = 255;
        cond[4] = 0;
        cond[5] = 0;
        cond[6] = 2;
        cond[7] = 0;

        streams.EmitFromBools(landed, cond, 0, 8, &EventsSandbox.MakeLanded);
        streams.Swap();

        var drained = new List<int>();
        while (streams.TryReadNext(out _, out var payload, out _))
        {
            drained.Add((*(EventsSandbox.Landed*)payload).Slot);
        }

        Assert.Equal(new[] { 0, 1, 3, 6 }, drained);
    }

    [Fact]
    public void EmitFromBools_EnabledMask_FiltersRows()
    {
        using var streams = EventStreams.Create();
        var landed = streams.CreateChannel<EventsSandbox.Landed>();
        var cond = stackalloc byte[128];
        for (var i = 64; i < 128; i++)
        {
            cond[i] = 1;
        }

        streams.EmitFromBools(landed, cond, 0b1010ul, 64, &EventsSandbox.MakeLanded);
        streams.Swap();

        var drained = new List<int>();
        while (streams.TryReadNext(out _, out var payload, out _))
        {
            drained.Add((*(EventsSandbox.Landed*)payload).Slot);
        }

        Assert.Equal(new[] { 65, 67 }, drained);
    }

    [Fact]
    public void EmitFromNonZero_CarriesAppliedDeltas()
    {
        using var streams = EventStreams.Create();
        var mana = streams.CreateChannel<EventsSandbox.ManaGained>();
        var applied = stackalloc int[4] { 5, 0, -3, 12 };

        streams.EmitFromNonZero(mana, applied, 0, 4, &EventsSandbox.MakeMana);
        streams.Swap();

        Assert.Equal(new[] { (0, 5), (2, -3), (3, 12) }, DrainMana(streams));
    }

    [Fact]
    public void EmitFromNonZero_EnabledMask_SkipsZeroAndDisabled()
    {
        using var streams = EventStreams.Create();
        var mana = streams.CreateChannel<EventsSandbox.ManaGained>();
        var applied = stackalloc int[128];
        applied[64] = 7;
        applied[65] = 0;
        applied[66] = 9;

        streams.EmitFromNonZero(mana, applied, 0b111ul, 64, &EventsSandbox.MakeMana);
        streams.Swap();

        Assert.Equal(new[] { (64, 7), (66, 9) }, DrainMana(streams));
    }

    [Fact]
    public void WalkNonZero_FnptrEscape_VisitsEnabledNonZero()
    {
        var applied = stackalloc int[128];
        applied[63] = 1;
        applied[64] = 2;
        applied[127] = 3;
        var visited = new List<(int, int)>();

        VisitCollector.Shared = visited!;
        EventStreams.WalkNonZero(applied, ulong.MaxValue, 0, &VisitCollector.Visit);
        EventStreams.WalkNonZero(applied, ulong.MaxValue, 64, &VisitCollector.Visit);

        Assert.Equal(new[] { (63, 1), (64, 2), (127, 3) }, visited);
    }

    [Fact]
    public void BoundaryCounts_FromFixtures_RoundTrip()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "mask-fixtures.json"));
        using var doc = JsonDocument.Parse(json);
        using var streams = EventStreams.Create();
        var landed = streams.CreateChannel<EventsSandbox.Landed>();

        foreach (var count in doc.RootElement.GetProperty("boundaryCounts").EnumerateArray())
        {
            var n = count.GetInt32();
            var low = n >= 64 ? ulong.MaxValue : (1ul << n) - 1;
            var high = n <= 64 ? 0ul : n >= 128 ? ulong.MaxValue : (1ul << (n - 64)) - 1;
            streams.EmitFromMask(landed, low, 0, &EventsSandbox.MakeLanded);
            streams.EmitFromMask(landed, high, 64, &EventsSandbox.MakeLanded);
            streams.Swap();

            var drainedCount = 0;
            while (streams.TryReadNext(out _, out _, out _))
            {
                drainedCount++;
            }

            Assert.Equal(n, drainedCount);
        }
    }

    [Fact]
    public void Growth_PreservesAllEntries()
    {
        using var streams = EventStreams.Create(writerCount: 1, bytesPerWriter: 64);
        var mana = streams.CreateChannel<EventsSandbox.ManaGained>();
        for (var i = 0; i < 200; i++)
        {
            mana.Write(new EventsSandbox.ManaGained { Slot = i, Amount = i });
        }

        streams.Swap();
        Assert.Equal(200, DrainMana(streams).Count);
    }
}

public static unsafe class VisitCollector
{
    [UnmanagedCallersOnly]
    public static void Visit(int row, int value)
    {
        Shared!.Add((row, value));
    }

    [ThreadStatic]
    public static List<(int, int)>? Shared;
}
