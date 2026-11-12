using Sv;
using Sv.Host;
using Sv.Pack;
using Xunit;
using Xunit.Abstractions;
using SentenceKernels = Sv.Kernels.Sentences.HostKernels;

namespace ScenarioTests
{
    public sealed class SentenceTests
    {
        readonly ITestOutputHelper output;

        public SentenceTests(ITestOutputHelper output) => this.output = output;

        public static TheoryData<string> Sentences => new TheoryData<string>
        {
            "r8_no_physical",
            "r8_immune_to_snipers",
            "r8_immune_to_damage",
            "r8_cursed_blocks_healing",
            "r8_heal_priority_while_jumping",
            "r8_immunity_bit_mask",
            "r8_immune_charges",
            "r8_shield",
            "r9_reflection",
            "r9_immune_and_still_reflect",
            "r9_not_taken_goes_to_them",
            "r9_mutual_reflection_terminates",
            "r9_shooter_immune",
            "r9_mutual_immunity",
            "r10_heal_per_damage",
            "r10_low_health_event",
            "r13_hand_and_shield_pass_to_owner",
        };

        static Scenario Read(string name) => Packs.Read(Packs.Sentences, Packs.SentenceFile(name));

        ScenarioOutcome Reference(string name) => Report("reference", ScenarioRunner.Reference(Packs.Sentences, Read(name)));

        ScenarioOutcome Compiled(string name, StoragePolicy policy) =>
            Report("compiled " + policy, ScenarioRunner.CompiledWith<SentenceKernels>(Packs.Sentences, Read(name), Packs.Options(policy)));

        ScenarioOutcome Report(string host, ScenarioOutcome outcome)
        {
            output.WriteLine(host + ": " + string.Join(" | ", outcome.Lines));
            foreach (var failure in outcome.Failures)
            {
                output.WriteLine(host + " FAIL " + failure);
            }

            return outcome;
        }

        [Theory]
        [MemberData(nameof(Sentences))]
        public void Reference_interpreter_meets_every_expected_number(string name) => Assert.Empty(Reference(name).Failures);

        [Theory]
        [MemberData(nameof(Sentences))]
        public void Compiled_kernels_meet_every_expected_number(string name) => Assert.Empty(Compiled(name, StoragePolicy.Shared).Failures);

        [Theory]
        [MemberData(nameof(Sentences))]
        public void Compiled_digest_equals_reference_digest(string name) =>
            Assert.Equal(ScenarioRunner.Hex(Reference(name).Digest), ScenarioRunner.Hex(Compiled(name, StoragePolicy.Shared).Digest));

        [Theory]
        [MemberData(nameof(Sentences))]
        public void Dense_policy_digest_equals_shared_policy_digest(string name) =>
            Assert.Equal(ScenarioRunner.Hex(Compiled(name, StoragePolicy.Shared).Digest), ScenarioRunner.Hex(Compiled(name, StoragePolicy.Dense).Digest));
    }
}
