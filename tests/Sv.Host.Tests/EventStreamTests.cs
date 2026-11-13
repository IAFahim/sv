using Ref = Sv.Rules.Reference;

namespace Sv.Host.Tests;

public sealed unsafe class EventStreamTests
{
    const int OutwardChannel = 16384;
    readonly ITestOutputHelper output;

    public EventStreamTests(ITestOutputHelper output) => this.output = output;

    sealed class Published
    {
        public readonly List<(int Event, int Slot, bool Entered)> Events = new();
        public readonly List<(int Event, int Dense)> Dense = new();

        public override string ToString() => Events.Count + " events, dense " + string.Join(",", Dense);
    }

    static Published Of<TKernels>(FrameLoop<TKernels> host)
        where TKernels : struct, IPackKernels
    {
        var published = new Published();
        for (var at = 0; at < host.EventCount; at++)
        {
            published.Events.Add((host.Events[at].Event, host.Events[at].Slot, host.Events[at].Entered));
        }

        var kernels = default(TKernels);
        for (var ordinal = 0; ordinal < kernels.EventCount; ordinal++)
        {
            published.Dense.Add((kernels.EventId(ordinal), host.DenseCounts[ordinal]));
        }

        return published;
    }

    static Published Of(List<Ref.OutwardEvent> events, PackPlan plan)
    {
        var published = new Published();
        foreach (var raised in plan.Schema.Events.Select(e => e.Id).OrderBy(id => id))
        {
            var dense = 0;
            foreach (var item in events.Where(e => e.Event == raised))
            {
                if (item.IsDense)
                {
                    dense = item.DenseCount;
                }
                else
                {
                    published.Events.Add((item.Event, item.Slot, item.Entered));
                }
            }

            published.Dense.Add((raised, dense));
        }

        return published;
    }

    static void Equal(Published expected, Published actual, string where)
    {
        Assert.True(expected.Events.Count == actual.Events.Count, $"{where}: {actual.Events.Count} events, expected {expected.Events.Count}");
        for (var at = 0; at < expected.Events.Count; at++)
        {
            Assert.True(expected.Events[at] == actual.Events[at], $"{where}, event {at}: {actual.Events[at]}, expected {expected.Events[at]}");
        }

        Assert.Equal(expected.Dense, actual.Dense);
    }

    [Fact]
    public void The_outward_event_channel_holds_16384_events_per_event_on_both_hosts()
    {
        Assert.Equal(OutwardChannel, new HostOptions().EventCapacity);
        Assert.Equal(OutwardChannel, new Ref.InterpreterOptions().EventCapacity);
    }

    static string OverflowKeepsTheLowestSlots<TKernels>(PackPlan plan, int capacity, int crowd)
        where TKernels : struct, IPackKernels
    {
        var options = Packs.Options();
        options.EventCapacity = capacity;
        using var host = new FrameLoop<TKernels>(HostPack.From(plan), options);
        var player = Packs.Template(plan, "player");
        var slots = new int[crowd];
        for (var at = 0; at < crowd; at++)
        {
            slots[at] = host.Spawn(player);
        }

        var physical = Packs.Id(plan, ValueClass.Channel, "Physical");
        for (var at = 0; at < crowd; at++)
        {
            host.Send(slots[at], EffectKey.NoSource, 0, physical, 80);
        }

        host.Step();
        var low = Packs.Id(plan, ValueClass.Event, "LowHealth");
        var published = Of(host);
        var lowEvents = published.Events.Where(e => e.Event == low).ToList();
        Assert.Equal(capacity, lowEvents.Count);
        Assert.Equal(slots.Order().Take(capacity), lowEvents.Select(e => e.Slot));
        Assert.All(lowEvents, e => Assert.True(e.Entered));
        Assert.Equal(crowd - capacity, published.Dense.Single(d => d.Event == low).Dense);
        return capacity + " published, " + (crowd - capacity) + " counted dense";
    }

    [Fact]
    public void More_entries_than_the_outward_channel_publish_the_lowest_16384_slots_and_count_the_rest()
    {
        output.WriteLine((string)Packs.Call(typeof(EventStreamTests), nameof(OverflowKeepsTheLowestSlots), CardPack.Kernels, CardPack.Plan, OutwardChannel, OutwardChannel + 300)!);
    }

