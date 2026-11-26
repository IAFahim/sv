namespace Sv.Columns.Tests;

public sealed class Lockstep : IDisposable
{
    static readonly KernelChoice[] Choices = { KernelChoice.Scalar, KernelChoice.Vector, KernelChoice.Best };
    readonly ColumnWorld[] worlds;
    readonly ReferenceWorld reference;
    readonly SortedDictionary<int, string> alive = new SortedDictionary<int, string>();

    public Lockstep(CompiledPack pack, int capacity = 4096, int writers = 1, int parts = 1)
    {
        Pack = pack;
        worlds = Array.ConvertAll(Choices, choice => new ColumnWorld(pack, capacity, writers, parts, choice));
        reference = new ReferenceWorld(pack.Source);
    }

    public CompiledPack Pack { get; }

    public ColumnWorld World => worlds[^1];

    public int Frame => World.Frame;

    public int Spawn(string template)
    {
        var slot = worlds[0].Spawn(Pack.Template(template));
        for (var k = 1; k < worlds.Length; k++)
        {
            Assert.Equal(slot, worlds[k].Spawn(Pack.Template(template)));
        }

        reference.Spawn(slot, template);
        alive[slot] = template;
        return slot;
    }

    public int TrySpawn(string template)
    {
        var slots = new int[worlds.Length];
        for (var k = 0; k < worlds.Length; k++)
        {
            try
            {
                slots[k] = worlds[k].Spawn(Pack.Template(template));
            }
            catch (InvalidOperationException)
            {
                slots[k] = -1;
            }
        }

        Assert.All(slots, slot => Assert.Equal(slots[0], slot));
        if (slots[0] >= 0)
        {
            reference.Spawn(slots[0], template);
            alive[slots[0]] = template;
        }

        return slots[0];
    }

    public void Despawn(int slot)
    {
        foreach (var world in worlds)
        {
            world.Despawn(slot);
        }

        reference.Despawn(slot);
        alive.Remove(slot);
    }

    public void Set(int slot, string lane, int value)
    {
        foreach (var world in worlds)
        {
            world.Set(slot, Pack.Lane(lane), value);
        }

        reference.Set(slot, lane, value);
    }

    public void Send(int target, string channel, long amount, int writer = 0, int hits = 1)
    {
        foreach (var world in worlds)
        {
            world.Writer(writer).Send(target, Pack.Channel(channel), amount, hits);
        }

        reference.Send(target, channel, amount, hits);
    }

    public void SendFrom(int source, int target, string channel, long amount, int writer = 0, int hits = 1)
    {
        foreach (var world in worlds)
        {
            world.Writer(writer).SendFrom(source, target, Pack.Channel(channel), amount, hits);
        }

        reference.SendFrom(source, target, channel, amount, hits);
    }

    public void SendStray(int target, int channel, long amount, int writer = 0)
    {
        foreach (var world in worlds)
        {
            world.Writer(writer).Send(target, channel, amount);
        }
    }

    public long Dropped
    {
        get
        {
            var dropped = worlds[0].Dropped;
            foreach (var world in worlds)
            {
                Assert.Equal(dropped, world.Dropped);
            }

            return dropped;
        }
    }

    public void Step()
    {
        foreach (var world in worlds)
        {
            world.Step();
        }

        reference.Step();
        var expected = reference.Digest();
        foreach (var world in worlds)
        {
            if (world.Digest() != expected)
            {
                Assert.Fail("frame " + world.Frame + " " + world.KernelPath + ": " + FirstDifference(world));
            }
        }
    }

    string FirstDifference(ColumnWorld world)
    {
        for (var slot = 0; slot < world.Capacity; slot++)
        {
            var expected = alive.TryGetValue(slot, out var template);
            if (expected != world.IsAlive(slot) || (expected && Pack.TemplateNames[world.TemplateOf(slot)] != template))
            {
                return "slot " + slot + " should be " + (expected ? "alive as " + template : "dead") + " but is " + (world.IsAlive(slot) ? "alive as " + Pack.TemplateNames[world.TemplateOf(slot)] : "dead");
            }
        }

        for (var slot = 0; slot < world.Capacity; slot++)
        {
            if (!world.IsAlive(slot))
            {
                continue;
            }

            foreach (var lane in Pack.LaneNames)
            {
                var want = reference.Get(slot, lane);
                var got = world.Get(slot, Pack.Lane(lane));
                if (want != got)
                {
                    return "slot " + slot + " template " + Pack.TemplateNames[world.TemplateOf(slot)] + " lane " + lane + ": reference " + want + ", kernel " + got;
                }
            }
        }

        return "digests differ but no lane does";
    }

    public int Get(int slot, string lane)
    {
        var value = reference.Get(slot, lane);
        foreach (var world in worlds)
        {
            Assert.Equal(value, world.Get(slot, Pack.Lane(lane)));
        }

        return value;
    }

    public void Dispose()
    {
        foreach (var world in worlds)
        {
            world.Dispose();
        }
    }
}
