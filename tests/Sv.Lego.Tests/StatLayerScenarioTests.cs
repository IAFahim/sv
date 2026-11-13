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
    public static class Layered
    {
        public static string ScenarioFolder => Path.Combine(Lego.PackFolder, "scenarios-layers");

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

    public sealed class StatLayerScenarioTests
    {
        readonly ITestOutputHelper output;

        public StatLayerScenarioTests(ITestOutputHelper output) => this.output = output;

        static int OkLines(ScenarioOutcome outcome) => outcome.Lines.Count(line => line.EndsWith(" ok", StringComparison.Ordinal));

        [Theory]
        [MemberData(nameof(Layered.ScenarioNames), MemberType = typeof(Layered))]
        public void Reference_and_compiled_give_every_layered_number_and_equal_digest(string name)
        {
            var expected = Layered.Read(name).Expects.Count;
            var reference = Layered.Reference(name);
            Assert.True(expected > 0);
            Assert.Empty(reference.Failures);
            Assert.Equal(expected, OkLines(reference));

            var shared1 = Layered.Compiled(name, StoragePolicy.Shared, 1);
            var dense4 = Layered.Compiled(name, StoragePolicy.Dense, 4);
            output.WriteLine(name + " reference " + ScenarioRunner.Hex(reference.Digest) + " compiled " + ScenarioRunner.Hex(shared1.Digest));
            Assert.Empty(shared1.Failures);
            Assert.Equal(expected, OkLines(shared1));
            Assert.Equal(reference.Digest, shared1.Digest);
            Assert.Empty(dense4.Failures);
            Assert.Equal(reference.Digest, dense4.Digest);
        }
    }
}
