using System;
using System.Collections.Generic;
using System.IO;
using Sv;
using Sv.Host;
using Sv.Pack;
using Sv.Rules;

namespace LegoTests
{
    public static class Lego
    {
        static PackPlan? plan;

        public static string Root
        {
            get
            {
                var folder = new DirectoryInfo(AppContext.BaseDirectory);
                while (folder != null && !Directory.Exists(Path.Combine(folder.FullName, "packs", "lego")))
                {
                    folder = folder.Parent;
                }

                return folder?.FullName ?? throw new DirectoryNotFoundException("no packs/lego above " + AppContext.BaseDirectory);
            }
        }

        public static string PackFolder => Path.Combine(Root, "packs", "lego");

        public static string ScenarioFolder => Path.Combine(PackFolder, "scenarios");

        public static PackPlan Plan
        {
            get
            {
                if (plan == null)
                {
                    var compiled = PackCompiler.Compile(Sv.Pack.PackFolder.Read(PackFolder));
                    plan = compiled.Value ?? throw new InvalidOperationException("packs/lego does not compile: " + string.Join("\n", compiled.Diagnostics));
                }

                return plan;
            }
        }

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
            return ScenarioReader.Read(file, File.ReadAllText(file), Plan.Schema);
        }

        public static ScenarioOutcome Reference(string name) => ScenarioRunner.Reference(Plan, Read(name));

        public static ScenarioOutcome Compiled(string name, StoragePolicy policy, int workers) =>
            ScenarioRunner.CompiledWith<Sv.Kernels.Lego.HostKernels>(Plan, Read(name), new HostOptions { Policy = policy, Workers = workers, Parts = 64 });
    }
}