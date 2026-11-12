using System;
using System.Collections.Generic;
using System.IO;
using Sv;
using Sv.Host;
using Sv.Pack;
using Sv.Rules;

namespace ScenarioTests
{
    public static class Packs
    {
        static readonly Dictionary<string, PackPlan> plans = new Dictionary<string, PackPlan>();

        public static string RepositoryRoot
        {
            get
            {
                var folder = new DirectoryInfo(AppContext.BaseDirectory);
                while (folder != null && !Directory.Exists(Path.Combine(folder.FullName, "packs", "reference")))
                {
                    folder = folder.Parent;
                }

                return folder?.FullName ?? throw new DirectoryNotFoundException("no packs/reference above " + AppContext.BaseDirectory);
            }
        }

        public static string ReferenceFolder => Path.Combine(RepositoryRoot, "packs", "reference");

        public static string SentencesFolder => Path.Combine(RepositoryRoot, "tests", "Sv.Kernels.Sentences", "pack");

        public static string WorkedExampleFile => Path.Combine(ReferenceFolder, "scenarios", "worked_example.scenario.json");

        public static string SentenceFile(string name) => Path.Combine(AppContext.BaseDirectory, "Scenarios", name + Sv.Pack.Scenario.Suffix);

        public static PackPlan Reference => Plan(ReferenceFolder);

        public static PackPlan Sentences => Plan(SentencesFolder);

        public static PackPlan Plan(string folder)
        {
            lock (plans)
            {
                if (!plans.TryGetValue(folder, out var plan))
                {
                    var compiled = PackCompiler.Compile(PackFolder.Read(folder));
                    plan = compiled.Value ?? throw new InvalidOperationException("pack " + folder + " does not compile: " + string.Join("\n", compiled.Diagnostics));
                    plans.Add(folder, plan);
                }

                return plan;
            }
        }

        public static Sv.Pack.Scenario Read(PackPlan plan, string file) => ScenarioReader.Read(file, File.ReadAllText(file), plan.Schema);

        public static HostOptions Options(StoragePolicy policy = StoragePolicy.Shared, int workers = 1, int parts = 64) =>
            new HostOptions { Policy = policy, Workers = workers, Parts = parts };
    }
}
