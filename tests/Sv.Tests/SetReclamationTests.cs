using System;
using Sv;
using Xunit;

public unsafe class SetReclamationTests
{
    static Schema Schema() => SchemaBuilder.Create()
        .Stat("power", 10f)
        .Build();

    [Fact]
    public void Table_EmptySet_IsFreed_ReuseIsGatedByGeneration()
    {
        using var memory = new NativeArena();
        using var table = new SetTable(memory);
        var entries = stackalloc StatEntry[1];
        entries[0] = new StatEntry(ModifierKind.Multi, 1.5f);

        var a = table.Resolve(0, entries, 1);
        table.AddRef(a);
        Assert.Equal(1, table.LiveSetCount);

        table.ReleaseRef(a);
        Assert.Equal(0, table.LiveSetCount);

        var b = table.Resolve(0, entries, 1);
        Assert.NotEqual(a, b);
        Assert.Equal(1, table.LiveSetCount);
        Assert.Equal(b, table.Resolve(0, entries, 1));

        table.AddRef(b);
        table.ReleaseRef(b);
        table.Tick();
        var c = table.Resolve(0, entries, 1);
        Assert.Equal(b, c);
        Assert.Equal(1, table.LiveSetCount);
    }

    [Fact]
    public void Table_Deletion_KeepsNeighborSetsResolvable()
    {
        using var memory = new NativeArena();
        using var table = new SetTable(memory);
        var entries = stackalloc StatEntry[1];
        var ids = new int[8];
        for (var i = 0; i < 8; i++)
        {
            entries[0] = new StatEntry(ModifierKind.Multi, 1f + i);
            ids[i] = table.Resolve(0, entries, 1);
            table.AddRef(ids[i]);
        }

        Assert.Equal(8, table.LiveSetCount);
        for (var i = 1; i < 8; i += 2)
        {
            table.ReleaseRef(ids[i]);
        }

        Assert.Equal(4, table.LiveSetCount);
        for (var i = 0; i < 8; i += 2)
        {
            entries[0] = new StatEntry(ModifierKind.Multi, 1f + i);
            Assert.Equal(ids[i], table.Resolve(0, entries, 1));
        }

        table.Tick();
        for (var k = 7; k >= 1; k -= 2)
        {
            entries[0] = new StatEntry(ModifierKind.Multi, 1f + k);
            var revived = table.Resolve(0, entries, 1);
            Assert.Equal(ids[k], revived);
            table.AddRef(revived);
        }

        Assert.Equal(8, table.LiveSetCount);
        for (var i = 0; i < 8; i++)
        {
            entries[0] = new StatEntry(ModifierKind.Multi, 1f + i);
            Assert.Equal(ids[i], table.Resolve(0, entries, 1));
        }
    }

    [Fact]
    public void BoardChurn_ReclaimsSetsAndReusesMemory()
    {
        using var board = Board.Create(Schema(), 8);
        var power = board.Schema.Stat("power");
        var column = stackalloc float[8];
        board.BindOutput(power, column);
        for (var slot = 0; slot < 8; slot++)
        {
            board.Acquire();
        }

        long bytesAtEighthRound = 0;
        for (var round = 0L; round < 40; round++)
        {
            for (var slot = 0; slot < 8; slot++)
            {
                board.AddModifier(slot, power, new Modifier(ModifierKind.Added, slot % 4), 0, round + 1);
            }

            board.Recalculate();
            Assert.Equal(4, board.SetCount);
            for (var slot = 0; slot < 8; slot++)
            {
                Assert.Equal(10f + slot % 4, column[slot]);
            }

            Assert.Equal(8, board.ExpireModifiers(round + 1));
            board.Recalculate();
            Assert.Equal(1, board.SetCount);
            for (var slot = 0; slot < 8; slot++)
            {
                Assert.Equal(10f, column[slot]);
            }

            if (round == 7)
            {
                bytesAtEighthRound = board.MemoryBytes;
            }
        }

        Assert.True(bytesAtEighthRound > 0, "churn must pass the warm-up rounds");
        Assert.Equal(bytesAtEighthRound, board.MemoryBytes);
    }

