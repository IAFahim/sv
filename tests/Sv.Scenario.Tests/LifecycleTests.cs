using Sv;
using Sv.Host;
using Sv.Pack;
using Sv.Rules.Reference;
using Xunit;
using Xunit.Abstractions;
using ReferenceKernels = Sv.Kernels.Reference.HostKernels;

namespace ScenarioTests
{
    public sealed class LifecycleTests
    {
        readonly ITestOutputHelper output;

        public LifecycleTests(ITestOutputHelper output) => this.output = output;

        static Twin<ReferenceKernels> NewTwin() => new Twin<ReferenceKernels>(Packs.Reference, Packs.Options());

        [Fact]
        public void A_killed_entity_is_retained_with_its_last_values_and_ignores_new_records()
        {
            using var twin = NewTwin();
            var player = twin.Spawn("player");
            var rifle = twin.Spawn("rifleman");
            twin.Send(player, rifle, "Physical", 30);
            twin.Step();
            twin.Kill(player);
            twin.Send(player, rifle, "Physical", 10);
            for (var frame = 0; frame < 10; frame++)
            {
                twin.Step();
            }

            output.WriteLine("host " + ScenarioRunner.Hex(twin.HostDigest) + " reference " + ScenarioRunner.Hex(twin.ReferenceDigest));
            Assert.Equal(SlotState.Dying, twin.Host.World.StateOf(player));
            Assert.Equal(SlotStatus.Dying, twin.Reference.Slots[player].Status);
            Assert.Equal(70, twin.HostValue(player, "Health"));
            Assert.Equal(70, twin.ReferenceValue(player, "Health"));
            Assert.Equal(twin.ReferenceDigest, twin.HostDigest);
        }

        [Fact]
        public void A_slot_is_not_reused_while_its_death_is_inside_the_ring()
        {
            using var twin = NewTwin();
            var first = twin.Spawn("player");
            twin.Step();
            twin.Kill(first);
            twin.Step();
            var second = twin.Spawn("player");
            Assert.NotEqual(first, second);
        }

        [Fact]
        public void A_slot_is_reused_after_the_ring_with_template_defaults_on_both_hosts()
        {
            using var twin = NewTwin();
            var first = twin.Spawn("player");
            var rifle = twin.Spawn("rifleman");
            twin.Send(first, rifle, "Physical", 40);
            twin.Step();
            twin.Kill(first);
            while (twin.Frame < 80)
            {
                twin.Step();
            }

            var reused = twin.Spawn("player");
            twin.Step();
            output.WriteLine("first " + first + " reused " + reused + " host " + ScenarioRunner.Hex(twin.HostDigest) + " reference " + ScenarioRunner.Hex(twin.ReferenceDigest));
            Assert.Equal(first, reused);
            Assert.Equal(100, twin.HostValue(reused, "Health"));
            Assert.Equal(100, twin.ReferenceValue(reused, "Health"));
            Assert.Equal(twin.ReferenceDigest, twin.HostDigest);
        }
    }
}
