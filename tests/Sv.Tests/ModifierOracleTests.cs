using System;
using System.Collections.Generic;
using System.Linq;
using Kernels.Testing;
using Sv;
using Xunit;

public unsafe class ModifierOracleTests
{
    static float OracleFold(float @base, IEnumerable<(ModifierKind kind, float value, int seq)> mods)
    {
        var canonical = mods.OrderBy(m => m.kind).ThenBy(m => m.seq).ToList();
        var value = @base;
        foreach (var m in canonical)
        {
            if (m.kind == ModifierKind.Added)
            {
                value = StrictFloat.Add(value, m.value);
            }
        }

        var additive = 1f;
        foreach (var m in canonical)
        {
            if (m.kind == ModifierKind.Additive)
            {
                additive = StrictFloat.Add(additive, m.value);
            }
        }

        value = StrictFloat.Multiply(value, additive);
        foreach (var m in canonical)
        {
            if (m.kind == ModifierKind.Multi)
            {
                value = StrictFloat.Multiply(value, StrictFloat.Add(1f, m.value));
            }
        }

        return value;
    }

    [Fact]
    public void RemoveAfterRecalc_RemovesTheHandlesOwnModifier()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("attack", 10f).Build(), 64);
        var attack = board.Schema.Stat("attack");
        var slot = board.Acquire();
        var multi = board.AddModifier(slot, attack, new Modifier(ModifierKind.Multi, 1f));
        board.AddModifier(slot, attack, new Modifier(ModifierKind.Added, 5f));
        board.Recalculate();
        Assert.Equal(30f, board.Output(attack)[slot]);
        board.RemoveModifier(slot, attack, multi);
        board.Recalculate();
        Assert.Equal(15f, board.Output(attack)[slot]);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(11)]
    [InlineData(2026)]
    public void RandomChurnWithRenumber_MatchesIndependentOracle(int seed)
    {
        const int n = 5000;
        var rng = new Random(seed);
        var bases = new[] { 6f, 100f };
        using var board = Board.Create(SchemaBuilder.Create().Stat("speed", bases[0]).Stat("mana", bases[1]).Build(), n);
        var stats = new[] { board.Schema.Stat("speed"), board.Schema.Stat("mana") };
        var model = new Dictionary<int, List<(int stat, ModifierKind kind, float value, int seq, ModifierHandle handle)>>();
        var seq = 0;
        for (var frame = 0; frame < 120; frame++)
        {
            for (var op = 0; op < 200; op++)
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
                else if (roll < 7)
                {
                    var slot = model.Keys.ElementAt(rng.Next(model.Count));
                    var stat = rng.Next(2);
                    var kind = (ModifierKind)rng.Next(3);
                    var value = rng.Next(-4, 5) * 0.25f;
                    var handle = board.AddModifier(slot, stats[stat], new Modifier(kind, value));
                    model[slot].Add((stat, kind, value, seq++, handle));
                }
                else
                {
                    var slot = model.Keys.ElementAt(rng.Next(model.Count));
                    var list = model[slot];
                    if (list.Count > 0)
                    {
                        var at = rng.Next(list.Count);
                        board.RemoveModifier(slot, stats[list[at].stat], list[at].handle);
                        list.RemoveAt(at);
                    }
                }
            }

            if (frame % 30 == 29)
            {
                var map = new int[board.Capacity];
                Array.Fill(map, -1);
                var next = 0;
                var renamed = new Dictionary<int, List<(int, ModifierKind, float, int, ModifierHandle)>>();
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
                for (var stat = 0; stat < 2; stat++)
                {
                    var expected = OracleFold(bases[stat], list.Where(m => m.stat == stat).Select(m => (m.kind, m.value, m.seq)));
                    Assert.True(
                        BitConverter.SingleToUInt32Bits(expected) == BitConverter.SingleToUInt32Bits(board.Output(stats[stat])[slot]),
                        $"frame {frame} slot {slot} stat {stat}: expected {expected} got {board.Output(stats[stat])[slot]}");
                }
            }
        }
    }
}
