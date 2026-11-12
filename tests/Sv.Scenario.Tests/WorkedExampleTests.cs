using System.Linq;
using Sv;
using Sv.Host;
using Sv.Pack;
using Xunit;
using Xunit.Abstractions;
using Ref = Sv.Rules.Reference;
using ReferenceKernels = Sv.Kernels.Reference.HostKernels;

namespace ScenarioTests
{
    public sealed class WorkedExampleTests
    {
        const ulong WorkedExampleDigest = 0xE0F445186E2C3672ul;
        readonly ITestOutputHelper output;

        public WorkedExampleTests(ITestOutputHelper output) => this.output = output;

        ScenarioOutcome RunReference() => ScenarioRunner.Reference(Packs.Reference, Packs.Read(Packs.Reference, Packs.WorkedExampleFile));

        ScenarioOutcome RunCompiled(StoragePolicy policy) =>
            ScenarioRunner.CompiledWith<ReferenceKernels>(Packs.Reference, Packs.Read(Packs.Reference, Packs.WorkedExampleFile), Packs.Options(policy));

        void Print(string host, ScenarioOutcome outcome)
        {
            output.WriteLine(host + ": " + string.Join(" | ", outcome.Lines) + " " + string.Join(" | ", outcome.Failures));
        }

        [Fact]
        public void Reference_interpreter_prints_health_95_then_75_and_the_pinned_digest()
        {
            var outcome = RunReference();
            Print("reference", outcome);
            Assert.Contains("frame 0 P.Health = 95 ok", outcome.Lines);
            Assert.Contains("frame 1 S2.Health = 75 ok", outcome.Lines);
            Assert.Empty(outcome.Failures);
            Assert.Equal(WorkedExampleDigest, outcome.Digest);
        }

        [Theory]
        [InlineData(StoragePolicy.Shared)]
        [InlineData(StoragePolicy.Dense)]
        public void Compiled_kernels_print_health_95_then_75_and_the_pinned_digest(StoragePolicy policy)
        {
            var outcome = RunCompiled(policy);
            Print("compiled " + policy, outcome);
            Assert.Contains("frame 0 P.Health = 95 ok", outcome.Lines);
            Assert.Contains("frame 1 S2.Health = 75 ok", outcome.Lines);
            Assert.Empty(outcome.Failures);
            Assert.Equal(WorkedExampleDigest, outcome.Digest);
        }

        [Fact]
        public void Reflection_leaves_two_depth_one_records_for_S2_on_the_next_frame()
        {
            var plan = Packs.Reference;
            var scenario = Packs.Read(plan, Packs.WorkedExampleFile);
            SlotPlanner.Plan(scenario);
            var interpreter = new Ref.Interpreter(plan);
            var inputs = new Ref.FrameInputs();
            foreach (var entity in scenario.Entities)
            {
                inputs.Spawn(entity.Slot, entity.Template);
            }

            foreach (var send in scenario.Sends)
            {
                var kind = send.Source == Scenario.Unset ? 0 : plan.Schema.Templates[scenario.Entities[send.Source].TemplateIndex].Name == "sniper" ? 1 : 2;
                inputs.Send(scenario.SlotOf(send.Target), scenario.SlotOf(send.Source), kind, send.Channel, send.Amount);
            }

            var result = interpreter.Step(interpreter.NewWorld(scenario.Capacity), inputs);
            var s2 = scenario.SlotOf(2);
            var toS2 = result.Next.Where(record => record.Target == s2).ToList();
            output.WriteLine(string.Join("\n", toS2.Select(record => Ref.Trace.Record(plan.Schema, record))));
            Assert.Equal(2, toS2.Count);
            Assert.All(toS2, record => Assert.Equal(1, record.Depth));
            Assert.Equal(25, toS2.Sum(record => record.Amount * record.Hits));
        }
    }
}
