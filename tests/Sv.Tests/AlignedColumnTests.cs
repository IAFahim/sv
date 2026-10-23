using System;
using Sv;
using Xunit;

public unsafe class AlignedColumnTests
{
    [Fact]
    public void OwnedColumns_AndMirrors_Are64Aligned()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("power", 10f).Build(), 128);
        var power = board.Schema.Stat("power");
        var slot = board.Acquire();
        board.Recalculate();
        var column = board.OutputPtr(power);
        Assert.Equal(0, (nint)column & 63);

        var mirror = board.MirrorBounds(power);
        Assert.Equal(0, (nint)mirror & 63);
        Assert.Equal(10, mirror[slot]);
    }

    [Fact]
    public void LargeColumns_AreHugePageAligned()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("power", 10f).Build(), 1 << 19);
        var power = board.Schema.Stat("power");
        board.Acquire();
        board.Recalculate();
        var column = board.OutputPtr(power);
        Assert.Equal(0, (nint)column & ((2 * 1024 * 1024) - 1));
    }

    [Fact]
    public void Alignment_IsNeverBilled()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("power", 10f).Build(), 64);
        var power = board.Schema.Stat("power");
        var slot = board.Acquire();
        board.Recalculate();
        var before = board.MemoryBytes;
        board.Output(power)[slot] += 0f;
        Assert.Equal(before + (long)board.Capacity * sizeof(float), board.MemoryBytes);
    }
}
