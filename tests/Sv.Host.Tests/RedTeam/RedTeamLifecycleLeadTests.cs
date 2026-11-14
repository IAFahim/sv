using System.Collections.Generic;
using Sv.Kernels.Reference;
using ReferenceKernels = Sv.Kernels.Reference.HostKernels;

namespace Sv.Host.Tests.RedTeam;

public sealed unsafe class RedTeamLifecycleLeadTests
{
    const int ChurnFrames = 40;
    const int RewindFrames = 12;
    const int RewindFrom = 9;
    const int RewindTo = 4;

    readonly ITestOutputHelper output;

    public RedTeamLifecycleLeadTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void A_record_sent_to_an_entity_killed_in_the_same_frame_never_lands_on_the_entity_spawned_after_it()
    {
        var options = new HostOptions { Workers = 1, EntityCapacity = 256 };
        using var host = new FrameLoop<ReferenceKernels>(PackData.Create(), options);
        var ringFrames = options.RingEpochs * options.FramesPerEpoch;
        var current = host.Spawn((int)Template.player);
        host.Step();
        var killedAt = new Dictionary<int, long>();
        for (var frame = 0; frame < ChurnFrames; frame++)
        {
            host.Send(current, -1, 0, (int)Channel.GiveCharges, 5);
            Assert.True(host.Kill(current));
            killedAt[current] = host.Frame;
            var next = host.Spawn((int)Template.player);
            foreach (var killed in killedAt)
            {
                Assert.False(next == killed.Key && host.Frame - killed.Value < ringFrames, "slot " + next + " was reused " + (host.Frame - killed.Value) + " frames after its entity died, inside the ring");
            }

            host.Step();
            var charges = host.Read(next, (int)Intrinsic.ImmuneCharges);
            Assert.True(charges == 0, "the entity spawned into slot " + next + " received " + charges + " charges sent to the entity killed in slot " + current);
            current = next;
        }

        output.WriteLine("churned " + ChurnFrames + " kill+spawn frames over " + killedAt.Count + " slots, ring " + ringFrames + " frames");
    }

    static void Inputs(FrameLoop<ReferenceKernels> host, long frame, ref int hero, ref int rifleman)
    {
        if (frame == 0)
        {
            hero = host.Spawn((int)Template.player);
            rifleman = host.Spawn((int)Template.rifleman);
        }

        if (frame == 2)
        {
            host.Modify(StatOp.Add(hero, (int)Stat.MaxHealth, ShapeKind.Added, 40, 9));
        }

        host.Send(hero, rifleman, (int)Kind.Rifle, (int)Channel.Physical, 3);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(0)]
    public void Rewind_and_replay_with_a_sparse_digest_ends_in_the_straight_runs_state(int digestEvery)
    {
        ulong straightDigest;
        using (var straight = new FrameLoop<ReferenceKernels>(PackData.Create(), new HostOptions { Workers = 1, EntityCapacity = 256 }))
        {
            var (hero, rifleman) = (0, 0);
            for (var frame = 0L; frame < RewindFrames; frame++)
            {
                Inputs(straight, frame, ref hero, ref rifleman);
                straight.Step();
            }

            straightDigest = straight.Digest();
        }

        using var host = new FrameLoop<ReferenceKernels>(PackData.Create(), new HostOptions { Workers = 2, EntityCapacity = 256, DigestEvery = digestEvery });
        var (h, r) = (0, 0);
        for (var frame = 0L; frame < RewindFrom; frame++)
        {
            Inputs(host, frame, ref h, ref r);
            host.Step();
        }

        Assert.True(host.Rewind(host.World.EpochAtOrBefore(RewindTo)));
        for (var frame = host.Frame; frame < RewindFrames; frame++)
        {
            Inputs(host, frame, ref h, ref r);
            host.Step();
        }

        output.WriteLine("DigestEvery " + digestEvery + ": rewound to frame " + RewindTo + ", final " + Packs.Hex(host.Digest()) + ", straight " + Packs.Hex(straightDigest));
        Assert.Equal(RewindFrames, host.Frame);
        Assert.Equal(straightDigest, host.Digest());
    }

    static ulong RunWithLate(LatePolicy policy, long lateFrame, bool late, out long accepted, out bool resolved)
    {
        using var host = new FrameLoop<ReferenceKernels>(PackData.Create(), new HostOptions { Workers = 1, EntityCapacity = 256, RingEpochs = 4, FramesPerEpoch = 2, Late = policy });
        var (hero, rifleman) = (0, 0);
        for (var frame = 0L; frame < 20; frame++)
        {
            Inputs(host, frame, ref hero, ref rifleman);
            host.Step();
        }

        accepted = long.MinValue;
        resolved = false;
        if (late)
        {
            var record = new LoggedRecord(EffectKey.Pack(hero, rifleman, (byte)Kind.Rifle, (byte)Channel.Physical, Origin.Direct, 0), 11, 1);
            accepted = host.Late(lateFrame, &record, 1);
            resolved = host.Resolve();
        }

        return host.Digest();
    }

    [Fact]
    public void Reject_policy_refuses_a_late_input_older_than_the_ring_and_leaves_the_state_alone()
    {
        var untouched = RunWithLate(LatePolicy.Reject, 0, false, out _, out _);
        var rejected = RunWithLate(LatePolicy.Reject, 2, true, out var rejectedAt, out var rejectedResolved);
        Assert.Equal(-1L, rejectedAt);
        Assert.False(rejectedResolved);
        Assert.Equal(untouched, rejected);

        var clamped = RunWithLate(LatePolicy.Clamp, 2, true, out var clampedAt, out var clampedResolved);
        Assert.True(clampedAt > 2, "Clamp should move the late input to the oldest frame in the ring, got " + clampedAt);
        Assert.True(clampedResolved);
        Assert.NotEqual(untouched, clamped);

        var inRing = RunWithLate(LatePolicy.Reject, 17, true, out var inRingAt, out var inRingResolved);
        Assert.Equal(17L, inRingAt);
        Assert.True(inRingResolved);
        Assert.NotEqual(untouched, inRing);
        output.WriteLine("reject old: " + rejectedAt + ", clamp old to frame " + clampedAt + ", reject in ring: frame " + inRingAt);
    }
}
