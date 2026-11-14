using System.Collections.Generic;
using System.Linq;
using Sv;
using Xunit;
using Xunit.Abstractions;

namespace DiffTests
{
    public sealed class CompiledVersusReference : IClassFixture<Seeds>
    {
        static readonly int[] WorkerCounts = { 1, 2, 4, 7, 11 };
        static readonly int[] PartCounts = { 16, 64 };
        readonly Seeds seeds;
        readonly ITestOutputHelper output;

        public CompiledVersusReference(Seeds seeds, ITestOutputHelper output)
        {
            this.seeds = seeds;
            this.output = output;
        }

        void Verdict(string test, int checkedCount, List<string> failures)
        {
            output.WriteLine(test + ": " + checkedCount + " seeds checked, " + failures.Count + " failed, seed offset " + Seeds.Offset + " (fixture build " + seeds.BuildMilliseconds + " ms)");
            foreach (var failure in failures)
            {
                output.WriteLine(failure);
            }

            Assert.True(checkedCount > 0, test + ": no seed reached this check");
            Assert.True(failures.Count == 0, failures.Count + " of " + checkedCount + " seeds failed; first: " + failures.FirstOrDefault());
        }

        List<string> AgainstReference(string test, IEnumerable<CompiledCase> items, out int checkedCount)
        {
            var failures = new List<string>();
            checkedCount = 0;
            foreach (var item in items.Where(item => item.Ready))
            {
                checkedCount++;
                var difference = RunTrace.FirstDifference(Seeds.Reference(item), Seeds.Compiled(item, Seeds.Options(1, 64, StoragePolicy.Shared)), true);
                if (difference.Length > 0)
                {
                    failures.Add(Seeds.Receipt(test, item, difference));
                }
            }

            return failures;
        }

        [Fact]
        public void Every_generated_pack_is_accepted_and_its_kernels_compile()
        {
            var failures = seeds.All.Where(item => !item.Ready).Select(item => Seeds.Receipt("compile", item, item.Failure)).ToList();
            Verdict("compile", seeds.All.Count, failures);
        }

        [Fact]
        public void Generated_kernels_compile_without_warnings()
        {
            var ready = seeds.All.Where(item => item.Ready).ToList();
            var failures = ready.Where(item => item.Warnings.Length > 0).Select(item => Seeds.Receipt("warnings", item, item.Warnings.TrimEnd())).ToList();
            Verdict("warnings", ready.Count, failures);
        }

        [Fact]
        public void Compiled_kernels_equal_the_reference_on_every_value_and_digest_every_frame()
        {
            var failures = AgainstReference("values", seeds.Range(Seeds.Main, Seeds.MainCount), out var checkedCount);
            Verdict("values", checkedCount, failures);
        }

        [Fact]
        public void Compiled_kernels_equal_the_reference_on_packs_without_absorb()
        {
            var failures = AgainstReference("noabsorb", seeds.Range(Seeds.NoAbsorb, Seeds.MainCount), out var checkedCount);
            Verdict("noabsorb", checkedCount, failures);
        }

        [Fact]
        public void Digests_are_equal_at_1_2_4_7_11_workers_and_16_or_64_parts()
        {
            var failures = new List<string>();
            var checkedCount = 0;
            foreach (var item in seeds.Range(Seeds.Main, Seeds.MainCount).Where(item => item.Ready))
            {
                checkedCount++;
                var baseline = Seeds.Compiled(item, Seeds.Options(1, 64, StoragePolicy.Shared));
                foreach (var workers in WorkerCounts)
                {
                    foreach (var parts in PartCounts)
                    {
                        if (workers == 1 && parts == 64)
                        {
                            continue;
                        }

                        var difference = RunTrace.FirstDifference(baseline, Seeds.Compiled(item, Seeds.Options(workers, parts, StoragePolicy.Shared)), true);
                        if (difference.Length > 0)
                        {
                            failures.Add(Seeds.Receipt("workers", item, workers + " workers, " + parts + " parts: " + difference));
                        }
                    }
                }
            }

            Verdict("workers", checkedCount, failures);
        }

        [Fact]
        public void Dense_policy_equals_shared_policy_on_every_value_and_digest()
        {
            var failures = new List<string>();
            var checkedCount = 0;
            foreach (var item in seeds.Range(Seeds.Main, Seeds.MainCount).Where(item => item.Ready))
            {
                checkedCount++;
                var shared = Seeds.Compiled(item, Seeds.Options(1, 64, StoragePolicy.Shared));
                foreach (var (workers, parts) in new[] { (1, 64), (4, 16) })
                {
                    var difference = RunTrace.FirstDifference(shared, Seeds.Compiled(item, Seeds.Options(workers, parts, StoragePolicy.Dense)), true);
                    if (difference.Length > 0)
                    {
                        failures.Add(Seeds.Receipt("dense", item, "dense at " + workers + " workers, " + parts + " parts: " + difference));
                    }
                }
            }

            Verdict("dense", checkedCount, failures);
        }

        [Fact]
        public void Card_modifiers_on_compiled_kernels_equal_the_reference()
        {
            var failures = AgainstReference("cardmodifiers", seeds.Range(Seeds.CardModifiers, Seeds.VariantCount), out var checkedCount);
            Verdict("cardmodifiers", checkedCount, failures);
        }

        [Fact]
        public void Stat_operations_on_compiled_kernels_equal_the_reference()
        {
            var failures = AgainstReference("statops", seeds.Range(Seeds.StatOps, Seeds.VariantCount), out var checkedCount);
            Verdict("statops", checkedCount, failures);
        }
    }
}
