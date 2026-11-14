using Sv.Host.Net;

namespace Sv.Laws.Tests;

[Collection(nameof(AllocationLaws))]
public sealed unsafe class AllocationLaws
{
    static long WarmBytes(Action run)
    {
        run();
        run();
        var least = long.MaxValue;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            run();
            var after = GC.GetAllocatedBytesForCurrentThread();
            least = Math.Min(least, after - before);
        }

        return least;
    }

    [Fact]
    public void SendAndSealAllocateNothingWarm()
    {
        var gen = new Gen(61);
        var live = SealHarness.LiveWords(gen);
        var records = SealHarness.Records(gen, SendShape.Uniform, 20000).ToArray();
        using var raw = new RawArena(records.Length, 3, ArenaPolicy.Grow);
        using var canonical = new CanonicalArena(SealHarness.Chunks, 3, int.MaxValue, ArenaPolicy.Grow);
        var words = (ulong*)Memory.Alloc((nuint)live.Length, sizeof(ulong));
        try
        {
            for (var chunk = 0; chunk < live.Length; chunk++)
            {
                words[chunk] = live[chunk];
            }

            var route = new RouteTable(words, null, live.Length);
            var bytes = WarmBytes(() =>
            {
                var mark = raw.Mark();
                for (var at = 0; at < records.Length; at++)
                {
                    raw.Writer(at % 3)->Send(records[at].Key, records[at].Amount, records[at].Hits);
                }

                raw.EndLayer(mark);
                var forward = default(NoForward);
                canonical.Run(raw, route, ref forward);
            });
            Assert.Equal(0, bytes);
            Assert.True(canonical.Count > 0);
        }
        finally
        {
            Memory.Free(words);
        }
    }

    [Fact]
    public void StatLawAllocatesNothingWarm()
    {
        var terms = (ShapeTerm*)Memory.Alloc(8, (nuint)sizeof(ShapeTerm));
        var lane = (int*)Memory.Alloc(Lanes.Rows, sizeof(int));
        var output = (int*)Memory.Alloc(Lanes.Rows, sizeof(int));
        try
        {
            var gen = new Gen(62);
            for (var row = 0; row < Lanes.Rows; row++)
            {
                lane[row] = gen.EdgeOrAny();
            }

            var sink = 0;
            var bytes = WarmBytes(() =>
            {
                for (var trial = 0; trial < 1000; trial++)
                {
                    for (var at = 0; at < 8; at++)
                    {
                        terms[at] = new ShapeTerm((ShapeKind)((trial + at) & 3), lane[(trial + at) & 63]);
                    }

                    StatLaw.Canonicalize(terms, 8);
                    sink ^= StatLaw.Fold(lane[trial & 63], terms, 8);
                    StatLaw.Rescale(lane, 1000, 10, output);
                    StatLaw.Rescale(lane, 10, 1000, output);
                    StatLaw.Cap(lane, output, output);
                    sink ^= (int)StatLaw.Below(lane, output, 3, 10);
                }
            });
            Assert.Equal(0, bytes);
        }
        finally
        {
            Memory.Free(terms);
            Memory.Free(lane);
            Memory.Free(output);
        }
    }

    [Fact]
    public void LaneMathAndCommitFoldAllocateNothingWarm()
    {
        var a = (int*)Memory.Alloc(Lanes.Rows, sizeof(int));
        var b = (int*)Memory.Alloc(Lanes.Rows, sizeof(int));
        var c = (int*)Memory.Alloc(Lanes.Rows, sizeof(int));
        var o = (int*)Memory.Alloc(Lanes.Rows, sizeof(int));
        var f = (float*)Memory.Alloc(Lanes.Rows, sizeof(float));
        try
        {
            var gen = new Gen(63);
            for (var row = 0; row < Lanes.Rows; row++)
            {
                a[row] = gen.EdgeOrAny();
                b[row] = gen.EdgeOrAny();
                c[row] = gen.EdgeOrAny();
                f[row] = gen.EdgeOrAny() / 3f;
            }

            var sink = 0ul;
            var bytes = WarmBytes(() =>
            {
                for (var trial = 0; trial < 1000; trial++)
                {
                    LaneMath.AddSaturate(a, b, o);
                    LaneMath.Clamp(a, b, c, o);
                    LaneMath.BoundOf(f, o);
                    LaneMath.Select(sink, a, b, o);
                    sink ^= Mask.Positive(o) ^ Mask.Differ(a, o) ^ Mask.Less(a, b);
                    sink ^= CommitFold.Lane(Widths.Widest(), o, a, b, c, ulong.MaxValue);
                }
            });
            Assert.Equal(0, bytes);
        }
        finally
        {
            Memory.Free(a);
            Memory.Free(b);
            Memory.Free(c);
            Memory.Free(o);
            Memory.Free(f);
        }
    }

    [Theory]
    [InlineData(StoragePolicy.Shared)]
    [InlineData(StoragePolicy.Dense)]
    public void WritesEpochMarksDigestAndRewindAllocateNothingWarm(StoragePolicy policy)
    {
        using var world = TestWorlds.Create(policy, framesPerEpoch: 1);
        var slots = new int[40];
        for (var at = 0; at < slots.Length; at++)
        {
            slots[at] = world.Acquire(at & 1);
        }

        var image = (byte*)Memory.Alloc((nuint)ChunkImage.MaxBytes(world.Layout));
        try
        {
            var value = 0;
            var bytes = WarmBytes(() =>
            {
                for (var frame = 0; frame < 40; frame++)
                {
                    var epoch = world.Epoch;
                    for (var at = 0; at < slots.Length; at++)
                    {
                        world.Write(slots[at], 1 + (at & 1), value++);
                        world.WriteLevel(slots[at], EventId.Of(TestWorlds.EventLane, at & 31), (value & 1) == 0);
                    }

                    world.EndFrame();
                    world.Digest();
                    var length = world.SaveChunk(0, image, ChunkImage.MaxBytes(world.Layout));
                    world.LoadChunk(0, image, length);
                    if ((frame & 7) == 7)
                    {
                        world.Rewind(epoch);
                    }
                }
            });
            Assert.Equal(0, bytes);
        }
        finally
        {
            Memory.Free(image);
        }
    }

    struct QuietStep : IFrameStep
    {
        public void Step(World world, in FrameInputs inputs, ref EventBuffer published)
        {
            for (var at = 0; at < inputs.RecordCount; at++)
            {
                var target = EffectKey.Target(inputs.Records[at].Key);
                if (world.StateOf(target) == SlotState.Live)
                {
                    world.Write(target, 1, LaneMath.AddSaturate(world.Read(target, 1), inputs.Records[at].Amount));
                }
            }
        }
    }

    [Fact]
    public void InputLogAndReplayAllocateNothingWarm()
    {
        using var world = TestWorlds.Create(StoragePolicy.Shared, framesPerEpoch: 1);
        using var log = new InputLog(InputLog.SpanOf(world), world.ChunkCapacity, 0);
        using var events = new EventLog(InputLog.SpanOf(world));
        using var replay = new Replay(log, events);
        var slots = Enumerable.Range(0, 30).Select(at => world.Acquire(0)).ToArray();
        var records = slots.Select(slot => new LoggedRecord(EffectKey.Pack(slot, 0, 0, 0, Origin.Direct, 0), 3, 1)).ToArray();
        var pinned = (LoggedRecord*)Memory.Alloc((nuint)records.Length, (nuint)sizeof(LoggedRecord));
        try
        {
            for (var at = 0; at < records.Length; at++)
            {
                pinned[at] = records[at];
            }

            var step = default(QuietStep);
            var bytes = WarmBytes(() =>
            {
                for (var frame = 0; frame < 20; frame++)
                {
                    var now = world.Frame;
                    log.Record(now, pinned, records.Length);
                    replay.Run(world, now + 1, ref step);
                }

                var epoch = world.EpochAtOrBefore(world.Frame - 10);
                var end = world.Frame;
                world.Rewind(epoch);
                replay.Run(world, end, ref step);
            });
            Assert.Equal(0, bytes);
        }
        finally
        {
            Memory.Free(pinned);
        }
    }
}
