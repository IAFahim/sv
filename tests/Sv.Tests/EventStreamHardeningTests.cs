using System;
using System.IO;
using Sv;
using Xunit;

public unsafe class EventStreamHardeningTests
{
    [Fact]
    public void Create_ZeroBytesPerWriter_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EventStreams.Create(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => EventStreams.Create(1, -4096));
    }

    [Fact]
    public void Create_SmallBytesPerWriter_RoundsUpAndGrows()
    {
        using var streams = EventStreams.Create(1, 1);
        var channel = streams.CreateChannel<byte>();
        var payload = (byte)7;
        for (var i = 0; i < 10_000; i++)
        {
            channel.Write(payload);
        }

        streams.Swap();
        Assert.True(streams.TryReadNext(out _, out _, out _));
    }

    [Fact]
    public void LoadState_HugeWrittenOffset_FailsDeterministically()
    {
        using var streams = EventStreams.Create(1, 64);
        streams.CreateChannel<int>();
        using var stale = new SvState();
        stale.BeginWrite();
        stale.Header(0x56455653u, ((long)1 << 32) | 1u);
        stale.Write(0);
        stale.Write(int.MaxValue);
        stale.Write(0);
        stale.Write(0);

        Assert.Throws<InvalidDataException>(() => streams.LoadState(stale));
    }

    [Fact]
    public void LoadState_NegativeOffset_FailsDeterministically()
    {
        using var streams = EventStreams.Create(1, 64);
        streams.CreateChannel<int>();
        using var stale = new SvState();
        stale.BeginWrite();
        stale.Header(0x56455653u, ((long)1 << 32) | 1u);
        stale.Write(0);
        stale.Write(-1);
        stale.Write(0);
        stale.Write(0);

        Assert.Throws<InvalidDataException>(() => streams.LoadState(stale));
    }
}
