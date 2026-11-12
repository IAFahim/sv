using System.Linq;
using Sv;
using Xunit;
using Xunit.Abstractions;

namespace DiffTests
{
    public sealed class HandwrittenTests
    {
        const string AbsorbValues = "{\"schema\":1,\"nextLane\":2,\"rows\":[\n"
            + "{\"id\":0,\"name\":\"Health\",\"class\":\"intrinsic\",\"default\":100},\n"
            + "{\"id\":1,\"name\":\"Budget\",\"class\":\"intrinsic\",\"default\":2},\n"
            + "{\"id\":0,\"name\":\"Hit\",\"class\":\"channel\"}\n]}";

        const string AbsorbRules = "{\"schema\":1,\"rows\":[\n"
            + "{\"id\":1,\"name\":\"hit_flow\",\"layer\":\"flow\",\"then\":[{\"subtract\":[\"Health\",\"Hit\"]}]},\n"
            + "{\"id\":2,\"name\":\"charges\",\"layer\":\"gate\",\"priority\":1,\"then\":[{\"absorb\":{\"budget\":\"Budget\",\"per\":\"Hit\"}}]}\n]}";

        const string NoCards = "{\"schema\":1,\"rows\":[]}";

        const string OneTemplate = "{\"schema\":1,\"rows\":[{\"id\":1,\"name\":\"base\"}]}";

        const string StatValues = "{\"schema\":1,\"nextLane\":2,\"rows\":[\n"
            + "{\"id\":0,\"name\":\"S0\",\"class\":\"stat\",\"default\":10.0},\n"
            + "{\"id\":1,\"name\":\"Card0\",\"class\":\"intrinsic\",\"default\":0},\n"
            + "{\"id\":0,\"name\":\"GiveCard0\",\"class\":\"channel\",\"into\":\"Card0\"}\n]}";

        const string NoRules = "{\"schema\":1,\"rows\":[]}";

        const string CardWithModifier = "{\"schema\":1,\"rows\":[{\"id\":1,\"name\":\"card0\",\"value\":\"Card0\",\"modifiers\":[{\"stat\":\"S0\",\"added\":5.0}]}]}";

        readonly ITestOutputHelper output;

        public HandwrittenTests(ITestOutputHelper output) => this.output = output;

        static string HitScenario(int hits) =>
            "{\"schema\":1,\"name\":\"absorb\",\"frames\":2,\"entities\":[{\"name\":\"E0\",\"template\":\"base\"}],"
            + "\"sends\":[{\"frame\":0,\"target\":\"E0\",\"channel\":\"Hit\",\"amount\":10,\"hits\":" + hits + "}]}";

        void SameOnBothHosts(GeneratedCase generated)
        {
            var item = KernelCompiler.Build(new[] { generated }).Single();
            Assert.True(item.Ready, item.Failure);
            var reference = Seeds.Reference(item);
            var compiled = Seeds.Compiled(item, Seeds.Options(1, 64, StoragePolicy.Shared));
            for (var frame = 0; frame < reference.Frames.Count; frame++)
            {
                output.WriteLine("frame " + frame + " reference " + Show(reference.Frames[frame]) + " | compiled " + Show(compiled.Frames[frame]));
            }

            Assert.Equal(string.Empty, RunTrace.FirstDifference(reference, compiled, true));
        }

        static string Show(FrameSnapshot snapshot) => string.Join(" ", snapshot.Names.Select((name, i) => name + "=" + snapshot.Values[i])) + " digest " + RunTrace.Hex(snapshot.Digest);

        [Fact]
        public void Absorb_spends_its_budget_when_it_absorbs_every_hit()
        {
            SameOnBothHosts(new GeneratedCase { Seed = -1, Namespace = "Diff.AbsorbAll", Values = AbsorbValues, Rules = AbsorbRules, Cards = NoCards, Templates = OneTemplate, Scenario = HitScenario(1) });
        }

        [Fact]
        public void Absorb_spends_its_budget_when_some_hits_are_taken()
        {
            SameOnBothHosts(new GeneratedCase { Seed = -2, Namespace = "Diff.AbsorbSome", Values = AbsorbValues, Rules = AbsorbRules, Cards = NoCards, Templates = OneTemplate, Scenario = HitScenario(3) });
        }

        [Fact]
        public void A_card_modifier_reaches_the_stat_on_both_hosts()
        {
            var scenario = "{\"schema\":1,\"name\":\"card\",\"frames\":3,\"entities\":[{\"name\":\"E0\",\"template\":\"base\"}],"
                + "\"sends\":[{\"frame\":0,\"target\":\"E0\",\"channel\":\"GiveCard0\",\"amount\":1}]}";
            SameOnBothHosts(new GeneratedCase { Seed = -3, Namespace = "Diff.CardModifier", Values = StatValues, Rules = NoRules, Cards = CardWithModifier, Templates = OneTemplate, Scenario = scenario });
        }

        [Fact]
        public void Two_modifiers_from_one_source_fold_alike_on_both_hosts()
        {
            var scenario = "{\"schema\":1,\"name\":\"source\",\"frames\":2,\"entities\":[{\"name\":\"E0\",\"template\":\"base\"}],\"modifiers\":["
                + "{\"frame\":0,\"entity\":\"E0\",\"stat\":\"S0\",\"shape\":\"Added\",\"value\":5,\"source\":1},"
                + "{\"frame\":0,\"entity\":\"E0\",\"stat\":\"S0\",\"shape\":\"Added\",\"value\":3,\"source\":1}]}";
            SameOnBothHosts(new GeneratedCase { Seed = -4, Namespace = "Diff.OneSource", Values = StatValues, Rules = NoRules, Cards = NoCards, Templates = OneTemplate, Scenario = scenario });
        }
    }
}
