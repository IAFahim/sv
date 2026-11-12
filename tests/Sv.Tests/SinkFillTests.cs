using Sv;
using Xunit;

public unsafe class SinkFillTests
{
    const int Capacity = 300;

    static Schema TestSchema() =>
        SchemaBuilder.Create()
            .Stat("max_hp", 100f)
            .Stat("armor", 10f)
            .Stat("strength", 10f)
            .Derived("atk", 1f, ("strength", 2f))
            .Build();

    static uint Canonical(float value) => float.IsNaN(value) ? 0x7FC00000u : BitConverter.SingleToUInt32Bits(value);

    static List<StatWrite> Drain(Board board, int chunk)
    {
        var all = new List<StatWrite>();
        var buffer = new StatWrite[chunk];
        while (true)
        {
            var taken = board.TakeBroadcast(buffer);
            if (taken == 0)
            {
                return all;
            }

            for (var i = 0; i < taken; i++)
            {
                all.Add(buffer[i]);
            }
        }
    }

    static void AssertAscending(List<StatWrite> writes)
    {
        for (var i = 1; i < writes.Count; i++)
        {
            var a = writes[i - 1];
            var b = writes[i];
            Assert.True(b.Slot > a.Slot || (b.Slot == a.Slot && b.Stat > a.Stat), $"({b.Slot},{b.Stat}) after ({a.Slot},{a.Stat})");
        }
    }

    [Fact]
    public void BindSink_FillsEveryExistingLiveRow_InCanonicalOrder()
    {
        var schema = TestSchema();
        using var board = Board.Create(schema, Capacity);
        var random = new Random(0xF111);
        var live = new List<int>();
        for (var i = 0; i < 200; i++)
        {
            live.Add(board.Acquire());
        }

        for (var i = 0; i < 40; i++)
        {
            var at = random.Next(live.Count);
            board.Release(live[at]);
            live.RemoveAt(at);
        }

        for (var i = 0; i < 300; i++)
        {
            var slot = live[random.Next(live.Count)];
            var stat = random.Next(3) == 0 ? schema.Stat("strength") : schema.Stat("max_hp");
            var value = random.Next(25) == 0 ? float.NaN : (float)Math.Round(random.NextDouble() * 6 - 2, 3);
            if (float.IsNaN(value))
            {
                continue;
            }

            board.AddModifier(slot, stat, new Modifier(ModifierKind.Added, value), random.Next(1, 4));
        }

        board.Recalculate();
        board.Recalculate();
        var hp = schema.Stat("max_hp");
        var atk = schema.Stat("atk");
        board.BindSink(atk, true);
        board.BindSink(hp, true);
        Assert.Equal(live.Count * 2, board.PendingBroadcast);
        var writes = Drain(board, 13);
        Assert.Equal(live.Count * 2, writes.Count);
        Assert.Equal(0, board.PendingBroadcast);
        AssertAscending(writes);
        live.Sort();
        for (var i = 0; i < live.Count; i++)
        {
            var first = writes[2 * i];
            var second = writes[2 * i + 1];
            Assert.Equal(live[i], first.Slot);
            Assert.Equal(live[i], second.Slot);
            Assert.Equal((ushort)hp.Index, first.Stat);
            Assert.Equal((ushort)atk.Index, second.Stat);
            Assert.Equal(Canonical(board.Stat(live[i], hp)), first.Bits);
            Assert.Equal(Canonical(board.Stat(live[i], atk)), second.Bits);
        }

        board.Recalculate();
        Assert.Equal(0, board.PendingBroadcast);
        Assert.Empty(Drain(board, 13));
        board.BindSink(schema.Stat("armor"));
        board.Recalculate();
        Assert.Equal(0, board.PendingBroadcast);
    }

    [Fact]
    public void BindSink_FillSurvivesARecalculateBeforeTheFirstTake()
    {
        var schema = TestSchema();
        using var board = Board.Create(schema, Capacity);
        for (var i = 0; i < 70; i++)
        {
            board.Acquire();
        }

        board.Recalculate();
        board.BindSink(schema.Stat("armor"), true);
        board.AddModifier(5, schema.Stat("armor"), new Modifier(ModifierKind.Added, 3f), 1);
        board.Recalculate();
        var writes = Drain(board, 64);
        Assert.Equal(70, writes.Count);
        AssertAscending(writes);
        Assert.Equal(Canonical(13f), writes[5].Bits);
        Assert.Equal(Canonical(10f), writes[6].Bits);
    }

    [Fact]
    public void Rollback_RewindsTheSinkCursor_AndHostColumnsMatchTheRestoredBoard()
    {
        var schema = TestSchema();
        var stats = schema.Stats.Select(s => schema.Stat(s.Name)).ToArray();
        using var board = Board.Create(schema, Capacity);
        var host = new float[stats.Length, Capacity];
        foreach (var stat in stats)
        {
            board.BindSink(stat, true);
        }

        var random = new Random(0x0B0B);
        var live = new List<int>();
        for (var i = 0; i < 64; i++)
        {
            live.Add(board.Acquire());
        }

        board.Recalculate();
        Apply(Drain(board, 9), host);
        board.BeginUndoWindow(6);
        for (var round = 0; round < 12; round++)
        {
            for (var frame = 0; frame < 5; frame++)
            {
                board.BeginUndoFrame();
                for (var i = 0; i < 25; i++)
                {
                    var slot = live[random.Next(live.Count)];
                    if (random.Next(5) == 0)
                    {
                        board.RemoveSource(slot, random.Next(1, 5));
                        continue;
                    }

                    var stat = stats[random.Next(stats.Length)];
                    if (stat == schema.Stat("atk"))
                    {
                        stat = schema.Stat("strength");
                    }

                    board.AddModifier(slot, stat, new Modifier(ModifierKind.Added, (float)Math.Round(random.NextDouble() * 8 - 3, 2)), random.Next(1, 5));
                }

                board.Recalculate();
                if (frame == 4 && round % 2 == 1)
                {
                    var buffer = new StatWrite[3];
                    var partial = board.TakeBroadcast(buffer);
                    Assert.Equal(3, partial);
                    for (var i = 0; i < partial; i++)
                    {
                        host[buffer[i].Stat, buffer[i].Slot] = BitConverter.UInt32BitsToSingle(buffer[i].Bits);
                    }

                    continue;
                }

                Apply(Drain(board, 9), host);
            }

            var depth = 1 + random.Next(4);
            board.Rollback(depth);
            Assert.Equal(live.Count * stats.Length, board.PendingBroadcast);
            var refill = Drain(board, 7);
            AssertAscending(refill);
            Assert.Equal(live[0], refill[0].Slot);
            Apply(refill, host);
            foreach (var slot in live)
            {
                for (var s = 0; s < stats.Length; s++)
                {
                    Assert.True(Canonical(board.Stat(slot, stats[s])) == Canonical(host[s, slot]), $"round {round} slot {slot} stat {s}");
                }
            }

            board.Recalculate();
            Apply(Drain(board, 9), host);
        }
    }

    static void Apply(List<StatWrite> writes, float[,] host)
    {
        foreach (var w in writes)
        {
            host[w.Stat, w.Slot] = BitConverter.UInt32BitsToSingle(w.Bits);
        }
    }
}
