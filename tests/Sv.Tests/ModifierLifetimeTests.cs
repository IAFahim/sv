using System;
using System.Collections.Generic;
using System.Linq;
using Kernels.Testing;
using Sv;
using Xunit;

public unsafe class ModifierLifetimeTests
{
    sealed class Mod
    {
        public int Stat;
        public ModifierKind Kind;
        public float Value;
        public int Seq;
        public int Source;
        public long Expires;
        public ModifierHandle Handle;
    }

    static float OracleFold(float @base, IEnumerable<Mod> mods)
    {
        var canonical = mods.OrderBy(m => m.Kind).ThenBy(m => m.Seq).ToList();
        var value = @base;
        foreach (var m in canonical.Where(m => m.Kind == ModifierKind.Added))
        {
            value = StrictFloat.Add(value, m.Value);
        }

        var additive = 1f;
        foreach (var m in canonical.Where(m => m.Kind == ModifierKind.Additive))
        {
            additive = StrictFloat.Add(additive, m.Value);
        }

        value = StrictFloat.Multiply(value, additive);
        foreach (var m in canonical.Where(m => m.Kind == ModifierKind.Multi))
        {
            value = StrictFloat.Multiply(value, StrictFloat.Add(1f, m.Value));
        }

        return value;
    }

    [Fact]
    public void Expiry_RemovesInTickThenInsertionOrder_AndSkipsStale()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("speed", 10f).Build(), 64);
        var speed = board.Schema.Stat("speed");
        var a = board.Acquire();
        var b = board.Acquire();
        board.AddModifier(a, speed, new Modifier(ModifierKind.Added, 1f), 7, 5);
        var early = board.AddModifier(a, speed, new Modifier(ModifierKind.Added, 2f), 7, 3);
        board.AddModifier(b, speed, new Modifier(ModifierKind.Multi, 1f), 9, 5);
        board.AddModifier(b, speed, new Modifier(ModifierKind.Added, 4f), 0);
        board.RemoveModifier(a, speed, early);
        Assert.Equal(2, board.PendingExpiries);
        Assert.Equal(0, board.ExpireModifiers(4));
        Assert.Equal(2, board.PendingExpiries);
        board.Recalculate();
        Assert.Equal(11f, board.Output(speed)[a]);
        Assert.Equal(28f, board.Output(speed)[b]);
        Assert.Equal(2, board.ExpireModifiers(5));
        board.Recalculate();
        Assert.Equal(10f, board.Output(speed)[a]);
        Assert.Equal(14f, board.Output(speed)[b]);
        Assert.Equal(1, board.RemoveSource(b, 0));
        Assert.Equal(0, board.RemoveSource(b, 0));
        board.Recalculate();
        Assert.Equal(10f, board.Output(speed)[b]);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(808)]
    public void Lifetimes_ChurnRenumber_MatchIndependentOracle(int seed)
    {
        const int n = 1500;
        var rng = new Random(seed);
        var bases = new[] { 5f, 50f };
        using var board = Board.Create(SchemaBuilder.Create().Stat("a", bases[0]).Stat("b", bases[1]).Build(), n);
        var stats = new[] { board.Schema.Stat("a"), board.Schema.Stat("b") };
        var model = new Dictionary<int, List<Mod>>();
        var seq = 0;
        for (var tick = 0; tick < 150; tick++)
        {
            for (var op = 0; op < 80; op++)
            {
                var roll = rng.Next(12);
                if ((roll < 2 || model.Count == 0) && model.Count < n)
                {
                    model[board.Acquire()] = new List<Mod>();
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
                    var mod = new Mod
                    {
                        Stat = rng.Next(2),
                        Kind = (ModifierKind)rng.Next(3),
                        Value = rng.Next(-4, 5) * 0.25f,
                        Seq = seq++,
                        Source = rng.Next(4),
                        Expires = rng.Next(3) == 0 ? long.MaxValue : tick + rng.Next(0, 20),
                    };
                    mod.Handle = board.AddModifier(slot, stats[mod.Stat], new Modifier(mod.Kind, mod.Value), mod.Source, mod.Expires);
                    model[slot].Add(mod);
                }
                else if (roll < 10)
                {
                    var slot = model.Keys.ElementAt(rng.Next(model.Count));
                    var source = rng.Next(4);
                    var expected = model[slot].RemoveAll(m => m.Source == source);
                    Assert.Equal(expected, board.RemoveSource(slot, source));
                }
                else
                {
                    var slot = model.Keys.ElementAt(rng.Next(model.Count));
                    var list = model[slot];
                    if (list.Count > 0)
                    {
                        var at = rng.Next(list.Count);
                        board.RemoveModifier(slot, stats[list[at].Stat], list[at].Handle);
                        list.RemoveAt(at);
                    }
                }
            }

            if (tick % 40 == 39)
            {
                var map = new int[board.Capacity];
                Array.Fill(map, -1);
                var next = 0;
                var renamed = new Dictionary<int, List<Mod>>();
                foreach (var slot in model.Keys.OrderByDescending(k => k))
                {
                    map[slot] = next;
                    renamed[next++] = model[slot];
                }

                board.RenumberSlots(map, model.Count);
                model = renamed;
            }

            var expired = 0;
            foreach (var list in model.Values)
            {
                expired += list.RemoveAll(m => m.Expires <= tick);
            }

            Assert.Equal(expired, board.ExpireModifiers(tick));
            board.Recalculate();
            foreach (var (slot, list) in model)
            {
                for (var stat = 0; stat < 2; stat++)
                {
                    var expected = OracleFold(bases[stat], list.Where(m => m.Stat == stat));
                    Assert.True(
                        BitConverter.SingleToUInt32Bits(expected) == BitConverter.SingleToUInt32Bits(board.Output(stats[stat])[slot]),
                        $"tick {tick} slot {slot} stat {stat}: expected {expected} got {board.Output(stats[stat])[slot]}");
                }
            }
        }
    }

    [Fact]
    public void LifetimeVerbs_WarmAllocateZeroManagedBytes()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("speed", 1f).Build(), 256);
        var speed = board.Schema.Stat("speed");
        for (var i = 0; i < 256; i++)
        {
            board.Acquire();
        }

        for (var warm = 0; warm < 2; warm++)
        {
            for (var i = 0; i < 256; i++)
            {
                board.AddModifier(i, speed, new Modifier(ModifierKind.Added, 1f), i & 3, warm * 10 + (i & 7));
            }

            board.ExpireModifiers(warm * 10 + 3);
            board.RemoveSource(5, 1);
            board.Recalculate();
        }

        board.ExpireModifiers(long.MaxValue - 1);
        board.Recalculate();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var tick = 100; tick < 300; tick++)
        {
            for (var i = 0; i < 256; i += 3)
            {
                board.AddModifier(i, speed, new Modifier(ModifierKind.Additive, 0.5f), i & 3, tick + (i & 7));
            }

            board.RemoveSource(tick & 255, tick & 3);
            board.ExpireModifiers(tick);
            board.Recalculate();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
