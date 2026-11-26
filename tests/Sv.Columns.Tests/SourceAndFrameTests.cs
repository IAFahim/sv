namespace Sv.Columns.Tests;

public sealed class SourceAndFrameTests
{
    static ColumnPack WetPack()
    {
        var pack = new ColumnPack();
        pack.Lanes.Add(new LaneSpec("Health", 100));
        pack.Lanes.Add(new LaneSpec("Wet", 1));
        pack.Lanes.Add(new LaneSpec("Hot"));
        pack.Channels.Add("Fire");
        pack.Templates.Add(new TemplateSpec("unit"));
        pack.Rules.Add(new ScaleRule(new[] { "Fire" }, 2, Cond.Gt("Wet", 0), null));
        pack.Rules.Add(new ScaleRule(new[] { "Fire" }, 3, null, Cond.Gt("Hot", 0)));
        pack.Rules.Add(new FlowRule("Health", System.Array.Empty<string>(), new[] { "Fire" }));
        pack.Rules.Add(new EventRule("Low", Cond.Lt("Health", 50)));
        return pack;
    }

    [Fact]
    public void ASourceThatIsNotAliveSendsAsNoSource()
    {
        using var compiled = WetPack().Compile();
        using var twin = new Lockstep(compiled, 64);
        var plain = twin.Spawn("unit");
        var fromLive = twin.Spawn("unit");
        var gone = twin.Spawn("unit");
        var fromGone = twin.Spawn("unit");
        var fromOutside = twin.Spawn("unit");
        twin.Despawn(gone);
        twin.Send(plain, "Fire", 5);
        twin.SendFrom(fromLive, fromLive, "Fire", 5);
        twin.SendFrom(gone, fromGone, "Fire", 5);
        twin.SendFrom(-1, fromOutside, "Fire", 5);
        twin.Step();
        Assert.Equal(90, twin.Get(plain, "Health"));
        Assert.Equal(90, twin.Get(fromLive, "Health"));
        Assert.Equal(90, twin.Get(fromGone, "Health"));
        Assert.Equal(90, twin.Get(fromOutside, "Health"));
    }

    [Fact]
    public void ALiveSourceStillCarriesItsOwnSourceCondition()
    {
        using var compiled = WetPack().Compile();
        using var twin = new Lockstep(compiled, 64);
        var hot = twin.Spawn("unit");
        var target = twin.Spawn("unit");
        twin.Set(hot, "Hot", 1);
        twin.SendFrom(hot, target, "Fire", 5);
        twin.Step();
        Assert.Equal(70, twin.Get(target, "Health"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(-1)]
    public void AnEventIndexOutsideThePackIsRefusedInsteadOfAliasingAnother(int eventIndex)
    {
        using var pack = WetPack().Compile();
        using var world = new ColumnWorld(pack, 64);
        var slot = world.Spawn(0);
        world.Set(slot, pack.Lane("Health"), 10);
        world.Step();
        Assert.True(world.Event(slot, 0));
        Assert.True(world.Entered(slot, 0));
        Assert.Equal(1ul, world.ChunkEntered(0, 0));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => world.Event(slot, eventIndex));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => world.Entered(slot, eventIndex));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => world.ChunkEntered(0, eventIndex));
    }

    [Fact]
    public void FramePhasesRunOnlyInOrder()
    {
        using var pack = WetPack().Compile();
        using var world = new ColumnWorld(pack, 64, history: 4);
        var slot = world.Spawn(0);
        Assert.Throws<System.InvalidOperationException>(() => world.Reduce(0));
        Assert.Throws<System.InvalidOperationException>(() => world.Resolve(0));
        Assert.Throws<System.InvalidOperationException>(() => world.EndFrame());
        world.BeginFrame();
        Assert.Throws<System.InvalidOperationException>(() => world.BeginFrame());
        world.Reduce(0);
        world.Resolve(0);
        world.EndFrame();
        Assert.Equal(1, world.Frame);
        Assert.Equal(1, world.Inputs(0).ActionCount);
        Assert.Equal(slot, world.Inputs(0).Action(0).Slot);
    }

    [Fact]
    public void AStrayEndFrameCannotWipeTheActionsOfTheCurrentFrame()
    {
        using var pack = WetPack().Compile();
        using var world = new ColumnWorld(pack, 64, history: 4);
        world.Step();
        var slot = world.Spawn(0);
        Assert.Throws<System.InvalidOperationException>(() => world.EndFrame());
        world.Step();
        Assert.Equal(1, world.Inputs(1).ActionCount);
        world.Rewind(1);
        Assert.False(world.IsAlive(slot));
    }
}
