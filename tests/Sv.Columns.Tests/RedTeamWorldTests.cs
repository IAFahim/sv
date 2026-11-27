namespace Sv.Columns.Tests;

public sealed unsafe class RedTeamWorldTests
{
    static CompiledPack Arena() => ArenaPack.Build().Compile();

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void A_resolve_part_or_reduce_writer_out_of_range_is_refused_and_changes_nothing(int bad)
    {
        using var pack = Arena();
        using var world = new ColumnWorld(pack, 128, writerCount: 2, resolveParts: 2);
        var hero = world.Spawn(pack.Template("dummy"));
        world.Writer(0).Send(hero, pack.Channel("Physical"), 30);
        world.BeginFrame();
        world.Reduce(0);
        world.Reduce(1);
        var before = world.Digest();
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Resolve(bad));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Reduce(bad));
        Assert.Equal(before, world.Digest());
        world.Resolve(0);
        world.Resolve(1);
        world.EndFrame();
        Assert.Equal(70, world.Get(hero, pack.Lane("Health")));
    }

    [Fact]
    public void Sends_inside_a_stepped_frame_are_refused_and_the_log_holds_exactly_what_was_applied()
    {
        using var pack = Arena();
        using var world = new ColumnWorld(pack, 64, history: 8);
        var hero = world.Spawn(pack.Template("dummy"));
        world.Step();
        world.Writer(0).Send(hero, pack.Channel("Physical"), 10);
        world.BeginFrame();
        Assert.Throws<InvalidOperationException>(() => world.Writer(0).Send(hero, pack.Channel("Physical"), 1));
        Assert.Throws<InvalidOperationException>(() => world.Writer(0).SendFrom(hero, hero, pack.Channel("Physical"), 1));
        Assert.Throws<InvalidOperationException>(() => world.Writer(0).SendRecord(0));
        Assert.Throws<InvalidOperationException>(() => world.Writer(0).Send(-1, 0, 1));
        world.Reduce(0);
        Assert.Throws<InvalidOperationException>(() => world.Writer(0).Send(hero, pack.Channel("Physical"), 1));
        world.Resolve(0);
        world.EndFrame();
        Assert.Equal(1, world.Inputs(1).RecordCount);
        Assert.Equal(90, world.Get(hero, pack.Lane("Health")));
        world.Writer(0).Send(hero, pack.Channel("Physical"), 5);
        world.Step();
        Assert.Equal(85, world.Get(hero, pack.Lane("Health")));
    }

    [Fact]
    public void A_burst_of_sends_inside_a_frame_cannot_write_past_the_record_log()
    {
        using var pack = Arena();
        using var world = new ColumnWorld(pack, 64, history: 8);
        var hero = world.Spawn(pack.Template("dummy"));
        world.Writer(0).Send(hero, pack.Channel("Physical"), 1);
        world.Step();
        world.BeginFrame();
        Assert.Throws<InvalidOperationException>(() =>
        {
            for (var i = 0; i < 200000; i++)
            {
                world.Writer(0).Send(hero, pack.Channel("Physical"), 1);
            }
        });
        world.Reduce(0);
        world.Resolve(0);
        world.EndFrame();
        Assert.Equal(0, world.Inputs(1).RecordCount);
    }

    [Fact]
    public void Sends_inside_an_open_fused_frame_are_refused_instead_of_lost()
    {
        using var pack = Arena();
        using var world = new ColumnWorld(pack, 64, history: 8);
        var hero = world.Spawn(pack.Template("dummy"));
        world.Writer(0).Send(hero, pack.Channel("Physical"), 10);
        var frame = world.OpenFrame();
        Assert.Throws<InvalidOperationException>(() => world.Writer(0).Send(hero, pack.Channel("Physical"), 20));
        for (var w = 0; w < frame.Writers; w++)
        {
            frame.BucketWriter(w);
        }

        var buckets = stackalloc RecordBucket[frame.Writers];
        for (var w = 0; w < frame.Writers; w++)
        {
            buckets[w] = frame.WriterBucket(w);
        }

        for (var p = 0; p < frame.Parts; p++)
        {
            frame.Resolve(p, buckets, frame.Writers);
        }

        world.CloseFrame();
        Assert.Equal(90, world.Get(hero, pack.Lane("Health")));
        Assert.Equal(1, world.Inputs(0).RecordCount);
        world.Writer(0).Send(hero, pack.Channel("Physical"), 20);
        world.StepFused();
        Assert.Equal(70, world.Get(hero, pack.Lane("Health")));
    }

    [Fact]
    public void History_cannot_be_edited_while_a_frame_runs()
    {
        using var pack = Arena();
        using var world = new ColumnWorld(pack, 64, history: 8);
        var hero = world.Spawn(pack.Template("dummy"));
        world.Step();
        for (var i = 1; i <= 3; i++)
        {
            world.Writer(0).Send(hero, pack.Channel("Physical"), i * 10);
        }

        world.BeginFrame();
        Assert.Throws<InvalidOperationException>(() => world.Inputs(world.Frame - 1).RemoveRecord(0));
        Assert.Throws<InvalidOperationException>(() => world.Inputs(world.Frame - 1).AddRecord(0));
        Assert.Throws<InvalidOperationException>(() => world.Inputs(0).AddAction(ColumnAction.Spawn(0)));
        world.Reduce(0);
        world.Resolve(0);
        world.EndFrame();
        Assert.Equal(3, world.Inputs(1).RecordCount);
        Assert.Equal(0, world.Inputs(0).RecordCount);
    }

    [Fact]
    public void A_refused_history_edit_does_not_mark_the_frame_edited()
    {
        using var pack = Arena();
        using var world = new ColumnWorld(pack, 64, history: 3);
        var hero = world.Spawn(pack.Template("dummy"));
        world.Step();
        world.Step();
        var inputs = world.Inputs(0);
        Assert.Throws<ArgumentOutOfRangeException>(() => inputs.ReplaceRecord(5, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => inputs.RemoveRecord(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => inputs.RemoveAction(7));
        Assert.Throws<ArgumentOutOfRangeException>(() => inputs.ReplaceAction(7, ColumnAction.Spawn(0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => inputs.InsertAction(-1, ColumnAction.Spawn(0)));
        for (var f = 0; f < 5; f++)
        {
            world.Step();
        }

        Assert.True(world.OldestFrame > 0);
        var before = world.Digest();
        world.Resimulate();
        Assert.Equal(before, world.Digest());
        world.Inputs(world.Frame - 1).AddAction(ColumnAction.Send(hero, pack.Channel("Physical"), 30));
        world.Resimulate();
        Assert.Equal(70, world.Get(hero, pack.Lane("Health")));
    }

    [Fact]
    public void After_a_rewind_frames_already_overwritten_stay_outside_the_window()
    {
        using var pack = Arena();
        using var world = new ColumnWorld(pack, 64, history: 2);
        var empty = world.Digest();
        for (var f = 0; f < 3; f++)
        {
            world.Spawn(0);
            world.Step();
        }

        Assert.Equal(1, world.OldestFrame);
        world.Rewind(1);
        Assert.Equal(1, world.Frame);
        Assert.Equal(1, world.OldestFrame);
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Rewind(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Inputs(0));
        Assert.Equal(1, world.Count);
        Assert.NotEqual(empty, world.Digest());
    }
}
