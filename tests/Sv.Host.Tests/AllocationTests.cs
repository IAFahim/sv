using ReferenceKernels = Sv.Kernels.Reference.HostKernels;

namespace Sv.Host.Tests;

[CollectionDefinition(nameof(AllocationTests), DisableParallelization = true)]
public sealed class AllocationCollection
{
}

[Collection(nameof(AllocationTests))]
public sealed class AllocationTests
{
    const int WarmFrames = 200;
    const int MeasuredFrames = 32;
    const int Windows = 3;
    readonly ITestOutputHelper output;

    public AllocationTests(ITestOutputHelper output) => this.output = output;

    static (long Thread, long Total) Measure<TKernels>(FrameLoop<TKernels> host, Workload work, int kind)
        where TKernels : struct, IPackKernels
    {
        var thread = long.MaxValue;
        var total = long.MaxValue;
        for (var window = 0; window < Windows; window++)
        {
            var threadBefore = GC.GetAllocatedBytesForCurrentThread();
            var workersBefore = host.WorkerAllocatedBytes;
            for (var frame = 0; frame < MeasuredFrames; frame++)
            {
                work.Feed(host, host.Frame, kind);
                host.Step();
            }

            var workersAfter = host.WorkerAllocatedBytes;
            var threadAfter = GC.GetAllocatedBytesForCurrentThread();
            thread = Math.Min(thread, threadAfter - threadBefore);
            total = Math.Min(total, threadAfter - threadBefore + workersAfter - workersBefore);
        }

        return (thread, total);
    }

    static (long Thread, long Total) Warm<TKernels>(PackPlan plan, int workers, string[] targets, string source, string[] damage, string healing, string[] grants)
        where TKernels : struct, IPackKernels
    {
        using var host = new FrameLoop<TKernels>(HostPack.From(plan), Packs.Options(workers));
        var work = Workload.Spawn(host, plan, targets, 8000, source, 16, damage, healing, grants);
        for (var frame = 0; frame < WarmFrames; frame++)
        {
            work.Feed(host, host.Frame, 1);
            host.Step();
        }

        return Measure(host, work, 1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void Warm_frames_of_the_reference_pack_allocate_nothing_on_any_sv_thread(int workers)
    {
        var bytes = Warm<ReferenceKernels>(Packs.Reference, workers, new[] { "worked_example_player", "player", "rifleman" }, "sniper", new[] { "Physical", "Poison", "Bleeding" }, "Healing", new[] { "GiveCharges" });
        output.WriteLine($"workers {workers}: thread {bytes.Thread} B, sv threads (main and workers) {bytes.Total} B over {MeasuredFrames} frames");
        Assert.Equal(0L, bytes.Thread);
        Assert.Equal(0L, bytes.Total);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void Warm_frames_of_a_pack_that_gains_and_loses_cards_allocate_nothing_on_any_sv_thread(int workers)
    {
        var bytes = (ValueTuple<long, long>)Packs.Call(typeof(AllocationTests), nameof(Warm), CardPack.Kernels, CardPack.Plan, workers, new[] { "player", "warded" }, "grunt", new[] { "Physical" }, "Healing", new[] { "GrantWard", "GrantFortify" })!;
        output.WriteLine($"workers {workers}: thread {bytes.Item1} B, sv threads (main and workers) {bytes.Item2} B over {MeasuredFrames} frames");
        Assert.Equal(0L, bytes.Item1);
        Assert.Equal(0L, bytes.Item2);
    }
}
