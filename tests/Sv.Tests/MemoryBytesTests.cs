using System;
using Sv;
using Xunit;

public unsafe class MemoryBytesTests
{
    [Fact]
    public void IntrinsicsOnly_ExactBill()
    {
        using var board = Board.Create(SchemaBuilder.Create().Intrinsic("mana").Build(), 10);
        Assert.Equal(10, board.RequestedCapacity);
        Assert.Equal(64, board.Capacity);
        Assert.Equal(64 * sizeof(int) + sizeof(ulong), board.MemoryBytes);
    }

    [Fact]
    public void StatsBoard_ExceedsCrossProduct_GrowsWithModifiers()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("attack", 10f).Build(), 100);
        Assert.Equal(100, board.RequestedCapacity);
        Assert.Equal(128, board.Capacity);
        var fresh = board.MemoryBytes;
        Assert.True(fresh > (long)128 * 24, $"pair cross product alone should be inside the bill (got {fresh})");

        board.Acquire();
        board.AddModifier(0, board.Schema.Stat("attack"), new Modifier(ModifierKind.Multi, 0.5f));
        board.Recalculate();
        Assert.True(board.MemoryBytes > fresh);
    }

    [Fact]
    public void MillionSlotBoard_BillScalesWithTouchedPages()
    {
        using var board = Board.Create(SchemaBuilder.Create()
            .Stat("attack", 10f)
            .Stat("max_health", 100f)
            .Stat("regen", 2f)
            .Build(), 1_000_000);
        var empty = board.MemoryBytes / (1024.0 * 1024.0);
        Assert.True(empty < 21, $"1M x 3-stat empty board pays output columns + slot bookkeeping + per-slot dirty masks, got {empty:0.0} MiB");

        for (var i = 0; i < 10_000; i++)
        {
            board.Acquire();
        }

        board.Recalculate();
        var sparse = board.MemoryBytes / (1024.0 * 1024.0);
        Assert.True(sparse - empty < 1, $"10k live slots touch 3 pages, got +{sparse - empty:0.00} MiB");

        for (var i = 10_000; i < 1_000_000; i++)
        {
            board.Acquire();
        }

        board.Recalculate();
        var full = board.MemoryBytes / (1024.0 * 1024.0);
        Assert.True(full > 30 && full < 60, $"1M live, modifier-free: 4 B/pair set-id pages for every slot, got {full:0.0} MiB");
    }
}
