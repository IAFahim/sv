using System;
using Sv;
using Xunit;

public unsafe class InputTests
{
    [Fact]
    public void Capture_ComputesDownPressedReleasedEdges()
    {
        using var input = InputState.Create(axes: 1, buttonWords: 1, ringTicks: 4);
        var axes = stackalloc uint[1];
        var buttons = stackalloc ulong[1];

        buttons[0] = 0b0110;
        input.Capture(axes, buttons);
        Assert.True(input.Down(1));
        Assert.True(input.Down(2));
        Assert.True(input.Pressed(1));
        Assert.False(input.Pressed(0));
        Assert.False(input.Released(1));

        buttons[0] = 0b0011;
        input.Capture(axes, buttons);
        Assert.True(input.Pressed(0));
        Assert.False(input.Pressed(1));
        Assert.True(input.Released(2));
        Assert.False(input.Released(1));
        Assert.False(input.Released(0));
        Assert.True(input.Down(2) == false);
    }

    [Fact]
    public void Axes_RawBitsRoundTrip_IncludingSpecials()
    {
        using var input = InputState.Create(axes: 4, buttonWords: 1, ringTicks: 2);
        var axes = stackalloc uint[4];
        axes[0] = 0x7F800000;
        axes[1] = 0xFF800000;
        axes[2] = 0x7FC00000;
        axes[3] = 0x80000000;
        var buttons = stackalloc ulong[1];

        input.Capture(axes, buttons);

        Assert.Equal(0x7F800000u, input.AxisBits(0));
        Assert.Equal(float.PositiveInfinity, input.AxisF(0));
        Assert.Equal(0xFF800000u, input.AxisBits(1));
        Assert.Equal(float.NegativeInfinity, input.AxisF(1));
        Assert.Equal(0x7FC00000u, input.AxisBits(2));
        Assert.True(float.IsNaN(input.AxisF(2)));
        Assert.Equal(0x80000000u, input.AxisBits(3));
        Assert.Equal(0x80000000u, BitConverter.SingleToUInt32Bits(input.AxisF(3)));
    }

    [Fact]
    public void RandomButtonSequences_EdgesMatchAndNot()
    {
        var rng = new Random(77);
        using var input = InputState.Create(axes: 1, buttonWords: 2, ringTicks: 16);
        var axes = stackalloc uint[1];
        var buttons = stackalloc ulong[2];

        for (var tick = 0; tick < 16; tick++)
        {
            var w0 = (ulong)rng.NextInt64(long.MinValue, long.MaxValue);
            var w1 = (ulong)rng.NextInt64(long.MinValue, long.MaxValue);
            var prev0 = tick == 0 ? 0ul : buttons[0];
            var prev1 = tick == 0 ? 0ul : buttons[1];
            buttons[0] = w0;
            buttons[1] = w1;
            input.Capture(axes, buttons);

            Assert.Equal(w0 & ~prev0, ExtractPressed(input, 0));
            Assert.Equal(~w0 & prev0, ExtractReleased(input, 0));
            Assert.Equal(w1 & ~prev1, ExtractPressed(input, 64));
            Assert.Equal(~w1 & prev1, ExtractReleased(input, 64));
        }
    }

    static ulong ExtractPressed(InputState input, int wordBase)
    {
        ulong mask = 0;
        for (var b = 0; b < 64; b++)
        {
            if (input.Pressed(wordBase + b))
            {
                mask |= 1ul << b;
            }
        }

        return mask;
    }

    static ulong ExtractReleased(InputState input, int wordBase)
    {
        ulong mask = 0;
        for (var b = 0; b < 64; b++)
        {
            if (input.Released(wordBase + b))
            {
                mask |= 1ul << b;
            }
        }

        return mask;
    }

    [Fact]
    public void Replay_FromTick_IsBitIdenticalToLiveRun()
    {
        var rng = new Random(4242);
        using var input = InputState.Create(axes: 3, buttonWords: 1, ringTicks: 8);
        var axes = stackalloc uint[3];
        var buttons = stackalloc ulong[1];

        uint[] liveAxes = new uint[3 * 5];
        ulong[] liveButtons = new ulong[5];
        bool[] livePressed = new bool[64 * 5];
        bool[] liveReleased = new bool[64 * 5];

        for (var tick = 0; tick < 5; tick++)
        {
            for (var a = 0; a < 3; a++)
            {
                axes[a] = (uint)rng.NextInt64(0, uint.MaxValue);
            }

            buttons[0] = (ulong)rng.NextInt64(long.MinValue, long.MaxValue);
            input.Capture(axes, buttons);
            Snapshot(input, tick, liveAxes, liveButtons, livePressed, liveReleased);
        }

        input.BeginReplay(0);
        var replayTick = 0;
        while (input.ReplayStep())
        {
            CompareSnapshot(input, replayTick, liveAxes, liveButtons, livePressed, liveReleased);
            replayTick++;
        }

        Assert.Equal(5, replayTick);
    }

