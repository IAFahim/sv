using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Sv;
using Xunit;
using Xunit.Abstractions;

namespace DiffTests
{
    public sealed class ChurnSeeds
    {
        public static readonly int First = 8501 + Seeds.Offset;
        public const int Count = 100;

        public ChurnSeeds()
        {
            var clock = Stopwatch.StartNew();
            var cases = new List<GeneratedCase>();
            for (var seed = First; seed < First + Count; seed++)
            {
                cases.Add(PackGenerator.Make(seed, new GeneratorOptions
                {
                    Churn = true,
                    StatOps = (seed & 1) == 0,
                    CardModifiers = (seed % 3) == 0,
                }));
            }

            All = KernelCompiler.Build(cases);
            BuildMilliseconds = clock.ElapsedMilliseconds;
        }

        public List<CompiledCase> All { get; }

        public long BuildMilliseconds { get; }
    }

    public sealed class ChurnDiffTests : IClassFixture<ChurnSeeds>
    {
        readonly ChurnSeeds seeds;
        readonly ITestOutputHelper output;

        public ChurnDiffTests(ChurnSeeds seeds, ITestOutputHelper output)
        {
            this.seeds = seeds;
            this.output = output;
        }

        [Fact]
        public void Entity_churn_compiled_kernels_equal_reference_under_normal_dense_and_tight_capacity()
        {
            var failures = new List<string>();
            var checkedCount = 0;
            foreach (var item in seeds.All)
            {
                checkedCount++;
                if (!item.Ready || item.Warnings.Length > 0)
                {
                    failures.Add(Seeds.Receipt("churn-compile", item, item.Failure + item.Warnings));
                    continue;
                }

                var reference = Seeds.Reference(item);

                var normal = RunTrace.FirstDifference(reference, Seeds.Compiled(item, Seeds.Options(1, 64, StoragePolicy.Shared)), true);
                if (normal.Length > 0)
                {
                    failures.Add(Seeds.Receipt("churn-shared", item, normal));
                    continue;
                }

                var dense = RunTrace.FirstDifference(reference, Seeds.Compiled(item, Seeds.Options(4, 16, StoragePolicy.Dense)), true);
                if (dense.Length > 0)
                {
                    failures.Add(Seeds.Receipt("churn-dense", item, dense));
                    continue;
                }

                var tight = Seeds.Options(1, 64, StoragePolicy.Shared);
                tight.EntityCapacity = 1;
                var tightDiff = RunTrace.FirstDifference(reference, Seeds.Compiled(item, tight), true);
                if (tightDiff.Length > 0)
                {
                    failures.Add(Seeds.Receipt("churn-tightcapacity", item, tightDiff));
                }
            }

            output.WriteLine(
                "churn: " + checkedCount + " seeds checked, " + failures.Count + " failed, seeds "
                + ChurnSeeds.First + "-" + (ChurnSeeds.First + ChurnSeeds.Count - 1)
                + " (fixture build " + seeds.BuildMilliseconds + " ms)");
            foreach (var failure in failures)
            {
                output.WriteLine(failure);
            }

            Assert.True(checkedCount == ChurnSeeds.Count, "churn: expected " + ChurnSeeds.Count + " seeds, checked " + checkedCount);
            Assert.True(failures.Count == 0, failures.Count + " of " + checkedCount + " seeds failed; first: " + failures.FirstOrDefault());
        }
    }
}
