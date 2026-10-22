using System;
using System.Runtime.InteropServices;
using Sv;
using Xunit;

public unsafe class BlockTests
{
    static Schema TestSchema() => SchemaBuilder.Create()
        .Stat("power", 10f)
        .Stat("armor", 5f)
        .Derived("attack", 1f, ("power", 2f))
        .Build();

    [Fact]
    public void Spawn_FillsColumnsAndMirrors_NoPairsResolve()
    {
        using var board = Board.Create(TestSchema(), 32);
        var power = board.Schema.Stat("power");
        var attack = board.Schema.Stat("attack");
        var column = stackalloc float[64];
        NativeMemory.Clear(column, 64 * sizeof(float));
        var template = board.Acquire();
        board.AddModifier(template, power, new Modifier(ModifierKind.Added, 2f));
        board.Recalculate();
        board.BindOutput(power, column);
        var mirror = board.MirrorBounds(attack);
        var setsBefore = board.SetCount;
        var slots = stackalloc int[10];

        board.Spawn(template, 10, slots);
        Assert.Equal(11, board.LiveCount);
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(i + 1, slots[i]);
            Assert.Equal(12f, column[slots[i]]);
            Assert.Equal(Clamp.BoundOf(25f), mirror[slots[i]]);
        }

        Assert.Equal(setsBefore, board.SetCount);
        var acquired = board.AcquiredWords(power);
        Assert.Equal(0b111_1111_1110ul, acquired[0]);
        Assert.Equal(1ul, board.ChangedWords(power)[0]);