    static void Snapshot(InputState input, int tick, uint[] axes, ulong[] buttons, bool[] pressed, bool[] released)
    {
        for (var a = 0; a < 3; a++)
        {
            axes[tick * 3 + a] = input.AxisBits(a);
        }

        buttons[tick] = ExtractDown(input);
        for (var b = 0; b < 64; b++)
        {
            pressed[tick * 64 + b] = input.Pressed(b);
            released[tick * 64 + b] = input.Released(b);
        }
    }

    static void CompareSnapshot(InputState input, int tick, uint[] axes, ulong[] buttons, bool[] pressed, bool[] released)
    {
        for (var a = 0; a < 3; a++)
        {
            Assert.Equal(axes[tick * 3 + a], input.AxisBits(a));
        }

        Assert.Equal(buttons[tick], ExtractDown(input));
        for (var b = 0; b < 64; b++)
        {
            Assert.Equal(pressed[tick * 64 + b], input.Pressed(b));
            Assert.Equal(released[tick * 64 + b], input.Released(b));
        }
    }

    static ulong ExtractDown(InputState input)
    {
        ulong mask = 0;
        for (var b = 0; b < 64; b++)
        {
            if (input.Down(b))
            {
                mask |= 1ul << b;
            }
        }

        return mask;
    }

    [Fact]
    public void Ring_EvictsOldest_ReplayRejectsEvictedTicks()
    {
        using var input = InputState.Create(axes: 1, buttonWords: 1, ringTicks: 4);
        var axes = stackalloc uint[1];
        var buttons = stackalloc ulong[1];

        for (var tick = 0; tick < 10; tick++)
        {
            buttons[0] = (ulong)tick;
            input.Capture(axes, buttons);
        }

        Assert.Equal(6, input.OldestRingTick);
        input.BeginReplay(6);
        Assert.True(input.ReplayStep());
        Assert.Equal(6ul, ExtractDown(input));

        Assert.Throws<ArgumentOutOfRangeException>(() => input.BeginReplay(5));
    }

    [Fact]
    public void Guards_RejectOutOfRangeQueries()
    {
        using var input = InputState.Create(axes: 2, buttonWords: 1, ringTicks: 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => input.Down(64));
        Assert.Throws<ArgumentOutOfRangeException>(() => input.Down(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => input.AxisBits(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => input.AxisF(-1));
    }
}

public unsafe class ReplayEdgeTests
{
    [Fact]
    public void BeginReplay_MidStream_HeldButtonIsNotPressedAgain()
    {
        using var input = InputState.Create(axes: 1, buttonWords: 1, ringTicks: 8);
        var axes = stackalloc uint[1];
        var buttons = stackalloc ulong[1];

        buttons[0] = 1ul;
        input.Capture(axes, buttons);
        Assert.True(input.Pressed(0));
        input.Capture(axes, buttons);
        Assert.False(input.Pressed(0));
        input.Capture(axes, buttons);
        Assert.False(input.Pressed(0));

        input.BeginReplay(1);
        Assert.True(input.ReplayStep());
        Assert.True(input.Down(0));
        Assert.False(input.Pressed(0));
        Assert.False(input.Released(0));
        Assert.True(input.ReplayStep());
        Assert.False(input.Pressed(0));
    }

    [Fact]
    public void BeginReplay_AtTickZero_ZeroPriorReportsPress()
    {
        using var input = InputState.Create(axes: 1, buttonWords: 1, ringTicks: 8);
        var axes = stackalloc uint[1];
        var buttons = stackalloc ulong[1];

        buttons[0] = 1ul;
        input.Capture(axes, buttons);
        input.Capture(axes, buttons);

        input.BeginReplay(0);
        Assert.True(input.ReplayStep());
        Assert.True(input.Pressed(0));
    }

    [Fact]
    public void BeginReplay_EvictedPrior_FallsBackToZeroSeed()
    {
        using var input = InputState.Create(axes: 1, buttonWords: 1, ringTicks: 2);
        var axes = stackalloc uint[1];
        var buttons = stackalloc ulong[1];

        buttons[0] = 1ul;
        for (var tick = 0; tick < 4; tick++)
        {
            input.Capture(axes, buttons);
        }

        Assert.Equal(2, input.OldestRingTick);
        input.BeginReplay(2);
        Assert.True(input.ReplayStep());
        Assert.True(input.Pressed(0));
    }

    [Fact]
    public void BeginReplay_MidStream_RealReleaseEdgeIsReproduced()
    {
        using var input = InputState.Create(axes: 1, buttonWords: 1, ringTicks: 8);
        var axes = stackalloc uint[1];
        var buttons = stackalloc ulong[1];

        buttons[0] = 1ul;
        input.Capture(axes, buttons);
        buttons[0] = 0;
        input.Capture(axes, buttons);
        Assert.True(input.Released(0));

        input.BeginReplay(1);
        Assert.True(input.ReplayStep());
        Assert.False(input.Down(0));
        Assert.True(input.Released(0));
        Assert.False(input.Pressed(0));
    }
}
