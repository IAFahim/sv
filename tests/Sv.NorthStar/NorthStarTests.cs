using System;
using System.Linq;
using Xunit;
using static Sv.NorthStar.NorthStarHarness;

namespace Sv.NorthStar;

public sealed unsafe class NorthStarTests
{
    readonly uint[] axesBits;

    readonly ulong buttons;

    public NorthStarTests()
    {
        axesBits = new[] { BitConverter.SingleToUInt32Bits(0.016666668f), 0x3F800000u };
        buttons = 1ul << RegenButton;
    }

    FrameRecord[] RunScenario()
    {
        using var live = new NorthStarHarness();
        var frames = new FrameRecord[3];
        for (var t = 0; t < 3; t++)
        {
            fixed (uint* ax = axesBits)
            fixed (ulong* bt = &buttons)
            {
                live.CaptureTick(ax, bt);
            }

            frames[t] = live.Record();
        }

        using var replay = new NorthStarHarness(live.Input);
        replay.Input.BeginReplay(0);
        for (var t = 0; t < 3; t++)
        {
            replay.ReplayTick();
            var replayed = replay.Record();
            Assert.Equal(frames[t].PositionBits, replayed.PositionBits);
            Assert.Equal(frames[t].VelocityBits, replayed.VelocityBits);
            Assert.Equal(frames[t].YBits, replayed.YBits);
            Assert.Equal(frames[t].MoveSpeedBits, replayed.MoveSpeedBits);
            Assert.Equal(frames[t].JumpPowerBits, replayed.JumpPowerBits);
            Assert.Equal(frames[t].MaxManaBits, replayed.MaxManaBits);
            Assert.Equal(frames[t].Mana, replayed.Mana);
            Assert.Equal(frames[t].Landed, replayed.Landed);
            Assert.Equal(frames[t].Events, replayed.Events);
        }

        return frames;
    }

    [Fact]
    public void Replay_FromRing_IsBitIdentical_AllThreeTicks()
    {
        RunScenario();
    }

    [Fact]
    public void Tick0_JumpArcLands_RegenApplied()
    {
        var frames = RunScenario();

        Assert.Equal(BitConverter.SingleToUInt32Bits(2f), frames[0].YBits[0]);
        Assert.All(frames[0].Landed, hit => Assert.Equal(1, hit));
        Assert.All(frames[0].Mana, value => Assert.Equal(65, value));
        Assert.All(frames[0].Applied, value => Assert.Equal(25, value));
        Assert.Equal(BitConverter.SingleToUInt32Bits(6f), frames[0].MoveSpeedBits[0]);
        Assert.Equal(BitConverter.SingleToUInt32Bits(1f), frames[0].JumpPowerBits[0]);
        Assert.Equal(BitConverter.SingleToUInt32Bits(100f), frames[0].MaxManaBits[0]);
    }

    [Fact]
    public void Tick1_DrainsLanded_BroadcastsModifiedSpeed_RegenDoesNotRefire()
    {
        var frames = RunScenario();

        Assert.Equal(BitConverter.SingleToUInt32Bits(15f), frames[1].MoveSpeedBits[0]);
        Assert.All(frames[1].Landed, hit => Assert.Equal(0, hit));
        Assert.All(frames[1].Mana, value => Assert.Equal(65, value));
        Assert.Contains("landed:0", frames[1].Events);
        Assert.Contains("mana:0:25", frames[1].Events);
        Assert.Equal(4, frames[1].Events.Count(e => e.StartsWith("landed:")));
        Assert.Equal(4, frames[1].Events.Count(e => e.StartsWith("mana:")));
    }

    [Fact]
    public void Tick2_NoLandedNoRegen_SteadyStateHolds()
    {
        var frames = RunScenario();

        Assert.All(frames[2].Landed, hit => Assert.Equal(0, hit));
        Assert.All(frames[2].Mana, value => Assert.Equal(65, value));
        Assert.Equal(frames[1].Events.Count, frames[2].Events.Count);
        Assert.Empty(frames[0].Events);
    }
}
