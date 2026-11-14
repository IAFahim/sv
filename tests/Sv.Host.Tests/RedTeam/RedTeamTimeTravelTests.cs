using Sv.Kernels.Reference;
using ReferenceKernels = Sv.Kernels.Reference.HostKernels;

namespace Sv.Host.Tests.RedTeam;

public sealed unsafe class RedTeamTimeTravelTests
{
    const int Frames = 8;
    const int LateAmount = 7;

    readonly ITestOutputHelper output;

    public RedTeamTimeTravelTests(ITestOutputHelper output) => this.output = output;

    static HostOptions Options() => new HostOptions { Workers = 1, EntityCapacity = 256 };

    static void InputsOf(FrameLoop<ReferenceKernels> host, long frame, int hero, int sniper)
    {
        if (frame == 1)
        {
            host.Modify(StatOp.Add(hero, (int)Stat.MaxHealth, ShapeKind.Added, 50, 7));
        }

        host.Send(hero, sniper, (int)Kind.Rifle, (int)Channel.Physical, 3);
    }

    static (int Hero, int Sniper) SpawnPair(FrameLoop<ReferenceKernels> host) =>
        (host.Spawn((int)Template.player), host.Spawn((int)Template.rifleman));

    static ulong LateKey(int hero, int sniper) =>
        EffectKey.Pack(hero, sniper, (byte)Kind.Rifle, (byte)Channel.Physical, Origin.Direct, 0);

    static ulong[] Straight(bool withLate, int lateFrame = -1)
    {
        using var host = new FrameLoop<ReferenceKernels>(PackData.Create(), Options());
        var digests = new ulong[Frames];
        var (hero, sniper) = (0, 0);
        for (var frame = 0L; frame < Frames; frame++)
        {
            if (frame == 0)
            {
                (hero, sniper) = SpawnPair(host);
            }

            InputsOf(host, frame, hero, sniper);
            if (withLate && frame == lateFrame)
            {
                host.Send(hero, sniper, (int)Kind.Rifle, (int)Channel.Physical, LateAmount);
            }

            host.Step();
            digests[frame] = host.LastDigest;
        }

        return digests;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void A_game_rollback_by_hand_then_a_late_input_resolves_to_the_straight_run(int lateFrame)
    {
        var expected = Straight(true, lateFrame);
        using var host = new FrameLoop<ReferenceKernels>(PackData.Create(), Options());
        var (hero, sniper) = (0, 0);
        for (var frame = 0L; frame < Frames; frame++)
        {
            if (frame == 0)
            {
                (hero, sniper) = SpawnPair(host);
            }

            InputsOf(host, frame, hero, sniper);
            host.Step();
        }

        Assert.True(host.Rewind(1u));
        Assert.Equal(0L, host.Frame);
        for (var frame = 0L; frame < Frames; frame++)
        {
            if (frame == 0)
            {
                (hero, sniper) = SpawnPair(host);
            }

            InputsOf(host, frame, hero, sniper);
            host.Step();
        }

        var record = new LoggedRecord(LateKey(hero, sniper), LateAmount, 1);
        Assert.Equal((long)lateFrame, host.Late(lateFrame, &record, 1));
        Assert.True(host.Resolve());
        output.WriteLine("replay faults " + host.ReplayFaults + ", MaxHealth " + host.Read(hero, (int)Stat.MaxHealth) + ", digest " + Packs.Hex(host.LastDigest) + ", straight " + Packs.Hex(expected[Frames - 1]));
        Assert.Equal(0L, host.ReplayFaults);
        Assert.Equal(150, host.Read(hero, (int)Stat.MaxHealth));
        Assert.Equal(expected[Frames - 1], host.LastDigest);
    }

    [Fact]
    public void A_game_rollback_by_hand_with_the_same_inputs_repeats_every_digest()
    {
        var expected = Straight(false);
        using var host = new FrameLoop<ReferenceKernels>(PackData.Create(), Options());
        var (hero, sniper) = (0, 0);
        for (var frame = 0L; frame < Frames; frame++)
        {
            if (frame == 0)
            {
                (hero, sniper) = SpawnPair(host);
            }

            InputsOf(host, frame, hero, sniper);
            host.Step();
        }

        Assert.True(host.Rewind(2u));
        for (var frame = host.Frame; frame < Frames; frame++)
        {
            InputsOf(host, frame, hero, sniper);
            host.Step();
            Assert.Equal(expected[frame], host.LastDigest);
        }
    }

    [Fact]
    public void Rewind_outside_the_ring_and_into_the_future_are_refused_and_change_nothing()
    {
        using var host = new FrameLoop<ReferenceKernels>(PackData.Create(), new HostOptions { Workers = 1, EntityCapacity = 256, RingEpochs = 2, FramesPerEpoch = 1 });
        var (hero, sniper) = SpawnPair(host);
        for (var frame = 0; frame < 6; frame++)
        {
            InputsOf(host, frame, hero, sniper);
            host.Step();
        }

        var before = host.Digest();
        var frameBefore = host.Frame;
        Assert.False(host.Rewind(host.World.OldestEpoch - 1u));
        Assert.False(host.Rewind(host.World.Epoch + 1u));
        Assert.False(host.Rewind(0u));
        Assert.Equal(before, host.Digest());
        Assert.Equal(frameBefore, host.Frame);
        Assert.True(host.Rewind(host.World.Epoch));
        Assert.True(host.Rewind(host.World.Epoch));
        Assert.Equal(frameBefore, host.Frame);
    }

    [Fact]
    public void A_capped_input_log_never_lets_a_resolve_silently_replay_less_than_was_played()
    {
        static FrameLoop<ReferenceKernels> Make() => new FrameLoop<ReferenceKernels>(PackData.Create(), new HostOptions { Workers = 1, EntityCapacity = 256, InputLogFrameLimit = 2 });
        int[] Feed(FrameLoop<ReferenceKernels> host, int[] targets, long frame)
        {
            foreach (var target in targets)
            {
                host.Send(target, -1, 0, (int)Channel.Physical, 1 + (int)frame);
            }

            return targets;
        }

        using var straight = Make();
        var heroes = Enumerable.Range(0, 5).Select(_ => straight.Spawn((int)Template.player)).ToArray();
        for (var frame = 0L; frame < 6; frame++)
        {
            Feed(straight, heroes, frame);
            if (frame == 2)
            {
                straight.Send(heroes[0], -1, 0, (int)Channel.Healing, 3);
            }

            straight.Step();
        }

        using var host = Make();
        heroes = Enumerable.Range(0, 5).Select(_ => host.Spawn((int)Template.player)).ToArray();
        for (var frame = 0L; frame < 6; frame++)
        {
            Feed(host, heroes, frame);
            host.Step();
        }

        var late = new LoggedRecord(EffectKey.Pack(heroes[0], EffectKey.NoSource, 0, (byte)Channel.Healing, Origin.Direct, 0), 3, 1);
        Assert.Equal(2L, host.Late(2, &late, 1));
        var refused = Assert.Throws<InvalidOperationException>(() => host.Resolve());
        output.WriteLine(refused.Message + "; dropped from log " + host.Log.DroppedRecords);
        Assert.Contains("input log cap", refused.Message);
    }
}
