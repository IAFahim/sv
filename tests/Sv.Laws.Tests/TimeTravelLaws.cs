using System.Reflection;

namespace Sv.Laws.Tests;

public sealed unsafe class TimeTravelLaws
{
    public static IEnumerable<object[]> PoliciesAndSeeds() =>
        from policy in new[] { StoragePolicy.Shared, StoragePolicy.Dense }
        from seed in new[] { 1ul, 2ul, 3ul }
        select new object[] { policy, seed };

    sealed class EpochHistory
    {
        public readonly Dictionary<uint, int[]> Cells = new();
        public readonly Dictionary<uint, ulong> Digests = new();
        public readonly Dictionary<uint, long> Frames = new();
        public readonly Dictionary<uint, byte[][]> Images = new();
    }

    static void Remember(World world, EpochHistory history)
    {
        history.Cells[world.Epoch] = TestWorlds.Snapshot(world);
        history.Digests[world.Epoch] = world.Digest();
        history.Frames[world.Epoch] = world.Frame;
        history.Images[world.Epoch] = Enumerable.Range(0, world.ChunkCapacity).Select(chunk => TestWorlds.Image(world, chunk)).ToArray();
    }

    static World RunEpochs(StoragePolicy policy, ulong seed, int frames, EpochHistory history)
    {
        var gen = new Gen(seed);
        var world = TestWorlds.Create(policy, entities: 384, framesPerEpoch: 1);
        var live = new List<int>();
        Remember(world, history);
        for (var frame = 0; frame < frames; frame++)
        {
            TestWorlds.Mutate(world, gen, live, 25);
            world.EndFrame();
            Remember(world, history);
        }

        TestWorlds.Mutate(world, gen, live, 25);
        return world;
    }

    static void AssertRestored(World world, EpochHistory history, uint epoch, string context)
    {
        Assert.True(world.Rewind(epoch), $"{context}: rewind to {epoch} refused");
        Assert.Equal(epoch, world.Epoch);
        Assert.Equal(history.Frames[epoch], world.Frame);
        Assert.True(history.Cells[epoch].AsSpan().SequenceEqual(TestWorlds.Snapshot(world)), $"{context}: lanes differ after rewind to {epoch}");
        Assert.Equal(history.Digests[epoch], world.Digest());
        for (var chunk = 0; chunk < world.ChunkCapacity; chunk++)
        {
            Assert.True(history.Images[epoch][chunk].AsSpan().SequenceEqual(TestWorlds.Image(world, chunk)), $"{context}: chunk {chunk} image differs at {epoch}");
        }
    }

    [Theory]
    [MemberData(nameof(PoliciesAndSeeds))]
    public void RewindingThroughEveryEpochOfTheRingRestoresBitIdenticalLanes(StoragePolicy policy, ulong seed)
    {
        var history = new EpochHistory();
        using var world = RunEpochs(policy, seed, 45, history);
        Assert.Equal(Journal.DefaultRing, (int)(world.Epoch - world.OldestEpoch + 1));
        var oldest = world.OldestEpoch;
        for (var epoch = world.Epoch; epoch >= oldest; epoch--)
        {
            AssertRestored(world, history, epoch, $"seed {seed} {policy}");
        }
    }

    [Theory]
    [InlineData(StoragePolicy.Shared, 4ul)]
    [InlineData(StoragePolicy.Dense, 4ul)]
    public void RewindingStraightToAnyEpochOfTheRingRestoresBitIdenticalLanes(StoragePolicy policy, ulong seed)
    {
        for (var back = 0; back < Journal.DefaultRing; back++)
        {
            var history = new EpochHistory();
            using var world = RunEpochs(policy, seed, 40, history);
            AssertRestored(world, history, world.Epoch - (uint)back, $"seed {seed} {policy} back {back}");
        }
    }

