using Sv.Columns.Canon;

namespace Sv.Columns.Tests;

public sealed unsafe class FusedFrameTests
{
    static string ArenaJson
    {
        get
        {
            var folder = AppContext.BaseDirectory;
            while (folder != null && !File.Exists(Path.Combine(folder, "packs", "columns", "arena.json")))
            {
                folder = Path.GetDirectoryName(folder);
            }

            Assert.NotNull(folder);
            return File.ReadAllText(Path.Combine(folder!, "packs", "columns", "arena.json"));
        }
    }

    [Theory]
    [InlineData(FrameKernel.Rows, 1, 1)]
    [InlineData(FrameKernel.Rows, 3, 7)]
    [InlineData(FrameKernel.Scalar, 1, 1)]
    [InlineData(FrameKernel.Scalar, 2, 5)]
    public void CanonRunOnTheFusedFrameGivesThePinnedDigest(FrameKernel kernel, int writers, int parts)
    {
        using var pack = PackJson.Read(ArenaJson).Compile();
        using var world = new ColumnWorld(pack, CanonRun.Capacity, writers, parts);
        Assert.Equal(CanonRun.Pinned, CanonRun.Run(world, () => world.StepFused(kernel)));
    }

    sealed class Trio : IDisposable
    {
        public readonly ColumnWorld Classic;
        public readonly ColumnWorld Rows;
        public readonly ColumnWorld Scalar;

        public Trio(CompiledPack pack, int capacity, int writers, int parts)
        {
            Classic = new ColumnWorld(pack, capacity, writers, parts);
            Rows = new ColumnWorld(pack, capacity, writers, parts);
            Scalar = new ColumnWorld(pack, capacity, writers, parts);
        }

        public IEnumerable<ColumnWorld> All => new[] { Classic, Rows, Scalar };

        public void Step()
        {
            Classic.Step();
            Rows.StepFused(FrameKernel.Rows);
            Scalar.StepFused(FrameKernel.Scalar);
            Assert.Equal(Classic.Digest(), Rows.Digest());
            Assert.Equal(Classic.Digest(), Scalar.Digest());
            Assert.Equal(Classic.Dropped, Rows.Dropped);
            Assert.Equal(Classic.Dropped, Scalar.Dropped);
        }

        public void Dispose()
        {
            foreach (var world in All)
            {
                world.Dispose();
            }
        }
    }

    static void Drive(CompiledPack pack, ulong seed, int frames, int writers, int parts)
    {
        var dice = new Dice(seed ^ 0x5EEDul);
        using var trio = new Trio(pack, 1024, writers, parts);
        var live = new List<int>();
        var lanes = Enumerable.Range(0, pack.LaneNames.Count).Where(l => pack.LaneNames[l][0] != '@').ToList();
        for (var i = dice.Range(1, 400); i > 0; i--)
        {
            var template = dice.Next(pack.TemplateNames.Count);
            var slot = trio.Classic.Spawn(template);
            Assert.Equal(slot, trio.Rows.Spawn(template));
            Assert.Equal(slot, trio.Scalar.Spawn(template));
            live.Add(slot);
        }

        for (var f = 0; f < frames; f++)
        {
            for (var i = dice.Range(0, 600); i > 0; i--)
            {
                var target = dice.Chance(3) ? dice.Range(-3, 1100) : dice.Pick(live);
                var channel = dice.Chance(3) ? dice.Range(-1, 17) : dice.Next(pack.ChannelNames.Count);
                var amount = dice.Chance(5) ? (long)dice.Range(-1, 1) << 40 : dice.Range(-30, 50);
                var hits = dice.Range(1, 3);
                var writer = dice.Next(writers);
                var source = dice.Pick(live);
                var raw = dice.Chance(2);
                var rawRecord = ((ulong)(uint)dice.Range(0, (1 << 22) - 1) << Record.TargetShift) | (ulong)(uint)dice.Range(0, 1 << 20);
                var sourced = dice.Chance(50);
                foreach (var world in trio.All)
                {
                    if (raw)
                    {
                        world.Writer(writer).SendRecord(rawRecord);
                    }
                    else if (sourced)
                    {
                        world.Writer(writer).SendFrom(source, target, channel, amount, hits);
                    }
                    else
                    {
                        world.Writer(writer).Send(target, channel, amount, hits);
                    }
                }
            }

            for (var i = dice.Range(0, 12); i > 0; i--)
            {
                var slot = dice.Pick(live);
                var lane = dice.Pick(lanes);
                var value = dice.Chance(15) ? dice.Range(int.MinValue / 2, int.MaxValue / 2) : dice.Range(-10, 90);
                foreach (var world in trio.All)
                {
                    world.Set(slot, lane, value);
                }
            }

            if (dice.Chance(30) && live.Count > 1)
            {
                var gone = dice.Pick(live);
                live.Remove(gone);
                foreach (var world in trio.All)
                {
                    world.Despawn(gone);
                }
            }

            if (dice.Chance(30) && live.Count < 1000)
            {
                var template = dice.Next(pack.TemplateNames.Count);
                var slot = trio.Classic.Spawn(template);
                Assert.Equal(slot, trio.Rows.Spawn(template));
                Assert.Equal(slot, trio.Scalar.Spawn(template));
                live.Add(slot);
            }

            trio.Step();
        }
    }

