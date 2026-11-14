using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sv.Rules;
using Xunit;
using Xunit.Abstractions;

namespace DiffTests.RedTeam
{
    public sealed class RedTeamPackLimitTests
    {
        const string NoCards = "{\"schema\":1,\"rows\":[]}";
        const string NoRules = "{\"schema\":1,\"rows\":[]}";

        readonly ITestOutputHelper output;

        public RedTeamPackLimitTests(ITestOutputHelper output) => this.output = output;

        static string Values(IEnumerable<string> rows, int nextLane) =>
            "{\"schema\":1,\"nextLane\":" + nextLane + ",\"rows\":[\n" + string.Join(",\n", rows) + "\n]}";

        static string Templates(params string[] names) =>
            "{\"schema\":1,\"rows\":[" + string.Join(",", names.Select((name, at) => "{\"id\":" + (at + 1) + ",\"name\":\"" + name + "\"}")) + "]}";

        static string Scenario(string template) =>
            "{\"schema\":1,\"name\":\"names\",\"frames\":1,\"entities\":[{\"name\":\"E0\",\"template\":\"" + template + "\"}]}";

        string Verdict(string label, string values, string rules, string cards, string templates, string scenarioTemplate)
        {
            var generated = new GeneratedCase
            {
                Seed = 0,
                Namespace = "Diff.RedTeam.Names." + label,
                Values = values,
                Rules = rules,
                Cards = cards,
                Templates = templates,
                Scenario = Scenario(scenarioTemplate),
            };
            CompiledCase item;
            try
            {
                item = KernelCompiler.Build(new[] { generated })[0];
            }
            catch (Exception error)
            {
                return label + ": compiler threw " + error.GetType().Name + ": " + error.Message;
            }

            if (item.Failure.StartsWith("pack rejected", StringComparison.Ordinal) || (item.Failure.StartsWith("emitter threw", StringComparison.Ordinal) && item.Failure.Contains("error SV", StringComparison.Ordinal)))
            {
                output.WriteLine(label + ": rejected (" + item.Failure.Replace("\n", " ") + ")");
                return string.Empty;
            }

            if (item.Failure.Length > 0)
            {
                return label + ": accepted but " + item.Failure.Split('\n').Take(3).Aggregate((a, b) => a + " | " + b);
            }

            if (item.Warnings.Length > 0)
            {
                return label + ": accepted but the generated C# warns: " + item.Warnings.Split('\n')[0];
            }

            output.WriteLine(label + ": accepted and compiles");
            return string.Empty;
        }

        static string Intrinsic(int id, string name) => "{\"id\":" + id + ",\"name\":\"" + name + "\",\"class\":\"intrinsic\",\"default\":0}";

        static string Channel(int id, string name) => "{\"id\":" + id + ",\"name\":\"" + name + "\",\"class\":\"channel\"}";

        static string Kind(int id, string name) => "{\"id\":" + id + ",\"name\":\"" + name + "\",\"class\":\"kind\"}";

        public static IEnumerable<object[]> NameCases()
        {
            yield return new object[] { "MangledIntrinsics", new[] { Intrinsic(0, "a-b"), Intrinsic(1, "a_b"), Channel(0, "Hit") }, "base" };
            yield return new object[] { "MangledChannels", new[] { Intrinsic(0, "Health"), Channel(0, "fire.dmg"), Channel(1, "fire_dmg") }, "base" };
            yield return new object[] { "UnicodeChannels", new[] { Intrinsic(0, "Health"), Channel(0, "féu"), Channel(1, "f_u") }, "base" };
            yield return new object[] { "KeywordNames", new[] { Intrinsic(0, "class"), Intrinsic(1, "int"), Channel(0, "event"), Kind(0, "default") }, "base" };
            yield return new object[] { "EnumReservedName", new[] { Intrinsic(0, "value__"), Channel(0, "Hit") }, "base" };
            yield return new object[] { "CaseOnly", new[] { Intrinsic(0, "health"), Intrinsic(1, "Health"), Channel(0, "Hit") }, "base" };
            yield return new object[] { "DigitFirst", new[] { Intrinsic(0, "1st"), Intrinsic(1, "_1st"), Channel(0, "Hit") }, "base" };
            yield return new object[] { "ContextualKeywords", new[] { Intrinsic(0, "var"), Intrinsic(1, "record"), Intrinsic(2, "nameof"), Channel(0, "dynamic") }, "base" };
            yield return new object[] { "TypeNamedValues", new[] { Intrinsic(0, "Layout"), Intrinsic(1, "Kernels"), Intrinsic(2, "Intrinsic"), Channel(0, "Channel") }, "base" };
        }

        [Theory]
        [MemberData(nameof(NameCases))]
        public void A_pack_with_awkward_names_is_rejected_or_compiles_clean(string label, string[] rows, string template)
        {
            var values = Values(rows, rows.Count(row => row.Contains("\"intrinsic\"") || row.Contains("\"stat\"")));
            var verdict = Verdict(label, values, NoRules, NoCards, Templates(template), template);
            Assert.True(verdict.Length == 0, verdict);
        }

        [Theory]
        [InlineData("TemplateKeyword", "class", "int")]
        [InlineData("TemplateMangled", "orc-boss", "orc_boss")]
        [InlineData("TemplateReserved", "value__", "base")]
        [InlineData("TemplateTypes", "Layout", "Values")]
        public void Templates_with_awkward_names_are_rejected_or_compile_clean(string label, string first, string second)
        {
            var values = Values(new[] { Intrinsic(0, "Health"), Channel(0, "Hit") }, 1);
            var verdict = Verdict(label, values, NoRules, NoCards, Templates(first, second), first);
            Assert.True(verdict.Length == 0, verdict);
        }

