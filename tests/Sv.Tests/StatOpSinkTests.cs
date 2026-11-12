using System.Runtime.InteropServices;
using Sv;
using Xunit;

public unsafe class StatOpSinkTests
{
    const int Capacity = 256;

    static Schema TestSchema() =>
        SchemaBuilder.Create()
            .Stat("max_hp", 100f)
            .Stat("armor", 10f)
            .Stat("strength", 10f)
            .Derived("atk", 1f, ("strength", 2f))
            .Stat("haste", 0f)
            .Build();

    static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);

    [Fact]
    public void StatOp_Is24Bytes()
    {
        Assert.Equal(24, sizeof(StatOp));
        Assert.Equal(24, Marshal.SizeOf<StatOp>());
    }

    [Fact]
    public void TwinBoards_SinkRecords_EqualBoundColumnBits()
    {
        var schema = TestSchema();
        var stats = schema.Stats.Select(s => schema.Stat(s.Name)).ToArray();
        using var bound = Board.Create(schema, Capacity);
        using var sunk = Board.Create(schema, Capacity);
        var boundColumns = new float*[stats.Length];
        var hostColumns = new float*[stats.Length];
        for (var s = 0; s < stats.Length; s++)
        {
            boundColumns[s] = (float*)NativeMemory.AllocZeroed(Capacity, sizeof(float));
            hostColumns[s] = (float*)NativeMemory.AllocZeroed(Capacity, sizeof(float));
            bound.BindOutput(stats[s], boundColumns[s]);
            sunk.BindSink(stats[s]);
            Assert.True(sunk.IsSinkBound(stats[s]));
        }

        try
        {
            var random = new Random(0x51A7);
            var live = new List<int>();
            var writes = new StatWrite[7];
            var kinds = new[] { ModifierKind.Added, ModifierKind.Additive, ModifierKind.Multi, ModifierKind.Cap };
            var totalRecords = 0;
            for (var frame = 0; frame < 80; frame++)
            {
                var acquires = random.Next(0, 9);
                for (var i = 0; i < acquires && live.Count < Capacity - 8; i++)
                {
                    var a = bound.Acquire();
                    var b = sunk.Acquire();
                    Assert.Equal(a, b);
                    live.Add(a);
                }

                if (live.Count > 4 && random.Next(3) == 0)
                {
                    var at = random.Next(live.Count);
                    var slot = live[at];
                    live.RemoveAt(at);
                    bound.Release(slot);
                    sunk.Release(slot);
                    for (var s = 0; s < stats.Length; s++)
                    {
                        hostColumns[s][slot] = 0f;
                    }
                }

                var ops = random.Next(0, 30);
                for (var i = 0; i < ops && live.Count > 0; i++)
                {
                    var slot = live[random.Next(live.Count)];
                    var source = random.Next(1, 6);
                    if (random.Next(4) == 0)
                    {
                        Assert.Equal(bound.RemoveSource(slot, source), sunk.RemoveSource(slot, source));
                        continue;
                    }

                    var stat = stats[random.Next(stats.Length)];
                    if (stat == schema.Stat("atk") && random.Next(2) == 0)
                    {
                        stat = schema.Stat("strength");
                    }

                    var kind = kinds[random.Next(kinds.Length)];
                    var value = random.Next(20) == 0 ? 1e30f : (float)Math.Round(random.NextDouble() * 4 - 1, 3);
                    var modifier = new Modifier(kind, value);
                    bound.AddModifier(slot, stat, modifier, source);
                    sunk.AddModifier(slot, stat, modifier, source);
                }

                bound.Recalculate();
                sunk.Recalculate();
                var pending = sunk.PendingBroadcast;
                var lastSlot = -1;
                var lastStat = -1;
                while (true)
                {
                    var taken = sunk.TakeBroadcast(writes);
                    if (taken == 0)
                    {
                        break;
                    }

                    for (var i = 0; i < taken; i++)
                    {
                        var w = writes[i];
                        Assert.True(w.Slot > lastSlot || (w.Slot == lastSlot && w.Stat > lastStat), $"frame {frame}: ({w.Slot},{w.Stat}) after ({lastSlot},{lastStat})");
                        lastSlot = w.Slot;
                        lastStat = w.Stat;
                        hostColumns[w.Stat][w.Slot] = BitConverter.UInt32BitsToSingle(w.Bits);
                        totalRecords++;
                    }

                    Assert.True(sunk.PendingBroadcast < pending);
                    pending = sunk.PendingBroadcast;
                }

                Assert.Equal(0, sunk.PendingBroadcast);
                foreach (var slot in live)
                {
                    for (var s = 0; s < stats.Length; s++)
                    {
                        Assert.True(Bits(boundColumns[s][slot]) == Bits(hostColumns[s][slot]), $"frame {frame} slot {slot} stat {s}: bound {boundColumns[s][slot]} sink {hostColumns[s][slot]}");
                    }
                }
            }

            Assert.True(totalRecords > 200);
        }
        finally
        {
            for (var s = 0; s < stats.Length; s++)
            {
                NativeMemory.Free(boundColumns[s]);
                NativeMemory.Free(hostColumns[s]);
            }
        }
    }

    [Fact]
    public void Sink_CarriesCanonicalNaNBits_AndNothingWithoutSinks()
    {
        var schema = SchemaBuilder.Create().Stat("power", float.MaxValue).Stat("plain", 3f).Build();
        var power = schema.Stat("power");
        using var board = Board.Create(schema, 64);
        var writes = new StatWrite[8];
        board.Acquire();
        board.Recalculate();
        Assert.Equal(0, board.PendingBroadcast);
        Assert.Equal(0, board.TakeBroadcast(writes));
        board.BindSink(power);
        var slot = board.Acquire();
        board.AddModifier(slot, power, new Modifier(ModifierKind.Added, float.MaxValue));
        board.AddModifier(slot, power, new Modifier(ModifierKind.Multi, -1f));
        board.Recalculate();
        Assert.Equal(1, board.PendingBroadcast);
        Assert.Equal(1, board.TakeBroadcast(writes));
        Assert.Equal(slot, writes[0].Slot);
        Assert.Equal(0, writes[0].Stat);
        Assert.Equal(0x7FC00000u, writes[0].Bits);
        Assert.Equal(0, board.TakeBroadcast(writes));
        board.Recalculate();
        Assert.Equal(0, board.PendingBroadcast);
    }

    static StatOp[] RandomOps(Random random, int count, int stats, int slots)
    {
        var kinds = new[] { ModifierKind.Added, ModifierKind.Additive, ModifierKind.Multi, ModifierKind.Cap };
        var ops = new StatOp[count];
        for (var i = 0; i < count; i++)
        {
            var slot = random.Next(10) == 0 ? (random.Next(2) == 0 ? -3 : slots + 5) : random.Next(slots);
            var source = random.Next(1, 5);
            switch (random.Next(10))
            {
                case 0:
                case 1:
                case 2:
                    ops[i] = StatOp.RemoveSource(slot, source);
                    break;
                case 3:
                    ops[i] = new StatOp(StatOpKind.Add, slot, source, (ushort)(stats + 1), ModifierKind.Added, Bits(1f), long.MaxValue);
                    break;
                case 4:
                    ops[i] = new StatOp(StatOpKind.Add, slot, source, 0, ModifierKind.Added, 0x7FC00001u, long.MaxValue);
                    break;
                case 5:
                    ops[i] = new StatOp(StatOpKind.Add, slot, source, 1, (ModifierKind)9, Bits(1f), long.MaxValue);
                    break;
                default:
                    ops[i] = new StatOp(StatOpKind.Add, slot, source, (ushort)random.Next(stats), kinds[random.Next(4)], Bits((float)Math.Round(random.NextDouble() * 3 - 1, 2)), long.MaxValue);
                    break;
            }
        }

        return ops;
    }

    [Fact]
    public void Canonicalize_HundredShuffles_GiveOneOrder()
    {
        var random = new Random(97);
        var ops = RandomOps(random, 3000, 5, 40);
        var bigRun = Enumerable.Range(0, 200).Select(i => StatOp.Add(7, default, ModifierKind.Added, i % 13, 200 - i)).ToArray();
        ops = ops.Concat(bigRun).ToArray();
        StatOp[]? first = null;
        var scratch = new StatOp[ops.Length];
        for (var round = 0; round < 100; round++)
        {
            var shuffled = ops.OrderBy(_ => random.Next()).ToArray();
            StatOps.Canonicalize(shuffled, scratch);
            for (var i = 1; i < shuffled.Length; i++)
            {
                Assert.True(StatOps.Compare(shuffled[i - 1], shuffled[i]) <= 0, $"round {round} position {i}");
            }

            if (first == null)
            {
                first = shuffled;
                continue;
            }

            Assert.True(MemoryMarshal.AsBytes(first.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(shuffled.AsSpan())), $"round {round}");
        }

        Assert.Equal(-3, first![0].Slot);
        Assert.Throws<ArgumentException>(() => StatOps.Canonicalize(new StatOp[4], new StatOp[3]));
    }

    [Fact]
    public void Canonicalize_RemoveSortsBeforeAdd_SameTickRetriggerRefreshes()
    {
        var schema = TestSchema();
        using var board = Board.Create(schema, 64);
        var slot = board.Acquire();
        var armor = schema.Stat("armor");
        var source = 0x40000000 | (3 << 8) | 1;
        board.AddModifier(slot, armor, new Modifier(ModifierKind.Added, 5f), source);
        board.Recalculate();
        Assert.Equal(15f, board.Stat(slot, armor));
        var ops = new[] { StatOp.Add(slot, armor, ModifierKind.Added, 7f, source), StatOp.RemoveSource(slot, source), StatOp.RemoveSource(slot, source) };
        StatOps.Canonicalize(ops, new StatOp[3]);
        Assert.Equal(StatOpKind.RemoveSource, ops[0].Op);
        Assert.Equal(StatOpKind.Add, ops[2].Op);
        Assert.Equal(2, board.Apply(ops));
        board.Recalculate();
        Assert.Equal(17f, board.Stat(slot, armor));
    }

    [Fact]
    public void Apply_Batch_EqualsSameCallsOneByOne()
    {
        var schema = TestSchema();
        var stats = schema.Stats.Count;
        using var batch = Board.Create(schema, 128);
        using var loop = Board.Create(schema, 128);
        var random = new Random(4242);
        for (var i = 0; i < 40; i++)
        {
            batch.Acquire();
            loop.Acquire();
        }

        batch.Release(11);
        loop.Release(11);
        var scratch = new StatOp[2000];
        for (var frame = 0; frame < 30; frame++)
        {
            var ops = RandomOps(random, 1 + random.Next(1500), stats, 40);
            StatOps.Canonicalize(ops, scratch);
            var applied = batch.Apply(ops);
            var expected = 0;
            foreach (var op in ops)
            {
                if (op.Slot < 0 || op.Slot >= 40 || op.Slot == 11)
                {
                    continue;
                }

                if (op.Op == StatOpKind.RemoveSource)
                {
                    expected += loop.RemoveSource(op.Slot, op.Source) > 0 ? 1 : 0;
                    continue;
                }

                var value = BitConverter.UInt32BitsToSingle(op.ValueBits);
                if (op.Stat >= stats || (byte)op.Kind > 3 || !float.IsFinite(value))
                {
                    continue;
                }

                loop.AddModifier(op.Slot, schema.Stat(schema.Stats[op.Stat].Name), new Modifier(op.Kind, value), op.Source, op.ExpiresAt);
                expected++;
            }

            Assert.Equal(expected, applied);
            batch.Recalculate();
            loop.Recalculate();
            for (var slot = 0; slot < 40; slot++)
            {
                if (slot == 11)
                {
                    continue;
                }

                for (var s = 0; s < stats; s++)
                {
                    var id = schema.Stat(schema.Stats[s].Name);
                    Assert.Equal(Bits(loop.Stat(slot, id)), Bits(batch.Stat(slot, id)));
                }
            }
        }
    }

    [Fact]
    public void Apply_Warm_AllocatesNothing()
    {
        var schema = TestSchema();
        using var board = Board.Create(schema, 128);
        board.BindSink(schema.Stat("armor"));
        for (var i = 0; i < 64; i++)
        {
            board.Acquire();
        }

        var random = new Random(5);
        var ops = RandomOps(random, 500, schema.Stats.Count, 64);
        var scratch = new StatOp[500];
        var writes = new StatWrite[64];
        var work = new StatOp[500];
        for (var round = 0; round < 3; round++)
        {
            ops.CopyTo(work, 0);
            StatOps.Canonicalize(work, scratch);
            board.Apply(work);
            board.Recalculate();
            while (board.TakeBroadcast(writes) > 0)
            {
            }
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var round = 0; round < 20; round++)
        {
            ops.CopyTo(work, 0);
            StatOps.Canonicalize(work, scratch);
            board.Apply(work);
            board.Recalculate();
            while (board.TakeBroadcast(writes) > 0)
            {
            }
        }

        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
