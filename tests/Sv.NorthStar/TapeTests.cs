using System;
using System.IO;
using Xunit;

namespace Sv.NorthStar;

public sealed unsafe class TapeTests
{
    const int Ticks = 3;

    static void RunTicks(NorthStarHarness harness, int seed)
    {
        for (var t = 0; t < Ticks; t++)
        {
            var axes = new[] { BitConverter.SingleToUInt32Bits(0.016666668f), BitConverter.SingleToUInt32Bits(1f + 0.25f * seed) };
            var buttons = new ulong[] { (t == 0 ? 1ul << NorthStarHarness.RegenButton : 0ul) | ((ulong)seed << 1) };
            fixed (uint* ax = axes)
            fixed (ulong* bt = buttons)
            {
                harness.CaptureTick(ax, bt);
            }
        }
    }

    static void AssertSameState(FrameRecord live, FrameRecord replay)
    {
        Assert.Equal(live.PositionBits, replay.PositionBits);
        Assert.Equal(live.VelocityBits, replay.VelocityBits);
        Assert.Equal(live.YBits, replay.YBits);
        Assert.Equal(live.MoveSpeedBits, replay.MoveSpeedBits);
        Assert.Equal(live.JumpPowerBits, replay.JumpPowerBits);
        Assert.Equal(live.MaxManaBits, replay.MaxManaBits);
        Assert.Equal(live.Mana, replay.Mana);
        Assert.Equal(live.Applied, replay.Applied);
        Assert.Equal(live.Landed, replay.Landed);
        Assert.Equal(live.Events, replay.Events);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    public void Tape_RecordThenPlaybackOnFreshHost_IsBitIdentical(int seed)
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"northstar-{seed}.tape");
        FrameRecord live;
        using (var recorder = new NorthStarHarness())
        {
            recorder.Host.Record(path);
            RunTicks(recorder, seed);
            live = recorder.Record();
            recorder.Host.EndRecord();
        }

        using var replay = new NorthStarHarness();
        var frames = replay.Host.Playback(path);
        Assert.Equal(Ticks, frames);
        AssertSameState(live, replay.Record());
    }

    [Fact]
    public void Tape_CorruptedDigest_ThrowsOnPlayback()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "northstar-corrupt.tape");
        using (var recorder = new NorthStarHarness())
        {
            recorder.Host.Record(path);
            RunTicks(recorder, 0);
            recorder.Host.EndRecord();
        }

        var bytes = File.ReadAllBytes(path);
        var digestAt = 8 + sizeof(uint) + sizeof(int) + sizeof(int) + 2 * sizeof(uint) + sizeof(ulong);
        bytes[digestAt] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
        using var replay = new NorthStarHarness();
        Assert.Throws<InvalidOperationException>(() => replay.Host.Playback(path));
    }

    [Fact]
    public void Tape_TruncatedFrame_ThrowsOnPlayback()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "northstar-truncated.tape");
        using (var recorder = new NorthStarHarness())
        {
            recorder.Host.Record(path);
            RunTicks(recorder, 0);
            recorder.Host.EndRecord();
        }

        var bytes = File.ReadAllBytes(path);
        Array.Resize(ref bytes, bytes.Length - 10);
        File.WriteAllBytes(path, bytes);
        using var replay = new NorthStarHarness();
        Assert.Throws<InvalidDataException>(() => replay.Host.Playback(path));
    }

    [Fact]
    public void Tape_SparseBoard_FrameBodyIsLiveSized()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "northstar-sparse.tape");
        using (var recorder = new NorthStarHarness())
        {
            recorder.Host.Record(path);
            RunTicks(recorder, 0);
            recorder.Host.EndRecord();
        }

        var length = new FileInfo(path).Length;
        using var replay = new NorthStarHarness();
        Assert.Equal(Ticks, replay.Host.Playback(path));
        Assert.True(length < 400, $"sparse frames not live-compressed: {length} bytes for 3 frames (capacity 64, 4 live; v1 wrote ~848 B/frame)");
    }

    [Fact]
    public void Tape_FutureVersion_IsRejected()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "northstar-version.tape");
        File.WriteAllBytes(path, new byte[] { (byte)'S', (byte)'V', (byte)'T', (byte)'P', 0x63, 0, 0, 0 });
        using var replay = new NorthStarHarness();
        var error = Assert.Throws<InvalidDataException>(() => replay.Host.Playback(path));
        Assert.Contains("version", error.Message);
        File.Delete(path);
    }

    [Fact]
    public void Harness_SteadyStateWarmPath_AllocatesZeroManagedBytes()
    {
        using var harness = new NorthStarHarness();
        var axes = new[] { BitConverter.SingleToUInt32Bits(0.016666668f), 0x3F800000u };
        var buttons = new ulong[] { 1ul << NorthStarHarness.RegenButton };
        for (var warm = 0; warm < 8; warm++)
        {
            fixed (uint* ax = axes)
            fixed (ulong* bt = buttons)
            {
                harness.CaptureTick(ax, bt);
            }
        }

        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        long sink = 0;
        for (var t = 0; t < 2000; t++)
        {
            fixed (uint* ax = axes)
            fixed (ulong* bt = buttons)
            {
                harness.CaptureTick(ax, bt);
            }

            sink += harness.LastApplied[t % NorthStarHarness.Entities] + harness.EventLog.Count;
        }

        var deltaBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(deltaBytes == 0, $"harness frame path allocated {deltaBytes} managed bytes over 2,000 frames (sink {sink})");
    }

    [Fact]
    public void Tape_RecordingSteadyState_AllocatesZeroManagedBytes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "northstar-warm.tape");
        using var harness = new NorthStarHarness();
        harness.Host.Record(path);
        var axes = new[] { BitConverter.SingleToUInt32Bits(0.016666668f), 0x3F800000u };
        var buttons = new ulong[] { 1ul << NorthStarHarness.RegenButton };
        for (var warm = 0; warm < 8; warm++)
        {
            fixed (uint* ax = axes)
            fixed (ulong* bt = buttons)
            {
                harness.CaptureTick(ax, bt);
            }
        }

        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        long sink = 0;
        for (var t = 0; t < 2000; t++)
        {
            fixed (uint* ax = axes)
            fixed (ulong* bt = buttons)
            {
                harness.CaptureTick(ax, bt);
            }

            sink += harness.LastApplied[t % NorthStarHarness.Entities];
        }

        var deltaBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        harness.Host.EndRecord();
        Assert.True(deltaBytes == 0, $"taping frame path allocated {deltaBytes} managed bytes over 2,000 frames (sink {sink})");
    }
}