    [Theory]
    [InlineData(StoragePolicy.Shared)]
    [InlineData(StoragePolicy.Dense)]
    public void RewindOutsideTheRingIsRefusedAndChangesNothing(StoragePolicy policy)
    {
        var history = new EpochHistory();
        using var world = RunEpochs(policy, 6, 40, history);
        var before = TestWorlds.Snapshot(world);
        Assert.False(world.Rewind(world.OldestEpoch - 1));
        Assert.False(world.Rewind(world.Epoch + 1));
        Assert.False(world.Rewind(0));
        Assert.True(before.AsSpan().SequenceEqual(TestWorlds.Snapshot(world)));
    }

    [Theory]
    [InlineData(StoragePolicy.Shared)]
    [InlineData(StoragePolicy.Dense)]
    public void AKilledSlotIsHeldWhileItsDeathEpochIsInsideTheRingAndThenReusedWithDefaults(StoragePolicy policy)
    {
        const int ring = 4;
        using var world = TestWorlds.Create(policy, ring: ring, framesPerEpoch: 1);
        world.Acquire(0);
        var killed = world.Acquire(0);
        world.Acquire(0);
        world.Write(killed, 1, 77);
        Assert.True(world.Release(killed));
        var death = world.Epoch;
        Assert.Equal(SlotState.Dying, world.StateOf(killed));
        Assert.Equal(death, world.DeathEpochOf(killed));
        Assert.False(world.Write(killed, 1, 5));
        Assert.False(world.Release(killed));
        while (world.Epoch < death + ring)
        {
            Assert.NotEqual(killed, world.Acquire(0));
            world.EndFrame();
        }

        Assert.Equal(killed, world.Acquire(0));
        Assert.Equal(SlotState.Live, world.StateOf(killed));
        Assert.Equal(0, world.Read(killed, 1));
        Assert.Equal(7, world.Read(killed, 2));
    }

    [Theory]
    [InlineData(StoragePolicy.Shared)]
    [InlineData(StoragePolicy.Dense)]
    public void RewindBeforeAKillBringsTheEntityBackWithItsValues(StoragePolicy policy)
    {
        using var world = TestWorlds.Create(policy, framesPerEpoch: 1);
        var slot = world.Acquire(0);
        world.Write(slot, 1, 77);
        world.EndFrame();
        var epoch = world.Epoch;
        world.Release(slot);
        world.EndFrame();
        world.EndFrame();
        Assert.True(world.Rewind(epoch));
        Assert.Equal(SlotState.Live, world.StateOf(slot));
        Assert.Equal(77, world.Read(slot, 1));
    }

    struct CountingStep : IFrameStep
    {
        public Dictionary<long, ulong> Digests;
        public Dictionary<long, string> Spawned;

        public void Step(World world, in FrameInputs inputs, ref EventBuffer published)
        {
            var spawned = new List<int>();
            for (var at = 0; at < inputs.CommandCount; at++)
            {
                spawned.Add(Replay.Apply(world, inputs.Commands[at]));
            }

            for (var at = 0; at < inputs.RecordCount; at++)
            {
                var record = inputs.Records[at];
                var target = EffectKey.Target(record.Key);
                if (world.StateOf(target) != SlotState.Live)
                {
                    continue;
                }

                var lane = 1 + EffectKey.Channel(record.Key) % 2;
                var after = LaneMath.AddSaturate(world.Read(target, lane), EffectLaw.Saturate32(EffectLaw.Term(record.Amount, record.Hits)));
                world.Write(target, lane, after);
                var raised = EventId.Of(TestWorlds.EventLane, 0);
                var low = after < 0;
                if (world.Level(target, raised) != low)
                {
                    world.WriteLevel(target, raised, low);
                    published.Add(new OutwardEvent(target, raised, low));
                }
            }

            Digests[inputs.Frame] = world.Digest();
            Spawned[inputs.Frame] = string.Join(",", spawned);
        }
    }

    sealed class LoggedRun : IDisposable
    {
        public readonly World World;
        public readonly InputLog Log;
        public readonly EventLog Events;
        public readonly Replay Replay;
        public CountingStep Step;

