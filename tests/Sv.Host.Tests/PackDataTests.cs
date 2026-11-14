using ReferenceKernels = Sv.Kernels.Reference.HostKernels;

namespace Sv.Host.Tests;

public sealed class PackDataTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Generated_PackData_runs_frame_for_frame_like_the_compiled_plan(int workers)
    {
        var plan = Packs.Reference;
        using var compiled = new FrameLoop<ReferenceKernels>(HostPack.From(plan), Packs.Options(workers));
        using var generated = new FrameLoop<ReferenceKernels>(Sv.Kernels.Reference.PackData.Create(), Packs.Options(workers));
        var targets = new[] { "worked_example_player", "player", "rifleman" };
        var damage = new[] { "Physical", "Poison", "Bleeding" };
        var grants = new[] { "GiveCharges" };
        var a = Workload.Spawn(compiled, plan, targets, 2000, "sniper", 16, damage, "Healing", grants);
        var b = Workload.Spawn(generated, plan, targets, 2000, "sniper", 16, damage, "Healing", grants);
        for (var frame = 0; frame < 60; frame++)
        {
            a.Feed(compiled, compiled.Frame, 1);
            b.Feed(generated, generated.Frame, 1);
            compiled.Step();
            generated.Step();
            Assert.Equal(compiled.LastDigest, generated.LastDigest);
        }

        Assert.Equal(compiled.Digest(), generated.Digest());
    }
}
