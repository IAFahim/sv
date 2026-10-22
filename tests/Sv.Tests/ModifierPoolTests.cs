using System;
using Sv;
using Xunit;

public unsafe class ModifierPoolTests
{
    static Schema Schema() => SchemaBuilder.Create()
        .Stat("power", 10f)
        .Build();

    [Fact]
    public void FirstModifiersOnAcquiredSlots_AllocateNoNewBlocks()
    {
        using var board = Board.Create(Schema(), 16);
        var power = board.Schema.Stat("power");
        var slots = new int[8];
        for (var i = 0; i < 8; i++)
        {
            slots[i] = board.Acquire();
        }

        board.Recalculate();
        var before = board.MemoryBytes;
        for (var i = 0; i < 8; i++)
        {
            board.AddModifier(slots[i], power, new Modifier(ModifierKind.Added, i + 1f));
        }

        Assert.Equal(before, board.MemoryBytes);
    }

    [Fact]
    public void IdenticalOpSequences_SaveByteIdentically()
    {
        static SvState Run()
        {
            using var board = Board.Create(Schema(), 8);
            var power = board.Schema.Stat("power");
            var a = board.Acquire();
            var b = board.Acquire();
            var first = board.AddModifier(a, power, new Modifier(ModifierKind.Added, 2f));
            board.AddModifier(a, power, new Modifier(ModifierKind.Multi, 0.5f));
            board.AddModifier(b, power, new Modifier(ModifierKind.Added, 5f));
            board.Recalculate();
            board.RemoveModifier(a, power, first);
            board.AddModifier(b, power, new Modifier(ModifierKind.Additive, 0.2f));
            board.Recalculate();
            var state = new SvState();
            board.SaveState(state);
            return state;
        }

        using var one = Run();
        using var two = Run();
        Assert.True(one.Bytes.SequenceEqual(two.Bytes), "identical op sequences must save byte-identically (pool reuse is deterministic)");
    }

    [Fact]
    public void Release_FreesTheSlotsRecords_ExpirySkipsThem()
    {
        using var board = Board.Create(Schema(), 8);
        var power = board.Schema.Stat("power");
        var a = board.Acquire();
        var b = board.Acquire();
        for (var i = 0; i < 4; i++)
        {
            board.AddModifier(a, power, new Modifier(ModifierKind.Added, i + 1f), 0, 100 + i);
            board.AddModifier(b, power, new Modifier(ModifierKind.Added, i + 1f), 0, 100 + i);
        }

        Assert.Equal(8, board.PendingExpiries);
        board.Release(a);
        Assert.Equal(4, board.ExpireModifiers(long.MaxValue));
        Assert.Equal(0, board.PendingExpiries);
    }

    [Fact]
    public void SaveLoad_MidFrameRestoresThePool()
    {
        using var board = Board.Create(Schema(), 8);
        var power = board.Schema.Stat("power");
        var slot = board.Acquire();
        var first = board.AddModifier(slot, power, new Modifier(ModifierKind.Added, 2f));
        board.AddModifier(slot, power, new Modifier(ModifierKind.Added, 30f));
        board.Recalculate();
        Assert.Equal(42f, board.Output(power)[slot]);

        board.RemoveModifier(slot, power, first);
        using var saved = new SvState();
        board.SaveState(saved);

        using var fresh = Board.Create(Schema(), 8);
        fresh.LoadState(saved);
        fresh.Recalculate();
        Assert.Equal(40f, fresh.Output(power)[slot]);
    }
}
