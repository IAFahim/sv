namespace Sv.Host.Tests;

public sealed class Workload
{
    public required int[] Targets { get; init; }

    public required int[] Sources { get; init; }

    public required int[] Damage { get; init; }

    public required int Healing { get; init; }

    public int[] Grants { get; init; } = Array.Empty<int>();

    public static Workload Spawn<TKernels>(FrameLoop<TKernels> host, PackPlan plan, string[] targetTemplates, int targets, string sourceTemplate, int sources, string[] damage, string healing, string[] grants)
        where TKernels : struct, IPackKernels
    {
        var targetSlots = new int[targets];
        for (var at = 0; at < targets; at++)
        {
            targetSlots[at] = host.Spawn(Packs.Template(plan, targetTemplates[at % targetTemplates.Length]));
        }

        var sourceSlots = new int[sources];
        for (var at = 0; at < sources; at++)
        {
            sourceSlots[at] = host.Spawn(Packs.Template(plan, sourceTemplate));
        }

        return new Workload
        {
            Targets = targetSlots,
            Sources = sourceSlots,
            Damage = damage.Select(name => Packs.Id(plan, ValueClass.Channel, name)).ToArray(),
            Healing = Packs.Id(plan, ValueClass.Channel, healing),
            Grants = grants.Select(name => Packs.Id(plan, ValueClass.Channel, name)).ToArray(),
        };
    }

    public void Feed<TKernels>(FrameLoop<TKernels> host, long frame, int kind)
        where TKernels : struct, IPackKernels
    {
        var step = (int)(frame % 997);
        for (var at = 0; at < Targets.Length; at++)
        {
            var phase = at + step;
            var target = Targets[at];
            var source = Sources[at % Sources.Length];
            if (phase % 3 == 0)
            {
                host.Send(target, source, kind, Damage[phase % Damage.Length], phase % 7 + 1);
            }

            if (phase % 2 == 0)
            {
                host.Send(target, source, kind, Healing, 4);
            }

            if (Grants.Length > 0 && phase % 11 == 0)
            {
                host.Send(target, source, kind, Grants[phase % Grants.Length], 1);
            }

            if (Grants.Length > 0 && phase % 11 == 5)
            {
                host.Send(target, source, kind, Grants[phase % Grants.Length], -1);
            }
        }
    }
}
