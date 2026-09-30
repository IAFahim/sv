using Sv;
using Xunit;

public class SlotTests
{
    static Schema TestSchema() =>
        SchemaBuilder.Create()
            .Intrinsic("mana", 0, 100)
            .Build();

    [Fact]
    public void InitialAcquireOrder_IsAscending()
    {
        using var board = Board.Create(TestSchema(), 4);
        Assert.Equal(0, board.Acquire());
        Assert.Equal(1, board.Acquire());
        Assert.Equal(2, board.Acquire());
        Assert.Equal(3, board.Acquire());
        Assert.Equal(4, board.LiveCount);
    }

    [Fact]
    public void Release_IsImmediateLifoReuse()
    {
        using var board = Board.Create(TestSchema(), 4);
        board.Acquire();
        var b = board.Acquire();
        board.Acquire();

        board.Release(b);
        Assert.Equal(b, board.Acquire());
        Assert.Equal(3, board.LiveCount);
    }

    [Fact]
    public void Exhaustion_Throws()
    {
        using var board = Board.Create(TestSchema(), 1);
        for (var i = 0; i < board.Capacity; i++)
        {
            board.Acquire();
        }

        Assert.Throws<InvalidOperationException>(() => board.Acquire());
    }

    [Fact]
    public void DoubleRelease_Throws()
    {
        using var board = Board.Create(TestSchema(), 2);
        var slot = board.Acquire();
        board.Release(slot);
        Assert.Throws<ArgumentException>(() => board.Release(slot));
    }

    [Fact]
    public void ReleaseOutOfRange_Throws()
    {
        using var board = Board.Create(TestSchema(), 2);
        Assert.Throws<ArgumentException>(() => board.Release(-1));
        Assert.Throws<ArgumentException>(() => board.Release(2));
    }

    [Fact]
    public void Create_RejectsNonPositiveCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Board.Create(TestSchema(), 0));
    }

    [Fact]
    public void Acquire_AfterDispose_Throws()
    {
        var board = Board.Create(TestSchema(), 2);
        board.Dispose();
        Assert.Throws<ObjectDisposedException>(() => board.Acquire());
    }
}

public class SchemaTests
{
    [Fact]
    public void IntrinsicIds_AreDenseAndLookupByName()
    {
        var schema = SchemaBuilder.Create()
            .Intrinsic("mana", 0, 100)
            .Intrinsic("charges")
            .Build();

        Assert.Equal(0, schema.Intrinsic("mana").Index);
        Assert.Equal(1, schema.Intrinsic("charges").Index);
        Assert.True(schema.TryIntrinsic("charges", out var id));
        Assert.Equal(1, id.Index);
        Assert.False(schema.TryIntrinsic("unknown", out _));
        Assert.Throws<KeyNotFoundException>(() => schema.Intrinsic("unknown"));
    }

    [Fact]
    public void DuplicateNames_RejectedAtBuild()
    {
        var builder = SchemaBuilder.Create()
            .Intrinsic("mana", 0, 100)
            .Intrinsic("mana");
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void InvertedBounds_RejectedAtDeclaration()
    {
        Assert.Throws<ArgumentException>(() => SchemaBuilder.Create().Intrinsic("mana", 10, 5));
    }

    [Fact]
    public void EmptySchema_RejectedAtBuild()
    {
        Assert.Throws<InvalidOperationException>(() => SchemaBuilder.Create().Build());
    }

    [Fact]
    public void EmptyName_Rejected()
    {
        Assert.Throws<ArgumentException>(() => SchemaBuilder.Create().Intrinsic(""));
    }
}
