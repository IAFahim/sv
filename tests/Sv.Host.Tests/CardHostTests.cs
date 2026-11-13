using Sv.Pack;

namespace Sv.Host.Tests;

public sealed unsafe class CardHostTests
{
    const int Frames = 24;
    const long GrantAfter = 6;
    readonly ITestOutputHelper output;

    public CardHostTests(ITestOutputHelper output) => this.output = output;

    sealed class Cast
    {
        public int[] Players = Array.Empty<int>();
        public int[] Grunts = Array.Empty<int>();
    }

    static Cast SpawnCast<TKernels>(FrameLoop<TKernels> host, PackPlan plan)
        where TKernels : struct, IPackKernels
    {
        return new Cast
        {
            Players = new[] { host.Spawn(Packs.Template(plan, "player")), host.Spawn(Packs.Template(plan, "warded")), host.Spawn(Packs.Template(plan, "player")) },
            Grunts = new[] { host.Spawn(Packs.Template(plan, "grunt")), host.Spawn(Packs.Template(plan, "grunt")) },
        };
    }

    static void Feed<TKernels>(FrameLoop<TKernels> host, PackPlan plan, Cast cast, long frame)
        where TKernels : struct, IPackKernels
    {
        var physical = Packs.Id(plan, ValueClass.Channel, "Physical");
        var healing = Packs.Id(plan, ValueClass.Channel, "Healing");
        for (var at = 0; at < cast.Players.Length; at++)
        {
            var phase = (int)frame + at;
            host.Send(cast.Players[at], cast.Grunts[phase % 2], 1, physical, phase % 5 * 4 + 6);
            if (phase % 3 == 0)
            {
                host.Send(cast.Players[at], cast.Grunts[0], 1, healing, 9);
            }
        }
    }

    static bool LastFrameOfEpoch(World world) => world.Journal.BeginOf(world.Epoch) + world.FramesPerEpoch - 1 == world.Frame;

    static List<ulong> Run<TKernels>(FrameLoop<TKernels> host, PackPlan plan, Cast cast, int grantAmount, long grantFrame, List<int>? moveSpeed = null)
        where TKernels : struct, IPackKernels
    {
        var grant = Packs.Id(plan, ValueClass.Channel, "GrantWard");
        var speed = Packs.Id(plan, ValueClass.Stat, "MoveSpeed");
        var digests = new List<ulong>();
        while (host.Frame < Frames)
        {
            var frame = host.Frame;
            Feed(host, plan, cast, frame);
            if (frame == grantFrame)
            {
                host.Send(cast.Players[1], cast.Grunts[0], 1, grant, grantAmount);
                host.Send(cast.Players[0], cast.Grunts[0], 1, grant, grantAmount);
            }

            host.Step();
            digests.Add(host.Digest());
            moveSpeed?.Add(host.Read(grantAmount > 0 ? cast.Players[0] : cast.Players[1], speed));
        }

        return digests;
    }

    static long FirstLastFrameOfEpochFrom<TKernels>(PackPlan plan, long from)
        where TKernels : struct, IPackKernels
    {
        using var probe = new FrameLoop<TKernels>(HostPack.From(plan), Packs.Options());
        while (probe.Frame < from || !LastFrameOfEpoch(probe.World))
        {
            probe.Step();
        }

        var frame = probe.Frame;
        var epoch = probe.World.Epoch;
        probe.Step();
        Assert.NotEqual(epoch, probe.World.Epoch);
        return frame;
    }

