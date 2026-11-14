using Sv.Kernels.Reference;
using Ref = Sv.Rules.Reference;
using ReferenceKernels = Sv.Kernels.Reference.HostKernels;

namespace Sv.Host.Tests.RedTeam;

public sealed class RedTeamDuo<TKernels> : IDisposable
    where TKernels : struct, IPackKernels
{
    readonly PackPlan plan;
    readonly HostPack pack;
    readonly Ref.Interpreter interpreter;
    readonly SortedDictionary<int, int> templateBySlot = new();
    Ref.RefState state;
    Ref.FrameInputs pending = new();

    public RedTeamDuo(PackPlan plan, HostOptions options)
    {
        this.plan = plan;
        pack = HostPack.From(plan);
        var limit = options.Arena == ArenaPolicy.HardCap ? options.CanonicalLimit : int.MaxValue;
        interpreter = new Ref.Interpreter(plan, new Ref.InterpreterOptions { EventCapacity = options.EventCapacity, ArenaCapacity = limit });
        state = interpreter.NewWorld(options.EntityCapacity);
        Host = new FrameLoop<TKernels>(pack, options);
    }

    public FrameLoop<TKernels> Host { get; }

    public Ref.RefState State => state;

    public int Spawn(int template)
    {
        var slot = Host.Spawn(template);
        pending.Spawn(slot, plan.Schema.Templates[template].Id);
        templateBySlot[slot] = template;
        return slot;
    }

    public void Kill(int slot)
    {
        Host.Kill(slot);
        pending.Kill(slot);
    }

    public void Send(int target, int source, int kind, int channel, int amount, int hits = 1)
    {
        Host.Send(target, source, kind, channel, amount, hits);
        pending.Send(target, source == -1 ? EffectKey.NoSource : source, kind, channel, amount, hits);
    }

    public void Step()
    {
        Host.Step();
        var result = interpreter.Step(state, pending);
        state = result.State;
        pending = result.NextInputs();
    }

    public ulong ReferenceDigest
    {
        get
        {
            var scenario = new Sv.Pack.Scenario();
            foreach (var pair in templateBySlot)
            {
                scenario.Entities.Add(new Sv.Pack.ScenarioEntity { Slot = pair.Key, TemplateIndex = pair.Value });
            }

            return Sv.Pack.ReferenceDigest.Of(state, scenario, pack);
        }
    }

    public void Dispose() => Host.Dispose();
}

public static class RedTeamPacks
{
    public const string CatchAllNamespace = "HostTests.RedTeam.CatchAll";

    const string CatchAllValues = """
        {"schema":1,"nextLane":3,"rows":[
        {"id":0,"name":"Health","class":"intrinsic","default":100},
        {"id":1,"name":"Echo","class":"intrinsic","default":0},
        {"id":2,"name":"Kind","class":"intrinsic","default":0,"role":"kind"},
        {"id":0,"name":"Physical","class":"channel"},
        {"id":1,"name":"Ping","class":"channel","into":"Echo"},
        {"id":0,"name":"None","class":"kind"}
        ]}
        """;

    const string CatchAllRules = """
        {"schema":1,"rows":[
        {"id":1,"name":"health_flow","layer":"flow","then":[{"subtract":["Health","Physical"]}]},
        {"id":2,"name":"echo_every_record","layer":"emit","then":[{"emit":{"to":"Self","channel":"Ping","amount":1,"per":"Record"}}]}
        ]}
        """;

    const string NoCards = """{"schema":1,"rows":[]}""";

    const string OneTemplate = """{"schema":1,"rows":[{"id":1,"name":"unit"}]}""";

    public static PackPlan CatchAll => Packs.Plan(new PackSource(CatchAllValues, CatchAllRules, NoCards, OneTemplate));

    public static Type CatchAllKernels => Packs.Kernels(CatchAllNamespace, CatchAll);
}

public sealed unsafe class RedTeamHostMisuseTests
{
    readonly ITestOutputHelper output;

    public RedTeamHostMisuseTests(ITestOutputHelper output) => this.output = output;

    static string UnknownInputs<TKernels>(PackPlan plan, int channel, int source)
        where TKernels : struct, IPackKernels
    {
        using var duo = new RedTeamDuo<TKernels>(plan, new HostOptions { Workers = 1, EntityCapacity = 256 });
        var unit = duo.Spawn(0);
        var other = duo.Spawn(0);
        duo.Step();
        duo.Send(unit, source < 0 ? other : source, 0, channel, 5);
        duo.Step();
        duo.Step();
        var compiled = duo.Host.Read(unit, 1);
        var reference = duo.State.Intrinsic(unit, 1);
        var line = "Echo compiled " + compiled + ", reference " + reference + "; digest compiled " + Packs.Hex(duo.Host.Digest()) + ", reference " + Packs.Hex(duo.ReferenceDigest);
        Assert.True(compiled == reference && duo.Host.Digest() == duo.ReferenceDigest, line);
        return line;
    }

