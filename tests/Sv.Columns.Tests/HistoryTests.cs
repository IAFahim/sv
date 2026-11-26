using Sv.Columns.Canon;
using Sv.Host;

namespace Sv.Columns.Tests;

public sealed class FrameScript
{
    public readonly List<List<ColumnAction>> Actions = new();
    public readonly List<List<ulong>> Records = new();

    public void Capture(ColumnWorld world, int frame)
    {
        while (Actions.Count <= frame)
        {
            Actions.Add(new List<ColumnAction>());
            Records.Add(new List<ulong>());
        }

        var inputs = world.Inputs(frame);
        Actions[frame].Clear();
        Records[frame].Clear();
        for (var i = 0; i < inputs.ActionCount; i++)
        {
            Actions[frame].Add(inputs.Action(i));
        }

        for (var i = 0; i < inputs.RecordCount; i++)
        {
            Records[frame].Add(inputs.Record(i));
        }
    }

    public static void Apply(ColumnWorld world, ColumnAction action)
    {
        switch (action.Kind)
        {
            case ColumnActionKind.Spawn:
                try
                {
                    if (action.Template >= 0 && action.Template < world.Pack.TemplateNames.Count)
                    {
                        world.Spawn(action.Template);
                    }
                }
                catch (InvalidOperationException)
                {
                }

                break;
            case ColumnActionKind.Despawn:
                if (world.IsAlive(action.Slot))
                {
                    world.Despawn(action.Slot);
                }

                break;
            case ColumnActionKind.Set:
                if (world.IsAlive(action.Slot) && action.Lane >= 0 && action.Lane < world.Pack.LaneNames.Count - 2)
                {
                    world.Set(action.Slot, action.Lane, action.Value);
                }

                break;
            case ColumnActionKind.Send:
                world.Writer(0).Send(action.Slot, action.Channel, action.Amount, action.Hits);
                break;
            case ColumnActionKind.SendFrom:
                world.Writer(0).Send(action.Slot, action.Source, action.Channel, action.Amount, action.Hits);
                break;
        }
    }

    public ulong[] Fresh(CompiledPack pack, int capacity, int frames, KernelChoice kernels = KernelChoice.Scalar)
    {
        using var world = new ColumnWorld(pack, capacity, 1, 1, kernels);
        var digests = new ulong[frames + 1];
        digests[0] = world.Digest();
        for (var f = 0; f < frames; f++)
        {
            foreach (var action in Actions[f])
            {
                Apply(world, action);
            }

            foreach (var record in Records[f])
            {
                world.Writer(0).SendRecord(record);
            }

            world.Step();
            digests[f + 1] = world.Digest();
        }

        return digests;
    }
}

