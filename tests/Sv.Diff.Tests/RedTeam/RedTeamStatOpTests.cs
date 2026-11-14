using System.Linq;
using Sv;
using Xunit;
using Xunit.Abstractions;

namespace DiffTests.RedTeam
{
    public sealed class RedTeamStatOpTests
    {
        const string Values = "{\"schema\":1,\"nextLane\":2,\"rows\":[\n"
            + "{\"id\":0,\"name\":\"Speed\",\"class\":\"stat\",\"default\":5},\n"
            + "{\"id\":1,\"name\":\"Health\",\"class\":\"intrinsic\",\"default\":100},\n"
            + "{\"id\":0,\"name\":\"Hit\",\"class\":\"channel\"}\n]}";

        const string NoRules = "{\"schema\":1,\"rows\":[]}";
        const string NoCards = "{\"schema\":1,\"rows\":[]}";
        const string OneTemplate = "{\"schema\":1,\"rows\":[{\"id\":1,\"name\":\"hero\"}]}";

        readonly ITestOutputHelper output;

        public RedTeamStatOpTests(ITestOutputHelper output) => this.output = output;

        void Same(string label, string modifiers, int frames, int expectedSpeed)
        {
            var scenario = "{\"schema\":1,\"name\":\"" + label + "\",\"frames\":" + frames + ",\"entities\":[{\"name\":\"Hero\",\"template\":\"hero\"}],\"modifiers\":[" + modifiers + "]}";
            var item = KernelCompiler.Build(new[] { new GeneratedCase { Seed = -9000, Namespace = "Diff.RedTeam.StatOps." + label, Values = Values, Rules = NoRules, Cards = NoCards, Templates = OneTemplate, Scenario = scenario } }).Single();
            Assert.True(item.Ready, item.Failure);
            var reference = Seeds.Reference(item);
            var compiled = Seeds.Compiled(item, Seeds.Options(1, 64, StoragePolicy.Shared));
            var last = frames - 1;
            var speedAt = reference.Frames[last].Names.IndexOf("Hero.Speed");
            output.WriteLine(label + ": reference Speed " + reference.Frames[last].Values[speedAt] + ", compiled Speed " + compiled.Frames[last].Values[speedAt] + ", contract " + expectedSpeed);
            Assert.Equal(expectedSpeed, compiled.Frames[last].Values[speedAt]);
            Assert.Equal(expectedSpeed, reference.Frames[last].Values[speedAt]);
            Assert.Equal(string.Empty, RunTrace.FirstDifference(reference, compiled, true));
        }

        [Fact]
        public void Refreshing_a_buff_by_removing_and_re_adding_its_source_in_one_frame_keeps_the_new_buff()
        {
            Same(
                "Refresh",
                "{\"frame\":0,\"entity\":\"Hero\",\"stat\":\"Speed\",\"shape\":\"Added\",\"value\":3,\"source\":42},"
                + "{\"frame\":1,\"entity\":\"Hero\",\"stat\":\"Speed\",\"source\":42,\"op\":\"remove\"},"
                + "{\"frame\":1,\"entity\":\"Hero\",\"stat\":\"Speed\",\"shape\":\"Added\",\"value\":4,\"source\":42}",
                2,
                9);
        }

        [Fact]
        public void Removing_and_adding_one_source_in_the_first_frame_keeps_the_add()
        {
            Same(
                "SameFrame",
                "{\"frame\":0,\"entity\":\"Hero\",\"stat\":\"Speed\",\"source\":7,\"op\":\"remove\"},"
                + "{\"frame\":0,\"entity\":\"Hero\",\"stat\":\"Speed\",\"shape\":\"Added\",\"value\":10,\"source\":7}",
                1,
                15);
        }

        [Fact]
        public void One_hundred_and_twenty_stacked_multipliers_saturate_alike()
        {
            var adds = string.Join(",", Enumerable.Range(0, 120).Select(at => "{\"frame\":0,\"entity\":\"Hero\",\"stat\":\"Speed\",\"shape\":\"Multi\",\"value\":" + (at % 3 == 0 ? 10000 : 2500) + ",\"source\":" + at + "}"));
            var scenario = "{\"schema\":1,\"name\":\"stack\",\"frames\":2,\"entities\":[{\"name\":\"Hero\",\"template\":\"hero\"}],\"modifiers\":[" + adds + "]}";
            var item = KernelCompiler.Build(new[] { new GeneratedCase { Seed = -9001, Namespace = "Diff.RedTeam.StatOps.Stack", Values = Values, Rules = NoRules, Cards = NoCards, Templates = OneTemplate, Scenario = scenario } }).Single();
            Assert.True(item.Ready, item.Failure);
            Assert.Equal(string.Empty, RunTrace.FirstDifference(Seeds.Reference(item), Seeds.Compiled(item, Seeds.Options(4, 16, StoragePolicy.Dense)), true));
        }
    }
}
