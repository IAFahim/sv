namespace Sv.Columns.Tests;

public sealed unsafe class PackCheckTests
{
    static ColumnPack Base()
    {
        var pack = new ColumnPack();
        pack.Lanes.Add(new LaneSpec("A", 5));
        pack.Lanes.Add(new LaneSpec("H", 100));
        pack.Channels.Add("D");
        pack.Templates.Add(new TemplateSpec("u"));
        pack.Rules.Add(new FlowRule("H", System.Array.Empty<string>(), new[] { "D" }));
        return pack;
    }

    static PackException Refused(ColumnPack pack) => Assert.Throws<PackException>(() => pack.Compile());

    [Fact]
    public void RepeatedAtomsAreDeduplicatedSoEqualConditionsShareOneEntry()
    {
        var pack = Base();
        pack.Rules.Add(new DenyRule(null, null, CondText.Parse("A > 0 and A > 0")));
        pack.Rules.Add(new DenyRule(null, null, CondText.Parse("A > 0")));
        using var compiled = pack.Compile();
        Assert.Equal(1, compiled.Tables->CondCount);
        Assert.Equal(1, compiled.Tables->Conds[0].Count);
    }

    [Fact]
    public void ANameProblemDoesNotHideTheRuleProblemsAfterIt()
    {
        var pack = Base();
        pack.Lanes.Add(new LaneSpec("A"));
        pack.Rules.Add(new ConvertRule("D", "D", 1, 3, null));
        var problems = Refused(pack).Problems;
        Assert.Contains(problems, p => p.Contains("declared twice"));
        Assert.Contains(problems, p => p.Contains("denominator 3"));
        Assert.Contains(problems, p => p.Contains("the same channel"));
    }

    [Fact]
    public void NullListsAndNamesArePackProblemsNotCrashes()
    {
        var flow = Base();
        flow.Rules.Add(new FlowRule("H", null!, new[] { "D" }));
        Assert.Contains(Refused(flow).Problems, p => p.Contains("flow add is null"));

        var absorb = Base();
        absorb.Rules.Add(new AbsorbRule("A", null!));
        Assert.Contains(Refused(absorb).Problems, p => p.Contains("absorb needs its channels"));

        var scale = Base();
        scale.Rules.Add(new ScaleRule(null!, 2, null, null));
        Assert.Contains(Refused(scale).Problems, p => p.Contains("scale needs its channels"));

        var eventName = Base();
        eventName.Rules.Add(new DenyRule(null, null, Cond.Event(null!)));
        Assert.Contains(Refused(eventName).Problems, p => p.Contains("unknown event"));

        var nullRule = Base();
        nullRule.Rules.Add(null!);
        Assert.Contains(Refused(nullRule).Problems, p => p == "rules[1] is null");

        Assert.Throws<System.ArgumentNullException>(() => Bound.Of((string)null!));
    }

    [Fact]
    public void ACardOpProblemIsReportedOnceAndNamesTheCard()
    {
        var pack = Base();
        pack.Lanes.Add(new LaneSpec("CardLane"));
        pack.Cards.Add(new CardSpec("burn", "CardLane", new OpSpec("Bogus", 1)));
        pack.Templates.Add(new TemplateSpec("v"));
        var problems = Refused(pack).Problems;
        Assert.Single(problems);
        Assert.StartsWith("card 'burn' op on 'Bogus'", problems[0]);
    }

    [Fact]
    public void CardLaneKeepsItsAnswerWhenTheSourcePackChangesAfterCompile()
    {
        var pack = Base();
        pack.Lanes.Add(new LaneSpec("OneLane"));
        pack.Lanes.Add(new LaneSpec("TwoLane"));
        pack.Cards.Add(new CardSpec("one", "OneLane"));
        using var compiled = pack.Compile();
        var lane = compiled.CardLane("one");
        pack.Cards.Insert(0, new CardSpec("two", "TwoLane"));
        Assert.Equal(lane, compiled.CardLane("one"));
        Assert.Equal(compiled.Lane("OneLane"), compiled.CardLane("one"));
    }

    [Theory]
    [InlineData("not > 3", "not")]
    [InlineData("event >= 1", "event")]
    [InlineData("entered != 0", "entered")]
    [InlineData("bit < 2", "bit")]
    public void LanesNamedLikeKeywordsCanBeWrittenInConditionText(string text, string lane)
    {
        var cond = CondText.Parse(text);
        Assert.Equal(lane, cond.Lane);
        Assert.False(cond.Negated);
    }

    [Fact]
    public void KeywordsStillWorkWhereTheyAreKeywords()
    {
        Assert.True(CondText.Parse("not A > 3").Negated);
        Assert.Equal(CondOp.Event, CondText.Parse("event Dead").Op);
        Assert.Equal(CondOp.Bit, CondText.Parse("bit Mask 3").Op);
        var below = CondText.Parse("H < 30% of M");
        Assert.Equal("M", below.Other);
        Assert.Equal("of", CondText.Parse("H < 30% of").Other);
        Assert.Equal("of", CondText.Parse("H < 30% of of").Other);
        Assert.Equal(5, CondText.Parse("A > +5").Value);
    }

    [Fact]
    public void AConditionOnLanesNamedLikeKeywordsCompilesAndRuns()
    {
        var pack = new ColumnPack();
        pack.Lanes.Add(new LaneSpec("not", 4));
        pack.Lanes.Add(new LaneSpec("bit", 0));
        pack.Channels.Add("D");
        pack.Templates.Add(new TemplateSpec("u"));
        pack.Rules.Add(new DenyRule(null, null, CondText.Parse("not > 3 and bit == 0")));
        pack.Rules.Add(new FlowRule("bit", new[] { "D" }, System.Array.Empty<string>()));
        using var compiled = pack.Compile();
        using var twin = new Lockstep(compiled, 64);
        var slot = twin.Spawn("u");
        twin.Send(slot, "D", 7);
        twin.Step();
        Assert.Equal(0, twin.Get(slot, "bit"));
    }
}