        public LoggedRun(StoragePolicy policy)
        {
            World = TestWorlds.Create(policy, entities: 384);
            Log = new InputLog(InputLog.SpanOf(World), World.ChunkCapacity, 0);
            Events = new EventLog(InputLog.SpanOf(World));
            Replay = new Replay(Log, Events);
            Step = new CountingStep { Digests = new Dictionary<long, ulong>(), Spawned = new Dictionary<long, string>() };
        }

        public void Dispose()
        {
            Replay.Dispose();
            Events.Dispose();
            Log.Dispose();
            World.Dispose();
        }
    }

    static LoggedRecord[] Canonical(IEnumerable<Sent> records)
    {
        var all = Enumerable.Repeat(ulong.MaxValue, Slot.MaxChunks).ToArray();
        return SealHarness.Reference(records, all).Select(record => new LoggedRecord(record.Key, record.Amount, record.Hits)).ToArray();
    }

    static List<Sent> FrameRecords(Gen gen, World world, int count)
    {
        var live = TestWorlds.LiveSlots(world);
        var records = new List<Sent>();
        for (var at = 0; at < count && live.Count > 0; at++)
        {
            var target = gen.Chance(5) ? gen.Below(world.ChunkCapacity * Slot.Rows) : live[gen.Below(live.Count)];
            var origin = gen.Chance(15) ? Origin.Reflected : gen.Chance(30) ? Origin.Timeline : Origin.Direct;
            var key = EffectKey.Pack(target, gen.Chance(20) ? EffectKey.NoSource : gen.Below(64), (byte)gen.Below(3), (byte)gen.Below(4), origin, origin == Origin.Reflected ? 1 : 0);
            records.Add(new Sent(key, gen.Between(-40, 30), gen.Between(1, 3)));
        }

        return records;
    }

    static void LogFrame(LoggedRun run, Gen gen, long frame, IEnumerable<Sent> extra)
    {
        var live = TestWorlds.LiveSlots(run.World);
        var spawns = gen.Below(3);
        for (var at = 0; at < spawns; at++)
        {
            run.Log.Spawn(frame, gen.Below(2), -1);
        }

        if (live.Count > 4 && gen.Chance(25))
        {
            run.Log.Kill(frame, live[gen.Below(live.Count)]);
        }

        var records = Canonical(FrameRecords(gen, run.World, 30).Concat(extra));
        fixed (LoggedRecord* pointer = records)
        {
            run.Log.Record(frame, pointer, records.Length);
        }
    }

    static void RunStraight(LoggedRun run, Gen gen, int frames, long lateFrame = -1, List<Sent>? late = null, bool logLate = true)
    {
        for (var frame = 0L; frame < frames; frame++)
        {
            var extra = new List<Sent>();
            if (frame == lateFrame)
            {
                late!.AddRange(FrameRecords(gen, run.World, 12).Where(record => EffectKey.OriginOf(record.Key) != Origin.Reflected));
                if (logLate)
                {
                    extra.AddRange(late);
                }
            }

            LogFrame(run, gen, frame, extra);
            run.Replay.Run(run.World, frame + 1, ref run.Step);
        }
    }

    [Theory]
    [MemberData(nameof(PoliciesAndSeeds))]
    public void ReplayFromEveryEpochOfTheRingReproducesEveryDigestOfTheStraightRun(StoragePolicy policy, ulong seed)
    {
        using var run = new LoggedRun(policy);
        RunStraight(run, new Gen(seed), 70);
        var straight = new Dictionary<long, ulong>(run.Step.Digests);
        var spawned = new Dictionary<long, string>(run.Step.Spawned);
        var now = run.World.Frame;
        var finalCells = TestWorlds.Snapshot(run.World);
        var oldest = run.World.OldestEpoch;
        for (var epoch = run.World.Epoch; epoch >= oldest; epoch--)
        {
            run.Step.Digests.Clear();
            run.Step.Spawned.Clear();
            run.Replay.ClearChanges();
            Assert.True(run.World.Rewind(epoch), $"seed {seed} {policy}: rewind to {epoch} refused");
            var from = run.World.Frame;
            run.Replay.Run(run.World, now, ref run.Step);
            for (var frame = from; frame < now; frame++)
            {
                Assert.True(straight[frame] == run.Step.Digests[frame], $"seed {seed} {policy}: epoch {epoch} frame {frame} digest {run.Step.Digests[frame]:X16}, straight {straight[frame]:X16}");
                Assert.Equal(spawned[frame], run.Step.Spawned[frame]);
            }

            Assert.Equal(0, run.Replay.ChangeCount);
            Assert.True(finalCells.AsSpan().SequenceEqual(TestWorlds.Snapshot(run.World)), $"seed {seed} {policy}: lanes differ after replay from {epoch}");
        }
    }

