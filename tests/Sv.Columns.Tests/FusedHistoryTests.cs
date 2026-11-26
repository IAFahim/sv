using Sv.Columns.Canon;

namespace Sv.Columns.Tests;

public sealed unsafe class FusedHistoryTests
{
    static string ArenaPath()
    {
        var folder = AppContext.BaseDirectory;
        while (folder != null && !File.Exists(Path.Combine(folder, "packs", "columns", "arena.json")))
        {
            folder = Path.GetDirectoryName(folder);
        }

        Assert.NotNull(folder);
        return Path.Combine(folder!, "packs", "columns", "arena.json");
    }

    static CompiledPack Canon() => PackJson.Read(File.ReadAllText(ArenaPath())).Compile();

    [Theory]
    [InlineData(FrameKernel.Rows, 1, 1)]
    [InlineData(FrameKernel.Rows, 3, 7)]
    [InlineData(FrameKernel.Scalar, 2, 5)]
    public void KeepingHistoryOnTheFusedFrameLeavesTheCanonDigestPinned(FrameKernel kernel, int writers, int parts)
    {
        using var pack = Canon();
        using var world = new ColumnWorld(pack, CanonRun.Capacity, writers, parts, history: 6);
        Assert.Equal(CanonRun.Pinned.ToString("X16"), CanonRun.Run(world, () => world.StepFused(kernel)).ToString("X16"));
    }

    [Theory]
    [InlineData(FrameKernel.Rows, 1, 1)]
    [InlineData(FrameKernel.Rows, 3, 7)]
    [InlineData(FrameKernel.Scalar, 2, 5)]
    public void TheCanonLateInputRunOnTheFusedFrameIsPinned(FrameKernel kernel, int writers, int parts)
    {
        using var pack = Canon();
        using var world = new ColumnWorld(pack, CanonRun.Capacity, writers, parts, history: CanonRun.LateHistory);
        var digest = CanonRun.RunWithLateInput(world, () => world.StepFused(kernel), () => world.ResimulateFused(kernel));
        Assert.Equal(CanonRun.PinnedLate.ToString("X16"), digest.ToString("X16"));
    }

    static List<int> Alive(ColumnWorld world)
    {
        var slots = new List<int>();
        for (var slot = 0; slot < world.Capacity; slot++)
        {
            if (world.IsAlive(slot))
            {
                slots.Add(slot);
            }
        }

        return slots;
    }

    static void Traffic(ColumnWorld world, Dice dice, int records, bool edits)
    {
        var pack = world.Pack;
        var live = Alive(world);
        if (edits)
        {
            for (var i = dice.Range(0, 3); i > 0; i--)
            {
                live.Add(world.Spawn(dice.Next(pack.TemplateNames.Count)));
            }

            if (live.Count > 4 && dice.Chance(40))
            {
                var gone = dice.Pick(live);
                world.Despawn(gone);
                live.Remove(gone);
            }

            for (var i = dice.Range(0, 3); i > 0 && live.Count > 0; i--)
            {
                world.Set(dice.Pick(live), dice.Next(pack.LaneNames.Count - 2), dice.Range(-5, 60));
            }
        }

        for (var r = 0; r < records && live.Count > 0; r++)
        {
            var writer = world.Writer(dice.Next(world.Writers));
            var target = dice.Chance(3) ? world.Capacity + dice.Next(50) : dice.Pick(live);
            var channel = dice.Chance(2) ? 20 : dice.Next(pack.ChannelNames.Count);
            var amount = dice.Range(-12, 28);
            if (dice.Chance(30))
            {
                writer.SendFrom(dice.Chance(10) ? -1 : dice.Pick(live), target, channel, amount, dice.Range(1, 3));
            }
            else
            {
                writer.Send(target, channel, amount);
            }
        }
    }

    static void Edit(ColumnWorld world, Dice dice, int frame, bool structure)
    {
        var pack = world.Pack;
        var inputs = world.Inputs(frame);
        var slot = dice.Next(400);
        var channel = dice.Next(pack.ChannelNames.Count);
        var kinds = structure ? 5 : 3;
        ColumnAction action = dice.Next(kinds) switch
        {
            0 => ColumnAction.Send(slot, channel, dice.Range(-40, 40), dice.Range(1, 3)),
            1 => ColumnAction.SendFrom(dice.Next(400), slot, channel, dice.Range(-40, 40), dice.Range(1, 3)),
            2 => ColumnAction.Set(slot, dice.Next(pack.LaneNames.Count - 2), dice.Range(-5, 120)),
            3 => ColumnAction.Despawn(slot),
            _ => ColumnAction.Spawn(dice.Next(pack.TemplateNames.Count)),
        };

        switch (dice.Next(6))
        {
            case 0:
                inputs.InsertAction(dice.Next(inputs.ActionCount + 1), action);
                break;
            case 1 when inputs.ActionCount > 0 && structure:
                inputs.RemoveAction(dice.Next(inputs.ActionCount));
                break;
            case 2 when inputs.ActionCount > 0 && structure:
                inputs.ReplaceAction(dice.Next(inputs.ActionCount), action);
                break;
            case 3 when inputs.RecordCount > 0:
                inputs.RemoveRecord(dice.Next(inputs.RecordCount));
                break;
            case 4 when inputs.RecordCount > 0:
                inputs.ReplaceRecord(dice.Next(inputs.RecordCount), Record.Pack(slot, channel, dice.Next(4), (uint)dice.Next(256), dice.Range(-40, 40)));
                break;
            default:
                inputs.AddRecord(Record.Pack(slot, channel, 0, 0, dice.Range(-40, 40)));
                break;
        }
    }

