using System;
using System.Collections.Generic;
using System.Linq;
using Kernels.Testing;
using Sv;
using Xunit;

public unsafe class DerivedStatTests
{
    static float Fold(float @base, IEnumerable<float> synthetic, IEnumerable<(ModifierKind kind, float value, int seq)> mods)
    {
        var value = @base;
        foreach (var s in synthetic)
        {
            value = StrictFloat.Add(value, s);
        }

        var canonical = mods.OrderBy(m => m.kind).ThenBy(m => m.seq).ToList();
        foreach (var m in canonical.Where(m => m.kind == ModifierKind.Added))
        {
            value = StrictFloat.Add(value, m.value);
        }

        var additive = 1f;
        foreach (var m in canonical.Where(m => m.kind == ModifierKind.Additive))
        {
            additive = StrictFloat.Add(additive, m.value);
        }

        value = StrictFloat.Multiply(value, additive);
        foreach (var m in canonical.Where(m => m.kind == ModifierKind.Multi))
        {
            value = StrictFloat.Multiply(value, StrictFloat.Add(1f, m.value));
        }

        return value;
    }

    static Schema Chain() => SchemaBuilder.Create()
        .Stat("str", 10f)
        .Derived("atk", 1f, ("str", 2f))
        .Derived("crit", 0f, ("atk", 0.5f), ("str", 0.25f))
        .Build();

    [Fact]
    public void Derived_FollowsSourceThroughAChain()
    {
        using var board = Board.Create(Chain(), 64);
        var str = board.Schema.Stat("str");
        var atk = board.Schema.Stat("atk");
        var crit = board.Schema.Stat("crit");
        var slot = board.Acquire();
        board.Recalculate();
        Assert.Equal(10f, board.Output(str)[slot]);
        Assert.Equal(21f, board.Output(atk)[slot]);
        Assert.Equal(13f, board.Output(crit)[slot]);
        var buff = board.AddModifier(slot, str, new Modifier(ModifierKind.Added, 2f));
        board.Recalculate();
        Assert.Equal(25f, board.Output(atk)[slot]);
        Assert.Equal(15.5f, board.Output(crit)[slot]);
        Assert.Equal(15.5f, board.Stat(slot, crit));
        board.RemoveModifier(slot, str, buff);
        board.Recalculate();
        Assert.Equal(21f, board.Output(atk)[slot]);
        Assert.Equal(13f, board.Output(crit)[slot]);
    }

    [Fact]
    public void Derived_RejectsForwardOrMissingSources()
    {
        Assert.Throws<ArgumentException>(() => SchemaBuilder.Create().Derived("atk", 1f, ("str", 2f)));
        Assert.Throws<ArgumentException>(() => SchemaBuilder.Create().Stat("str", 1f).Derived("atk", 1f));
        Assert.Throws<ArgumentException>(() => SchemaBuilder.Create().Stat("str", 1f).Derived("atk", 1f, ("atk", 1f)));
        Assert.Throws<ArgumentException>(() => SchemaBuilder.Create().Stat("str", 1f).Derived("atk", 1f, ("str", float.NaN)));
        var schema = SchemaBuilder.Create().Stat("str", 1f).Derived("atk", 1f, ("str", 3f)).Build();
        Assert.Equal(new StatSource(schema.Stat("str"), 3f), schema.Stats[1].Sources[0]);
    }

    [Theory]
    [InlineData(21)]
    [InlineData(1234)]
    public void Derived_ChurnRenumberLifetimes_MatchIndependentOracle(int seed)
    {
        const int n = 1200;
        var rng = new Random(seed);
        using var board = Board.Create(Chain(), n);
        var ids = new[] { board.Schema.Stat("str"), board.Schema.Stat("atk"), board.Schema.Stat("crit") };
        var model = new Dictionary<int, List<(int stat, ModifierKind kind, float value, int seq, int source, ModifierHandle handle)>>();
        var seq = 0;
        for (var frame = 0; frame < 120; frame++)
        {
            for (var op = 0; op < 70; op++)
            {
                var roll = rng.Next(10);
                if ((roll < 2 || model.Count == 0) && model.Count < n)
                {
                    model[board.Acquire()] = new();
                }
                else if (roll < 3)
                {
                    var slot = model.Keys.ElementAt(rng.Next(model.Count));
                    board.Release(slot);
                    model.Remove(slot);
                }
                else if (roll < 8)
                {
                    var slot = model.Keys.ElementAt(rng.Next(model.Count));
                    var stat = rng.Next(3);
                    var kind = (ModifierKind)rng.Next(3);
                    var value = rng.Next(-4, 5) * 0.375f;
                    var source = rng.Next(3);
                    var handle = board.AddModifier(slot, ids[stat], new Modifier(kind, value), source);
                    model[slot].Add((stat, kind, value, seq++, source, handle));
                }
                else
                {
                    var slot = model.Keys.ElementAt(rng.Next(model.Count));
                    var source = rng.Next(3);
                    Assert.Equal(model[slot].RemoveAll(m => m.source == source), board.RemoveSource(slot, source));
                }
            }

            if (frame % 50 == 49)
            {
                var map = new int[board.Capacity];
                Array.Fill(map, -1);
                var next = 0;
                var renamed = new Dictionary<int, List<(int, ModifierKind, float, int, int, ModifierHandle)>>();
                foreach (var slot in model.Keys.OrderByDescending(k => k))
                {
                    map[slot] = next;
                    renamed[next++] = model[slot];
                }

                board.RenumberSlots(map, model.Count);
                model = renamed;
            }

            board.Recalculate();
            foreach (var (slot, list) in model)
            {
                IEnumerable<(ModifierKind, float, int)> Of(int stat) => list.Where(m => m.stat == stat).Select(m => (m.kind, m.value, m.seq));
                var str = Fold(10f, Array.Empty<float>(), Of(0));
                var atk = Fold(1f, new[] { StrictFloat.Multiply(2f, str) }, Of(1));
                var crit = Fold(0f, new[] { StrictFloat.Multiply(0.5f, atk), StrictFloat.Multiply(0.25f, str) }, Of(2));
                var expected = new[] { str, atk, crit };
                for (var stat = 0; stat < 3; stat++)
                {
                    var actual = board.Output(ids[stat])[slot];
                    Assert.True(
                        BitConverter.SingleToUInt32Bits(expected[stat]) == BitConverter.SingleToUInt32Bits(actual),
                        $"frame {frame} slot {slot} stat {stat}: expected {expected[stat]} got {actual}");
                }
            }
        }
    }
}