    static string CardChangedInTheLastFrameOfAnEpochSurvivesRewind<TKernels>(PackPlan plan, int grantAmount)
        where TKernels : struct, IPackKernels
    {
        var last = FirstLastFrameOfEpochFrom<TKernels>(plan, GrantAfter);
        using var host = new FrameLoop<TKernels>(HostPack.From(plan), Packs.Options());
        var cast = SpawnCast(host, plan);
        var speeds = new List<int>();
        var straight = Run(host, plan, cast, grantAmount, last, speeds);
        Assert.NotEqual(speeds[(int)last - 1], speeds[(int)last + 1]);
        var epoch = host.World.EpochAtOrBefore(last + 1);
        Assert.Equal(last + 1, host.World.Journal.BeginOf(epoch));
        Assert.True(host.Rewind(epoch));
        Assert.Equal(last + 1, host.Frame);
        Assert.Equal(Packs.Hex(straight[(int)last]), Packs.Hex(host.Digest()));
        var replayedSpeeds = new List<int>();
        var replayed = Run(host, plan, cast, grantAmount, -1L, replayedSpeeds);
        for (var at = 0; at < replayed.Count; at++)
        {
            var frame = (int)last + 1 + at;
            Assert.True(straight[frame] == replayed[at], $"frame {frame}: straight {Packs.Hex(straight[frame])}, replayed {Packs.Hex(replayed[at])}, speed straight {speeds[frame]} replayed {replayedSpeeds[at]}");
        }

        return "card change " + grantAmount + " at frame " + last + " (last of its epoch), speed " + speeds[(int)last - 1] + " -> " + speeds[(int)last + 1] + ", final " + Packs.Hex(straight[Frames - 1]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void A_card_changed_in_the_last_frame_of_an_epoch_survives_a_rewind_to_the_next_epoch(int grantAmount)
    {
        output.WriteLine((string)Packs.Call(typeof(CardHostTests), nameof(CardChangedInTheLastFrameOfAnEpochSurvivesRewind), CardPack.Kernels, CardPack.Plan, grantAmount)!);
    }

    static string LateCardResolvedByRewind<TKernels>(PackPlan plan)
        where TKernels : struct, IPackKernels
    {
        var late = FirstLastFrameOfEpochFrom<TKernels>(plan, GrantAfter);
        ulong onTime;
        using (var host = new FrameLoop<TKernels>(HostPack.From(plan), Packs.Options()))
        {
            var cast = SpawnCast(host, plan);
            onTime = Run(host, plan, cast, 1, late)[Frames - 1];
        }

        using (var host = new FrameLoop<TKernels>(HostPack.From(plan), Packs.Options()))
        {
            var cast = SpawnCast(host, plan);
            var withoutGrant = Run(host, plan, cast, 1, -1)[Frames - 1];
            Assert.NotEqual(onTime, withoutGrant);
            var grant = (byte)Packs.Id(plan, ValueClass.Channel, "GrantWard");
            var records = stackalloc LoggedRecord[2];
            records[0] = new LoggedRecord(EffectKey.Pack(cast.Players[1], cast.Grunts[0], 1, grant, Origin.Direct, 0), 1, 1);
            records[1] = new LoggedRecord(EffectKey.Pack(cast.Players[0], cast.Grunts[0], 1, grant, Origin.Direct, 0), 1, 1);
            Assert.Equal(late, host.Late(late, records, 2));
            Assert.True(host.Resolve());
            Assert.Equal(Frames, host.Frame);
            Assert.Equal(0L, host.ReplayFaults);
            Assert.Equal(Packs.Hex(onTime), Packs.Hex(host.Digest()));
            return "late card grant at frame " + late + ": on time " + Packs.Hex(onTime) + ", without " + Packs.Hex(withoutGrant) + ", resolved " + Packs.Hex(host.Digest());
        }
    }

    [Fact]
    public void A_late_card_grant_resolved_by_rewind_equals_the_run_that_had_it_on_time()
    {
        output.WriteLine((string)Packs.Call(typeof(CardHostTests), nameof(LateCardResolvedByRewind), CardPack.Kernels, CardPack.Plan)!);
    }

    static List<ulong> Crowd<TKernels>(PackPlan plan, int workers)
        where TKernels : struct, IPackKernels
    {
        using var host = new FrameLoop<TKernels>(HostPack.From(plan), Packs.Options(workers));
        var work = Workload.Spawn(host, plan, new[] { "player", "warded", "player" }, 12000, "grunt", 24, new[] { "Physical" }, "Healing", new[] { "GrantWard", "GrantFortify" });
        var digests = new List<ulong>();
        for (var frame = 0; frame < 40; frame++)
        {
            work.Feed(host, host.Frame, 1);
            host.Step();
            digests.Add(host.Digest());
        }

        return digests;
    }

    [Fact]
    public void Digests_with_cards_are_equal_at_1_2_4_7_and_11_workers()
    {
        var one = (List<ulong>)Packs.Call(typeof(CardHostTests), nameof(Crowd), CardPack.Kernels, CardPack.Plan, 1)!;
        output.WriteLine("1 worker final " + Packs.Hex(one[^1]));
        foreach (var workers in new[] { 2, 4, 7, 11 })
        {
            var other = (List<ulong>)Packs.Call(typeof(CardHostTests), nameof(Crowd), CardPack.Kernels, CardPack.Plan, workers)!;
            for (var frame = 0; frame < one.Count; frame++)
            {
                Assert.True(one[frame] == other[frame], $"{workers} workers, frame {frame}: {Packs.Hex(other[frame])}, 1 worker {Packs.Hex(one[frame])}");
            }
        }
    }

    static string CardPackEqualsTheReference<TKernels>(PackPlan plan)
        where TKernels : struct, IPackKernels
    {
        using var rig = new Rig<TKernels>(plan, Packs.Options());
        var players = new[] { rig.Spawn("player"), rig.Spawn("warded"), rig.Spawn("player") };
        var grunt = rig.Spawn("grunt");
        for (var frame = 0; frame < Frames; frame++)
        {
            for (var at = 0; at < players.Length; at++)
            {
                var phase = frame + at;
                rig.Send(players[at], grunt, "Physical", phase % 5 * 4 + 6);
                if (phase % 3 == 0)
                {
                    rig.Send(players[at], grunt, "Healing", 9);
                }

                if (phase % 7 == 2)
                {
                    rig.Send(players[at], grunt, "GrantWard", phase % 2 == 0 ? 1 : -1);
                }
            }

            rig.Step();
            for (var at = 0; at < players.Length; at++)
            {
                Assert.Equal(rig.ReferenceStat(players[at], "MoveSpeed"), rig.Stat(players[at], "MoveSpeed"));
                Assert.Equal(rig.ReferenceStat(players[at], "MaxHealth"), rig.Stat(players[at], "MaxHealth"));
                Assert.Equal(rig.ReferenceValue(players[at], "Health"), rig.Value(players[at], "Health"));
            }

            Assert.True(rig.ReferenceDigest == rig.Digest, $"frame {frame}: reference {Packs.Hex(rig.ReferenceDigest)}, host {Packs.Hex(rig.Digest)}");
        }

        return "card pack final " + Packs.Hex(rig.Digest);
    }

    [Fact]
    public void The_card_pack_on_compiled_kernels_equals_the_reference_interpreter_every_frame()
    {
        output.WriteLine((string)Packs.Call(typeof(CardHostTests), nameof(CardPackEqualsTheReference), CardPack.Kernels, CardPack.Plan)!);
    }
}