public sealed class HistoryTests
{
    static CompiledPack Arena() => ArenaPack.Build().Compile();

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
            var target = dice.Pick(live);
            var channel = dice.Next(pack.ChannelNames.Count);
            var amount = dice.Range(-12, 28);
            if (dice.Chance(30))
            {
                writer.Send(target, dice.Pick(live), channel, amount, dice.Range(1, 3));
            }
            else
            {
                writer.Send(target, channel, amount);
            }
        }
    }

    [Theory]
    [InlineData(KernelChoice.Scalar, 1, 1)]
    [InlineData(KernelChoice.Best, 1, 1)]
    [InlineData(KernelChoice.Best, 3, 5)]
    [InlineData(KernelChoice.Vector, 2, 3)]
    public void KeepingHistoryLeavesTheCanonDigestPinned(KernelChoice kernels, int writers, int parts)
    {
        using var pack = PackJson.Read(File.ReadAllText(PackPath())).Compile();
        using var world = new ColumnWorld(pack, CanonRun.Capacity, writers, parts, kernels, history: 6);
        Assert.Equal(CanonRun.Pinned.ToString("X16"), CanonRun.Run(world, world.Step).ToString("X16"));
    }

    [Theory]
    [InlineData(KernelChoice.Scalar, 1, 1)]
    [InlineData(KernelChoice.Best, 1, 1)]
    [InlineData(KernelChoice.Best, 3, 7)]
    [InlineData(KernelChoice.Vector, 2, 5)]
    public void TheCanonLateInputRunEqualsInputOnTimeAndIsPinned(KernelChoice kernels, int writers, int parts)
    {
        using var pack = PackJson.Read(File.ReadAllText(PackPath())).Compile();
        using var onTime = new ColumnWorld(pack, CanonRun.Capacity, writers, parts, kernels);
        using var late = new ColumnWorld(pack, CanonRun.Capacity, writers, parts, kernels, CanonRun.LateHistory);
        var expected = CanonRun.RunWithInputOnTime(onTime, onTime.Step);
        Assert.Equal(expected.ToString("X16"), CanonRun.RunWithLateInput(late, late.Step, late.Resimulate).ToString("X16"));
        Assert.Equal(CanonRun.PinnedLate.ToString("X16"), expected.ToString("X16"));
    }

    [Fact]
    public void TheCanonLateInputRunOnWorkersIsPinned()
    {
        using var pack = PackJson.Read(File.ReadAllText(PackPath())).Compile();
        using var world = new ColumnWorld(pack, CanonRun.Capacity, 3, 9, KernelChoice.Best, CanonRun.LateHistory);
        using var workers = new Workers(3);
        using var runner = new ColumnRunner(world, workers);
        Assert.Equal(CanonRun.PinnedLate.ToString("X16"), CanonRun.RunWithLateInput(world, runner.Step, runner.Resimulate).ToString("X16"));
    }

    static string PackPath()
    {
        var folder = AppContext.BaseDirectory;
        while (folder != null && !File.Exists(Path.Combine(folder, "packs", "columns", "arena.json")))
        {
            folder = Path.GetDirectoryName(folder);
        }

        return Path.Combine(folder!, "packs", "columns", "arena.json");
    }

    [Theory]
    [InlineData(KernelChoice.Scalar, 1, 1)]
    [InlineData(KernelChoice.Best, 2, 4)]
    [InlineData(KernelChoice.Vector, 3, 2)]
    public void TheLogReplaysEveryFrameOfALiveRun(KernelChoice kernels, int writers, int parts)
    {
        using var pack = Arena();
        const int frames = 40;
        using var world = new ColumnWorld(pack, 1024, writers, parts, kernels, history: frames);
        var dice = new Dice(77);
        var live = new ulong[frames + 1];
        live[0] = world.Digest();
        for (var f = 0; f < frames; f++)
        {
            Traffic(world, dice, 300, edits: true);
            world.Step();
            live[f + 1] = world.Digest();
        }

        var script = new FrameScript();
        for (var f = 0; f < frames; f++)
        {
            script.Capture(world, f);
        }

        Assert.Equal(live, script.Fresh(pack, 1024, frames));
    }

    [Theory]
    [InlineData(KernelChoice.Scalar, 1, 1)]
    [InlineData(KernelChoice.Best, 3, 5)]
    [InlineData(KernelChoice.Vector, 2, 2)]
    public void RewindGoesBackToTheDigestOfThatFrameAndResteppingMatches(KernelChoice kernels, int writers, int parts)
    {
        using var pack = Arena();
        using var world = new ColumnWorld(pack, 1024, writers, parts, kernels, history: 8);
        var digests = new List<ulong> { world.Digest() };
        var seeds = new List<ulong>();
        for (var f = 0; f < 30; f++)
        {
            seeds.Add((ulong)(f * 31 + 5));
            Traffic(world, new Dice(seeds[f]), 400, edits: true);
            world.Step();
            digests.Add(world.Digest());
        }

        foreach (var back in new[] { 30, 29, 25, 22 })
        {
            world.Rewind(back);
            Assert.Equal(back, world.Frame);
            Assert.Equal(digests[back], world.Digest());
            for (var f = back; f < 30; f++)
            {
                Traffic(world, new Dice(seeds[f]), 400, edits: true);
                world.Step();
                Assert.Equal(digests[f + 1], world.Digest());
            }
        }
    }

    [Fact]
    public void RewindDropsWhatWasSentForTheFramesAfterIt()
    {
        using var pack = Arena();
        using var world = new ColumnWorld(pack, 256, 1, 1, KernelChoice.Best, history: 4);
        var hero = world.Spawn(pack.Template("dummy"));
        world.Step();
        world.Writer(0).Send(hero, pack.Channel("Physical"), 30);
        world.Step();
        Assert.Equal(70, world.Get(hero, pack.Lane("Health")));
        world.Writer(0).Send(hero, pack.Channel("Physical"), 10);
        world.Rewind(1);
        Assert.Equal(100, world.Get(hero, pack.Lane("Health")));
        world.Step();
        Assert.Equal(100, world.Get(hero, pack.Lane("Health")));
        Assert.Equal(0, world.Inputs(1).RecordCount);
    }

    [Theory]
    [InlineData(KernelChoice.Scalar, 1, 1, 1ul)]
    [InlineData(KernelChoice.Best, 1, 1, 2ul)]
    [InlineData(KernelChoice.Best, 3, 5, 3ul)]
    [InlineData(KernelChoice.Vector, 2, 3, 4ul)]
    [InlineData(KernelChoice.Best, 4, 7, 5ul)]
    [InlineData(KernelChoice.Scalar, 2, 2, 6ul)]
    public void LateInputFuzzEqualsAFreshRunOfTheCorrectedInputs(KernelChoice kernels, int writers, int parts, ulong seed)
    {
        using var pack = Arena();
        const int capacity = 1024;
        const int history = 8;
        const int frames = 60;
        using var world = new ColumnWorld(pack, capacity, writers, parts, kernels, history);
        var dice = new Dice(seed * 1009);
        var script = new FrameScript();
        var pending = 0;
        for (var f = 0; f < frames; f++)
        {
            Traffic(world, dice, dice.Range(0, 300), edits: dice.Chance(70));
            world.Step();
            script.Capture(world, f);
            if (pending > 0 && --pending == 0)
            {
                Resimulate(world, script, f + 1);
            }

            if (f < 3 || !dice.Chance(45))
            {
                continue;
            }

            for (var e = dice.Range(1, 4); e > 0; e--)
            {
                Edit(world, script, dice, world.OldestFrame + 2 + dice.Next(world.Frame - world.OldestFrame - 2));
            }

            if (dice.Chance(70))
            {
                Resimulate(world, script, f + 1);
            }
            else
            {
                pending = 1;
            }
        }

        if (pending > 0)
        {
            Resimulate(world, script, frames);
        }

        Assert.Equal(script.Fresh(pack, capacity, frames)[frames].ToString("X16"), world.Digest().ToString("X16"));
    }

    static void Resimulate(ColumnWorld world, FrameScript script, int frames)
    {
        world.Resimulate();
        Assert.Equal(frames, world.Frame);
        Assert.False(world.Resimulating);
        Assert.Equal(script.Fresh(world.Pack, world.Capacity, frames)[frames].ToString("X16"), world.Digest().ToString("X16"));
    }

    static void Edit(ColumnWorld world, FrameScript script, Dice dice, int frame)
    {
        var pack = world.Pack;
        var inputs = world.Inputs(frame);
        var actions = script.Actions[frame];
        var records = script.Records[frame];
        var slot = dice.Next(400);
        var channel = dice.Next(pack.ChannelNames.Count);
        ColumnAction action = dice.Next(5) switch
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
                var at = dice.Next(actions.Count + 1);
                inputs.InsertAction(at, action);
                actions.Insert(at, action);
                break;
            case 1 when actions.Count > 0:
                var gone = dice.Next(actions.Count);
                inputs.RemoveAction(gone);
                actions.RemoveAt(gone);
                break;
            case 2 when actions.Count > 0:
                var replaced = dice.Next(actions.Count);
                inputs.ReplaceAction(replaced, action);
                actions[replaced] = action;
                break;
            case 3 when records.Count > 0:
                var lost = dice.Next(records.Count);
                inputs.RemoveRecord(lost);
                records[lost] = records[records.Count - 1];
                records.RemoveAt(records.Count - 1);
                break;
            case 4 when records.Count > 0:
                var swapped = dice.Next(records.Count);
                var record = Record.Pack(slot, channel, dice.Next(4), (uint)dice.Next(256), dice.Range(-40, 40));
                inputs.ReplaceRecord(swapped, record);
                records[swapped] = record;
                break;
            default:
                var added = Record.Pack(slot, channel, 0, 0, dice.Range(-40, 40));
                inputs.AddRecord(added);
                records.Add(added);
                break;
        }
    }

    [Fact]
    public void ResimulateOnWorkersEqualsResimulateOnOneThread()
    {
        using var pack = Arena();
        using var serial = new ColumnWorld(pack, 2048, 3, 6, KernelChoice.Best, history: 6);
        using var parallel = new ColumnWorld(pack, 2048, 3, 6, KernelChoice.Best, history: 6);
        using var workers = new Workers(3);
        using var runner = new ColumnRunner(parallel, workers);
        for (var i = 0; i < 100; i++)
        {
            serial.Spawn(i % pack.TemplateNames.Count);
            parallel.Spawn(i % pack.TemplateNames.Count);
        }

        for (var f = 0; f < 20; f++)
        {
            Traffic(serial, new Dice((ulong)f + 9), 500, edits: true);
            Traffic(parallel, new Dice((ulong)f + 9), 500, edits: true);
            serial.Step();
            runner.Step();
            if (f % 4 == 3)
            {
                var late = ColumnAction.SendFrom(3, 70, pack.Channel("Fire"), 9, 2);
                serial.Inputs(f - 3).AddAction(late);
                parallel.Inputs(f - 3).AddAction(late);
                serial.Resimulate();
                runner.Resimulate();
            }

            Assert.Equal(serial.Digest(), parallel.Digest());
        }
    }

    [Fact]
    public void ResimulateWithNoEditsChangesNothing()
    {
        using var pack = Arena();
        using var world = new ColumnWorld(pack, 512, 1, 1, KernelChoice.Best, history: 4);
        Traffic(world, new Dice(4), 100, edits: true);
        world.Step();
        var digest = world.Digest();
        world.Resimulate();
        Assert.False(world.Resimulating);
        Assert.Equal(1, world.Frame);
        Assert.Equal(digest, world.Digest());
    }

    [Fact]
    public void ActionsAlreadyTakenThisFrameAreKeptAcrossAResimulate()
    {
        using var pack = Arena();
        using var world = new ColumnWorld(pack, 512, 1, 1, KernelChoice.Best, history: 4);
        var hero = world.Spawn(pack.Template("dummy"));
        world.Step();
        world.Step();
        var ally = world.Spawn(pack.Template("dummy"));
        world.Set(ally, pack.Lane("Shield"), 7);
        world.Inputs(0).AddAction(ColumnAction.Send(hero, pack.Channel("Physical"), 25));
        world.Resimulate();
        Assert.Equal(75, world.Get(hero, pack.Lane("Health")));
        Assert.True(world.IsAlive(ally));
        Assert.Equal(7, world.Get(ally, pack.Lane("Shield")));
        Assert.Equal(2, world.Inputs(0).ActionCount);
        Assert.Equal(0, world.Inputs(1).ActionCount);
    }

    [Fact]
    public void ALateSpawnReportsTheSlotItGotAndAFullWorldSkipsIt()
    {
        using var pack = Arena();
        using var world = new ColumnWorld(pack, 64, 1, 1, KernelChoice.Best, history: 4);
        for (var i = 0; i < 64; i++)
        {
            world.Spawn(pack.Template("dummy"));
        }

        world.Step();
        world.Despawn(5);
        world.Step();
        world.Inputs(1).AddAction(ColumnAction.Spawn(pack.Template("dummy")));
        world.Inputs(1).AddAction(ColumnAction.Spawn(pack.Template("dummy")));
        world.Resimulate();
        Assert.True(world.Inputs(1).Action(1).Applied);
        Assert.Equal(5, world.Inputs(1).Action(1).Slot);
        Assert.False(world.Inputs(1).Action(2).Applied);
        Assert.Equal(-1, world.Inputs(1).Action(2).Slot);
        Assert.Equal(64, world.Count);
    }

    [Fact]
    public void AWarmFrameAndAWarmResimulateAllocateNothing()
    {
        using var pack = Arena();
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

            world.Step();
            world.Inputs(Math.Max(world.OldestFrame, world.Frame - 4)).AddAction(ColumnAction.Send(f % 2000, 2, 5));
            world.Resimulate();
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

    [Fact]
    public void MisuseIsRefusedWithAReason()
    {
        using var pack = Arena();
        using var plain = new ColumnWorld(pack, 64);
        Assert.Throws<InvalidOperationException>(() => plain.Rewind(0));
        Assert.Throws<InvalidOperationException>(() => plain.Inputs(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ColumnWorld(pack, 64, history: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ColumnWorld(pack, 64, history: ColumnWorld.HistoryLimit + 1));

        using var world = new ColumnWorld(pack, 64, history: 3);
        var hero = world.Spawn(0);
        for (var f = 0; f < 5; f++)
        {
            world.Step();
        }

        Assert.Equal(2, world.OldestFrame);
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Rewind(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Inputs(5));
        world.Inputs(3).AddAction(ColumnAction.Send(hero, 0, -1));
        world.Writer(0).Send(hero, 0, -1);
        Assert.Throws<InvalidOperationException>(() => world.Resimulate());
        world.Step();
        world.BeginResimulate();
        Assert.True(world.Resimulating);
        Assert.Throws<InvalidOperationException>(() => world.Spawn(0));
        Assert.Throws<InvalidOperationException>(() => world.Rewind(5));
        while (world.Resimulating)
        {
            world.Step();
        }

        world.Inputs(4).AddAction(ColumnAction.Send(hero, 0, -1));
        world.Step();
        world.Step();
        world.Step();
        Assert.Throws<InvalidOperationException>(() => world.Resimulate());
    }
}
