using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sv;
using Xunit;

public unsafe class StatsTests
{
    static Schema Schema() =>
        SchemaBuilder.Create()
            .Stat("move_speed", 6f)
            .Stat("max_mana", 100f)
            .Intrinsic("mana", 0, 1000)
            .Build();

    static uint Bits(float f) => BitConverter.SingleToUInt32Bits(f);

    static float OracleFold(float @base, List<(ModifierKind kind, float value)> canonical)
    {
        var value = @base;
        foreach (var (kind, v) in canonical)
        {
            if (kind == ModifierKind.Added)
            {
                value = StrictFloat.Add(value, v);
            }
        }

        var additive = 1f;
        foreach (var (kind, v) in canonical)
        {
            if (kind == ModifierKind.Additive)
            {
                additive = StrictFloat.Add(additive, v);
            }
        }

        value = StrictFloat.Mul(value, additive);
        foreach (var (kind, v) in canonical)
        {
            if (kind == ModifierKind.Multi)
            {
                value = StrictFloat.Mul(value, StrictFloat.Add(1f, v));
            }
        }

        return value;
    }

    [Fact]
    public void InsertionOrder_IsCanonical_AndChangesBits()
    {
        using var board = Board.Create(Schema(), 4);
        var speed = board.Schema.Stat("move_speed");
        var column = stackalloc float[4];
        board.BindOutput(speed, column);

        var a = board.Acquire();
        var b = board.Acquire();
        board.AddModifier(a, speed, new Modifier(ModifierKind.Added, 1e8f));
        board.AddModifier(a, speed, new Modifier(ModifierKind.Added, -1e8f));
        board.AddModifier(a, speed, new Modifier(ModifierKind.Added, 1f));

        board.AddModifier(b, speed, new Modifier(ModifierKind.Added, 1f));
        board.AddModifier(b, speed, new Modifier(ModifierKind.Added, 1e8f));
        board.AddModifier(b, speed, new Modifier(ModifierKind.Added, -1e8f));

        board.Recalculate();

        Assert.Equal(9f, column[a]);
        Assert.Equal(8f, column[b]);
        Assert.NotEqual(Bits(column[a]), Bits(column[b]));
    }

    [Fact]
    public void KindOrder_AddedThenAdditiveThenMulti()
    {
        using var board = Board.Create(Schema(), 2);
        var speed = board.Schema.Stat("move_speed");
        var column = stackalloc float[2];
        board.BindOutput(speed, column);
        var slot = board.Acquire();

        board.AddModifier(slot, speed, new Modifier(ModifierKind.Multi, 0.5f));
        board.AddModifier(slot, speed, new Modifier(ModifierKind.Added, 5f));
        board.AddModifier(slot, speed, new Modifier(ModifierKind.Additive, 0.5f));
        board.Recalculate();

        Assert.Equal(24.75f, column[slot]);
    }

    [Fact]
    public void SameContent_SameOrder_SharesOneSet_SameBits()
    {
        using var board = Board.Create(Schema(), 4);
        var speed = board.Schema.Stat("move_speed");
        var column = stackalloc float[4];
        board.BindOutput(speed, column);

        for (var i = 0; i < 4; i++)
        {
            var slot = board.Acquire();
            board.AddModifier(slot, speed, new Modifier(ModifierKind.Added, 3f));
            board.AddModifier(slot, speed, new Modifier(ModifierKind.Multi, 0.25f));
        }

        board.Recalculate();

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(Bits((6f + 3f) * 1.25f), Bits(column[i]));
        }

