using Sv.Pack;
using Sv.Rules.Emit;

namespace Sv.Columns.Tests;

public sealed class RedTeamGlueTests
{
    const string EntitiesPackage = "packages/com.sv.columns.entities";

    const string Hostile = "a\"b\\c\nd\re\u2028f\u0085g\th";

    const string HostileLiteral = @"""a\""b\\c\u000Ad\u000De\u2028f\u0085g\u0009h""";

    static string Root
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder != null && !Directory.Exists(Path.Combine(folder.FullName, EntitiesPackage)))
            {
                folder = folder.Parent;
            }

            return folder?.FullName ?? throw new DirectoryNotFoundException(EntitiesPackage);
        }
    }

    static ColumnPack TestPack() => PackJson.Read(File.ReadAllText(Path.Combine(Root, EntitiesPackage, "Tests/Glue/Pack~/arena-tl.json")));

    [Theory]
    [InlineData("", "\"\"")]
    [InlineData("plain", "\"plain\"")]
    [InlineData("say \"hi\"", @"""say \""hi\""""")]
    [InlineData(@"c:\tmp", @"""c:\\tmp""")]
    [InlineData("two\nlines", @"""two\u000Alines""")]
    [InlineData("\u2028\u2029\u0085\u007F\u0000", @"""\u2028\u2029\u0085\u007F\u0000""")]
    [InlineData("\u00E9", @"""\u00E9""")]
    public void A_glue_literal_escapes_every_character_that_could_end_the_string_or_the_line(string text, string literal)
    {
        Assert.Equal(literal, Names.Literal(text));
    }

    [Fact]
    public void Hostile_timeline_names_emit_escaped_one_line_literals_in_both_glue_files()
    {
        var pack = TestPack();
        pack.Timelines.Starts.Add(new TimelineStart("LowHealth", Hostile));
        pack.Timelines.Outbox.Add(new TimelineOutbox("Healing2", Hostile));
        foreach (var text in new[] { ColumnsGlue.Emit(pack, "Game", false), ColumnsGlue.EmitEntities(pack, "Game") })
        {
            Assert.Contains(HostileLiteral, text);
            foreach (var bad in new[] { '\r', '\u2028', '\u2029', '\u0085', '\t' })
            {
                Assert.False(text.Contains(bad), "raw \\u" + ((int)bad).ToString("X4") + " in the glue");
            }

            foreach (var line in text.Split('\n'))
            {
                Assert.Equal(0, line.Replace("\\\\", string.Empty).Replace("\\\"", string.Empty).Count(c => c == '"') % 2);
            }
        }
    }

    [Fact]
    public void Two_cards_that_differ_only_in_case_are_refused_instead_of_emitting_one_type_twice()
    {
        var pack = TestPack();
        pack.Timelines.Cards.Add("Poison");
        var problems = ColumnsGlue.Problems(pack);
        Assert.Contains("timelines: card 'poison' and card 'Poison' both become the glue type PoisonCard; rename one", problems);
        var error = Assert.Throws<PackException>(() => ColumnsGlue.Files(pack, "Game", false));
        Assert.Contains(problems[0], error.Problems);
    }

    [Fact]
    public void A_card_and_an_outbox_channel_that_land_on_the_same_glue_type_are_refused()
    {
        var pack = TestPack();
        pack.Timelines.Outbox.Add(new TimelineOutbox("PoisonCard"));
        var problems = ColumnsGlue.Problems(pack);
        Assert.Contains("timelines: card 'poison' and outbox channel 'PoisonCard' both become the glue type PoisonCardFeed; rename one", problems);
        Assert.Throws<PackException>(() => ColumnsGlue.Files(pack, "Game", true));
    }

    [Fact]
    public void The_shipped_test_pack_has_no_glue_problems()
    {
        Assert.Empty(ColumnsGlue.Problems(TestPack()));
    }
}
