using System;
using Sv;
using Xunit;

public class BitmapAllocatorTests
{
    static Schema TestSchema() =>
        SchemaBuilder.Create()
            .Intrinsic("mana")
            .Build();

    [Fact]
    public void ReleasedHoles_RefillLowestFirst()
    {
        using var board = Board.Create(TestSchema(), 16);
        for (var i = 0; i < 9; i++)
        {
            board.Acquire();
        }

        board.Release(5);
        board.Release(2);
        Assert.Equal(2, board.Acquire());
        Assert.Equal(5, board.Acquire());
        Assert.Equal(9, board.Acquire());
        Assert.Equal(10, board.LiveCount);
    }

    [Fact]
    public void ScatteredHoles_FillBeforeFreshSlots()
    {
        using var board = Board.Create(TestSchema(), 64);
        for (var i = 0; i < 32; i++)
        {
            board.Acquire();
        }

        int[] holes = [3, 7, 11, 15, 19, 23, 27, 31];
        foreach (var hole in holes)
        {
            board.Release(hole);
        }

        foreach (var hole in holes)
        {
            Assert.Equal(hole, board.Acquire());
        }

        Assert.Equal(32, board.Acquire());
        Assert.Equal(33, board.LiveCount);
    }

    [Fact]
    public void Exhaustion_AtExactlyCapacity_Throws()
    {
        using var board = Board.Create(TestSchema(), 4);
        Assert.Equal(64, board.Capacity);
        for (var i = 0; i < board.Capacity; i++)
        {
            Assert.Equal(i, board.Acquire());
        }

        Assert.Throws<InvalidOperationException>(() => board.Acquire());
    }

    [Fact]
    public void FullReleaseStorm_RefillsFromZero()
    {
        using var board = Board.Create(TestSchema(), 8);
        for (var i = 0; i < board.Capacity; i++)
        {
            board.Acquire();
        }

        for (var i = 0; i < board.Capacity; i++)
        {
            board.Release(i);
        }

        Assert.Equal(0, board.Acquire());
        Assert.Equal(1, board.Acquire());
    }

    [Fact]
    public void SaveLoadWithHoles_KeepsLowestFirstAndBytesStable()
    {
        using var board = Board.Create(TestSchema(), 16);
        for (var i = 0; i < 6; i++)
        {
            board.Acquire();
        }

        board.Release(2);
        board.Release(4);
        using var saved = new SvState();
        board.SaveState(saved);
        var before = saved.Bytes.ToArray();

        board.Acquire();
        board.LoadState(saved);
        using var resaved = new SvState();
        board.SaveState(resaved);
        Assert.True(before.AsSpan().SequenceEqual(resaved.Bytes), "save after load must be byte-identical with holes");

        Assert.Equal(2, board.Acquire());
        Assert.Equal(4, board.Acquire());
        Assert.Equal(6, board.Acquire());
    }
}