    static void Same(ColumnWorld classic, ColumnWorld fused)
    {
        Assert.Equal(classic.Frame, fused.Frame);
        Assert.Equal(classic.Digest().ToString("X16"), fused.Digest().ToString("X16"));
        Assert.Equal(classic.Dropped, fused.Dropped);
        Assert.Equal(classic.Count, fused.Count);
        for (var f = classic.OldestFrame; f < classic.Frame; f++)
        {
            var a = classic.Inputs(f);
            var b = fused.Inputs(f);
            Assert.Equal(a.ActionCount, b.ActionCount);
            Assert.Equal(a.RecordCount, b.RecordCount);
            var left = new ulong[a.RecordCount];
            var right = new ulong[b.RecordCount];
            for (var i = 0; i < left.Length; i++)
            {
                left[i] = a.Record(i);
                right[i] = b.Record(i);
            }

            Array.Sort(left);
            Array.Sort(right);
            Assert.Equal(left, right);
        }
    }

    [Theory]
    [InlineData(FrameKernel.Rows, 1, 1, 1ul, true)]
    [InlineData(FrameKernel.Rows, 3, 5, 2ul, true)]
    [InlineData(FrameKernel.Scalar, 2, 3, 3ul, true)]
    [InlineData(FrameKernel.Rows, 1, 1, 4ul, false)]
    [InlineData(FrameKernel.Rows, 4, 7, 5ul, false)]
    [InlineData(FrameKernel.Scalar, 2, 2, 6ul, false)]
    public void FusedFramesAndFusedResimulateMatchTheClassicWorldWithHistory(FrameKernel kernel, int writers, int parts, ulong seed, bool structure)
    {
        using var pack = ArenaPack.Build().Compile();
        const int capacity = 1024;
        const int frames = 60;
        using var classic = new ColumnWorld(pack, capacity, writers, parts, KernelChoice.Best, 8);
        using var fused = new ColumnWorld(pack, capacity, writers, parts, KernelChoice.Best, 8);
        var plan = new Dice(seed * 104729);
        var classicTraffic = new Dice(seed * 7919);
        var fusedTraffic = new Dice(seed * 7919);
        var resimulated = 0;
        for (var f = 0; f < frames; f++)
        {
            var records = plan.Range(0, 300);
            var edits = plan.Chance(70);
            Traffic(classic, classicTraffic, records, edits);
            Traffic(fused, fusedTraffic, records, edits);
            classic.Step();
            fused.StepFused(kernel);
            Same(classic, fused);
            if (f < 3 || !plan.Chance(45))
            {
                continue;
            }

            for (var e = plan.Range(1, 4); e > 0; e--)
            {
                var at = classic.OldestFrame + 2 + plan.Next(classic.Frame - classic.OldestFrame - 2);
                var editSeed = (ulong)plan.Next(1 << 30) + 1;
                Edit(classic, new Dice(editSeed), at, structure);
                Edit(fused, new Dice(editSeed), at, structure);
            }

            classic.Resimulate();
            fused.ResimulateFused(kernel);
            resimulated++;
            Same(classic, fused);
        }

        Assert.True(resimulated > 5);
    }

