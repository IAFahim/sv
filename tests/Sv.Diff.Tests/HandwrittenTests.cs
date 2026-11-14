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
        public void An_event_that_reads_a_channel_total_sees_the_frame_total_on_both_hosts()
        {
            var values = "{\"schema\":1,\"nextLane\":2,\"rows\":[\n"
                + "{\"id\":0,\"name\":\"Health\",\"class\":\"intrinsic\",\"default\":100},\n"
                + "{\"lane\":1,\"word\":0,\"name\":\"BigHit\",\"class\":\"event\",\"default\":false},\n"
                + "{\"id\":0,\"name\":\"Hit\",\"class\":\"channel\"}\n]}";
            var rules = "{\"schema\":1,\"rows\":[\n"
                + "{\"id\":1,\"name\":\"hit_flow\",\"layer\":\"flow\",\"then\":[{\"subtract\":[\"Health\",\"Hit\"]}]},\n"
                + "{\"id\":2,\"name\":\"big_hit\",\"layer\":\"event\",\"event\":\"BigHit\",\"enter\":{\"gt\":[{\"total\":\"Hit\"},15]}}\n]}";
            var scenario = "{\"schema\":1,\"name\":\"total\",\"frames\":3,\"entities\":[{\"name\":\"E0\",\"template\":\"base\"}],"
                + "\"sends\":[{\"frame\":0,\"target\":\"E0\",\"channel\":\"Hit\",\"amount\":10,\"hits\":2}]}";
            SameOnBothHosts(new GeneratedCase { Seed = -5, Namespace = "Diff.EventTotal", Values = values, Rules = rules, Cards = NoCards, Templates = OneTemplate, Scenario = scenario });
        }

        [Fact]
        public void A_pack_without_forwarding_templates_compiles_without_warnings()
        {
            var item = KernelCompiler.Build(new[] { new GeneratedCase { Seed = -6, Namespace = "Diff.NoForwards", Values = AbsorbValues, Rules = AbsorbRules, Cards = NoCards, Templates = OneTemplate, Scenario = HitScenario(1) } }).Single();
            Assert.True(item.Ready, item.Failure);
            Assert.Equal(string.Empty, item.Warnings);
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

        const string FourChannelValues = "{\"schema\":1,\"nextLane\":1,\"rows\":[\n"
            + "{\"id\":0,\"name\":\"Health\",\"class\":\"intrinsic\",\"default\":10},\n"
            + "{\"id\":0,\"name\":\"C0\",\"class\":\"channel\"},\n"
            + "{\"id\":1,\"name\":\"C1\",\"class\":\"channel\"},\n"
            + "{\"id\":2,\"name\":\"C2\",\"class\":\"channel\"},\n"
            + "{\"id\":3,\"name\":\"C3\",\"class\":\"channel\"}\n]}";

        static string AggregateEmitRules(string per, string offered, string taken) => "{\"schema\":1,\"rows\":[\n"
            + "{\"id\":1,\"name\":\"flow\",\"layer\":\"flow\",\"then\":[{\"subtract\":[\"Health\",\"C0\"]}]},\n"
            + "{\"id\":2,\"name\":\"emit\",\"layer\":\"emit\",\"depthLimit\":1,\"when\":{\"and\":[{\"gt\":[{\"offered\":[" + offered + "]},0]},{\"gt\":[{\"taken\":[" + taken + "]},0]}]},"
            + "\"then\":[{\"emit\":{\"to\":\"Self\",\"channel\":\"C0\",\"amount\":1,\"per\":\"" + per + "\"}}]}\n]}";

        static string SendsScenario(params string[] channels) =>
            "{\"schema\":1,\"name\":\"aggregate\",\"frames\":2,\"entities\":[{\"name\":\"E0\",\"template\":\"base\"}],\"sends\":["
            + string.Join(",", channels.Select(channel => "{\"frame\":0,\"target\":\"E0\",\"channel\":\"" + channel + "\",\"amount\":1}")) + "]}";

        [Theory]
        [InlineData("rule", -1)]
        [InlineData("card", -1)]
        [InlineData("template", -1)]
        [InlineData("rule", 1)]
        public void A_negative_rule_card_or_template_id_is_rejected_when_the_pack_compiles(string row, int id)
        {
            var values = "{\"schema\":1,\"nextLane\":2,\"rows\":[{\"id\":0,\"name\":\"Health\",\"class\":\"intrinsic\",\"default\":10},{\"id\":1,\"name\":\"Charges\",\"class\":\"intrinsic\",\"default\":0},{\"id\":0,\"name\":\"C0\",\"class\":\"channel\"}]}";
            var ruleId = row == "rule" ? id : 1;
            var cardId = row == "card" ? id : 1;
            var templateId = row == "template" ? id : 1;
            var rules = "{\"schema\":1,\"rows\":[{\"id\":" + ruleId + ",\"name\":\"emit\",\"layer\":\"emit\",\"depthLimit\":0,\"when\":true,\"then\":[{\"emit\":{\"to\":\"Self\",\"channel\":\"C0\",\"amount\":1}}]}]}";
            var cards = "{\"schema\":1,\"rows\":[{\"id\":" + cardId + ",\"name\":\"card\",\"value\":\"Charges\",\"modifiers\":[]}]}";
            var templates = "{\"schema\":1,\"rows\":[{\"id\":" + templateId + ",\"name\":\"base\"}]}";
            var compiled = Sv.Rules.PackCompiler.Compile(new Sv.Rules.PackSource(values, rules, cards, templates));
            output.WriteLine(row + " id " + id + ": " + string.Join("; ", compiled.Diagnostics.Select(d => d.ToString())));
            if (id < 0)
            {
                Assert.False(compiled.Ok);
                Assert.Contains(compiled.Diagnostics, d => d.Code == Sv.Rules.DiagnosticCode.IdOutOfRange);
            }
            else
            {
                Assert.True(compiled.Ok);
            }
        }

        [Theory]
        [InlineData(-7, "Frame", "\"C1\"", "\"C0\"", "C0,C1")]
        [InlineData(-8, "Source", "\"C1\"", "\"C0\"", "C0,C1")]
        [InlineData(-9, "Frame", "\"C2\",\"C0\"", "\"C2\",\"C3\"", "C0,C3")]
        [InlineData(-10, "Source", "\"C2\",\"C1\"", "\"C2\",\"C3\"", "C1,C3")]
        [InlineData(-11, "Frame", "\"C2\",\"C0\"", "\"C2\",\"C3\"", "C2")]
        [InlineData(-12, "Frame", "\"C1\"", "\"C0\"", "C0")]
        public void An_aggregate_emit_fires_when_each_conjunct_sees_its_own_channel(int seed, string per, string offered, string taken, string sends)
        {
            var scenario = SendsScenario(sends.Split(','));
            SameOnBothHosts(new GeneratedCase { Seed = seed, Namespace = "Diff.Aggregate" + (-seed), Values = FourChannelValues, Rules = AggregateEmitRules(per, offered, taken), Cards = NoCards, Templates = OneTemplate, Scenario = scenario });
        }
    }
}