    [Theory]
    [MemberData(nameof(PoliciesAndSeeds))]
    public void LateInputsReplayedFromTheirEpochEqualARunThatHadThemOnTime(StoragePolicy policy, ulong seed)
    {
        const int frames = 60;
        const long lateFrame = 41;
        using var onTime = new LoggedRun(policy);
        RunStraight(onTime, new Gen(seed), frames, lateFrame, new List<Sent>(), true);
        using var delayed = new LoggedRun(policy);
        var late = new List<Sent>();
        RunStraight(delayed, new Gen(seed), frames, lateFrame, late, false);
        var records = Canonical(late);
        fixed (LoggedRecord* pointer = records)
        {
            Assert.Equal(lateFrame, delayed.Replay.Late(delayed.World, lateFrame, pointer, records.Length, LatePolicy.Clamp));
        }

        Assert.True(delayed.Replay.Resolve(delayed.World, ref delayed.Step));
        Assert.Equal(onTime.World.Frame, delayed.World.Frame);
        for (var frame = lateFrame; frame < frames; frame++)
        {
            Assert.True(onTime.Step.Digests[frame] == delayed.Step.Digests[frame], $"seed {seed} {policy}: frame {frame}");
        }

        Assert.Equal(onTime.World.Digest(), delayed.World.Digest());
    }

    [Fact]
    public void InputLogKeepsOnlyDirectAndTimelineRecords()
    {
        using var log = new InputLog(8, 16, 0);
        var records = Canonical(new[]
        {
            new Sent(EffectKey.Pack(5, 1, 0, 0, Origin.Direct, 0), 3, 1),
            new Sent(EffectKey.Pack(5, 1, 0, 0, Origin.Timeline, 0), 3, 1),
            new Sent(EffectKey.Pack(5, 1, 0, 0, Origin.Reflected, 1), 3, 1),
            new Sent(EffectKey.Pack(70, 1, 0, 0, Origin.Triggered, 1), 3, 1),
        });
        fixed (LoggedRecord* pointer = records)
        {
            log.Record(2, pointer, records.Length);
        }

        Assert.Equal(2, log.RecordCount(2));
        var copy = new LoggedRecord[2];
        fixed (LoggedRecord* destination = copy)
        {
            Assert.Equal(2, log.CopyRecords(2, destination, copy.Length));
        }

        Assert.All(copy, record => Assert.True(LoggedRecordLaw.External(record.Key)));
    }

    [Fact]
    public void InputLogForgetsFramesOlderThanItsSpan()
    {
        using var log = new InputLog(4, 16, 0);
        var records = Canonical(new[] { new Sent(EffectKey.Pack(5, 1, 0, 0, Origin.Direct, 0), 3, 1) });
        fixed (LoggedRecord* pointer = records)
        {
            for (var frame = 0L; frame < 9; frame++)
            {
                log.Record(frame, pointer, records.Length);
            }
        }

        for (var frame = 0L; frame < 9; frame++)
        {
            Assert.Equal(frame >= 5, log.Holds(frame));
        }
    }

