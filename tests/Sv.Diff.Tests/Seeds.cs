using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Sv;
using Sv.Host;

namespace DiffTests
{
    public sealed class Seeds
    {
        public static readonly int Offset = int.TryParse(Environment.GetEnvironmentVariable("SV_FUZZ_OFFSET"), out var offset) && offset > 0 ? offset : 0;
        public static readonly int Main = 1 + Offset;
        public const int MainCount = 300;
        public static readonly int NoAbsorb = 3001 + Offset;
        public static readonly int CardModifiers = 5001 + Offset;
        public static readonly int StatOps = 7001 + Offset;
        public const int VariantCount = 40;

        static readonly MethodInfo compiledRun = typeof(Runs).GetMethod(nameof(Runs.Compiled))!;

        public Seeds()
        {
            var clock = Stopwatch.StartNew();
            var receipts = Path.Combine(AppContext.BaseDirectory, "receipts");
            if (Directory.Exists(receipts))
            {
                Directory.Delete(receipts, true);
            }

            var cases = new List<GeneratedCase>();
            for (var seed = Main; seed < Main + MainCount; seed++)
            {
                cases.Add(PackGenerator.Make(seed, new GeneratorOptions()));
            }

            for (var seed = NoAbsorb; seed < NoAbsorb + MainCount; seed++)
            {
                cases.Add(PackGenerator.Make(seed, new GeneratorOptions { Absorb = false }));
            }

            for (var seed = CardModifiers; seed < CardModifiers + VariantCount; seed++)
            {
                cases.Add(PackGenerator.Make(seed, new GeneratorOptions { CardModifiers = true, Absorb = false }));
            }

            for (var seed = StatOps; seed < StatOps + VariantCount; seed++)
            {
                cases.Add(PackGenerator.Make(seed, new GeneratorOptions { StatOps = true, Absorb = false }));
            }

            All = KernelCompiler.Build(cases);
            BuildMilliseconds = clock.ElapsedMilliseconds;
        }

        public List<CompiledCase> All { get; }

        public long BuildMilliseconds { get; }

        public IEnumerable<CompiledCase> Range(int first, int count) => All.Where(item => item.Case.Seed >= first && item.Case.Seed < first + count);

        public static RunTrace Compiled(CompiledCase item, HostOptions options) =>
            (RunTrace)compiledRun.MakeGenericMethod(item.Kernels!).Invoke(null, new object[] { item.Plan!, Runs.Read(item.Plan!, item.Case.Scenario), options })!;

        public static RunTrace Reference(CompiledCase item) => Runs.Reference(item.Plan!, Runs.Read(item.Plan!, item.Case.Scenario));

        public static HostOptions Options(int workers, int parts, StoragePolicy policy) =>
            new HostOptions { Workers = workers, Parts = parts, Policy = policy, EntityCapacity = 1024 };

        public static string Receipt(string test, CompiledCase item, string finding)
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "receipts");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, test + "-seed" + item.Case.Seed + ".txt");
            File.WriteAllText(path, finding + "\n\n" + item.Case.Receipt);
            var generated = Path.Combine(folder, "seed" + item.Case.Seed);
            Directory.CreateDirectory(generated);
            foreach (var file in item.Files)
            {
                File.WriteAllText(Path.Combine(generated, file.Key), file.Value);
            }

            return "seed " + item.Case.Seed + ": " + finding + " (receipt " + path + ")";
        }
    }
}
