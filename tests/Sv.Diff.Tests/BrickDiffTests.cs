using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Sv;
using Xunit;
using Xunit.Abstractions;

namespace DiffTests
{
    public sealed class BrickSeeds
    {
        public static readonly int First = 9001 + Seeds.Offset;
        public const int Count = 160;

        public BrickSeeds()
        {
            var clock = Stopwatch.StartNew();
            var cases = new List<GeneratedCase>();
            for (var seed = First; seed < First + Count; seed++)
            {
                cases.Add(PackGenerator.Make(seed, new GeneratorOptions { Bricks = true, CardModifiers = true, StatOps = (seed & 1) == 0 }));
            }

            All = KernelCompiler.Build(cases);
            BuildMilliseconds = clock.ElapsedMilliseconds;
        }

        public List<CompiledCase> All { get; }

        public long BuildMilliseconds { get; }
    }

    public sealed class BricksCompiledVersusReference : IClassFixture<BrickSeeds>
    {
        static readonly int[] WorkerCounts = { 1, 2, 4, 7, 11 };
        readonly BrickSeeds seeds;
        readonly ITestOutputHelper output;

        public BricksCompiledVersusReference(BrickSeeds seeds, ITestOutputHelper output)
        {
            this.seeds = seeds;
            this.output = output;
        }

        void Verdict(string test, int checkedCount, List<string> failures)
        {
            output.WriteLine(test + ": " + checkedCount + " brick seeds checked, " + failures.Count + " failed, seeds " + BrickSeeds.First + "-" + (BrickSeeds.First + BrickSeeds.Count - 1) + " (fixture build " + seeds.BuildMilliseconds + " ms)");
            foreach (var failure in failures)
            {
                output.WriteLine(failure);
            }

            Assert.True(checkedCount > 0, test + ": no seed reached this check");
            Assert.True(failures.Count == 0, failures.Count + " of " + checkedCount + " seeds failed; first: " + failures.FirstOrDefault());
        }

        IEnumerable<CompiledCase> Ready => seeds.All.Where(item => item.Ready);

        [Fact]
        public void Every_generated_brick_pack_is_accepted_and_its_kernels_compile_without_warnings()
        {
            var failures = seeds.All.Where(item => !item.Ready || item.Warnings.Length > 0)
                .Select(item => Seeds.Receipt("bricks-compile", item, item.Failure + item.Warnings)).ToList();
            Verdict("bricks-compile", seeds.All.Count, failures);
        }

        [Fact]
        public void Generated_brick_packs_use_every_brick()
        {
            var rules = string.Concat(Ready.Select(item => item.Case.Values + item.Case.Rules + item.Case.Cards));
            foreach (var brick in new[] { "\"mode\":\"set\"", "\"mode\":\"max\"", "\"mode\":\"min\"", "\"mode\":\"source\"", "{\"cell\":", "\"perCount\":true", "{\"roll\":", "{\"spend\":", "\"seed\":" })
            {
                Assert.Contains(brick, rules);
            }
        }

        [Fact]
        public void Compiled_kernels_equal_the_reference_on_every_value_and_digest_every_frame()
        {
            var failures = new List<string>();
            var checkedCount = 0;
            foreach (var item in Ready)
            {
                checkedCount++;
                var difference = RunTrace.FirstDifference(Seeds.Reference(item), Seeds.Compiled(item, Seeds.Options(1, 64, StoragePolicy.Shared)), true);
                if (difference.Length > 0)
                {
                    failures.Add(Seeds.Receipt("bricks-values", item, difference));
                }
            }

            Verdict("bricks-values", checkedCount, failures);
        }

        [Fact]
        public void Digests_equal_the_reference_at_1_2_4_7_11_workers_on_both_storage_policies()
        {
            var failures = new List<string>();
            var checkedCount = 0;
            foreach (var item in Ready)
            {
                checkedCount++;
                var reference = Seeds.Reference(item);
                foreach (var workers in WorkerCounts)
                {
                    var policy = workers == 4 ? StoragePolicy.Dense : StoragePolicy.Shared;
                    var difference = RunTrace.FirstDifference(reference, Seeds.Compiled(item, Seeds.Options(workers, workers == 7 ? 16 : 64, policy)), true);
                    if (difference.Length > 0)
                    {
                        failures.Add(Seeds.Receipt("bricks-workers", item, workers + " workers " + policy + ": " + difference));
                    }
                }
            }

            Verdict("bricks-workers", checkedCount, failures);
        }
    }
}