    static string BothHostsPublishTheSameEvents<TKernels>(PackPlan plan, int capacity)
        where TKernels : struct, IPackKernels
    {
        var options = Packs.Options();
        options.EventCapacity = capacity;
        using var rig = new Rig<TKernels>(plan, options);
        var players = new List<int>();
        for (var at = 0; at < 24; at++)
        {
            players.Add(rig.Spawn(at % 3 == 1 ? "warded" : "player"));
        }

        var grunt = rig.Spawn("grunt");
        var frames = new List<string>();
        var raised = new HashSet<int>();
        for (var frame = 0; frame < 30; frame++)
        {
            for (var at = 0; at < players.Count; at++)
            {
                var phase = frame + at;
                if (phase % 2 == 0)
                {
                    rig.Send(players[at], grunt, "Physical", 25 + phase % 3 * 10);
                }

                if (phase % 5 == 0)
                {
                    rig.Send(players[at], grunt, "Healing", 30);
                }

                if (phase % 9 == 3)
                {
                    rig.Send(players[at], grunt, "GrantWard", phase % 2 == 0 ? 1 : -1);
                }
            }

            rig.Step();
            var host = Of(rig.Host);
            Equal(Of(rig.ReferenceEvents, plan), host, "frame " + frame);
            frames.Add(host.ToString());
            raised.UnionWith(host.Events.Select(e => e.Event));
        }

        Assert.Equal(plan.Schema.Events.Length, raised.Count);
        return string.Join("; ", frames.Distinct());
    }

    [Theory]
    [InlineData(OutwardChannel)]
    [InlineData(3)]
    public void Compiled_kernels_and_the_reference_publish_the_same_events_in_the_same_order(int capacity)
    {
        output.WriteLine((string)Packs.Call(typeof(EventStreamTests), nameof(BothHostsPublishTheSameEvents), CardPack.Kernels, CardPack.Plan, capacity)!);
    }

    static string EventsComeOutOrderedByEventThenSlot<TKernels>(PackPlan plan)
        where TKernels : struct, IPackKernels
    {
        using var host = new FrameLoop<TKernels>(HostPack.From(plan), Packs.Options());
        var work = Workload.Spawn(host, plan, new[] { "player", "warded" }, 3000, "grunt", 8, new[] { "Physical" }, "Healing", new[] { "GrantWard" });
        var total = 0;
        for (var frame = 0; frame < 30; frame++)
        {
            work.Feed(host, host.Frame, 1);
            host.Step();
            var events = Of(host).Events;
            for (var at = 1; at < events.Count; at++)
            {
                var before = ((long)events[at - 1].Event << 32) | (uint)events[at - 1].Slot;
                var after = ((long)events[at].Event << 32) | (uint)events[at].Slot;
                Assert.True(before < after, $"frame {frame}, event {at}: {events[at]} after {events[at - 1]}");
            }

            total += events.Count;
        }

        Assert.True(total > 0);
        return total + " events over 30 frames";
    }

    [Fact]
    public void Published_events_are_ordered_by_event_then_slot_with_one_entry_per_slot_and_event()
    {
        output.WriteLine((string)Packs.Call(typeof(EventStreamTests), nameof(EventsComeOutOrderedByEventThenSlot), CardPack.Kernels, CardPack.Plan)!);
    }

    static string EdgeEmitFollowsTheEnterEdge<TKernels>(PackPlan plan)
        where TKernels : struct, IPackKernels
    {
        using var rig = new Rig<TKernels>(plan, Packs.Options());
        var player = rig.Spawn("player");
        rig.Send(player, EffectKey.NoSource, "Physical", 80);
        rig.Step();
        var low = Packs.Id(plan, ValueClass.Event, "LowHealth");
        Assert.Contains((low, player, true), Of(rig.Host).Events);
        Assert.Equal(0, rig.Value(player, "Fortify"));
        rig.Step();
        Assert.Equal(1, rig.Value(player, "Fortify"));
        Assert.Equal(100, rig.Stat(player, "MaxHealth"));
        rig.Step();
        Assert.Equal(150, rig.Stat(player, "MaxHealth"));
        Assert.Equal(rig.ReferenceStat(player, "MaxHealth"), rig.Stat(player, "MaxHealth"));
        Assert.Equal(rig.ReferenceDigest, rig.Digest);
        return "enter at frame 0, Fortify at frame 1, MaxHealth 150 at frame 2";
    }