        Assert.Equal(2, board.SetCount);
    }

    [Fact]
    public void DifferentOrder_DistinctSets()
    {
        using var board = Board.Create(Schema(), 2);
        var speed = board.Schema.Stat("move_speed");
        var column = stackalloc float[2];
        board.BindOutput(speed, column);

        var a = board.Acquire();
        var b = board.Acquire();
        board.AddModifier(a, speed, new Modifier(ModifierKind.Added, 1e8f));
        board.AddModifier(a, speed, new Modifier(ModifierKind.Added, -1e8f));
        board.AddModifier(b, speed, new Modifier(ModifierKind.Added, -1e8f));
        board.AddModifier(b, speed, new Modifier(ModifierKind.Added, 1e8f));
        board.Recalculate();

        Assert.Equal(3, board.SetCount);
        Assert.Equal(8f, column[a]);
        Assert.Equal(8f, column[b]);
    }

    [Fact]
    public void Broadcast_RespectsStride()
    {
        using var board = Board.Create(Schema(), 2);
        var speed = board.Schema.Stat("move_speed");
        var column = stackalloc float[8];
        board.BindOutput(speed, column, 3);
        var slot = board.Acquire();
        board.AddModifier(slot, speed, new Modifier(ModifierKind.Added, 4f));
        board.Recalculate();
        Assert.Equal(10f, column[slot * 3]);
        Assert.Equal(0f, column[1]);
    }

    [Fact]
    public void RemoveModifier_ReturnsToBase_AndBroadcastsIt()
    {
        using var board = Board.Create(Schema(), 2);
        var speed = board.Schema.Stat("move_speed");
        var column = stackalloc float[2];
        board.BindOutput(speed, column);
        var slot = board.Acquire();
        var handle = board.AddModifier(slot, speed, new Modifier(ModifierKind.Added, 100f));
        board.Recalculate();
        Assert.Equal(106f, column[slot]);

        board.RemoveModifier(slot, speed, handle);
        board.Recalculate();
        Assert.Equal(6f, column[slot]);
    }

    [Fact]
    public void UnboundStat_RecalculatesWithoutBroadcast_ToolingReadStillFolds()
    {
        using var board = Board.Create(Schema(), 2);
        var mana = board.Schema.Stat("max_mana");
        var slot = board.Acquire();
        board.AddModifier(slot, mana, new Modifier(ModifierKind.Multi, 0.5f));
        board.Recalculate();
        Assert.Equal(150f, board.Stat(slot, mana));
    }

    [Fact]
    public void ClampStatMax_ResolvesBoundColumn_TruncatesFloat()
    {
        using var board = Board.Create(Schema(), 2);
        var maxMana = board.Schema.Stat("max_mana");
        var bound = stackalloc float[2] { 100f, 49.9f };
        board.BindOutput(maxMana, bound);
        var clamp = Clamp.StatMax(board, maxMana);

        var mana = board.Schema.Intrinsic("mana");
        Assert.Equal(0, mana.Index);
        var value = stackalloc int[2] { 60, 60 };
        var applied = stackalloc int[2];
        board.RunIntrinsicsChunk(value, 60, clamp, 0, 2, applied);

        Assert.Equal(100, value[0]);
        Assert.Equal(49, value[1]);
        Assert.Equal(40, applied[0]);
        Assert.Equal(-11, applied[1]);
    }

    [Fact]
    public void ClampStat_BoundsAreFrameFrozen_AtBroadcast()
    {
        using var board = Board.Create(Schema(), 2);
        var slot = board.Acquire();
        var maxMana = board.Schema.Stat("max_mana");
        var bound = stackalloc float[2] { 100f, 100f };
        board.BindOutput(maxMana, bound);
        var clamp = Clamp.StatMax(board, maxMana);
        var mana = board.Schema.Intrinsic("mana");
        var value = stackalloc int[2] { 90, 90 };
        var applied = stackalloc int[2];

        board.RunIntrinsicsChunk(value, 30, clamp, 0, 2, applied);
        Assert.Equal(100, value[0]);

        board.AddModifier(slot, maxMana, new Modifier(ModifierKind.Multi, 1f));
        board.Recalculate();
        Assert.Equal(200f, bound[0]);

        value[0] = 90;
        board.RunIntrinsicsChunk(value, 150, clamp, 0, 2, applied);
        Assert.Equal(200, value[0]);
        Assert.Equal(100, value[1]);
    }

    [Fact]
    public void ClampStat_UnboundStat_Throws()
    {
        using var board = Board.Create(Schema(), 2);
        var maxMana = board.Schema.Stat("max_mana");
        Assert.Throws<ArgumentException>(() => Clamp.StatMax(board, maxMana));
    }

    [Fact]
    public void Fold_MatchesStrictFloatOracle_OnRandomSets()
    {
        var rng = new Random(20260929);
        float[] pool = { 0.5f, 3f, -7f, 1e8f, -1e8f, 1f, 0.1f, 12345.678f, -0.25f, 1e-8f };
        using var board = Board.Create(Schema(), 8);
        var speed = board.Schema.Stat("move_speed");
        var column = stackalloc float[8];
        board.BindOutput(speed, column);

        for (var iteration = 0; iteration < 200; iteration++)
        {
            var slot = board.Acquire();
            var mirror = new List<(ModifierKind kind, float value, int order)>();
            var count = rng.Next(0, 7);
            for (var i = 0; i < count; i++)
            {
                var kind = (ModifierKind)rng.Next(0, 3);
                var value = pool[rng.Next(pool.Length)];
                board.AddModifier(slot, speed, new Modifier(kind, value));
                mirror.Add((kind, value, i));
            }

            mirror.Sort((x, y) => x.kind != y.kind ? x.kind.CompareTo(y.kind) : x.order.CompareTo(y.order));
            board.Recalculate();

            var expected = OracleFold(6f, mirror.Select(m => (m.kind, m.value)).ToList());
            Assert.Equal(Bits(expected), Bits(column[slot]));
            board.Release(slot);
        }
    }

    [Fact]
    public void Modifier_RequiresFiniteValue()
    {
        Assert.Throws<ArgumentException>(() => new Modifier(ModifierKind.Added, float.NaN));
        Assert.Throws<ArgumentException>(() => new Modifier(ModifierKind.Added, float.PositiveInfinity));
    }

    [Fact]
    public void RemoveModifier_UnknownHandle_Throws()
    {
        using var board = Board.Create(Schema(), 2);
        var speed = board.Schema.Stat("move_speed");
        var slot = board.Acquire();
        Assert.Throws<ArgumentException>(() => board.RemoveModifier(slot, speed, new ModifierHandle(999)));
    }
}

public class SchemaStatTests
{
    [Fact]
    public void StatIds_AreDenseAndLookUpByName()
    {
        var schema = SchemaBuilder.Create().Stat("a", 1f).Stat("b", 2f).Build();
        Assert.Equal(0, schema.Stat("a").Index);
        Assert.Equal(1, schema.Stat("b").Index);
        Assert.True(schema.TryStat("b", out var id));
        Assert.Equal(1, id.Index);
        Assert.Throws<KeyNotFoundException>(() => schema.Stat("nope"));
    }

    [Fact]
    public void DuplicateStatNames_Rejected()
    {
        var builder = SchemaBuilder.Create().Stat("a", 1f).Stat("a", 2f);
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void NonFiniteBase_Rejected()
    {
        Assert.Throws<ArgumentException>(() => SchemaBuilder.Create().Stat("a", float.NaN));
    }
}