    [Fact]
    public void LogSpanCoversTheRingPlusOneEpoch()
    {
        using var world = TestWorlds.Create(StoragePolicy.Shared, ring: 32, framesPerEpoch: 2);
        Assert.Equal(66, InputLog.SpanOf(world));
    }

    [Fact]
    public void SegmentTableCoversExactlyTheThirtyTwoBitLaneSpace()
    {
        Assert.Equal(1L << 32, (long)LanePool.MaxSegments << Lanes.SegmentShift);
        Assert.Equal(4L * 1024 * 1024, (long)LanePool.MaxSegments * sizeof(IntPtr));
        Assert.Equal(Lanes.SegmentLanes * sizeof(uint), Lanes.HeaderLanes * Lanes.Bytes);
        Assert.Equal(WorldLayout.MaxLanesPerChunk, Lanes.UsableLanes);
        Assert.Equal(2 * 1024 * 1024, Lanes.SegmentBytes);
    }

    [Fact]
    public void PoolHandsOutOnlyUsableLanesAcrossSegmentBoundaries()
    {
        using var pool = new LanePool();
        var view = default(WorldView);
        view.Segments = pool.Segments;
        var taken = new HashSet<uint>();
        for (var at = 0; at < 2 * Lanes.UsableLanes + 5; at++)
        {
            var lane = pool.Take();
            Assert.True(taken.Add(lane));
            Assert.True((lane & Lanes.SegmentMask) >= Lanes.HeaderLanes, $"lane {lane} is a header lane");
            var segment = view.Segments[lane >> Lanes.SegmentShift];
            Assert.True(Lanes.At(view, lane) >= segment + Lanes.HeaderLanes * Lanes.Bytes);
            Assert.True(Lanes.At(view, lane) + Lanes.Bytes <= segment + Lanes.SegmentBytes);
            Assert.True((byte*)Lanes.EpochOf(view, lane) < segment + Lanes.HeaderLanes * Lanes.Bytes);
        }

        Assert.Equal(3, pool.SegmentCount);
    }

    [Fact]
    public void PoolRefusesReservationsOutsideOneSegment()
    {
        using var pool = new LanePool();
        Assert.Throws<ArgumentOutOfRangeException>(() => pool.AddSegment(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => pool.AddSegment(Lanes.UsableLanes + 1));
        pool.AddSegment(Lanes.UsableLanes);
        Assert.Equal(0, pool.Available);
        Assert.Equal(1, pool.SegmentCount);
    }

    [Fact]
    public void PoolRefusesASegmentPastTheTable()
    {
        using var pool = new LanePool();
        var count = typeof(LanePool).GetField("segmentCount", BindingFlags.NonPublic | BindingFlags.Instance)!;
        count.SetValue(pool, LanePool.MaxSegments);
        try
        {
            Assert.Throws<InvalidOperationException>(() => pool.AddSegment(0));
        }
        finally
        {
            count.SetValue(pool, 0);
        }
    }

    [Theory]
    [InlineData(StoragePolicy.Shared)]
    [InlineData(StoragePolicy.Dense)]
    public void AChunkOfTheMostLanesFitsOneSegmentAndOneMoreIsRefused(StoragePolicy policy)
    {
        var classes = Enumerable.Repeat(LaneClass.Intrinsic, WorldLayout.MaxLanesPerChunk - WorldLayout.MetaLanes).ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => World.Create(classes.Append(LaneClass.Intrinsic).ToArray(), 1, 64, policy));
        using var world = World.Create(classes, 1, 128, policy, framesPerEpoch: 1);
        world.AddTemplate(Enumerable.Repeat(9u, classes.Length).ToArray());
        var first = world.Acquire(0);
        var last = classes.Length - 1;
        world.EndFrame();
        var epoch = world.Epoch;
        world.Write(first, last, -5);
        Assert.Equal(-5, world.Read(first, last));
        Assert.Equal(9, world.Read(first, last - 1));
        world.EndFrame();
        Assert.True(world.Rewind(epoch));
        Assert.Equal(9, world.Read(first, last));
    }
}
