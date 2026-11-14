using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Sv.Rules;
using Xunit;
using Xunit.Abstractions;

namespace DiffTests
{
    public sealed class RoundTripDiffTests
    {
        public static readonly int First = 1001 + Seeds.Offset;
        public const int Count = 200;
        readonly ITestOutputHelper output;

        public RoundTripDiffTests(ITestOutputHelper output) => this.output = output;

        void Verdict(string test, int checkedCount, List<string> failures)
        {
            output.WriteLine(test + ": " + checkedCount + " seeds checked, " + failures.Count + " failed, seeds " + First + "-" + (First + Count - 1));
            foreach (var failure in failures)
            {
                output.WriteLine(failure);
            }

            Assert.True(checkedCount > 0, test + ": no seed reached this check");
            Assert.True(failures.Count == 0, failures.Count + " of " + checkedCount + " seeds failed; first: " + failures.FirstOrDefault());
        }

        [Fact]
        public void Writing_and_reading_generated_packs_preserves_pack_hash()
        {
            var clock = Stopwatch.StartNew();
            var failures = new List<string>();
            var checkedCount = 0;
            for (var seed = First; seed < First + Count; seed++)
            {
                var options = new GeneratorOptions
                {
                    Bricks = (seed & 1) == 0,
                    CardModifiers = (seed & 2) == 0,
                    StatOps = (seed & 4) == 0,
                    Absorb = (seed & 8) != 0,
                };
                var made = PackGenerator.Make(seed, options);
                var item = new CompiledCase { Case = made };
                checkedCount++;

                var firstRead = PackReader.Read(made.Source);
                if (!firstRead.Ok || firstRead.Value == null)
                {
                    failures.Add(Seeds.Receipt("roundtrip", item, "initial read failed: " + string.Join("; ", firstRead.Diagnostics.Select(d => d.ToString()))));
                    continue;
                }

                var firstHash = PackHash.Of(firstRead.Value);
                var written = PackWriter.Write(firstRead.Value);
                var secondRead = PackReader.Read(written);
                if (!secondRead.Ok || secondRead.Value == null)
                {
                    failures.Add(Seeds.Receipt("roundtrip", item, "round-trip read failed: " + string.Join("; ", secondRead.Diagnostics.Select(d => d.ToString()))));
                    continue;
                }

                var secondHash = PackHash.Of(secondRead.Value);
                if (firstHash != secondHash)
                {
                    failures.Add(Seeds.Receipt("roundtrip", item, "PackHash mismatch: expected " + RunTrace.Hex(firstHash) + ", got " + RunTrace.Hex(secondHash)));
                }
            }

            output.WriteLine("roundtrip elapsed: " + clock.ElapsedMilliseconds + " ms");
            Verdict("roundtrip", checkedCount, failures);
        }
    }
}
