using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Sv.Pack;
using Sv.Rules;
using Sv.Rules.Emit;
using Xunit;

namespace DiffTests.RedTeam
{
    public sealed class RedTeamPackTests
    {
        static string Root
        {
            get
            {
                var folder = new DirectoryInfo(AppContext.BaseDirectory);
                while (folder != null && !Directory.Exists(Path.Combine(folder.FullName, "packs", "reference")))
                {
                    folder = folder.Parent;
                }

                return folder?.FullName ?? throw new DirectoryNotFoundException("packs/reference");
            }
        }

        static PackSource Reference() => PackFolder.Read(Path.Combine(Root, "packs", "reference"));

        static JsonObject Row(JsonNode file, string key, object value) =>
            file["rows"]!.AsArray().Select(row => row!.AsObject()).First(row => row[key]!.ToString() == value.ToString());

        static string Edit(string json, Action<JsonNode> edit)
        {
            var root = JsonNode.Parse(json)!;
            edit(root);
            return root.ToJsonString();
        }

        [Fact]
        public void Channel_numbers_outside_the_schema_in_an_in_set_survive_a_write_and_read_with_the_same_hash()
        {
            var source = Reference();
            var rules = Edit(source.Rules, root =>
            {
                var set = Row(root, "id", 4102)["when"]!["in"]![1]!.AsArray();
                set.Add(50);
                set.Add(4294967297L);
            });
            var first = PackCompiler.Compile(new PackSource(source.Values, rules, source.Cards, source.Templates));
            Assert.True(first.Ok, string.Join(" | ", first.Diagnostics.Select(d => d.Message)));
            var written = PackWriter.Write(first.Value!.Pack);
            Assert.DoesNotContain("\"?", written.Rules);
            var again = PackCompiler.Compile(written);
            Assert.True(again.Ok, string.Join(" | ", again.Diagnostics.Select(d => d.Message)));
            Assert.Equal(first.Value.Hash, again.Value!.Hash);
        }

        [Fact]
        public void A_template_whose_cards_land_on_the_same_value_twice_is_refused()
        {
            var source = Reference();
            var cards = Edit(source.Cards, root => Row(root, "name", "sniper_ward")["value"] = "Leech");
            var result = PackCompiler.Compile(new PackSource(source.Values, source.Rules, cards, source.Templates));
            Assert.False(result.Ok);
            Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.DuplicateName && d.Message.Contains("sets Leech twice", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("Leech", "SniperWard", "counts in a different intrinsic than the one named Leech")]
        [InlineData("leech", "SniperWard", "two cards are named leech")]
        public void A_card_name_that_could_resolve_to_two_different_cards_is_refused(string name, string value, string message)
        {
            var source = Reference();
            var cards = Edit(source.Cards, root => root["rows"]!.AsArray().Add(new JsonObject { ["id"] = 900, ["name"] = name, ["value"] = value }));
            var result = PackCompiler.Compile(new PackSource(source.Values, source.Rules, cards, source.Templates));
            Assert.False(result.Ok);
            Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.DuplicateName && d.Message.Contains(message, StringComparison.Ordinal));
        }

        [Fact]
        public void A_rule_name_with_tabs_and_line_breaks_keeps_every_rule_map_line_at_six_fields()
        {
            var source = Reference();
            var rules = Edit(source.Rules, root => Row(root, "id", 4102)["name"] = "thorns\tbad\nname\r\u2028\u0085end");
            var unit = PackEmitter.Compile(new PackSource(source.Values, rules, source.Cards, source.Templates));
            var map = unit.Files.First(file => file.Name == "Rules.map.txt").Text;
            var lines = map.Split('\n').Where(line => line.Length > 0).ToArray();
            Assert.NotEmpty(lines);
            foreach (var line in lines)
            {
                var fields = line.Split('\t');
                Assert.Equal(6, fields.Length);
                Assert.False(fields.Any(field => field.Any(c => c < ' ' || c == '\u2028' || c == '\u0085')), line);
            }

            Assert.Contains(lines, line => line.Contains("thorns\\u0009bad\\u000Aname\\u000D\\u2028\\u0085end", StringComparison.Ordinal));
        }
    }
}
