using System;
using System.Collections.Generic;
using System.IO;
using Sv;
using Sv.Host;
using Sv.Pack;
using Sv.Rules;
using Xunit;

namespace TwinFixtures
{
    public sealed class TwinTests
    {
        const int Wide = 11;

        static string Root
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

        static string TwinFolder => Path.Combine(Root, "packages", "com.sv.unity.v4", "Tests", "Editor", "Twin");

        static readonly (string Pack, string Folder, string[] Scenarios)[] Sets =
        {
            ("reference", "packs/reference", new[] { "scenarios" }),
            ("lego", "packs/lego", new[] { "scenarios", "scenarios-bricks", "scenarios-layers" }),
        };

        public static IEnumerable<object[]> Cases()
        {
            foreach (var set in Sets)
            {
                foreach (var folder in set.Scenarios)
                {
                    var files = Directory.GetFiles(Path.Combine(Root, set.Folder, folder), "*" + Scenario.Suffix);
                    Array.Sort(files, string.CompareOrdinal);
                    foreach (var file in files)
                    {
                        yield return new object[] { set.Pack, Path.GetRelativePath(Root, file).Replace('\\', '/') };
                    }
                }
            }
        }

        static Scenario SealCompactScenario()
        {
            var scenario = new Scenario { Name = "seal_compact_shift", Frames = 6, Capacity = 1 << 12 };
            for (var e = 0; e < 300; e++)
            {
                scenario.Entities.Add(new ScenarioEntity { Name = "e" + e, TemplateIndex = e % 2, Template = e % 2, Frame = 0 });
            }

            foreach (var frame in new long[] { 0, 1, 2, 3, 4, 5 })
            {
                foreach (var target in new[] { 3, 3, 3, 130, 130, 260 })
                {
                    scenario.Sends.Add(new ScenarioSend { Frame = frame, Target = target, Source = 1, Kind = 1, Channel = 0, Amount = 2, Hits = 1, Origin = Origin.Direct });
                }
            }

            return scenario;
        }

        [Fact]
        public void The_synthetic_seal_compact_journal_matches_the_committed_twin()
        {
            var plan = Plans.Of("reference");
            string Record(int workers) => Journal.Record<Sv.Kernels.Reference.HostKernels>("reference", Sv.Kernels.Reference.Layout.PackHash, plan, SealCompactScenario(), new HostOptions { Workers = workers, Parts = 64 });
            var narrow = Record(1);
            Assert.Equal(narrow, Record(Wide));
            Check(Path.Combine(TwinFolder, "reference", "synthetic", "seal_compact_shift.twin.txt"), narrow);
        }

        static void Check(string file, string journal)
        {
            if (Environment.GetEnvironmentVariable("SV_UPDATE_TWIN") == "1")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, journal);
                return;
            }

            Assert.True(File.Exists(file), "missing " + file + ", run with SV_UPDATE_TWIN=1");
            Assert.Equal(File.ReadAllText(file).Replace("\r\n", "\n"), journal);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Journal_matches_the_committed_twin_at_one_and_eleven_workers(string pack, string scenarioFile)
        {
            var plan = Plans.Of(pack);
            var path = Path.Combine(Root, scenarioFile);
            string Record(int workers)
            {
                var scenario = ScenarioReader.Read(path, File.ReadAllText(path), plan.Schema);
                var options = new HostOptions { Workers = workers, Parts = 64 };
                return pack == "reference"
                    ? Journal.Record<Sv.Kernels.Reference.HostKernels>(pack, Sv.Kernels.Reference.Layout.PackHash, plan, scenario, options)
                    : Journal.Record<Sv.Kernels.Lego.HostKernels>(pack, Sv.Kernels.Lego.Layout.PackHash, plan, scenario, options);
            }

            var narrow = Record(1);
            var wide = Record(Wide);
            Assert.Equal(narrow, wide);

            var name = Path.GetFileName(scenarioFile);
            name = name.Substring(0, name.Length - Scenario.Suffix.Length);
            var file = Path.Combine(TwinFolder, pack, scenarioFile.Contains("scenarios-bricks") ? "bricks" : scenarioFile.Contains("scenarios-layers") ? "layers" : "main", name + ".twin.txt");
            Check(file, narrow);
        }
    }

    static class Plans
    {
        static readonly Dictionary<string, PackPlan> Cache = new Dictionary<string, PackPlan>();

        public static PackPlan Of(string pack)
        {
            lock (Cache)
            {
                if (!Cache.TryGetValue(pack, out var plan))
                {
                    var folder = Path.Combine(Root(), "packs", pack);
                    var compiled = PackCompiler.Compile(PackFolder.Read(folder));
                    plan = compiled.Value ?? throw new InvalidOperationException(folder + " does not compile: " + string.Join("\n", compiled.Diagnostics));
                    Cache.Add(pack, plan);
                }

                return plan;
            }
        }

        static string Root()
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder != null && !Directory.Exists(Path.Combine(folder.FullName, "packs", "reference")))
            {
                folder = folder.Parent;
            }

            return folder!.FullName;
        }
    }
}
