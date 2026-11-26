using Sv.Columns;

namespace Sv.Fuzz;

public sealed class FrameScript
{
    public readonly List<List<ColumnAction>> Actions = new();
    public readonly List<List<ulong>> Records = new();

    public void Capture(ColumnWorld world, int frame)
    {
        while (Actions.Count <= frame)
        {
            Actions.Add(new List<ColumnAction>());
            Records.Add(new List<ulong>());
        }

        var inputs = world.Inputs(frame);
        Actions[frame].Clear();
        Records[frame].Clear();
        for (var i = 0; i < inputs.ActionCount; i++)
        {
            Actions[frame].Add(inputs.Action(i));
        }

        for (var i = 0; i < inputs.RecordCount; i++)
        {
            Records[frame].Add(inputs.Record(i));
        }
    }

    public static void Apply(ColumnWorld world, ColumnAction action)
    {
        switch (action.Kind)
        {
            case ColumnActionKind.Spawn:
                try
                {
                    if (action.Template >= 0 && action.Template < world.Pack.TemplateNames.Count)
                    {
                        world.Spawn(action.Template);
                    }
                }
                catch (InvalidOperationException)
                {
                }

                break;
            case ColumnActionKind.Despawn:
                if (world.IsAlive(action.Slot))
                {
                    world.Despawn(action.Slot);
                }

                break;
            case ColumnActionKind.Set:
                if (world.IsAlive(action.Slot) && action.Lane >= 0 && action.Lane < world.Pack.LaneNames.Count - 2)
                {
                    world.Set(action.Slot, action.Lane, action.Value);
                }

                break;
            case ColumnActionKind.Send:
                world.Writer(0).Send(action.Slot, action.Channel, action.Amount, action.Hits);
                break;
            case ColumnActionKind.SendFrom:
                world.Writer(0).SendFrom(action.Source, action.Slot, action.Channel, action.Amount, action.Hits);
                break;
        }
    }

    public ulong[] Fresh(CompiledPack pack, int capacity, int frames, KernelChoice kernels = KernelChoice.Scalar)
    {
        using var world = new ColumnWorld(pack, capacity, 1, 1, kernels);
        var digests = new ulong[frames + 1];
        digests[0] = world.Digest();
        for (var f = 0; f < frames; f++)
        {
            foreach (var action in Actions[f])
            {
                Apply(world, action);
            }

            foreach (var record in Records[f])
            {
                world.Writer(0).SendRecord(record);
            }

            world.Step();
            digests[f + 1] = world.Digest();
        }

        return digests;
    }
}
