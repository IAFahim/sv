using System.Linq;
using Sv;
using Sv.Host;
using Sv.Pack;
using Xunit;
using Xunit.Abstractions;

namespace LegoTests
{
    public sealed class ScenarioTests
    {
        readonly ITestOutputHelper output;

        public ScenarioTests(ITestOutputHelper output) => this.output = output;

        void Print(string host, ScenarioOutcome outcome)
        {
            output.WriteLine(host);
            foreach (var line in outcome.Lines.Concat(outcome.Failures.Select(failure => "FAIL " + failure)))
            {
                output.WriteLine("  " + line);
            }
        }

        static int OkLines(ScenarioOutcome outcome) => outcome.Lines.Count(line => line.EndsWith(" ok", System.StringComparison.Ordinal));

        [Fact]
        public void The_lego_pack_compiles_without_diagnostics()
        {
            var compiled = Sv.Rules.PackCompiler.Compile(Sv.Pack.PackFolder.Read(Lego.PackFolder));
            Assert.NotNull(compiled.Value);
            Assert.Empty(compiled.Diagnostics);
        }

        [Fact]
        public void There_are_twenty_two_mechanic_scenarios()
        {
            Assert.Equal(22, Lego.ScenarioNames().Count());
        }

        [Theory]
        [MemberData(nameof(Lego.ScenarioNames), MemberType = typeof(Lego))]
        public void Reference_interpreter_gives_every_hand_computed_number(string name)
        {
            var expected = Lego.Read(name).Expects.Count;
            var outcome = Lego.Reference(name);
            Print("reference " + name, outcome);
            Assert.True(expected > 0);
            Assert.Empty(outcome.Failures);
            Assert.Equal(expected, OkLines(outcome));
        }

        [Theory]
        [MemberData(nameof(Lego.ScenarioNames), MemberType = typeof(Lego))]
        public void Compiled_kernels_give_every_hand_computed_number(string name)
        {
            var expected = Lego.Read(name).Expects.Count;
            var outcome = Lego.Compiled(name, StoragePolicy.Shared, 1);
            Print("compiled " + name, outcome);
            Assert.Empty(outcome.Failures);
            Assert.Equal(expected, OkLines(outcome));
        }

        [Theory]
        [MemberData(nameof(Lego.ScenarioNames), MemberType = typeof(Lego))]
        public void Compiled_digest_equals_the_reference_digest(string name)
        {
            var reference = Lego.Reference(name);
            var compiled = Lego.Compiled(name, StoragePolicy.Shared, 1);
            output.WriteLine(name + " reference " + ScenarioRunner.Hex(reference.Digest) + " compiled " + ScenarioRunner.Hex(compiled.Digest));
            Assert.Equal(reference.Digest, compiled.Digest);
        }

        [Theory]
        [MemberData(nameof(Lego.ScenarioNames), MemberType = typeof(Lego))]
        public void Dense_storage_and_four_workers_give_the_reference_digest(string name)
        {
            var reference = Lego.Reference(name).Digest;
            var dense = Lego.Compiled(name, StoragePolicy.Dense, 1);
            var workers = Lego.Compiled(name, StoragePolicy.Shared, 4);
            output.WriteLine(name + " reference " + ScenarioRunner.Hex(reference) + " dense " + ScenarioRunner.Hex(dense.Digest) + " workers4 " + ScenarioRunner.Hex(workers.Digest));
            Assert.Empty(dense.Failures);
            Assert.Empty(workers.Failures);
            Assert.Equal(reference, dense.Digest);
            Assert.Equal(reference, workers.Digest);
        }
    }
}