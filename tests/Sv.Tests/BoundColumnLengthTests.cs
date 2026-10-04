using System;
using Sv;
using Xunit;

public unsafe class BoundColumnLengthTests
{
    static Schema Schema() =>
        SchemaBuilder.Create()
            .Stat("move_speed", 6f)
            .Build();

    [Fact]
    public void ShortColumn_ThrowsUnderChecked_WritesThroughInRelease()
    {
        using var board = Board.Create(Schema(), 8);
        var speed = board.Schema.Stat("move_speed");
        var column = stackalloc float[64];
        for (var i = 0; i < 5; i++)
        {
            board.Acquire();
        }

        board.BindOutput(speed, column, columnLength: 4);
        board.AddModifier(4, speed, new Modifier(ModifierKind.Added, 1f));
#if SV_CHECKED
        Assert.Throws<InvalidOperationException>(() => board.Recalculate());
#else
        board.Recalculate();
        Assert.Equal(7f, column[4]);
#endif
    }

    [Fact]
    public void ExactCapacityLength_NeverThrows()
    {
        using var board = Board.Create(Schema(), 8);
        var speed = board.Schema.Stat("move_speed");
        var column = stackalloc float[64];
        for (var i = 0; i < board.Capacity; i++)
        {
            board.Acquire();
            board.AddModifier(i, speed, new Modifier(ModifierKind.Added, 1f));
        }

        board.BindOutput(speed, column, columnLength: board.Capacity);
        board.Recalculate();
        Assert.Equal(7f, column[board.Capacity - 1]);
    }

    [Fact]
    public void Stride_MultipliesAgainstLength()
    {
        using var board = Board.Create(Schema(), 8);
        var speed = board.Schema.Stat("move_speed");
        var column = stackalloc float[128];
        for (var i = 0; i < 6; i++)
        {
            board.Acquire();
        }

        board.BindOutput(speed, column, stride: 2, columnLength: 9);
        board.AddModifier(5, speed, new Modifier(ModifierKind.Added, 1f));
#if SV_CHECKED
        Assert.Throws<InvalidOperationException>(() => board.Recalculate());
#else
        board.Recalculate();
        Assert.Equal(7f, column[10]);
#endif
    }

    [Fact]
    public void DefaultMinusOne_PreservesLegacySemantics()
    {
        using var board = Board.Create(Schema(), 8);
        var speed = board.Schema.Stat("move_speed");
        var column = stackalloc float[64];
        for (var i = 0; i < 20; i++)
        {
            board.Acquire();
        }

        board.BindOutput(speed, column);
        board.AddModifier(19, speed, new Modifier(ModifierKind.Added, 1f));
        board.Recalculate();
        Assert.Equal(7f, column[19]);
    }

    [Fact]
    public void ZeroLength_NoLiveSlots_NoThrow()
    {
        using var board = Board.Create(Schema(), 8);
        var speed = board.Schema.Stat("move_speed");
        var column = stackalloc float[64];
        board.BindOutput(speed, column, columnLength: 0);
        board.Recalculate();
    }

    [Fact]
    public void BindExternal_AcceptsColumnLength_AndThrowsUnderChecked()
    {
        using var board = Board.Create(Schema(), 8);
        var speed = board.Schema.Stat("move_speed");
        var column = stackalloc float[64];
        board.Acquire();
        board.BindExternal(speed, column, columnLength: 0);
        board.AddModifier(0, speed, new Modifier(ModifierKind.Added, 1f));
#if SV_CHECKED
        Assert.Throws<InvalidOperationException>(() => board.Recalculate());
#else
        board.Recalculate();
        Assert.Equal(7f, column[0]);
#endif
    }

    [Fact]
    public void InvalidLength_RejectedInBothModes()
    {
        using var board = Board.Create(Schema(), 8);
        var speed = board.Schema.Stat("move_speed");
        var column = stackalloc float[64];
        Assert.Throws<ArgumentOutOfRangeException>(() => board.BindOutput(speed, column, columnLength: -2));
        Assert.Throws<ArgumentOutOfRangeException>(() => board.BindExternal(speed, column, columnLength: -2));
    }
}
