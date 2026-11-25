using Sv.Columns.Canon;
using Sv.Host;

namespace Sv.Columns.Tests;

public sealed class CanonTests
{
    static string Arena()
    {
        var folder = AppContext.BaseDirectory;
        while (folder != null && !File.Exists(Path.Combine(folder, "packs", "columns", "arena.json")))
        {
            folder = Path.GetDirectoryName(folder);
        }

        Assert.NotNull(folder);
        return File.ReadAllText(Path.Combine(folder!, "packs", "columns", "arena.json"));
    }

    [Theory]
    [InlineData(KernelChoice.Scalar, 1, 1)]
    [InlineData(KernelChoice.Best, 1, 1)]
    [InlineData(KernelChoice.Best, 3, 7)]
    [InlineData(KernelChoice.Scalar, 4, 16)]
    public void TheCanonRunHasThePinnedDigest(KernelChoice kernels, int writers, int parts)
    {
        using var pack = PackJson.Read(Arena()).Compile();
        using var world = new ColumnWorld(pack, CanonRun.Capacity, writers, parts, kernels);
        Assert.Equal(CanonRun.Pinned.ToString("X16"), CanonRun.Run(world, world.Step).ToString("X16"));
    }

    [Fact]
    public void TheCanonRunOnWorkersHasThePinnedDigest()
    {
        using var pack = PackJson.Read(Arena()).Compile();
        using var world = new ColumnWorld(pack, CanonRun.Capacity, 3, 9);
        using var workers = new Workers(3);
        using var runner = new ColumnRunner(world, workers);
        Assert.Equal(CanonRun.Pinned.ToString("X16"), CanonRun.Run(world, runner.Step).ToString("X16"));
    }
}