        [Fact]
        public void A_pack_with_256_channels_and_256_kinds_compiles_clean_and_257_is_rejected()
        {
            var rows = new List<string> { Intrinsic(0, "Health") };
            rows.AddRange(Enumerable.Range(0, 256).Select(at => Channel(at, "C" + at)));
            rows.AddRange(Enumerable.Range(0, 256).Select(at => Kind(at, "K" + at)));
            var rules = "{\"schema\":1,\"rows\":[{\"id\":1,\"name\":\"flow\",\"layer\":\"flow\",\"then\":[{\"subtract\":[\"Health\"," + string.Join(",", Enumerable.Range(0, 256).Select(at => "\"C" + at + "\"")) + "]}]},"
                + "{\"id\":2,\"name\":\"gate\",\"layer\":\"gate\",\"priority\":1,\"when\":{\"eq\":[\"record.channel\",\"C255\"]},\"then\":[{\"deny\":{}}]}]}";
            var verdict = Verdict("Channels256", Values(rows, 1), rules, NoCards, Templates("base"), "base");
            Assert.True(verdict.Length == 0, verdict);
            rows.Add(Channel(256, "C256"));
            var compiled = PackCompiler.Compile(new PackSource(Values(rows, 1), NoRules, NoCards, Templates("base")));
            Assert.Null(compiled.Value);
            output.WriteLine(string.Join("\n", compiled.Diagnostics.Where(d => d.Severity == Severity.Error)));
        }

        [Fact]
        public void A_derive_cycle_through_three_stats_is_rejected()
        {
            var values = "{\"schema\":1,\"nextLane\":3,\"rows\":[\n"
                + "{\"id\":0,\"name\":\"A\",\"class\":\"stat\",\"default\":1.0,\"derive\":{\"from\":\"C\",\"coefficient\":1.0}},\n"
                + "{\"id\":1,\"name\":\"B\",\"class\":\"stat\",\"default\":1.0,\"derive\":{\"from\":\"A\",\"coefficient\":1.0}},\n"
                + "{\"id\":2,\"name\":\"C\",\"class\":\"stat\",\"default\":1.0,\"derive\":{\"from\":\"B\",\"coefficient\":1.0}},\n"
                + "{\"id\":0,\"name\":\"Hit\",\"class\":\"channel\"}\n]}";
            var compiled = PackCompiler.Compile(new PackSource(values, NoRules, NoCards, Templates("base")));
            Assert.Null(compiled.Value);
            Assert.Contains(compiled.Diagnostics, d => d.Code == DiagnosticCode.CyclicStat);
        }

        [Fact]
        public void Huge_and_hostile_numbers_in_a_pack_are_diagnostics_never_exceptions()
        {
            var hostile = new[] { "2147483648", "-2147483649", "1e400", "-0", "0.0000001", "NaN", "\"7\"", "99999999999999999999999999", "true", "null", "[]" };
            var failures = new List<string>();
            foreach (var number in hostile)
            {
                var candidates = new[]
                {
                    Values(new[] { "{\"id\":0,\"name\":\"Health\",\"class\":\"intrinsic\",\"default\":" + number + "}", Channel(0, "Hit") }, 1),
                    Values(new[] { "{\"id\":0,\"name\":\"Speed\",\"class\":\"stat\",\"default\":" + number + ",\"scale\":1000000}", Channel(0, "Hit") }, 1),
                    Values(new[] { "{\"id\":0,\"name\":\"Speed\",\"class\":\"stat\",\"default\":1.0,\"scale\":" + number + "}", Channel(0, "Hit") }, 1),
                    Values(new[] { Intrinsic(0, "Health"), "{\"id\":" + number + ",\"name\":\"Hit\",\"class\":\"channel\"}" }, 1),
                };
                foreach (var values in candidates)
                {
                    try
                    {
                        var compiled = PackCompiler.Compile(new PackSource(values, NoRules, NoCards, Templates("base")));
                        if (compiled.Value == null && !compiled.Diagnostics.Any(d => d.Severity == Severity.Error))
                        {
                            failures.Add(number + ": rejected without an error diagnostic");
                        }
                    }
                    catch (Exception error)
                    {
                        failures.Add(number + ": " + error.GetType().Name + ": " + error.Message);
                    }
                }
            }

            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        [Fact]
        public void Deep_nesting_in_a_rule_is_a_diagnostic_not_a_stack_overflow()
        {
            var depth = 20000;
            var when = new StringBuilder();
            for (var at = 0; at < depth; at++)
            {
                when.Append("{\"and\":[");
            }

            when.Append("{\"gt\":[\"self.Health\",0]}");
            for (var at = 0; at < depth; at++)
            {
                when.Append("]}");
            }

            var rules = "{\"schema\":1,\"rows\":[{\"id\":1,\"name\":\"deep\",\"layer\":\"gate\",\"priority\":1,\"when\":" + when + ",\"then\":[{\"deny\":{}}]}]}";
            var values = Values(new[] { Intrinsic(0, "Health"), Channel(0, "Hit") }, 1);
            var compiled = PackCompiler.Compile(new PackSource(values, rules, NoCards, Templates("base")));
            Assert.Null(compiled.Value);
            Assert.NotEmpty(compiled.Diagnostics);
        }
    }
}