        board.Recalculate();
        Assert.Equal(0ul, board.ChangedWords(power)[0]);
        Assert.Equal(0ul, board.AcquiredWords(power)[0]);
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(12f, column[slots[i]]);
        }
    }

    [Fact]
    public void SpawnedSibling_ModifierReinternsAlone()
    {
        using var board = Board.Create(TestSchema(), 16);
        var power = board.Schema.Stat("power");
        var template = board.Acquire();
        board.AddModifier(template, power, new Modifier(ModifierKind.Added, 2f));
        board.Recalculate();
        var slots = stackalloc int[3];
        board.Spawn(template, 3, slots);

        board.AddModifier(slots[1], power, new Modifier(ModifierKind.Added, 100f));
        board.Recalculate();
        var column = board.Output(power);
        Assert.Equal(110f, column[slots[1]]);
        Assert.Equal(12f, column[slots[0]]);
        Assert.Equal(12f, column[slots[2]]);
        Assert.Equal(12f, column[template]);
    }

    [Fact]
    public void LastBlockUser_ReleaseFreesSets()
    {
        using var board = Board.Create(TestSchema(), 16);
        var power = board.Schema.Stat("power");
        var template = board.Acquire();
        board.AddModifier(template, power, new Modifier(ModifierKind.Added, 2f));
        board.Recalculate();
        var slots = stackalloc int[3];
        board.Spawn(template, 3, slots);
        Assert.True(board.SetCount > 0);

        board.Release(template);
        board.Release(slots[0]);
        board.Release(slots[2]);
        Assert.True(board.SetCount > 0);

        board.Release(slots[1]);
        Assert.Equal(0, board.SetCount);
    }

    [Fact]
    public void SpawnSaveLoad_IsByteStableAndRestoresBlocks()
    {
        using var board = Board.Create(TestSchema(), 16);
        var power = board.Schema.Stat("power");
        var template = board.Acquire();
        board.AddModifier(template, power, new Modifier(ModifierKind.Added, 2f));
        board.Recalculate();
        var slots = stackalloc int[4];
        board.Spawn(template, 4, slots);
        board.AddModifier(slots[2], power, new Modifier(ModifierKind.Added, 30f));
        board.Recalculate();
        Assert.Equal(40f, board.Output(power)[slots[2]]);
        using var saved = new SvState();
        board.SaveState(saved);
        var before = saved.Bytes.ToArray();

        board.AddModifier(slots[1], power, new Modifier(ModifierKind.Added, 5f));
        board.Recalculate();
        board.LoadState(saved);
        using var resaved = new SvState();
        board.SaveState(resaved);
        Assert.True(before.AsSpan().SequenceEqual(resaved.Bytes), "save after load must be byte-identical with blocks");
        Assert.Equal(40f, board.Output(power)[slots[2]]);
        Assert.Equal(12f, board.Output(power)[slots[1]]);
    }

    [Fact]
    public void IdenticalSequences_WithSpawnChurn_SaveByteIdentically()
    {
        static byte[] Run()
        {
            using var board = Board.Create(TestSchema(), 32);
            var power = board.Schema.Stat("power");
            var template = board.Acquire();
            board.AddModifier(template, power, new Modifier(ModifierKind.Multi, 0.5f));
            board.Recalculate();
            var slots = stackalloc int[6];
            board.Spawn(template, 6, slots);
            board.Release(slots[3]);
            board.Recalculate();
            var slots2 = stackalloc int[2];
            board.Spawn(slots[0], 2, slots2);
            board.AddModifier(slots2[1], power, new Modifier(ModifierKind.Added, 1f));
            board.Recalculate();
            using var state = new SvState();
            board.SaveState(state);
            return state.Bytes.ToArray();
        }

        Assert.True(Run().AsSpan().SequenceEqual(Run()), "identical spawn-churn sequences must save byte-identically");
    }

    [Fact]
    public void Spawn_ThenRenumber_KeepsValuesBitExact()
    {
        using var board = Board.Create(TestSchema(), 16);
        var power = board.Schema.Stat("power");
        var attack = board.Schema.Stat("attack");
        var template = board.Acquire();
        board.AddModifier(template, power, new Modifier(ModifierKind.Added, 2f));
        board.Recalculate();
        var slots = stackalloc int[3];
        board.Spawn(template, 3, slots);
        var powerColumn = board.Output(power);
        var attackColumn = board.Output(attack);

        var map = new int[board.Capacity];
        Array.Fill(map, -1);
        var next = 0;
        int[] live = [template, slots[0], slots[1], slots[2]];
        for (var i = live.Length - 1; i >= 0; i--)
        {
            map[live[i]] = next++;
        }

        board.RenumberSlots(map, live.Length);
        board.Recalculate();
        var newPower = board.Output(power);
        var newAttack = board.Output(attack);
        for (var i = 0; i < live.Length; i++)
        {
            Assert.Equal(powerColumn[live[i]], newPower[i]);
            Assert.Equal(attackColumn[live[i]], newAttack[i]);
        }
    }

    [Fact]
    public void Spawn_ExhaustionThrowsBeforeAnyMutation()
    {
        using var board = Board.Create(TestSchema(), 4);
        Assert.Equal(64, board.Capacity);
        var template = board.Acquire();
        board.Recalculate();
        for (var i = 0; i < 59; i++)
        {
            board.Acquire();
        }

        var slots = stackalloc int[5];
        Assert.Throws<InvalidOperationException>(() => board.Spawn(template, 5, slots));
        Assert.Equal(60, board.LiveCount);

        var ok = stackalloc int[4];
        board.Spawn(template, 4, ok);
        Assert.Equal(64, board.LiveCount);
    }

    [Fact]
    public void Spawn_UnresolvedTemplate_FallsBackToAcquire()
    {
        using var board = Board.Create(TestSchema(), 8);
        var power = board.Schema.Stat("power");
        var template = board.Acquire();
        var slots = stackalloc int[2];
        board.Spawn(template, 2, slots);
        Assert.Equal(3, board.LiveCount);
        Assert.Equal(1, slots[0]);
        Assert.Equal(2, slots[1]);
        Assert.Equal(0b111ul, board.AcquiredWords(power)[0]);

        board.AddModifier(slots[1], power, new Modifier(ModifierKind.Added, 2f));
        board.Recalculate();
        Assert.Equal(12f, board.Output(power)[slots[1]]);
        Assert.Equal(10f, board.Output(power)[slots[0]]);
    }
}
