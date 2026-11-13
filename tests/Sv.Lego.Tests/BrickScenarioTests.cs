using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sv;
using Sv.Host;
using Sv.Pack;
using Xunit;
using Xunit.Abstractions;

namespace LegoTests
{
    public static class Bricks
    {
        public static string ScenarioFolder => Path.Combine(Lego.PackFolder, "scenarios-bricks");

        public static IEnumerable<object[]> ScenarioNames()
        {
            var files = Directory.GetFiles(ScenarioFolder, "*" + Scenario.Suffix);
            Array.Sort(files, string.CompareOrdinal);
            foreach (var file in files)
            {
                yield return new object[] { Path.GetFileName(file).Substring(0, Path.GetFileName(file).Length - Scenario.Suffix.Length) };
            }
        }

        public static Scenario Read(string name)
        {
            var file = Path.Combine(ScenarioFolder, name + Scenario.Suffix);
            return ScenarioReader.Read(file, File.ReadAllText(file), Lego.Plan.Schema);
        }

        public static ScenarioOutcome Reference(string name) => ScenarioRunner.Reference(Lego.Plan, Read(name));

        public static ScenarioOutcome Compiled(string name, StoragePolicy policy, int workers) =>
            ScenarioRunner.CompiledWith<Sv.Kernels.Lego.HostKernels>(Lego.Plan, Read(name), new HostOptions { Policy = policy, Workers = workers, Parts = 64 });
    }

    public sealed class BrickScenarioTests
    {
        readonly ITestOutputHelper output;

        public BrickScenarioTests(ITestOutputHelper output) => this.output = output;

        void Print(string host, ScenarioOutcome outcome)
        {
            output.WriteLine(host);
            foreach (var line in outcome.Lines.Concat(outcome.Failures.Select(failure => "FAIL " + failure)))
            {
                output.WriteLine("  " + line);
            }
        }

        static int OkLines(ScenarioOutcome outcome) => outcome.Lines.Count(line => line.EndsWith(" ok", StringComparison.Ordinal));

        [Fact]
        public void There_are_seven_brick_scenarios_one_per_awkward_mechanic()
        {
            Assert.Equal(7, Bricks.ScenarioNames().Count());
        }

        [Theory]
        [MemberData(nameof(Bricks.ScenarioNames), MemberType = typeof(Bricks))]
        public void Reference_interpreter_gives_every_hand_computed_number(string name)
        {
            var expected = Bricks.Read(name).Expects.Count;
            var outcome = Bricks.Reference(name);
            Print("reference " + name, outcome);
            Assert.True(expected > 0);
            Assert.Empty(outcome.Failures);
            Assert.Equal(expected, OkLines(outcome));
        }

        [Theory]
        [MemberData(nameof(Bricks.ScenarioNames), MemberType = typeof(Bricks))]
        public void Compiled_kernels_give_every_hand_computed_number(string name)
        {
            var expected = Bricks.Read(name).Expects.Count;
            var outcome = Bricks.Compiled(name, StoragePolicy.Shared, 1);
            Print("compiled " + name, outcome);
            Assert.Empty(outcome.Failures);
            Assert.Equal(expected, OkLines(outcome));
        }

        [Theory]
        [MemberData(nameof(Bricks.ScenarioNames), MemberType = typeof(Bricks))]
        public void Compiled_digest_equals_the_reference_digest(string name)
        {
            var reference = Bricks.Reference(name);
            var compiled = Bricks.Compiled(name, StoragePolicy.Shared, 1);
            output.WriteLine(name + " reference " + ScenarioRunner.Hex(reference.Digest) + " compiled " + ScenarioRunner.Hex(compiled.Digest));
            Assert.Equal(reference.Digest, compiled.Digest);
        }

        [Theory]
        [MemberData(nameof(Bricks.ScenarioNames), MemberType = typeof(Bricks))]
        public void Dense_storage_and_several_worker_counts_give_the_reference_digest(string name)
        {
            var reference = Bricks.Reference(name).Digest;
            var dense = Bricks.Compiled(name, StoragePolicy.Dense, 1);
            Assert.Empty(dense.Failures);
            Assert.Equal(reference, dense.Digest);
            foreach (var workers in new[] { 2, 4, 7, 11 })
            {
                var shared = Bricks.Compiled(name, StoragePolicy.Shared, workers);
                output.WriteLine(name + " workers " + workers + " " + ScenarioRunner.Hex(shared.Digest));
                Assert.Empty(shared.Failures);
                Assert.Equal(reference, shared.Digest);
            }
        }
    }
}