    [Fact]
    public void ArenaPackFusedFrameMatchesTheClassicFrameUnderRandomTraffic()
    {
        using var pack = PackJson.Read(ArenaJson).Compile();
        for (ulong seed = 1; seed <= 16; seed++)
        {
            Drive(pack, seed, 24, 1 + (int)(seed % 4), 1 + (int)(seed % 5));
        }
    }

    [Fact]
    public void RandomPacksFusedFrameMatchesTheClassicFrame()
    {
        var compiled = 0;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            CompiledPack pack;
            try
            {
                pack = RandomPacks.Build(seed).Compile();
            }
            catch (PackException)
            {
                continue;
            }

            using (pack)
            {
                Drive(pack, seed, 12, 1 + (int)(seed % 3), 1 + (int)(seed % 4));
            }

            compiled++;
        }

        Assert.True(compiled > 100, "only " + compiled + " random packs compiled");
    }

    [Fact]
    public void ExternalBucketsCountLikeWriterRecords()
    {
        using var pack = PackJson.Read(ArenaJson).Compile();
        using var classic = new ColumnWorld(pack, 2048, 1, 3);
        using var fused = new ColumnWorld(pack, 2048, 1, 3);
        var dice = new Dice(77);
        var slots = new List<int>();
        for (var i = 0; i < 300; i++)
        {
            var template = dice.Next(pack.TemplateNames.Count);
            slots.Add(classic.Spawn(template));
            Assert.Equal(slots[^1], fused.Spawn(template));
        }

        var external = new ulong[5000];
        var sorted = new ulong[external.Length];
        var view = fused.View;
        var buckets = stackalloc RecordBucket[2];
        for (var frame = 0; frame < 20; frame++)
        {
            var count = 0;
            var refused = 0L;
            for (var i = 0; i < external.Length; i++)
            {
                var target = dice.Chance(2) ? 2100 + dice.Next(5000) : dice.Pick(slots);
                var channel = dice.Chance(2) ? 20 : dice.Next(pack.ChannelNames.Count);
                var amount = dice.Range(-40, 60);
                var source = dice.Pick(slots);
                if (dice.Chance(40))
                {
                    classic.Writer(0).SendFrom(source, target, channel, amount, 2);
                    if (view.TryRecordFrom(source, target, channel, amount, 2, out var record))
                    {
                        external[count++] = record;
                    }
                    else
                    {
                        refused++;
                    }
                }
                else
                {
                    classic.Writer(0).Send(target, channel, amount);
                    if (view.TryRecord(target, channel, amount, 1, out var record))
                    {
                        external[count++] = record;
                    }
                    else
                    {
                        refused++;
                    }
                }
            }

            classic.Step();
            var open = fused.OpenFrame();
            var starts = new int[open.StartsLength];
            fixed (ulong* records = external)
            fixed (ulong* into = sorted)
            fixed (int* at = starts)
            {
                open.BucketWriter(0);
                var outside = open.Bucket(records, count, into, at);
                buckets[0] = open.WriterBucket(0);
                buckets[1] = new RecordBucket { Records = into, Starts = at };
                for (var p = 0; p < open.Parts; p++)
                {
                    open.Resolve(p, buckets, 2);
                }

                fused.CloseFrame(refused + outside);
            }

            Assert.Equal(classic.Digest(), fused.Digest());
            Assert.Equal(classic.Dropped, fused.Dropped);
        }
    }

    [Fact]
    public void ViewReadsWhatTheWorldReads()
    {
        using var pack = PackJson.Read(ArenaJson).Compile();
        using var world = new ColumnWorld(pack, 2048);
        var slots = Enumerable.Range(0, 100).Select(i => world.Spawn(i % pack.TemplateNames.Count)).ToList();
        world.Despawn(slots[5]);
        for (var frame = 0; frame < 6; frame++)
        {
            foreach (var slot in slots)
            {
                world.Writer(0).Send(slot, frame % pack.ChannelNames.Count, 40);
            }

            world.StepFused();
        }

        var view = world.View;
        Assert.Equal(world.Capacity, view.Capacity);
        for (var slot = -2; slot < world.Capacity + 2; slot++)
        {
            Assert.Equal(world.IsAlive(slot), view.IsAlive(slot));
            if (!world.IsAlive(slot))
            {
                Assert.Equal(0, view.Get(slot, 0));
                Assert.Equal(-1, view.TemplateOf(slot));
                continue;
            }

            Assert.Equal(world.TemplateOf(slot), view.TemplateOf(slot));
            for (var lane = 0; lane < pack.LaneNames.Count; lane++)
            {
                Assert.Equal(world.Get(slot, lane), view.Get(slot, lane));
            }

            for (var e = 0; e < pack.EventNames.Count; e++)
            {
                Assert.Equal(world.Event(slot, e), view.Event(slot, e));
                Assert.Equal(world.Entered(slot, e), view.Entered(slot, e));
            }
        }
    }

    [Fact]
    public void FrameGuardsHold()
    {
        using var pack = PackJson.Read(ArenaJson).Compile();
        using var world = new ColumnWorld(pack, 128);
        var slot = world.Spawn(0);
        Assert.Throws<InvalidOperationException>(() => world.CloseFrame());
        world.OpenFrame();
        Assert.True(world.FrameOpen);
        Assert.Throws<InvalidOperationException>(() => world.OpenFrame());
        Assert.Throws<InvalidOperationException>(() => world.Spawn(0));
        Assert.Throws<InvalidOperationException>(() => world.Set(slot, 0, 1));
        Assert.Throws<InvalidOperationException>(() => world.Despawn(slot));
        world.CloseFrame();
        Assert.False(world.FrameOpen);
        world.Set(slot, 0, 1);
        world.Dispose();
        Assert.Throws<ObjectDisposedException>(() => world.OpenFrame());
        Assert.Throws<ObjectDisposedException>(() => world.View);
    }

    [Fact]
    public void WarmFusedFramesAllocateNothing()
    {
        using var pack = PackJson.Read(ArenaJson).Compile();
        using var world = new ColumnWorld(pack, 4096, 2, 4);
        var slots = Enumerable.Range(0, 3000).Select(i => world.Spawn(i % pack.TemplateNames.Count)).ToArray();
        void Traffic()
        {
            for (var i = 0; i < slots.Length; i++)
            {
                world.Writer(i & 1).Send(slots[i], i % pack.ChannelNames.Count, 7);
            }
        }

        for (var f = 0; f < 4; f++)
        {
            Traffic();
            world.StepFused();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var f = 0; f < 16; f++)
        {
            Traffic();
            world.StepFused();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
