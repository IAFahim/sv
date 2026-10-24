using System;
using Sv;
using Sv.Tests;
using Xunit;

public unsafe class CurveTests
{
    static readonly float[] table = { 0f, 10f, 18f, 24f, 28f, 30f };

    static Schema Schema() => SchemaBuilder.Create()
        .Stat("power", 1f)
        .Curve("damage", 0f, "power", table, 0f, 25f)
        .Build();

    [Fact]
    public void CurveSource_QuantizesAcrossBuckets_MatchesOracle()
    {
        using var board = Board.Create(Schema(), 8);
        var power = board.Schema.Stat("power");
        var damage = board.Schema.Stat("damage");
        var column = stackalloc float[8];
        board.BindOutput(damage, column);
        var model = new EagerModel(
            new[] { 1f, 0f },
            new (int, float)[][] { Array.Empty<(int, float)>(), new[] { (0, 0f) } },
            new (float[] table, float min, float step)[][] { Array.Empty<(float[] table, float min, float step)>(), new[] { (table, 0f, 25f) } });
        model.Grow(8);
        var adds = new[] { 0f, 24f, 25f, 99f, 124f, 500f, -30f, 3e38f };
        var slots = new int[8];
        for (var i = 0; i < 8; i++)
        {
            slots[i] = board.Acquire();
            model.Acquire(slots[i]);
            if (adds[i] != 0f)
            {
                board.AddModifier(slots[i], power, new Modifier(ModifierKind.Added, adds[i]));
                model.AddModifier(slots[i], 0, 0, adds[i], -1, long.MaxValue);
            }
        }

        board.Recalculate();
        model.Recalculate();
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(model.Value(slots[i], 1), board.Stat(slots[i], damage));
            Assert.Equal(column[slots[i]], board.Stat(slots[i], damage));
        }

        Assert.Equal(0f, column[slots[0]]);
        Assert.Equal(10f, column[slots[1]]);
        Assert.Equal(10f, column[slots[2]]);
        Assert.Equal(28f, column[slots[3]]);
        Assert.Equal(30f, column[slots[4]]);
        Assert.Equal(30f, column[slots[5]]);
        Assert.Equal(0f, column[slots[6]]);
        Assert.Equal(30f, column[slots[7]]);
    }

    [Fact]
    public void CurveSource_SameBucket_SharesFold()
    {
        using var board = Board.Create(Schema(), 4);
        var power = board.Schema.Stat("power");
        var damage = board.Schema.Stat("damage");
        var a = board.Acquire();
        var b = board.Acquire();
        var c = board.Acquire();
        board.AddModifier(a, power, new Modifier(ModifierKind.Added, 30f));
        board.AddModifier(b, power, new Modifier(ModifierKind.Added, 40f));
        board.AddModifier(c, power, new Modifier(ModifierKind.Added, 90f));
        board.Recalculate();
        Assert.Equal(board.Stat(a, damage), board.Stat(b, damage));
        Assert.Equal(24f, board.Stat(c, damage));
        board.AddModifier(a, power, new Modifier(ModifierKind.Added, 60f));
        board.Recalculate();
        Assert.Equal(board.Stat(a, damage), board.Stat(c, damage));
        Assert.Equal(24f, board.Stat(a, damage));
    }

    [Fact]
    public void CurveSource_TracksSourceChanges()
    {
        using var board = Board.Create(Schema(), 2);
        var power = board.Schema.Stat("power");
        var damage = board.Schema.Stat("damage");
        var slot = board.Acquire();
        var handle = board.AddModifier(slot, power, new Modifier(ModifierKind.Added, 60f));
        board.Recalculate();
        Assert.Equal(18f, board.Stat(slot, damage));
        board.RemoveModifier(slot, power, handle);
        board.AddModifier(slot, power, new Modifier(ModifierKind.Added, 10f));
        board.Recalculate();
        Assert.Equal(0f, board.Stat(slot, damage));
    }

    [Fact]
    public void CurveSource_AnchorInterplay()
    {
        using var board = Board.Create(Schema(), 4);
        var power = board.Schema.Stat("power");
        var damage = board.Schema.Stat("damage");
        var slots = new int[4];
        for (var i = 0; i < 4; i++)
        {
            slots[i] = board.Acquire();
        }

        board.AddModifier(slots[2], power, new Modifier(ModifierKind.Added, 80f));
        board.BindSourceAnchor(damage, 0, slots[2]);
        board.Recalculate();
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(24f, board.Stat(slots[i], damage));
        }

        board.AddModifier(slots[2], power, new Modifier(ModifierKind.Added, 60f));
        board.Recalculate();
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(30f, board.Stat(slots[i], damage));
        }
    }

    [Fact]
    public void Curve_Builder_Guards()
    {
        Assert.Throws<ArgumentException>(() => SchemaBuilder.Create().Stat("power", 1f).Curve("damage", 0f, "power", Array.Empty<float>(), 0f, 1f).Build());
        Assert.Throws<ArgumentException>(() => SchemaBuilder.Create().Stat("power", 1f).Curve("damage", 0f, "power", new[] { 1f, 2f }, 0f, 0f).Build());
        Assert.Throws<ArgumentException>(() => SchemaBuilder.Create().Stat("power", 1f).Curve("damage", 0f, "power", new[] { 1f, float.NaN }, 0f, 1f).Build());
        Assert.Throws<ArgumentException>(() => SchemaBuilder.Create().Stat("power", 1f).Curve("damage", 0f, "mana", new[] { 1f }, 0f, 1f).Build());
    }
}
