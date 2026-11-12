using System.Runtime.InteropServices;
using Sv;
using Xunit;

public unsafe class SinkKeyframeTests
{
    const int Capacity = 512;

    static Schema TestSchema() =>
        SchemaBuilder.Create()
            .Stat("max_hp", 100f)
            .Stat("armor", 10f)
            .Stat("strength", 10f)
            .Derived("atk", 1f, ("strength", 2f))
            .Build();

    static uint Canonical(float value) => float.IsNaN(value) ? 0x7FC00000u : BitConverter.SingleToUInt32Bits(value);

    static int Drain(Board board, float[,] host, int chunk)
    {
        var buffer = new StatWrite[chunk];
        var total = 0;
        while (true)
        {
            var taken = board.TakeBroadcast(buffer);
            if (taken == 0)
            {
                return total;
            }

            for (var i = 0; i < taken; i++)
            {
                host[buffer[i].Stat, buffer[i].Slot] = BitConverter.UInt32BitsToSingle(buffer[i].Bits);
            }

            total += taken;
        }
    }

    static void Mutate(Board board, Schema schema, Random random, List<int> live, int ops)
    {
        var strength = schema.Stat("strength");
        var maxHp = schema.Stat("max_hp");
        var armor = schema.Stat("armor");
        for (var i = 0; i < ops; i++)
        {
            var slot = live[random.Next(live.Count)];
            if (random.Next(5) == 0)
            {
                board.RemoveSource(slot, random.Next(1, 5));
                continue;
            }

            var pick = random.Next(3);
            var stat = pick == 0 ? strength : pick == 1 ? maxHp : armor;
            board.AddModifier(slot, stat, new Modifier(ModifierKind.Added, (float)Math.Round(random.NextDouble() * 8 - 3, 2)), random.Next(1, 5));
        }
    }

    [Fact]
    public void KeyframeLoad_AfterRollback_RefillsTheSink_AndHostColumnsMatch()
    {
        var schema = TestSchema();
        var stats = schema.Stats.Select(s => schema.Stat(s.Name)).ToArray();
        using var source = Board.Create(schema, Capacity);
        using var receiver = Board.Create(schema, Capacity);
        var host = new float[stats.Length, Capacity];
        foreach (var stat in stats)
        {
            receiver.BindSink(stat, true);
        }

        var live = new List<int>();
        for (var i = 0; i < 96; i++)
        {
            live.Add(source.Acquire());
        }

        source.Recalculate();
        using var baseline = new SvState();
        source.SaveState(baseline);
        receiver.LoadState(baseline);
        Assert.Equal(live.Count * stats.Length, receiver.PendingBroadcast);
        Drain(receiver, host, 11);
        using var keyframe = new SvState();
        source.SaveKeyframe(keyframe);
        var authority = new Random(0xA17);
        var predicted = new Random(0xB22);
        for (var round = 0; round < 10; round++)
        {
            receiver.BeginUndoWindow(4);
            for (var frame = 0; frame < 3; frame++)
            {
                receiver.BeginUndoFrame();
                Mutate(receiver, schema, predicted, live, 20);
                receiver.Recalculate();
                if (frame == 2)
                {
                    var partial = new StatWrite[5];
                    var taken = receiver.TakeBroadcast(partial);
                    for (var i = 0; i < taken; i++)
                    {
                        host[partial[i].Stat, partial[i].Slot] = BitConverter.UInt32BitsToSingle(partial[i].Bits);
                    }

                    continue;
                }

                Drain(receiver, host, 9);
            }

            receiver.Rollback(3);
            Drain(receiver, host, 13);
            var seed = authority.Next();
            Mutate(receiver, schema, new Random(seed), live, 30);
            receiver.Recalculate();
            var early = new StatWrite[4];
            var earlyTaken = receiver.TakeBroadcast(early);
            for (var i = 0; i < earlyTaken; i++)
            {
                host[early[i].Stat, early[i].Slot] = BitConverter.UInt32BitsToSingle(early[i].Bits);
            }

            Mutate(source, schema, new Random(seed), live, 30);
            source.Recalculate();
            source.SaveKeyframe(keyframe);
            receiver.LoadKeyframe(keyframe);
            Assert.Equal(source.Digest, receiver.Digest);
            Assert.Equal(live.Count * stats.Length, receiver.PendingBroadcast);
            Assert.Equal(live.Count * stats.Length, Drain(receiver, host, 7));
            foreach (var slot in live)
            {
                for (var s = 0; s < stats.Length; s++)
                {
                    Assert.True(Canonical(source.Stat(slot, stats[s])) == Canonical(host[s, slot]), $"round {round} slot {slot} stat {s}");
                }
            }
        }
    }

