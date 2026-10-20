using System;
using Sv;
using Xunit;

public unsafe class CanonicalNaNTests
{
    [Fact]
    public void Recalculate_NaNResult_IsCanonicalAcrossSignBits()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("power", float.MaxValue).Build(), 64);
        var stat = board.Schema.Stat("power");
        var slot = board.Acquire();
        board.AddModifier(slot, stat, new Modifier(ModifierKind.Added, float.MaxValue));
        board.AddModifier(slot, stat, new Modifier(ModifierKind.Multi, -1f));
        board.Recalculate();

        Assert.Equal(0x7FC00000u, BitConverter.SingleToUInt32Bits(board.Output(stat)[slot]));
    }

    [Fact]
    public void Recalculate_InfiniteResult_IsUnchanged()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("power", float.MaxValue).Build(), 64);
        var stat = board.Schema.Stat("power");
        var slot = board.Acquire();
        board.AddModifier(slot, stat, new Modifier(ModifierKind.Added, float.MaxValue));
        board.Recalculate();

        Assert.Equal(float.PositiveInfinity, board.Output(stat)[slot]);
    }

    [Fact]
    public void CanonicalNaN_LeavesFiniteValuesAlone()
    {
        Assert.Equal(0f, Board.CanonicalNaN(0f));
        Assert.Equal(-1.5f, Board.CanonicalNaN(-1.5f));
        Assert.Equal(float.NegativeInfinity, Board.CanonicalNaN(float.NegativeInfinity));
        Assert.Equal(0x7FC00000u, BitConverter.SingleToUInt32Bits(Board.CanonicalNaN(float.NaN)));
        Assert.Equal(0x7FC00000u, BitConverter.SingleToUInt32Bits(Board.CanonicalNaN(BitConverter.UInt32BitsToSingle(0xFFC0_0001u))));
    }
}
