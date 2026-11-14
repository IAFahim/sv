using Sv.Pack;
using Ref = Sv.Rules.Reference;

namespace Sv.Host.Tests;

public sealed class Rig<TKernels> : IDisposable
    where TKernels : struct, IPackKernels
{
    readonly HostPack pack;
    readonly Ref.Interpreter interpreter;
    readonly SortedDictionary<int, int> templateBySlot = new();
    Ref.RefState state;
    Ref.FrameInputs pending = new();

    public Rig(PackPlan plan, HostOptions options, bool withReference = true)
    {
        Plan = plan;
        pack = HostPack.From(plan);
        WithReference = withReference;
        interpreter = new Ref.Interpreter(plan, new Ref.InterpreterOptions { EventCapacity = options.EventCapacity });
        state = interpreter.NewWorld(options.EntityCapacity);
        Host = new FrameLoop<TKernels>(pack, options);
    }

    public PackPlan Plan { get; }

    public bool WithReference { get; }

    public FrameLoop<TKernels> Host { get; }

    public List<Ref.OutwardEvent> ReferenceEvents { get; private set; } = new();

    public long Frame => Host.Frame;

    public int Spawn(string template)
    {
        var index = Packs.Template(Plan, template);
        var slot = Host.Spawn(index);
        if (WithReference)
        {
            pending.Spawn(slot, Plan.Schema.Templates[index].Id);
        }

        templateBySlot[slot] = index;
        return slot;
    }

    public void Send(int target, int source, string channel, int amount, int hits = 1)
    {
        var channelId = Packs.Id(Plan, ValueClass.Channel, channel);
        var kindLane = Plan.Schema.KindIntrinsic;
        var raw = source == EffectKey.NoSource || kindLane < 0 ? 0 : Host.Read(source, kindLane);
        var kind = raw < 0 ? 0 : raw > 255 ? 255 : raw;
        Host.Send(target, source, kind, channelId, amount, hits);
        if (WithReference)
        {
            pending.Send(target, source, kind, channelId, amount, hits);
        }
    }

    public void Step()
    {
        Host.Step();
        if (!WithReference)
        {
            return;
        }

        var result = interpreter.Step(state, pending);
        state = result.State;
        pending = result.NextInputs();
        ReferenceEvents = result.Events;
    }

    public int Value(int slot, string intrinsic) => Host.Read(slot, Packs.Id(Plan, ValueClass.Intrinsic, intrinsic));

    public int Stat(int slot, string stat) => Host.Read(slot, Packs.Id(Plan, ValueClass.Stat, stat));

    public int ReferenceValue(int slot, string intrinsic) => state.Intrinsic(slot, Packs.Id(Plan, ValueClass.Intrinsic, intrinsic));

    public int ReferenceStat(int slot, string stat) => state.Stat(slot, Packs.Id(Plan, ValueClass.Stat, stat));

    public ulong Digest => Host.Digest();

    public ulong ReferenceDigest
    {
        get
        {
            var scenario = new Sv.Pack.Scenario();
            foreach (var pair in templateBySlot)
            {
                scenario.Entities.Add(new ScenarioEntity { Slot = pair.Key, TemplateIndex = pair.Value });
            }

            return Sv.Pack.ReferenceDigest.Of(state, scenario, pack);
        }
    }

    public unsafe List<(int Event, int Slot, bool Entered)> HostEvents()
    {
        var events = new List<(int, int, bool)>();
        for (var at = 0; at < Host.EventCount; at++)
        {
            events.Add((Host.Events[at].Event, Host.Events[at].Slot, Host.Events[at].Entered));
        }

        return events;
    }

    public void Dispose() => Host.Dispose();
}
