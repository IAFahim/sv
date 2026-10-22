using System;
using Sv;
using Xunit;

public unsafe class DirtyMaskTests
{
    static Schema Schema() => SchemaBuilder.Create()
        .Stat("source", 0f)
        .Derived("derived", 3f, ("source", 1f))
        .Build();

    [Fact]
    public void DerivedMarkedBeforeSource_NoChangedBitWhenFinalEqualsPrevious()
    {
        using var board = Board.Create(Schema(), 4);
        var source = board.Schema.Stat("source");
        var derived = board.Schema.Stat("derived");
        var slot = board.Acquire();
        var buff = board.AddModifier(slot, source, new Modifier(ModifierKind.Added, 5f));
        board.Recalculate();
        Assert.Equal(8f, board.Output(derived)[slot]);

        board.AddModifier(slot, derived, new Modifier(ModifierKind.Added, 5f));
        board.RemoveModifier(slot, source, buff);
        board.Recalculate();

        Assert.Equal(8f, board.Output(derived)[slot]);
        var words = board.ChangedWords(derived);
        Assert.Equal(0ul, words[slot >> 6] & (1ul << (slot & 63)));
    }

    [Fact]
    public void SourceAndDerivedDirtyFrame_ResolvesOnce_NoTransientLiveSet()
    {
        using var board = Board.Create(Schema(), 4);
        var source = board.Schema.Stat("source");
        var derived = board.Schema.Stat("derived");
        var slot = board.Acquire();
        board.Recalculate();
        var setsAfterFirst = board.SetCount;
        Assert.True(setsAfterFirst >= 2);

        board.AddModifier(slot, source, new Modifier(ModifierKind.Added, 5f));
        board.Recalculate();
        Assert.Equal(5f, board.Output(source)[slot]);
        Assert.Equal(8f, board.Output(derived)[slot]);

        board.AddModifier(slot, derived, new Modifier(ModifierKind.Added, 1f));
        board.AddModifier(slot, source, new Modifier(ModifierKind.Added, 2f));
        board.Recalculate();
        Assert.Equal(7f, board.Output(source)[slot]);
        Assert.Equal(11f, board.Output(derived)[slot]);
        var settled = board.SetCount;

        board.Recalculate();
        Assert.Equal(7f, board.Output(source)[slot]);
        Assert.Equal(11f, board.Output(derived)[slot]);
        Assert.Equal(settled, board.SetCount);
        Assert.Equal(0ul, board.ChangedWords(source)[slot >> 6]);
        Assert.Equal(0ul, board.ChangedWords(derived)[slot >> 6]);
    }

    [Fact]
    public void Recalculate_TwiceIsOne_EvenMidChain()
    {
        using var board = Board.Create(Schema(), 4);
        var source = board.Schema.Stat("source");
        var derived = board.Schema.Stat("derived");
        var slot = board.Acquire();
        board.AddModifier(slot, source, new Modifier(ModifierKind.Added, 4f));
        board.Recalculate();

        board.Recalculate();
        Assert.Equal(0ul, board.ChangedWords(source)[slot >> 6]);
        Assert.Equal(0ul, board.ChangedWords(derived)[slot >> 6]);
    }

    [Fact]
    public void SaveLoad_MidFrameKeepsPendingPairs()
    {
        using var board = Board.Create(Schema(), 4);
        var source = board.Schema.Stat("source");
        var slot = board.Acquire();
        board.Recalculate();
        Assert.Equal(0f, board.Output(source)[slot]);

        board.AddModifier(slot, source, new Modifier(ModifierKind.Added, 9f));
        using var saved = new SvState();
        board.SaveState(saved);

        using var fresh = Board.Create(Schema(), 4);
        fresh.LoadState(saved);
        fresh.Recalculate();
        Assert.Equal(9f, fresh.Output(source)[slot]);
        Assert.Equal(0f, board.Output(source)[slot]);
    }
}
