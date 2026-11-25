namespace Sv.Columns.Tests;

public sealed class Twin : IDisposable
{
    readonly ColumnWorld scalar;
    readonly ColumnWorld best;
    readonly ReferenceWorld reference;

    public Twin(CompiledPack pack, int capacity = 4096, int writers = 1, int parts = 1)
    {
        Pack = pack;
        scalar = new ColumnWorld(pack, capacity, writers, parts, KernelChoice.Scalar);
        best = new ColumnWorld(pack, capacity, writers, parts);
        reference = new ReferenceWorld(pack.Source);
    }

    public CompiledPack Pack { get; }

    public ColumnWorld World => best;

    public int Spawn(string template)
    {
        var slot = scalar.Spawn(Pack.Template(template));
        Assert.Equal(slot, best.Spawn(Pack.Template(template)));
        reference.Spawn(slot, template);
        return slot;
    }

    public void Despawn(int slot)
    {
        scalar.Despawn(slot);
        best.Despawn(slot);
        reference.Despawn(slot);
    }

    public void Set(int slot, string lane, int value)
    {
        scalar.Set(slot, Pack.Lane(lane), value);
        best.Set(slot, Pack.Lane(lane), value);
        reference.Set(slot, lane, value);
    }

    public void Send(int target, string channel, long amount, int writer = 0, int hits = 1)
    {
        scalar.Writer(writer).Send(target, Pack.Channel(channel), amount, hits);
        best.Writer(writer).Send(target, Pack.Channel(channel), amount, hits);
        reference.Send(target, channel, amount, hits);
    }

    public void Send(int target, int source, string channel, long amount, int writer = 0, int hits = 1)
    {
        scalar.Writer(writer).Send(target, source, Pack.Channel(channel), amount, hits);
        best.Writer(writer).Send(target, source, Pack.Channel(channel), amount, hits);
        reference.Send(target, source, channel, amount, hits);
    }

    public void Step()
    {
        scalar.Step();
        best.Step();
        reference.Step();
        var expected = reference.Digest();
        if (expected != scalar.Digest() || expected != best.Digest())
        {
            Assert.Fail("frame " + best.Frame + ": " + FirstDifference());
        }
    }

    string FirstDifference()
    {
        for (var slot = 0; slot < best.Capacity; slot++)
        {
            if (!best.IsAlive(slot))
            {
                continue;
            }

            foreach (var lane in Pack.LaneNames)
            {
                var want = reference.Get(slot, lane);
                var gotScalar = scalar.Get(slot, Pack.Lane(lane));
                var gotBest = best.Get(slot, Pack.Lane(lane));
                if (want != gotScalar || want != gotBest)
                {
                    return "slot " + slot + " template " + Pack.TemplateNames[best.TemplateOf(slot)] + " lane " + lane + ": reference " + want + ", scalar " + gotScalar + ", best " + gotBest;
                }
            }
        }

        return "digests differ but no lane does";
    }

    public int Get(int slot, string lane)
    {
        var value = reference.Get(slot, lane);
        Assert.Equal(value, best.Get(slot, Pack.Lane(lane)));
        return value;
    }

    public bool Event(int slot, string name)
    {
        var value = reference.Event(slot, name);
        Assert.Equal(value, best.Event(slot, Pack.Event(name)));
        return value;
    }

    public bool Entered(int slot, string name)
    {
        var value = reference.Entered(slot, name);
        Assert.Equal(value, best.Entered(slot, Pack.Event(name)));
        return value;
    }

    public void Dispose()
    {
        scalar.Dispose();
        best.Dispose();
    }
}