    [Fact]
    public void RecordsKeptFromOutsideTheWorldReplayLikeWriterRecords()
    {
        using var pack = ArenaPack.Build().Compile();
        const int capacity = 2048;
        using var classic = new ColumnWorld(pack, capacity, 1, 3, KernelChoice.Best, 6);
        using var fused = new ColumnWorld(pack, capacity, 1, 3, KernelChoice.Best, 6);
        var slots = new List<int>();
        for (var i = 0; i < 600; i++)
        {
            var template = i * pack.TemplateNames.Count / 600;
            slots.Add(classic.Spawn(template));
            Assert.Equal(slots[^1], fused.Spawn(template));
        }

        var dice = new Dice(11);
        var view = fused.View;
        var external = new ulong[4000];
        var scratch = new ulong[external.Length];
        var starts = new int[fused.Chunks + 2];
        var buckets = new RecordBucket[fused.FrameRecordSets + 1];
        for (var frame = 0; frame < 24; frame++)
        {
            var count = 0;
            var refused = 0L;
            for (var i = 0; i < external.Length; i++)
            {
                var target = dice.Chance(5) ? capacity + dice.Next(100) : dice.Pick(slots);
                var channel = dice.Chance(2) ? 20 : dice.Next(pack.ChannelNames.Count);
                var amount = dice.Range(-30, 40);
                var source = dice.Chance(10) ? -1 : dice.Pick(slots);
                classic.Writer(0).SendFrom(source, target, channel, amount, 1);
                if (view.TryRecordFrom(source, target, channel, amount, 1, out var record))
                {
                    external[count++] = record;
                }
                else
                {
                    refused++;
                }
            }

            classic.Step();
            var open = fused.OpenFrame();
            fixed (ulong* records = external)
            fixed (ulong* into = scratch)
            fixed (int* at = starts)
            fixed (RecordBucket* all = buckets)
            {
                for (var s = 0; s < open.Writers; s++)
                {
                    open.BucketWriter(s);
                    all[s] = open.WriterBucket(s);
                }

                all[open.Writers] = open.Bucket(records, count, into, at, out var outside);
                for (var p = 0; p < open.Parts; p++)
                {
                    open.Resolve(p, all, open.Writers + 1);
                }

                fused.KeepFrameRecords(records, count);
                fused.CloseFrame(refused, 0);
            }

            Assert.Equal(classic.Digest(), fused.Digest());
            Assert.Equal(classic.Dropped, fused.Dropped);
            if (frame > 6 && frame % 3 == 0)
            {
                var late = classic.Frame - 1 - dice.Next(4);
                var edit = Record.Pack(dice.Pick(slots), dice.Next(pack.ChannelNames.Count), 0, 0, dice.Range(-50, 50));
                classic.Inputs(late).AddRecord(edit);
                fused.Inputs(late).AddRecord(edit);
                classic.Resimulate();
                fused.ResimulateFused();
                Assert.Equal(classic.Digest(), fused.Digest());
                Assert.Equal(classic.Dropped, fused.Dropped);
            }
        }
    }

    [Fact]
    public void ARecordSetAlreadyInChunkOrderIsBucketedInPlace()
    {
        using var pack = ArenaPack.Build().Compile();
        using var world = new ColumnWorld(pack, 4096);
        var slots = new List<int>();
        for (var i = 0; i < 900; i++)
        {
            slots.Add(world.Spawn(i * pack.TemplateNames.Count / 900));
        }

        slots.Sort();
        var ordered = new ulong[3000];
        for (var i = 0; i < ordered.Length; i++)
        {
            ordered[i] = Record.Pack(slots[i * slots.Count / ordered.Length], i % pack.ChannelNames.Count, 0, 0, (i % 9) - 3);
        }

        var shuffled = (ulong[])ordered.Clone();
        new Random(3).Shuffle(shuffled);
        var scratch = new ulong[ordered.Length];
        var starts = new int[world.Chunks + 2];
        var open = world.OpenFrame();
        fixed (ulong* inOrder = ordered)
        fixed (ulong* mixed = shuffled)
        fixed (ulong* into = scratch)
        fixed (int* at = starts)
        {
            var inPlace = open.Bucket(inOrder, ordered.Length, into, at, out var none);
            Assert.True(inPlace.Records == inOrder);
            Assert.Equal(0, none);
            Assert.Equal(ordered.Length, at[world.Chunks]);
            for (var chunk = 0; chunk < world.Chunks; chunk++)
            {
                for (var i = at[chunk]; i < at[chunk + 1]; i++)
                {
                    Assert.Equal(chunk, (int)(ordered[i] >> Record.TargetShift) >> 6);
                }
            }

            var moved = open.Bucket(mixed, shuffled.Length, into, at, out _);
            Assert.True(moved.Records == into);
            Assert.Equal(ordered.Length, at[world.Chunks]);
        }

        world.CloseFrame();
    }

    [Fact]
    public void WarmFusedFramesAndAWarmFusedResimulateAllocateNothing()
    {
        using var pack = ArenaPack.Build().Compile();
        using var world = new ColumnWorld(pack, 4096, 2, 4, KernelChoice.Best, history: 8);
        for (var i = 0; i < 2000; i++)
        {
            world.Spawn(i % pack.TemplateNames.Count);
        }

        void Frame(int f)
        {
            for (var r = 0; r < 3000; r++)
            {
                world.Writer(r & 1).Send((r * 7 + f) % 2000, r % 6, (r % 13) - 4);
            }

            world.StepFused();
            world.Inputs(Math.Max(world.OldestFrame, world.Frame - 4)).AddAction(ColumnAction.Send(f % 2000, 2, 5));
            world.ResimulateFused();
        }

        for (var f = 0; f < 30; f++)
        {
            Frame(f);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var f = 30; f < 60; f++)
        {
            Frame(f);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
