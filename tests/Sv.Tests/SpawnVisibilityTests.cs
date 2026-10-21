using System;
using Sv;
using Xunit;

public unsafe class SpawnVisibilityTests
{
    static Schema TestSchema() => SchemaBuilder.Create()
        .Stat("power", 10f)
        .Intrinsic("hp")
        .Build();

    [Fact]
    public void Release_ZeroesRows_NewOccupantSeesZeroBeforeRecalculate()
    {
        using var board = Board.Create(TestSchema(), 64);
        var stat = board.Schema.Stat("power");
        var slot = board.Acquire();
        board.AddModifier(slot, stat, new Modifier(ModifierKind.Added, 90f));
        board.Recalculate();
        Assert.Equal(100f, board.Output(stat)[slot]);

        board.Release(slot);
        var respawned = board.Acquire();

        Assert.Equal(slot, respawned);
        Assert.Equal(0f, board.Output(stat)[respawned]);
    }

    [Fact]
    public void Acquire_MarksAcquiredNotChanged()
    {
        using var board = Board.Create(TestSchema(), 64);
        var stat = board.Schema.Stat("power");
        var slot = board.Acquire();

        var changedWords = board.ChangedWords(stat);
        var acquiredWords = board.AcquiredWords(stat);
        Assert.Equal(0ul, changedWords[slot >> 6]);
        Assert.Equal(1ul << (slot & 63), acquiredWords[slot >> 6]);

        board.Recalculate();

        Assert.Equal(1ul << (slot & 63), changedWords[slot >> 6]);
        Assert.Equal(0ul, acquiredWords[slot >> 6]);

        board.Recalculate();

        Assert.Equal(0ul, changedWords[slot >> 6]);
    }

    [Fact]
    public void SpawnWithBaseZeroStat_IsVisibleInAcquiredWordsOnly()
    {
        var schema = SchemaBuilder.Create()
            .Stat("bonus_armor", 0f)
            .Stat("power", 10f)
            .Intrinsic("hp")
            .Build();
        using var board = Board.Create(schema, 64);
        var bonus = schema.Stat("bonus_armor");
        var power = schema.Stat("power");
        var slot = board.Acquire();

        Assert.Equal(1ul << (slot & 63), board.AcquiredWords(bonus)[slot >> 6]);
        Assert.Equal(0ul, board.ChangedWords(bonus)[slot >> 6]);

        board.Recalculate();

        Assert.Equal(0ul, board.AcquiredWords(bonus)[slot >> 6]);
        Assert.Equal(0ul, board.ChangedWords(bonus)[slot >> 6]);
        Assert.Equal(0f, board.Output(bonus)[slot]);
        Assert.Equal(1ul << (slot & 63), board.ChangedWords(power)[slot >> 6]);
    }

    [Fact]
    public void AcquiredWords_ClearPerWord_NotWholeMask()
    {
        using var board = Board.Create(TestSchema(), 128);
        var stat = board.Schema.Stat("power");
        var slotA = board.Acquire();
        board.Recalculate();
        var slotB = board.Acquire();

        var acquiredB = board.AcquiredWords(stat);
        Assert.Equal(0ul, acquiredB[slotA >> 6] & (1ul << (slotA & 63)));
        Assert.Equal(1ul << (slotB & 63), acquiredB[slotB >> 6]);
    }

    [Fact]
    public void Release_ZeroesMirrorRows()
    {
        using var board = Board.Create(TestSchema(), 64);
        var stat = board.Schema.Stat("power");
        var slot = board.Acquire();
        board.AddModifier(slot, stat, new Modifier(ModifierKind.Added, 90f));
        board.Recalculate();
        var mirror = board.MirrorBounds(stat);
        Assert.Equal(100, mirror[slot]);

        board.Release(slot);

        Assert.Equal(0, mirror[slot]);
    }

    [Fact]
    public void RenumberSlots_ClearsChangedMasks()
    {
        using var board = Board.Create(TestSchema(), 128);
        var stat = board.Schema.Stat("power");
        var slotA = board.Acquire();
        var slotB = board.Acquire();
        board.AddModifier(slotA, stat, new Modifier(ModifierKind.Added, 5f));
        board.Recalculate();
        var words = board.ChangedWords(stat);
        Assert.NotEqual(0ul, words[slotA >> 6]);

        var oldToNew = new int[board.Capacity];
        for (var i = 0; i < board.Capacity; i++)
        {
            oldToNew[i] = -1;
        }

        oldToNew[slotA] = 0;
        oldToNew[slotB] = 1;
        board.RenumberSlots(oldToNew, board.LiveCount);

        for (var w = 0; w < board.LiveWordCount; w++)
        {
            Assert.Equal(0ul, board.ChangedWords(stat)[w]);
        }
    }
}
