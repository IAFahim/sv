using System;
using System.Collections.Generic;
using Xunit;

namespace Sv.NorthStar;

public sealed unsafe class MigrationTests
{
    static readonly uint[][] PrePosition =
    {
        new uint[] { 0, 0, 0, 0 },
        new uint[] { 0, 0, 0, 0 },
        new uint[] { 993158993, 993158993, 993158993, 993158993 },
    };

    static readonly uint[][] PreVelocity =
    {
        new uint[] { 0, 0, 0, 0 },
        new uint[] { 1042759899, 1042759899, 1042759899, 1042759899 },
        new uint[] { 1051148507, 1051148507, 1051148507, 1051148507 },
    };

    static readonly uint[][] PreY =
    {
        new uint[] { 1073741824, 1073741824, 1073741824, 1073741824 },
        new uint[] { 1073741824, 1073741824, 1073741824, 1073741824 },
        new uint[] { 1073741824, 1073741824, 1073741824, 1073741824 },
    };

    static readonly uint[][] PreMoveSpeed =
    {
        new uint[] { 1086324736, 1086324736, 1086324736, 1086324736 },
        new uint[] { 1097859072, 1097859072, 1097859072, 1097859072 },
        new uint[] { 1097859072, 1097859072, 1097859072, 1097859072 },
    };

    static readonly uint[][] PreJumpPower =
    {
        new uint[] { 1065353216, 1065353216, 1065353216, 1065353216 },
        new uint[] { 1065353216, 1065353216, 1065353216, 1065353216 },
        new uint[] { 1065353216, 1065353216, 1065353216, 1065353216 },
    };

    static readonly uint[][] PreMaxMana =
    {
        new uint[] { 1120403456, 1120403456, 1120403456, 1120403456 },
        new uint[] { 1120403456, 1120403456, 1120403456, 1120403456 },
        new uint[] { 1120403456, 1120403456, 1120403456, 1120403456 },
    };

    static readonly int[][] PreMana =
    {
        new[] { 65, 65, 65, 65 },
        new[] { 65, 65, 65, 65 },
        new[] { 65, 65, 65, 65 },
    };

    static readonly int[][] PreApplied =
    {
        new[] { 25, 25, 25, 25 },
        new[] { 0, 0, 0, 0 },
        new[] { 0, 0, 0, 0 },
    };

    static readonly byte[][] PreLanded =
    {
        new byte[] { 1, 1, 1, 1 },
        new byte[] { 0, 0, 0, 0 },
        new byte[] { 0, 0, 0, 0 },
    };

    static readonly string[][] PreNewEvents =
    {
        new string[] { },
        new[] { "landed:0", "landed:1", "landed:2", "landed:3", "mana:0:25", "mana:1:25", "mana:2:25", "mana:3:25" },
        new string[] { },
    };

    [Fact]
    public void FrameRunHarness_MatchesPreMigrationManualDigests_BitIdentical()
    {
        var axesBits = new[] { BitConverter.SingleToUInt32Bits(0.016666668f), 0x3F800000u };
        var buttons = new ulong[] { 1ul << NorthStarHarness.RegenButton };
        using var harness = new NorthStarHarness();
        var eventCount = 0;
        for (var t = 0; t < 3; t++)
        {
            fixed (uint* ax = axesBits)
            fixed (ulong* bt = buttons)
            {
                harness.CaptureTick(ax, bt);
            }

            var frame = harness.Record();
            Assert.Equal(PrePosition[t], frame.PositionBits);
            Assert.Equal(PreVelocity[t], frame.VelocityBits);
            Assert.Equal(PreY[t], frame.YBits);
            Assert.Equal(PreMoveSpeed[t], frame.MoveSpeedBits);
            Assert.Equal(PreJumpPower[t], frame.JumpPowerBits);
            Assert.Equal(PreMaxMana[t], frame.MaxManaBits);
            Assert.Equal(PreMana[t], frame.Mana);
            Assert.Equal(PreApplied[t], frame.Applied);
            Assert.Equal(PreLanded[t], frame.Landed);
            var expected = new List<string>();
            for (var prior = 0; prior <= t; prior++)
            {
                expected.AddRange(PreNewEvents[prior]);
            }

            Assert.Equal(expected, frame.Events);
            eventCount += PreNewEvents[t].Length;
        }

        Assert.Equal(8, eventCount);
    }
}
