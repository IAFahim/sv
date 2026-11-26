using Sv.Pack;

namespace Sv.Columns.Tests;

public sealed class TlGlueTests
{
    const string EntitiesPackage = "packages/com.sv.columns.entities";

    static string Root
    {
        get
        {
            var folder = AppContext.BaseDirectory;
            while (folder != null && !Directory.Exists(Path.Combine(folder, EntitiesPackage)))
            {
                folder = Path.GetDirectoryName(folder);
            }

            Assert.NotNull(folder);
            return folder!;
        }
    }

    static string Repo(string path) => Path.Combine(Root, path);

    static string TestPack() => File.ReadAllText(Repo(EntitiesPackage + "/Tests/Glue/Pack~/arena-tl.json"));

    static bool Rewrite => Environment.GetEnvironmentVariable("SV_UPDATE_GLUE") == "1";

    static void RewriteOrCompare(string path, string emitted)
    {
        if (Rewrite)
        {
            File.WriteAllText(path, emitted);
            return;
        }

        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), emitted);
    }

    static string WithTimelines(string timelines)
    {
        var arena = File.ReadAllText(Repo("packs/columns/arena.json")).TrimEnd();
        return arena.Substring(0, arena.Length - 1).TrimEnd() + ",\n  \"timelines\": " + timelines + "\n}\n";
    }

    static string Problems(string json)
    {
        var pack = PackJson.Read(json);
        var error = Assert.Throws<PackException>(() => pack.Compile().Dispose());
        return string.Join("\n", error.Problems);
    }

    [Fact]
    public void The_clock_test_cells_are_the_generated_entities_glue()
    {
        RewriteOrCompare(Repo(EntitiesPackage + "/Tests/Glue/GlueEntities.g.cs"), ColumnsGlue.EmitEntities(PackJson.Read(TestPack()), "Sv.Columns.Entities.Tests"));
    }

    [Fact]
    public void The_tl_test_glue_is_the_generated_tl_and_entities_glue()
    {
        var pack = PackJson.Read(TestPack());
        RewriteOrCompare(Repo(EntitiesPackage + "/Tests/TlGlue/Glue.g.cs"), ColumnsGlue.Emit(pack, "Sv.Columns.Entities.TlTests", true));
        RewriteOrCompare(Repo(EntitiesPackage + "/Tests/TlGlue/GlueEntities.g.cs"), ColumnsGlue.EmitEntities(pack, "Sv.Columns.Entities.TlTests"));
    }

    [Fact]
    public void Every_listed_card_and_outbox_channel_gets_a_cell_a_registration_a_kind_a_clip_and_a_track()
    {
        var pack = PackJson.Read(TestPack());
        var tl = ColumnsGlue.Emit(pack, "Game", true);
        var entities = ColumnsGlue.EmitEntities(pack, "Game");
        Assert.Contains("RegisterGenericSystemType(typeof(global::Sv.Columns.Entities.ColumnsCardSystem<global::Game.Glue.PoisonCard>))", entities);
        Assert.Contains("RegisterGenericSystemType(typeof(global::Sv.Columns.Entities.ColumnsOutboxSystem<global::Game.Glue.FireOut>))", entities);
        Assert.Contains("RegisterGenericSystemType(typeof(global::Sv.Columns.Entities.ColumnsOutboxSystem<global::Game.Glue.HealingOut>))", entities);
        Assert.Contains("public string Card => \"poison\";", entities);
        Assert.Contains("public string TargetLane => \"Aim\";", entities);
        Assert.Contains("public string TargetLane => \"\";", entities);
        Assert.Contains("Event = pack.Event(\"LowHealth\"), Timeline = findTimeline(\"poison\")", entities);
        foreach (var type in new[] { "PoisonCardFeed", "PoisonCardClip", "PoisonCardTrack", "FireFeed", "FireClip", "FireTrack", "HealingFeed", "HealingClip", "HealingTrack" })
        {
            Assert.Contains("public struct " + type, tl);
        }

        Assert.Contains("frame.Has(FrameFlags.ClipStart) && !frame.IsBackward ? frame.Clip.Level : 0", tl);
        Assert.Contains("[TlClip(\"Game/poison card\")]", tl);
        Assert.Contains("[TlClip(\"Game/Fire\")]", tl);
    }

    [Fact]
    public void With_the_unity_host_the_cell_fields_live_in_the_entities_file_so_cells_work_without_tl()
    {
        var pack = PackJson.Read(TestPack());
        var unity = ColumnsGlue.Files(pack, "Game", true);
        Assert.Equal(new[] { ColumnsGlue.FileName, ColumnsGlue.EntitiesFileName }, unity.Select(file => file.Name));
        Assert.Contains("public partial struct PoisonCard { }", unity[0].Text);
        Assert.Contains("public int Value;", unity[1].Text);
        var plain = ColumnsGlue.Files(pack, "Game", false);
        Assert.Single(plain);
        Assert.Contains("public partial struct PoisonCard { public int Value; }", plain[0].Text);
        Assert.Contains("public partial struct FireOut { public int Value; }", plain[0].Text);
    }

    [Fact]
    public void The_plain_host_gets_a_timeline_requests_class_and_the_unity_host_gets_rules_for_the_request_system()
    {
        var pack = PackJson.Read(TestPack());
        var plain = ColumnsGlue.Emit(pack, "Game", false);
        Assert.Contains("public sealed class TimelineRequests", plain);
        Assert.Contains("public void Apply(PlaybackTable players, global::Sv.Columns.ColumnWorld world)", plain);
        Assert.Contains("\"LowHealth\",", plain);
        Assert.Contains("\"poison\",", plain);
        Assert.Contains("players.Start(slot, resolved[i]);", plain);
        Assert.Contains("players.Stop(slot, resolved[i]);", plain);
        Assert.DoesNotContain("TimelineRequests", ColumnsGlue.Emit(pack, "Game", true));
        Assert.DoesNotContain("TimelineRequests", ColumnsGlue.Emit(PackJson.Read(File.ReadAllText(Repo("packs/columns/arena.json"))), "Game", false));
    }

    [Fact]
    public void A_pack_without_timelines_emits_empty_glue_and_reads_as_before()
    {
        var arena = PackJson.Read(File.ReadAllText(Repo("packs/columns/arena.json")));
        Assert.Empty(arena.Timelines.Cards);
        Assert.Empty(arena.Timelines.Outbox);
        Assert.Empty(arena.Timelines.Starts);
        var entities = ColumnsGlue.EmitEntities(arena, "Game");
        Assert.DoesNotContain("RegisterGenericSystemType", entities);
        Assert.DoesNotContain("TimelineRules", entities);
    }

    [Fact]
    public void The_timelines_section_reads_into_the_pack_and_does_not_change_the_compiled_world()
    {
        var pack = PackJson.Read(TestPack());
        Assert.Equal(new[] { "poison" }, pack.Timelines.Cards);
        Assert.Equal(new[] { "Fire", "Healing" }, pack.Timelines.Outbox.Select(outbox => outbox.Channel));
        Assert.Equal("Aim", pack.Timelines.Outbox[0].Target);
        Assert.Null(pack.Timelines.Outbox[1].Target);
        Assert.Equal("LowHealth", pack.Timelines.Starts[0].Event);
        Assert.Equal("poison", pack.Timelines.Starts[0].Timeline);
        var json = File.ReadAllText(Repo("packs/columns/arena.json"));
        using var plain = PackJson.Read(json).Compile();
        using var glued = PackJson.Read(WithTimelines("{ \"cards\": [\"poison\"], \"outbox\": [{ \"channel\": \"Fire\" }] }")).Compile();
        using var left = new ColumnWorld(plain, 1024);
        using var right = new ColumnWorld(glued, 1024);
        foreach (var template in plain.TemplateNames)
        {
            Assert.Equal(left.Spawn(plain.Template(template)), right.Spawn(glued.Template(template)));
        }

        for (var frame = 0; frame < 6; frame++)
        {
            left.Writer(0).Send(frame % 4, plain.Channel("Fire"), 7 + frame);
            right.Writer(0).Send(frame % 4, glued.Channel("Fire"), 7 + frame);
            left.Step();
            right.Step();
            Assert.Equal(left.Digest(), right.Digest());
        }
    }

    [Theory]
    [InlineData("{ \"cards\": [\"frozen\"] }", "timelines.cards[0]: unknown card 'frozen'")]
    [InlineData("{ \"cards\": [\"poison\", \"poison\"] }", "timelines.cards[1]: card 'poison' is listed twice")]
    [InlineData("{ \"outbox\": [{ \"channel\": \"Ice\" }] }", "timelines.outbox[0]: unknown channel 'Ice'")]
    [InlineData("{ \"outbox\": [{ \"channel\": \"Fire\" }, { \"channel\": \"Fire\" }] }", "timelines.outbox[1]: channel 'Fire' is listed twice")]
    [InlineData("{ \"outbox\": [{ \"channel\": \"Fire\", \"target\": \"Nowhere\" }] }", "timelines.outbox[0].target: unknown lane 'Nowhere'")]
    [InlineData("{ \"starts\": [{ \"event\": \"Stunned\", \"timeline\": \"stun\" }] }", "timelines.starts[0]: unknown event 'Stunned'")]
    [InlineData("{ \"starts\": [{ \"event\": \"Dead\", \"timeline\": \"\" }] }", "timelines.starts[0]: a start names its timeline")]
    public void Every_timelines_entry_must_name_something_the_pack_declares(string timelines, string problem)
    {
        Assert.Contains(problem, Problems(WithTimelines(timelines)));
    }

    [Theory]
    [InlineData("{ \"card\": [\"poison\"] }", "pack.timelines: unknown key 'card' (expected cards, outbox, starts)")]
    [InlineData("{ \"outbox\": [{ \"channel\": \"Fire\", \"aim\": \"Kind\" }] }", "pack.timelines.outbox[0]: unknown key 'aim' (expected channel, target)")]
    [InlineData("[]", "pack.timelines: expected an object")]
    public void The_timelines_section_rejects_unknown_shapes(string timelines, string problem)
    {
        var error = Assert.Throws<PackException>(() => PackJson.Read(WithTimelines(timelines)));
        Assert.Contains(problem, error.Problems);
    }

    [Theory]
    [InlineData("pack.json", true)]
    [InlineData("packs/columns/arena.json", true)]
    [InlineData("tests/Sv.Kernels.NorthStar3/pack", false)]
    [InlineData("values.sv.json", false)]
    [InlineData("Generated/pack.svb", false)]
    public void Sv_pack_treats_a_json_file_as_a_v4_column_pack(string path, bool columns)
    {
        Assert.Equal(columns, ColumnsGlue.IsColumnPack(path));
    }
}