    [Fact]
    public void BoardRelease_FreesTheLastReferencedSet()
    {
        using var board = Board.Create(Schema(), 4);
        var power = board.Schema.Stat("power");
        var a = board.Acquire();
        var b = board.Acquire();
        board.AddModifier(a, power, new Modifier(ModifierKind.Added, 2f));
        board.AddModifier(b, power, new Modifier(ModifierKind.Added, 2f));
        board.Recalculate();
        Assert.Equal(1, board.SetCount);

        board.Release(a);
        Assert.Equal(1, board.SetCount);

        board.Release(b);
        Assert.Equal(0, board.SetCount);

        var c = board.Acquire();
        board.AddModifier(c, power, new Modifier(ModifierKind.Added, 2f));
        board.Recalculate();
        Assert.Equal(1, board.SetCount);
        Assert.Equal(12f, board.Stat(c, power));
    }

    [Fact]
    public void SaveLoad_AcrossChurn_IsByteStable()
    {
        using var board = Board.Create(Schema(), 8);
        var power = board.Schema.Stat("power");
        for (var slot = 0; slot < 8; slot++)
        {
            board.Acquire();
        }

        for (var round = 0L; round < 12; round++)
        {
            for (var slot = 0; slot < 8; slot++)
            {
                board.AddModifier(slot, power, new Modifier(ModifierKind.Added, slot % 4), 0, round + 1);
            }

            board.Recalculate();
            board.ExpireModifiers(round + 1);
            board.Recalculate();
        }

        for (var slot = 0; slot < 8; slot++)
        {
            board.AddModifier(slot, power, new Modifier(ModifierKind.Added, slot % 4), 0, 100);
        }

        board.Recalculate();
        using var saved = new SvState();
        board.SaveState(saved);
        var before = saved.Bytes.ToArray();
        Assert.Equal(4, board.SetCount);

        Assert.Equal(8, board.ExpireModifiers(100));
        board.Recalculate();
        Assert.Equal(1, board.SetCount);

        board.LoadState(saved);
        Assert.Equal(4, board.SetCount);
        for (var slot = 0; slot < 8; slot++)
        {
            Assert.Equal(10f + slot % 4, board.Stat(slot, power));
        }

        using var resaved = new SvState();
        board.SaveState(resaved);
        Assert.True(before.AsSpan().SequenceEqual(resaved.Bytes), "save after load must be byte-identical");
    }

    [Fact]
    public void SameChurn_TwoBoards_ProduceIdenticalSaves()
    {
        var first = RunChurnAndSave();
        var second = RunChurnAndSave();
        Assert.True(first.AsSpan().SequenceEqual(second), "identical churn programs must serialize identically");
    }

    static byte[] RunChurnAndSave()
    {
        using var board = Board.Create(Schema(), 8);
        var power = board.Schema.Stat("power");
        for (var slot = 0; slot < 8; slot++)
        {
            board.Acquire();
        }

        for (var round = 0L; round < 15; round++)
        {
            for (var slot = 0; slot < 8; slot++)
            {
                var count = (int)(round + slot) % 3;
                for (var i = 0; i < count; i++)
                {
                    board.AddModifier(slot, power, new Modifier(ModifierKind.Added, (round + i) % 5), 0, round + 1);
                }
            }

            board.Recalculate();
            board.ExpireModifiers(round + 1);
            board.Recalculate();
            for (var slot = 0; slot < 8; slot++)
            {
                board.AddModifier(slot, power, new Modifier(ModifierKind.Multi, 1f + slot / 4));
            }

            board.Recalculate();
        }

        using var state = new SvState();
        board.SaveState(state);
        return state.Bytes.ToArray();
    }
}