    [Fact]
    public void An_enter_edge_emits_into_the_next_frame_and_its_card_folds_one_frame_later()
    {
        output.WriteLine((string)Packs.Call(typeof(EventStreamTests), nameof(EdgeEmitFollowsTheEnterEdge), CardPack.Kernels, CardPack.Plan)!);
    }

    static List<Published> Crowd<TKernels>(PackPlan plan, int workers, int capacity)
        where TKernels : struct, IPackKernels
    {
        var options = Packs.Options(workers);
        options.EventCapacity = capacity;
        using var host = new FrameLoop<TKernels>(HostPack.From(plan), options);
        var work = Workload.Spawn(host, plan, new[] { "player", "warded", "player" }, 12000, "grunt", 24, new[] { "Physical" }, "Healing", new[] { "GrantWard", "GrantFortify" });
        var frames = new List<Published>();
        for (var frame = 0; frame < 30; frame++)
        {
            work.Feed(host, host.Frame, 1);
            host.Step();
            frames.Add(Of(host));
        }

        return frames;
    }

    [Theory]
    [InlineData(OutwardChannel)]
    [InlineData(500)]
    public void Published_events_and_dense_counts_are_equal_at_1_2_4_7_and_11_workers(int capacity)
    {
        var one = (List<Published>)Packs.Call(typeof(EventStreamTests), nameof(Crowd), CardPack.Kernels, CardPack.Plan, 1, capacity)!;
        Assert.True(one.Sum(frame => frame.Events.Count) > 0);
        output.WriteLine("1 worker: " + string.Join("; ", one.Select(frame => frame.ToString()).Distinct().Take(6)));
        foreach (var workers in new[] { 2, 4, 7, 11 })
        {
            var other = (List<Published>)Packs.Call(typeof(EventStreamTests), nameof(Crowd), CardPack.Kernels, CardPack.Plan, workers, capacity)!;
            for (var frame = 0; frame < one.Count; frame++)
            {
                Equal(one[frame], other[frame], workers + " workers, frame " + frame);
            }
        }
    }
    static string LateInputChangesTheEventStream<TKernels>(PackPlan plan)
        where TKernels : struct, IPackKernels
    {
        const long late = 6;
        const int frames = 12;
        var physical = (byte)Packs.Id(plan, ValueClass.Channel, "Physical");
        var low = Packs.Id(plan, ValueClass.Event, "LowHealth");
        ulong onTime;
        using (var host = new FrameLoop<TKernels>(HostPack.From(plan), Packs.Options()))
        {
            var slot = host.Spawn(Packs.Template(plan, "player"));
            while (host.Frame < frames)
            {
                if (host.Frame == late)
                {
                    host.Send(slot, EffectKey.NoSource, 0, physical, 80);
                }

                host.Step();
            }

            onTime = host.Digest();
        }

        using (var host = new FrameLoop<TKernels>(HostPack.From(plan), Packs.Options()))
        {
            var slot = host.Spawn(Packs.Template(plan, "player"));
            while (host.Frame < frames)
            {
                host.Step();
            }

            Assert.Equal(0, host.ChangeCount);
            var record = new LoggedRecord(EffectKey.Pack(slot, EffectKey.NoSource, 0, physical, Origin.Direct, 0), 80, 1);
            Assert.Equal(late, host.Late(late, &record, 1));
            Assert.True(host.Resolve());
            Assert.Equal(Packs.Hex(onTime), Packs.Hex(host.Digest()));
            var changes = new List<(long Frame, OutwardEvent Event, bool Added)>();
            for (var at = 0; at < host.ChangeCount; at++)
            {
                changes.Add((host.Changes[at].Frame, OutwardEvent.FromKey(host.Changes[at].Event), host.Changes[at].Added));
            }

            Assert.Single(changes);
            Assert.Equal(late, changes[0].Frame);
            Assert.Equal(slot, changes[0].Event.Slot);
            Assert.Equal(low, changes[0].Event.Event);
            Assert.True(changes[0].Event.Entered);
            Assert.True(changes[0].Added);
            host.ClearChanges();
            Assert.Equal(0, host.ChangeCount);
            return "late hit at frame " + late + ": one added LowHealth enter, digest " + Packs.Hex(onTime);
        }
    }

    [Fact]
    public void A_late_input_resolved_by_rewind_reports_exactly_the_events_it_added()
    {
        output.WriteLine((string)Packs.Call(typeof(EventStreamTests), nameof(LateInputChangesTheEventStream), CardPack.Kernels, CardPack.Plan)!);
    }
}
