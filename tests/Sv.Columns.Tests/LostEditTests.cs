namespace Sv.Columns.Tests;

public sealed class LostEditTests
{
    static CompiledPack Arena() => ArenaPack.Build().Compile();

    static void StepWith(ColumnWorld world, bool fused)
    {
        if (fused)
        {
            world.StepFused();
        }
        else
        {
            world.Step();
        }
    }

    static void ResimulateWith(ColumnWorld world, bool fused)
    {
        if (fused)
        {
            world.ResimulateFused();
        }
        else
        {
            world.Resimulate();
        }
    }

    [Fact]
    public void An_edit_that_left_the_window_is_refused_until_the_host_discards_it()
    {
        using var pack = Arena();
        using var world = new ColumnWorld(pack, 64, history: 3);
        var hero = world.Spawn(pack.Template("dummy"));
        world.Step();
        world.Inputs(0).AddAction(ColumnAction.Send(hero, pack.Channel("Physical"), 30));
        Assert.Equal(0, world.EditedFrame);
        for (var f = 0; f < 5; f++)
        {
            world.Step();
        }

        Assert.True(world.OldestFrame > 0);
        var before = world.Digest();
        var refused = Assert.Throws<InvalidOperationException>(() => world.Resimulate());
        Assert.Contains("DiscardLostEdits", refused.Message);
        Assert.Throws<InvalidOperationException>(() => world.ResimulateFused());
        Assert.Equal(before, world.Digest());
        Assert.Equal(0, world.EditedFrame);
        Assert.Equal(0, world.DiscardLostEdits());
        Assert.Equal(world.OldestFrame, world.EditedFrame);
        world.Resimulate();
        Assert.Equal(-1, world.EditedFrame);
        Assert.Equal(before, world.Digest());
        Assert.Equal(100, world.Get(hero, pack.Lane("Health")));
        Assert.Equal(-1, world.DiscardLostEdits());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_discard_gives_up_only_the_lost_edit_and_keeps_the_edits_still_in_the_window(bool fused)
    {
        using var pack = Arena();
        using var lost = new ColumnWorld(pack, 128, history: 3);
        using var kept = new ColumnWorld(pack, 128, history: 3);
        var physical = pack.Channel("Physical");
        var heroes = new int[2];
        foreach (var world in new[] { lost, kept })
        {
            heroes[0] = world.Spawn(pack.Template("dummy"));
            heroes[1] = world.Spawn(pack.Template("dummy"));
        }

        for (var f = 0; f < 6; f++)
        {
            foreach (var world in new[] { lost, kept })
            {
                world.Writer(0).Send(heroes[f & 1], physical, 3 + f);
                StepWith(world, fused);
            }

            if (f == 1)
            {
                lost.Inputs(1).AddRecord(Record.Pack(heroes[1], physical, 0, 0, 40));
            }
        }

        Assert.Equal(1, lost.EditedFrame);
        Assert.True(lost.OldestFrame > 1);
        foreach (var world in new[] { lost, kept })
        {
            world.Inputs(5).AddAction(ColumnAction.Send(heroes[0], physical, 7));
        }

        Assert.Throws<InvalidOperationException>(() => ResimulateWith(lost, fused));
        Assert.Equal(1, lost.DiscardLostEdits());
        ResimulateWith(lost, fused);
        ResimulateWith(kept, fused);
        Assert.Equal(-1, lost.EditedFrame);
        Assert.Equal(kept.Digest(), lost.Digest());
        Assert.Equal(kept.Get(heroes[0], pack.Lane("Health")), lost.Get(heroes[0], pack.Lane("Health")));
        Assert.Equal(kept.Get(heroes[1], pack.Lane("Health")), lost.Get(heroes[1], pack.Lane("Health")));
    }

    [Fact]
    public void A_discard_with_nothing_lost_changes_nothing()
    {
        using var pack = Arena();
        using var world = new ColumnWorld(pack, 64, history: 8);
        var hero = world.Spawn(pack.Template("dummy"));
        world.Step();
        world.Step();
        Assert.Equal(-1, world.EditedFrame);
        Assert.Equal(-1, world.DiscardLostEdits());
        world.Inputs(1).AddAction(ColumnAction.Send(hero, pack.Channel("Physical"), 30));
        Assert.Equal(-1, world.DiscardLostEdits());
        Assert.Equal(1, world.EditedFrame);
        world.Resimulate();
        Assert.Equal(70, world.Get(hero, pack.Lane("Health")));
    }

    [Fact]
    public void A_discard_after_a_rewind_to_the_oldest_frame_leaves_nothing_to_replay()
    {
        using var pack = Arena();
        using var world = new ColumnWorld(pack, 64, history: 2);
        var hero = world.Spawn(pack.Template("dummy"));
        world.Step();
        world.Inputs(0).AddAction(ColumnAction.Send(hero, pack.Channel("Physical"), 30));
        for (var f = 0; f < 4; f++)
        {
            world.Step();
        }

        world.Rewind(world.OldestFrame);
        var at = world.Frame;
        var before = world.Digest();
        Assert.Equal(0, world.DiscardLostEdits());
        world.Resimulate();
        Assert.Equal(at, world.Frame);
        Assert.Equal(-1, world.EditedFrame);
        Assert.Equal(before, world.Digest());
    }

    [Fact]
    public void A_world_without_history_reports_no_edit_and_refuses_a_discard()
    {
        using var pack = Arena();
        using var world = new ColumnWorld(pack, 64);
        Assert.Equal(-1, world.EditedFrame);
        Assert.Throws<InvalidOperationException>(() => world.DiscardLostEdits());
    }
}