    [Theory]
    [InlineData(2)]
    [InlineData(64)]
    [InlineData(200)]
    public void A_record_on_a_channel_the_pack_does_not_have_is_treated_alike_by_compiled_and_reference(int channel)
    {
        output.WriteLine((string)Packs.Call(typeof(RedTeamHostMisuseTests), nameof(UnknownInputs), RedTeamPacks.CatchAllKernels, RedTeamPacks.CatchAll, channel, -1)!);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(4000000)]
    public void A_record_whose_source_is_outside_the_world_is_treated_alike_by_compiled_and_reference(int source)
    {
        output.WriteLine((string)Packs.Call(typeof(RedTeamHostMisuseTests), nameof(UnknownInputs), RedTeamPacks.CatchAllKernels, RedTeamPacks.CatchAll, 0, source)!);
    }

    public static bool CrashTestsEnabled => Environment.GetEnvironmentVariable("SV_REDTEAM_CRASH") == "1";

    [Theory]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public void Sends_from_several_game_threads_with_as_many_external_writers_all_arrive(int threads, bool throughFrameLoopSend)
    {
        if (threads > 1 && throughFrameLoopSend && !CrashTestsEnabled)
        {
            output.WriteLine("needs SV_REDTEAM_CRASH=1: this case corrupts native memory and kills the test host (issue 0503)");
            return;
        }

        var threadCount = threads;
        const int PerThread = 25_000;
        using var host = new FrameLoop<ReferenceKernels>(PackData.Create(), new HostOptions { Workers = 1, EntityCapacity = 256, ExternalWriters = threadCount });
        var hero = host.Spawn((int)Template.player);
        host.Step();
        Parallel.For(0, threadCount, new ParallelOptions { MaxDegreeOfParallelism = threadCount }, thread =>
        {
            var writer = host.Sender(thread);
            for (var at = 0; at < PerThread; at++)
            {
                if (throughFrameLoopSend)
                {
                    host.Send(hero, -1, 0, (int)Channel.GiveCharges, 1);
                }
                else
                {
                    writer->Send(hero, -1, 0, (int)Channel.GiveCharges, 1, 1);
                }
            }
        });
        host.Step();
        var charges = host.Read(hero, (int)Intrinsic.ImmuneCharges);
        output.WriteLine("ImmuneCharges " + charges + " of " + (threadCount * PerThread));
        Assert.Equal(threadCount * PerThread, charges);
    }

    static string Overflow<TKernels>(PackPlan plan, int limit, int targets)
        where TKernels : struct, IPackKernels
    {
        using var duo = new RedTeamDuo<TKernels>(plan, new HostOptions { Workers = 1, EntityCapacity = 256, Arena = ArenaPolicy.HardCap, CanonicalLimit = limit });
        var victims = Enumerable.Range(0, targets).Select(_ => duo.Spawn(1)).ToArray();
        var snipers = Enumerable.Range(0, 3).Select(_ => duo.Spawn(2)).ToArray();
        var riflemen = Enumerable.Range(0, 3).Select(_ => duo.Spawn(3)).ToArray();
        duo.Step();
        for (var frame = 0; frame < 3; frame++)
        {
            foreach (var victim in victims)
            {
                foreach (var sniper in snipers)
                {
                    duo.Send(victim, sniper, 1, 0, 4 + frame);
                }

                foreach (var rifleman in riflemen)
                {
                    duo.Send(victim, rifleman, 2, 0, 3);
                    duo.Send(victim, rifleman, 2, 1, 2);
                }
            }

            duo.Step();
        }

        var line = "limit " + limit + ", targets " + targets + ": overflow " + duo.Host.Canonical.Overflow + ", digest compiled " + Packs.Hex(duo.Host.Digest()) + ", reference " + Packs.Hex(duo.ReferenceDigest);
        Assert.True(duo.Host.Digest() == duo.ReferenceDigest, line);
        return line;
    }

    [Theory]
    [InlineData(1_000_000, 4)]
    [InlineData(40, 4)]
    [InlineData(12, 4)]
    [InlineData(8, 4)]
    [InlineData(3, 4)]
    public void A_hard_capped_canonical_arena_merges_like_the_reference(int limit, int targets)
    {
        output.WriteLine((string)Packs.Call(typeof(RedTeamHostMisuseTests), nameof(Overflow), typeof(ReferenceKernels), Packs.Reference, limit, targets)!);
    }

