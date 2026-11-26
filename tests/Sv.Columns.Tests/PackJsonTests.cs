namespace Sv.Columns.Tests;

public sealed class PackJsonTests
{
    static string PackFile(string name)
    {
        var folder = AppContext.BaseDirectory;
        while (folder != null && !File.Exists(Path.Combine(folder, "packs", "columns", name)))
        {
            folder = Path.GetDirectoryName(folder);
        }

        Assert.NotNull(folder);
        return File.ReadAllText(Path.Combine(folder!, "packs", "columns", name));
    }

    [Fact]
    public void TheArenaJsonIsTheArenaPack()
    {
        var fromJson = PackJson.Read(PackFile("arena.json"));
        Assert.Equal(PackText.Describe(ArenaPack.Build()), PackText.Describe(fromJson));
        using var a = ArenaPack.Build().Compile();
        using var b = fromJson.Compile();
        using var left = new ColumnWorld(a, 1024);
        using var right = new ColumnWorld(b, 1024);
        var dice = new Dice(8);
        var slots = new List<int>();
        for (var i = 0; i < 400; i++)
        {
            var template = dice.Pick(a.TemplateNames);
            var slot = left.Spawn(a.Template(template));
            Assert.Equal(slot, right.Spawn(b.Template(template)));
            slots.Add(slot);
        }

        for (var frame = 0; frame < 20; frame++)
        {
            for (var i = 0; i < 2000; i++)
            {
                var target = dice.Pick(slots);
                var source = dice.Pick(slots);
                var channel = dice.Next(a.ChannelNames.Count);
                var amount = dice.Range(-10, 30);
                left.Writer(0).Send(target, source, channel, amount);
                right.Writer(0).Send(target, source, channel, amount);
            }

            left.Step();
            right.Step();
            Assert.Equal(left.Digest(), right.Digest());
        }
    }

    [Fact]
    public void ConditionsParseIntoTheSameAtoms()
    {
        Assert.Equal(Cond.Le("Health", 0).ToString(), CondText.Parse("Health <= 0").ToString());
        Assert.Equal(Cond.Below("Health", "MaxHealth", 30).ToString(), CondText.Parse("Health < 30% of MaxHealth").ToString());
        Assert.Equal(Cond.Below("Health", "MaxHealth", 30).ToString(), CondText.Parse("Health<30% MaxHealth").ToString());
        Assert.Equal(Cond.All(Cond.Gt("Execute", 0), Cond.Not(Cond.Event("Dead")), Cond.Compare(CondOp.Ne, "Shield", "Armor")).ToString(), CondText.Parse("Execute > 0 and not event Dead and Shield != Armor").ToString());
        Assert.Equal(Cond.All(Cond.Bit("ImmuneMask", 3), Cond.Entered("Dead"), Cond.Ge("Rage", -5)).ToString(), CondText.Parse("bit ImmuneMask 3 and entered Dead and Rage >= -5").ToString());
    }

    [Fact]
    public void MistakesAreReportedWithTheirPlace()
    {
        var error = Assert.Throws<PackException>(() => PackJson.Read("""
            {
              "lanes": [ { "name": "Health", "default": 100, "colour": 3 } ],
              "channels": ["Damage"],
              "templates": [ { "name": "unit", "ops": [ { "channel": "Damage", "amount": 1, "when": "Health ~ 3" } ] } ],
              "rules": [ { "explode": {} }, { "flow": { "lane": "Health", "subtract": ["Damage"] }, "extra": 1 } ]
            }
            """));
        var message = string.Join("\n", error.Problems);
        Assert.Contains("pack.lanes[0]: unknown key 'colour'", message);
        Assert.Contains("pack.templates[0].ops[0].when", message);
        Assert.Contains("unknown rule kind 'explode'", message);
        Assert.Contains("pack.rules[1]: a rule is an object with exactly one key", message);
    }

    [Fact]
    public void BrokenJsonNamesTheLine()
    {
        var error = Assert.Throws<PackException>(() => PackJson.Read("{\n  \"lanes\": [\n    { \"name\": \"Health\", }\n  ]\n}"));
        Assert.Contains("json line 3", error.Message);
    }

    [Fact]
    public void FractionsAreRefused()
    {
        var error = Assert.Throws<PackException>(() => PackJson.Read("{ \"lanes\": [ { \"name\": \"Health\", \"default\": 1.5 } ] }"));
        Assert.Contains("whole numbers", error.Message);
    }
}
