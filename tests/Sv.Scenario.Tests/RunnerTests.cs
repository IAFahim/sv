using Sv.Pack;
using Xunit;
using Xunit.Abstractions;
using ReferenceKernels = Sv.Kernels.Reference.HostKernels;

namespace ScenarioTests
{
    public sealed class RunnerTests
    {
        const string SourceSpawnsLater = "{\"schema\":1,\"name\":\"source_spawns_later\",\"frames\":2,"
            + "\"entities\":[{\"name\":\"P\",\"template\":\"worked_example_player\"},{\"name\":\"S1\",\"template\":\"sniper\",\"frame\":1}],"
            + "\"sends\":[{\"frame\":0,\"target\":\"P\",\"source\":\"S1\",\"channel\":\"Physical\",\"amount\":40}]}";

        readonly ITestOutputHelper output;

        public RunnerTests(ITestOutputHelper output) => this.output = output;

        [Fact]
        public void A_send_from_an_entity_that_spawns_later_gets_one_kind_on_both_runners()
        {
            var plan = Packs.Reference;
            var reference = ScenarioRunner.Reference(plan, ScenarioReader.Read("inline", SourceSpawnsLater, plan.Schema));
            var compiled = ScenarioRunner.CompiledWith<ReferenceKernels>(plan, ScenarioReader.Read("inline", SourceSpawnsLater, plan.Schema), Packs.Options());
            output.WriteLine("reference " + string.Join(" | ", reference.Lines) + "; compiled " + string.Join(" | ", compiled.Lines));
            Assert.Equal(ScenarioRunner.Hex(reference.Digest), ScenarioRunner.Hex(compiled.Digest));
        }
    }
}
