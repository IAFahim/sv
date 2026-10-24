using System;
using Sv;
using Sv.Tests;
using Xunit;

public class IndirectStatTests
{
    static Schema AuraSchema() => SchemaBuilder.Create()
        .Stat("aura", 0f)
        .Derived("damage", 10f, ("aura", 0.5f))
        .Build();

    [Fact]
    public unsafe void GlobalBuff_OneAnchorModifier_ReachesEverySlot()
    {
        using var board = Board.Create(AuraSchema(), 8);
        var aura = board.Schema.Stat("aura");
        var damage = board.Schema.Stat("damage");
        var column = stackalloc float[8];
        board.BindOutput(damage, column);
        var slots = new int[8];
        for (var i = 0; i < 8; i++)
        {
            slots[i] = board.Acquire();
        }

        board.BindSourceAnchor(damage, 0, slots[0]);
        board.AddModifier(slots[0], aura, new Modifier(ModifierKind.Added, 10f));
        board.Recalculate();
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(15f, column[slots[i]]);
        }

        Assert.Equal(3, board.SetCount);
        board.AddModifier(slots[0], aura, new Modifier(ModifierKind.Added, 5f));
        board.Recalculate();
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(17.5f, column[slots[i]]);
        }
    }

    [Fact]
    public unsafe void AnchorAtAnySlot_IsOrderIndependent()
    {
        var first = new float[8];
        var last = new float[8];
        var column = stackalloc float[8];
        for (var pass = 0; pass < 2; pass++)
        {
            using var board = Board.Create(AuraSchema(), 8);
            var aura = board.Schema.Stat("aura");
            var damage = board.Schema.Stat("damage");
            board.BindOutput(damage, column);
            var slots = new int[8];
            for (var i = 0; i < 8; i++)
            {
                slots[i] = board.Acquire();
            }

            board.BindSourceAnchor(damage, 0, slots[pass == 0 ? 0 : 7]);
            board.AddModifier(slots[pass == 0 ? 0 : 7], aura, new Modifier(ModifierKind.Added, 4f));
            board.AddModifier(slots[1], aura, new Modifier(ModifierKind.Added, 100f));
            board.Recalculate();
            for (var i = 0; i < 8; i++)
            {
                (pass == 0 ? first : last)[slots[i]] = column[slots[i]];
            }
        }

        for (var slot = 0; slot < 8; slot++)
        {
            Assert.Equal(first[slot], last[slot]);
        }
    }

    [Fact]
    public unsafe void AnchoredProgram_MatchesEagerOracle()
    {
        using var board = Board.Create(AuraSchema(), 8);
        var aura = board.Schema.Stat("aura");
        var damage = board.Schema.Stat("damage");
        var model = new EagerModel(new[] { 0f, 10f }, new (int, float)[][] { Array.Empty<(int, float)>(), new[] { (0, 0.5f) } });
        model.Grow(8);
        var slots = new int[8];
        for (var i = 0; i < 8; i++)
        {
            slots[i] = board.Acquire();
            model.Acquire(slots[i]);
        }

        board.BindSourceAnchor(damage, 0, slots[3]);
        model.BindSourceAnchor(1, 0, slots[3]);
        board.AddModifier(slots[3], aura, new Modifier(ModifierKind.Added, 7f));
        model.AddModifier(slots[3], 0, 0, 7f, -1, long.MaxValue);
        board.Recalculate();
        model.Recalculate();
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(model.Value(slots[i], 1), board.Stat(slots[i], damage));
        }

        board.AddModifier(slots[3], aura, new Modifier(ModifierKind.Added, 3f));
        model.AddModifier(slots[3], 0, 0, 3f, -1, long.MaxValue);
        board.Recalculate();
        model.Recalculate();
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(model.Value(slots[i], 1), board.Stat(slots[i], damage));
        }

        board.Release(slots[3]);
        model.Release(slots[3]);
        board.Recalculate();
        model.Recalculate();
        for (var i = 0; i < 8; i++)
        {
            if (slots[i] == slots[3])
            {
                continue;
            }

            Assert.Equal(model.Value(slots[i], 1), board.Stat(slots[i], damage));
        }
    }

    [Fact]
    public unsafe void ReleasedAnchor_RevertsToPerSlotReads()
    {
        using var board = Board.Create(AuraSchema(), 4);
        var aura = board.Schema.Stat("aura");
        var damage = board.Schema.Stat("damage");
        var column = stackalloc float[4];
        board.BindOutput(damage, column);
        var slots = new int[4];
        for (var i = 0; i < 4; i++)
        {
            slots[i] = board.Acquire();
        }

        board.BindSourceAnchor(damage, 0, slots[0]);
        board.AddModifier(slots[0], aura, new Modifier(ModifierKind.Added, 10f));
        board.Recalculate();
        board.Release(slots[0]);
        board.Recalculate();
        Assert.Equal(10f, column[slots[1]]);
        Assert.Equal(10f, column[slots[2]]);
    }

    [Fact]
    public unsafe void Renumber_ParksAnchors_BackToPerSlot()
    {
        using var board = Board.Create(AuraSchema(), 4);
        var aura = board.Schema.Stat("aura");
        var damage = board.Schema.Stat("damage");
        var column = stackalloc float[4];
        board.BindOutput(damage, column);
        var slots = new int[4];
        for (var i = 0; i < 4; i++)
        {
            slots[i] = board.Acquire();
        }

        board.BindSourceAnchor(damage, 0, slots[0]);
        board.AddModifier(slots[0], aura, new Modifier(ModifierKind.Added, 10f));
        board.Recalculate();
        var map = new int[board.Capacity];
        for (var i = 0; i < board.Capacity; i++)
        {
            map[i] = -1;
        }

        for (var i = 0; i < 4; i++)
        {
            map[slots[i]] = slots[i];
        }

        board.RenumberSlots(map, board.LiveCount);
        board.BindOutput(damage, column);
        board.Recalculate();
        for (var i = 1; i < 4; i++)
        {
            Assert.Equal(10f, column[slots[i]]);
        }

        Assert.Equal(-1, board.SourceAnchorOf(damage, 0));
    }

    [Fact]
    public unsafe void BindSourceAnchor_Guards()
    {
        using var board = Board.Create(AuraSchema(), 4);
        var aura = board.Schema.Stat("aura");
        var damage = board.Schema.Stat("damage");
        var slot = board.Acquire();
        Assert.Throws<ArgumentOutOfRangeException>(() => board.BindSourceAnchor(damage, 1, slot));
        Assert.Throws<ArgumentOutOfRangeException>(() => board.BindSourceAnchor(damage, -1, slot));
        Assert.Throws<ArgumentException>(() => board.BindSourceAnchor(damage, 0, slot + 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => board.BindSourceAnchor(aura, 0, slot));
        board.BindSourceAnchor(damage, 0, slot);
        board.ClearSourceAnchor(damage, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => board.ClearSourceAnchor(damage, 5));
    }
}
