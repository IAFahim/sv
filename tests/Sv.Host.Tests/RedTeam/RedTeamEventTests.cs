using ReferenceKernels = Sv.Kernels.Reference.HostKernels;

namespace Sv.Host.Tests.RedTeam;

public sealed unsafe class RedTeamEventTests
{
    readonly ITestOutputHelper output;

    public RedTeamEventTests(ITestOutputHelper output) => this.output = output;

    [Theory]
    [InlineData(16384, 1)]
    [InlineData(3, 1)]
    [InlineData(0, 4)]
    [InlineData(3, 7)]
    public void Events_that_flap_every_frame_past_the_capacity_match_the_reference(int capacity, int workers)
    {
        var plan = Packs.Reference;
        using var rig = new Rig<ReferenceKernels>(plan, new HostOptions { Workers = workers, EntityCapacity = 1024, EventCapacity = capacity });
        var heroes = Enumerable.Range(0, 70).Select(_ => rig.Spawn("player")).ToArray();
        rig.Step();
        for (var frame = 0; frame < 12; frame++)
        {
            foreach (var hero in heroes)
            {
                rig.Send(hero, EffectKey.NoSource, frame % 2 == 0 ? "Physical" : "Healing", 75);
            }

            rig.Step();
            var host = rig.HostEvents().Select(e => (e.Event, e.Slot, e.Entered)).OrderBy(e => e.Slot).ThenBy(e => e.Event).ToList();
            var reference = rig.ReferenceEvents.Where(e => e.Slot >= 0).Select(e => (e.Event, e.Slot, e.Entered)).OrderBy(e => e.Slot).ThenBy(e => e.Event).ToList();
            var referenceDense = rig.ReferenceEvents.Where(e => e.Slot < 0).Sum(e => e.DenseCount);
            var hostDense = rig.Host.DenseCounts[0];
            Assert.True(host.SequenceEqual(reference), "frame " + frame + ": host events " + host.Count + ", reference " + reference.Count);
            Assert.Equal(referenceDense, hostDense);
            Assert.Equal(rig.ReferenceDigest, rig.Digest);
        }

        output.WriteLine("capacity " + capacity + " workers " + workers + ": events and digests equal for 12 flapping frames");
    }
}