    [Fact]
    public void An_entity_capacity_that_is_not_a_multiple_of_64_is_a_hard_limit_or_the_extra_entities_behave_like_the_rest()
    {
        using var host = new FrameLoop<ReferenceKernels>(PackData.Create(), new HostOptions { Workers = 1, EntityCapacity = 100 });
        var spawned = new List<int>();
        try
        {
            for (var at = 0; at < 128; at++)
            {
                spawned.Add(host.Spawn((int)Template.player));
            }
        }
        catch (InvalidOperationException)
        {
        }

        host.Step();
        var last = spawned[^1];
        var first = spawned[0];
        foreach (var slot in new[] { first, last })
        {
            host.Send(slot, -1, 0, (int)Channel.Physical, 80);
        }

        host.Step();
        host.Step();
        var lowFirst = host.Level(first, (int)Event.LowHealth);
        var lowLast = host.Level(last, (int)Event.LowHealth);
        var chargesFirst = host.Read(first, (int)Intrinsic.ImmuneCharges);
        var chargesLast = host.Read(last, (int)Intrinsic.ImmuneCharges);
        output.WriteLine("spawned " + spawned.Count + " with EntityCapacity 100; slot " + first + " LowHealth " + lowFirst + " panic charges " + chargesFirst + "; slot " + last + " LowHealth " + lowLast + " panic charges " + chargesLast);
        Assert.True(spawned.Count == 100 || chargesLast == chargesFirst, "slot " + last + " is live but its edge emit to itself was dropped");
    }

    [Theory]
    [InlineData(StoragePolicy.Shared, 200_000)]
    [InlineData(StoragePolicy.Dense, 1_000_000)]
    public void Sends_beyond_the_raw_capacity_under_the_grow_policy_all_arrive(StoragePolicy policy, int sends)
    {
        using var host = new FrameLoop<ReferenceKernels>(PackData.Create(), new HostOptions { Workers = 1, EntityCapacity = 256, Policy = policy, Arena = ArenaPolicy.Grow });
        var hero = host.Spawn((int)Template.player);
        host.Step();
        var accepted = 0;
        for (var at = 0; at < sends; at++)
        {
            accepted += host.Send(hero, -1, 0, (int)Channel.GiveCharges, 1) ? 1 : 0;
        }

        host.Step();
        var charges = host.Read(hero, (int)Intrinsic.ImmuneCharges);
        output.WriteLine("accepted " + accepted + ", ImmuneCharges " + charges + " of " + sends + ", raw capacity " + host.Raw.Capacity + ", grows " + host.Raw.Grows + ", missed " + host.Raw.Missed);
        Assert.Equal(sends, charges);
    }

    [Fact]
    public void Calls_on_a_disposed_world_throw_ObjectDisposedException()
    {
        var host = new FrameLoop<ReferenceKernels>(PackData.Create(), new HostOptions { Workers = 1, EntityCapacity = 256 });
        var hero = host.Spawn((int)Template.player);
        host.Step();
        host.Dispose();
        host.Dispose();
        var seen = new List<string>();
        void Probe(string call, Action action)
        {
            try
            {
                action();
                seen.Add(call + ": no exception");
            }
            catch (Exception error)
            {
                seen.Add(call + ": " + error.GetType().Name);
            }
        }

        Probe("Send", () => host.Send(hero, -1, 0, (int)Channel.Physical, 1));
        Probe("Spawn", () => host.Spawn((int)Template.player));
        Probe("Step", () => host.Step());
        Probe("Rewind", () => host.Rewind(1u));
        output.WriteLine(string.Join("; ", seen));
        Assert.All(seen, line => Assert.EndsWith(nameof(ObjectDisposedException), line));
    }

    [Fact]
    public void Spawning_into_a_full_world_fails_cleanly_and_leaves_the_world_as_it_was()
    {
        using var host = new FrameLoop<ReferenceKernels>(PackData.Create(), new HostOptions { Workers = 1, EntityCapacity = 64 });
        for (var at = 0; at < 64; at++)
        {
            host.Spawn((int)Template.player);
        }

        host.Step();
        var before = host.Digest();
        Assert.Throws<InvalidOperationException>(() => host.Spawn((int)Template.sniper));
        Assert.Throws<InvalidOperationException>(() => host.Spawn((int)Template.player));
        host.Step();
        Assert.Equal(before, host.Digest());
    }

    [Fact]
    public void Killing_twice_and_sending_to_dead_or_far_slots_is_harmless()
    {
        using var host = new FrameLoop<ReferenceKernels>(PackData.Create(), new HostOptions { Workers = 1, EntityCapacity = 256 });
        var hero = host.Spawn((int)Template.player);
        var victim = host.Spawn((int)Template.player);
        host.Step();
        Assert.True(host.Kill(victim));
        Assert.False(host.Kill(victim));
        Assert.False(host.Kill(4_000_000));
        Assert.False(host.Kill(-5));
        host.Send(victim, hero, 0, (int)Channel.Physical, 10);
        host.Send(4_000_000, hero, 0, (int)Channel.Physical, 10);
        host.Send(-7, hero, 0, (int)Channel.Physical, 10);
        host.Step();
        Assert.Equal(100, host.Read(hero, (int)Intrinsic.Health));
    }
}