    [Fact]
    public void StatRowsBounds_OverAHostFloatColumn_FoldsLikeBoardStatBounds()
    {
        const int Rows = 1024;
        const int Words = Rows / 64;
        var schema = TestSchema();
        var maxHp = schema.Stat("max_hp");
        var armor = schema.Stat("armor");
        using var board = Board.Create(schema, Rows);
        var random = new Random(0x5B0D);
        for (var i = 0; i < Rows; i++)
        {
            board.Acquire();
        }

        for (var i = 0; i < 2000; i++)
        {
            var slot = random.Next(Rows);
            var stat = random.Next(2) == 0 ? maxHp : armor;
            var multi = random.Next(4) == 0;
            var value = multi ? (float)Math.Round(random.NextDouble() * 3 - 1, 2) : (float)Math.Round(random.NextDouble() * 400 - 150, 2);
            board.AddModifier(slot, stat, new Modifier(multi ? ModifierKind.Multi : ModifierKind.Added, value), random.Next(1, 4));
        }

        board.BindSink(maxHp, true);
        board.BindSink(armor, true);
        board.Recalculate();
        var host = new float[schema.Stats.Count, Rows];
        Drain(board, host, 333);
        var floorHost = (float*)NativeMemory.AlignedAlloc(Rows * sizeof(float), 64);
        var ceilingHost = (float*)NativeMemory.AlignedAlloc(Rows * sizeof(float), 64);
        var valueA = (int*)NativeMemory.AlignedAlloc(Rows * sizeof(int), 64);
        var valueB = (int*)NativeMemory.AlignedAlloc(Rows * sizeof(int), 64);
        var deltaA = (int*)NativeMemory.AlignedAlloc(Rows * sizeof(int), 64);
        var deltaB = (int*)NativeMemory.AlignedAlloc(Rows * sizeof(int), 64);
        var changedA = stackalloc ulong[Words];
        var changedB = stackalloc ulong[Words];
        try
        {
            var owned = board.OutputPtr(maxHp);
            for (var slot = 0; slot < Rows; slot++)
            {
                ceilingHost[slot] = host[maxHp.Index, slot];
                floorHost[slot] = host[armor.Index, slot] - 40f;
                Assert.Equal(Canonical(owned[slot]), Canonical(ceilingHost[slot]));
                var start = random.Next(-300, 300);
                valueA[slot] = start;
                valueB[slot] = start;
            }

            var digests = new ulong[8];
            for (var tick = 0; tick < 8; tick++)
            {
                var reference = new int[Rows];
                for (var slot = 0; slot < Rows; slot++)
                {
                    var delta = random.Next(16) == 0 ? (random.Next(2) == 0 ? int.MaxValue : int.MinValue) : random.Next(-90, 90);
                    deltaA[slot] = delta;
                    deltaB[slot] = delta;
                    reference[slot] = IntrinsicLaw.Step(valueA[slot], delta, int.MinValue, Clamp.BoundOf(ceilingHost[slot]));
                }

                Kernel.Fold(new Column<int>(valueA, Rows), Window.Range(0, Rows), new Column<int>(deltaA, Rows), Bounds.StatRows(null, ceilingHost), new Column<ulong>(changedA, Words));
                Kernel.Fold(new Column<int>(valueB, Rows), Window.Range(0, Rows), new Column<int>(deltaB, Rows), Bounds.Stats(board, null, maxHp), new Column<ulong>(changedB, Words));
                for (var w = 0; w < Words; w++)
                {
                    Assert.Equal(changedB[w], changedA[w]);
                }

                for (var slot = 0; slot < Rows; slot++)
                {
                    Assert.Equal(reference[slot], valueA[slot]);
                    Assert.Equal(valueB[slot], valueA[slot]);
                    Assert.Equal(0, deltaA[slot]);
                }

                Assert.Equal(CellDigest.Sum(7, valueB, 0, Rows), CellDigest.Sum(7, valueA, 0, Rows));
                for (var slot = 0; slot < Rows; slot++)
                {
                    deltaA[slot] = random.Next(-50, 50);
                    reference[slot] = IntrinsicLaw.Step(valueA[slot], deltaA[slot], Clamp.BoundOf(floorHost[slot]), Clamp.BoundOf(ceilingHost[slot]));
                }

                Kernel.Fold(new Column<int>(valueA, Rows), Window.Range(0, Rows), new Column<int>(deltaA, Rows), Bounds.StatRows(floorHost, ceilingHost), new Column<ulong>(changedA, Words));
                for (var slot = 0; slot < Rows; slot++)
                {
                    Assert.Equal(reference[slot], valueA[slot]);
                    valueB[slot] = valueA[slot];
                }

                digests[tick] = CellDigest.Sum(7, valueA, 0, Rows);
            }

            Assert.Equal(8, digests.Distinct().Count());
            Assert.Throws<ArgumentException>(() => Bounds.StatRows(null, null));
        }
        finally
        {
            NativeMemory.AlignedFree(floorHost);
            NativeMemory.AlignedFree(ceilingHost);
            NativeMemory.AlignedFree(valueA);
            NativeMemory.AlignedFree(valueB);
            NativeMemory.AlignedFree(deltaA);
            NativeMemory.AlignedFree(deltaB);
        }
    }
}
