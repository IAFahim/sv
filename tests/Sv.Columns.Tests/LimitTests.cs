namespace Sv.Columns.Tests;

public sealed class LimitTests
{
    static ColumnPack Small()
    {
        var pack = new ColumnPack();
        pack.Lanes.Add(new LaneSpec("Health", 100));
        pack.Channels.Add("Damage");
        pack.Templates.Add(new TemplateSpec("unit"));
        pack.Rules.Add(new FlowRule("Health", System.Array.Empty<string>(), new[] { "Damage" }));
        return pack;
    }

    static string Fails(ColumnPack pack)
    {
        var error = Assert.Throws<PackException>(() => pack.Compile().Dispose());
        Assert.NotEmpty(error.Problems);
        return error.Message;
    }

    [Fact]
    public void TheSmallPackCompiles()
    {
        using var compiled = Small().Compile();
        Assert.Equal(0, compiled.Lane("Health"));
    }

    [Fact]
    public void TooManyLanesIsRejected()
    {
        var pack = Small();
        for (var i = 0; i < PackLimits.Lanes; i++)
        {
            pack.Lanes.Add(new LaneSpec("L" + i));
        }

        Fails(pack);
    }

    [Fact]
    public void TooManyChannelsIsRejected()
    {
        var pack = Small();
        for (var i = 0; i < PackLimits.Channels; i++)
        {
            pack.Channels.Add("C" + i);
        }

        Fails(pack);
    }

    [Fact]
    public void UnknownNamesAreRejected()
    {
        var pack = Small();
        pack.Rules.Add(new FlowRule("Mana", new[] { "Damage" }, System.Array.Empty<string>()));
        pack.Rules.Add(new EventRule("Dead", Cond.Le("Hp", 0)));
        var message = Fails(pack);
        Assert.Contains("Mana", message);
        Assert.Contains("Hp", message);
    }

    [Fact]
    public void ScaleAboveTheProductLimitIsRejected()
    {
        var pack = Small();
        pack.Rules.Add(new ScaleRule(new[] { "Damage" }, PackLimits.ScaleProduct + 1, null, null));
        Fails(pack);
    }

    [Fact]
    public void ConvertDenominatorMustBeAPowerOfTwo()
    {
        var pack = Small();
        pack.Channels.Add("Heal");
        pack.Rules.Add(new ConvertRule("Damage", "Heal", 1, 3, null));
        Fails(pack);
    }

    [Fact]
    public void OpAmountsAboveSixteenBitsAreRejected()
    {
        var pack = Small();
        pack.Templates[0].Ops.Add(new OpSpec("Damage", PackLimits.OpAmount + 1));
        Fails(pack);
    }

    [Fact]
    public void DuplicateNamesAreRejected()
    {
        var pack = Small();
        pack.Lanes.Add(new LaneSpec("Health"));
        Fails(pack);
    }

    [Fact]
    public void InternalLaneNamesAreReserved()
    {
        var pack = Small();
        pack.Lanes.Add(new LaneSpec(PackLimits.EventsLane));
        Fails(pack);
    }
}
