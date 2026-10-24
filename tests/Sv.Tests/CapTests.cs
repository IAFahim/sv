using System;
using Sv;
using Sv.Tests;
using Xunit;

public unsafe class CapTests
{
    static Schema Schema() =>
        SchemaBuilder.Create()
            .Stat("crit", 5f)
            .Stat("damage", 10f)
            .Derived("total", 0f, ("damage", 0.5f))
            .Build();

    static uint Bits(float f) => BitConverter.SingleToUInt32Bits(f);

    [Fact]
    public void Cap_FoldsAfterMulti()
    {
        using var board = Board.Create(Schema(), 4);
        var crit = board.Schema.Stat("crit");
        var column = stackalloc float[4];
        board.BindOutput(crit, column);
        var slot = board.Acquire();
        board.AddModifier(slot, crit, new Modifier(ModifierKind.Multi, 2f));
        board.AddModifier(slot, crit, new Modifier(ModifierKind.Cap, 10f));
        board.Recalculate();
        Assert.Equal(10f, column[slot]);
    }

    [Fact]
    public void Cap_AboveFoldedValue_IsInert()
    {
        using var board = Board.Create(Schema(), 4);
        var crit = board.Schema.Stat("crit");
        var column = stackalloc float[4];
        board.BindOutput(crit, column);
        var slot = board.Acquire();
        board.AddModifier(slot, crit, new Modifier(ModifierKind.Multi, 0.2f));
        board.AddModifier(slot, crit, new Modifier(ModifierKind.Cap, 90f));
        board.Recalculate();
        Assert.Equal(6f, column[slot]);
    }

    [Fact]
    public void TightestCap_Wins()
    {
        using var board = Board.Create(Schema(), 4);
        var crit = board.Schema.Stat("crit");
        var column = stackalloc float[4];
        board.BindOutput(crit, column);
        var slot = board.Acquire();
        board.AddModifier(slot, crit, new Modifier(ModifierKind.Added, 95f));
        board.AddModifier(slot, crit, new Modifier(ModifierKind.Cap, 85f));
        board.AddModifier(slot, crit, new Modifier(ModifierKind.Cap, 75f));
        board.AddModifier(slot, crit, new Modifier(ModifierKind.Cap, 95f));
        board.Recalculate();
        Assert.Equal(75f, column[slot]);
    }

    [Fact]
    public void CapLoadout_RandomPermutations_Converge()
    {
        var rng = new Random(20261106);
        float[] pool = { 0.25f, 0.5f, 2f, -7f, 40f, 12345.678f, -0.25f };
        using var board = Board.Create(Schema(), 16);
        var crit = board.Schema.Stat("crit");
        var column = stackalloc float[16];
        board.BindOutput(crit, column);
        var slots = new int[16];
        var loadout = new (ModifierKind kind, float value)[7];
        for (var i = 0; i < loadout.Length; i++)
        {
            loadout[i] = ((ModifierKind)rng.Next(0, 4), pool[rng.Next(pool.Length)]);
        }

        var order = new int[loadout.Length];
        for (var slot = 0; slot < 16; slot++)
        {
            slots[slot] = board.Acquire();
            for (var i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            for (var i = order.Length - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            for (var i = 0; i < order.Length; i++)
            {
                board.AddModifier(slots[slot], crit, new Modifier(loadout[order[i]].kind, loadout[order[i]].value));
            }
        }

        board.Recalculate();
        for (var slot = 1; slot < 16; slot++)
        {
            Assert.Equal(Bits(column[slots[0]]), Bits(column[slots[slot]]));
        }

        Assert.Equal(3, board.SetCount);
    }

    [Fact]
    public void CapProgram_MatchesEagerOracle()
    {
        using var board = Board.Create(Schema(), 8);
        var crit = board.Schema.Stat("crit");
        var damage = board.Schema.Stat("damage");
        var model = new EagerModel(new[] { 5f, 10f, 0f }, new (int, float)[][] { Array.Empty<(int, float)>(), Array.Empty<(int, float)>(), new[] { (1, 0.5f) } });
        model.Grow(8);
        var slots = new int[8];
        for (var i = 0; i < 8; i++)
        {
            slots[i] = board.Acquire();
            model.Acquire(slots[i]);
        }

        void Round((ModifierKind kind, float value)[] program)
        {
            for (var i = 0; i < slots.Length; i++)
            {
                for (var k = 0; k < program.Length; k++)
                {
                    board.AddModifier(slots[i], crit, new Modifier(program[k].kind, program[k].value));
                    model.AddModifier(slots[i], 0, (byte)program[k].kind, program[k].value, -1, long.MaxValue);
                }
            }

            board.Recalculate();
            model.Recalculate();
            for (var i = 0; i < slots.Length; i++)
            {
                Assert.Equal(model.Value(slots[i], 0), board.Stat(slots[i], crit));
                Assert.Equal(model.Value(slots[i], 2), board.Stat(slots[i], board.Schema.Stat("total")));
            }
        }

        Round(new[] { (ModifierKind.Added, 30f), (ModifierKind.Multi, 0.5f), (ModifierKind.Cap, 40f) });
        Round(new[] { (ModifierKind.Cap, 12.5f) });
        Round(new[] { (ModifierKind.Added, -60f), (ModifierKind.Cap, -50f) });
    }

    [Fact]
    public void Modifier_Kind_AcceptsCap_RejectsUnknown()
    {
        Assert.Throws<ArgumentException>(() => new Modifier((ModifierKind)99, 1f));
        var cap = new Modifier(ModifierKind.Cap, 75f);
        Assert.Equal(ModifierKind.Cap, cap.Kind);
        Assert.Throws<ArgumentException>(() => new Modifier(ModifierKind.Cap, float.NaN));
    }
}
