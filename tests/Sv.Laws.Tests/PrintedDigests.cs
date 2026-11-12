using System.Runtime.Intrinsics;
using Xunit.Abstractions;

namespace Sv.Laws.Tests;

public sealed class PrintedDigests
{
    readonly ITestOutputHelper output;

    public PrintedDigests(ITestOutputHelper output) => this.output = output;

    static ulong RandomWorldDigest(StoragePolicy policy)
    {
        var gen = new Gen(1);
        using var world = TestWorlds.Create(policy);
        var live = new List<int>();
        for (var frame = 0; frame < 20; frame++)
        {
            TestWorlds.Mutate(world, gen, live, 40);
            world.EndFrame();
        }

        return world.Digest();
    }

    [Fact]
    public void RandomWorldDigestIsTheSameUnderBothPoliciesAndIsPrinted()
    {
        var shared = RandomWorldDigest(StoragePolicy.Shared);
        var dense = RandomWorldDigest(StoragePolicy.Dense);
        output.WriteLine($"random world seed 1, 20 frames: shared {shared:X16} dense {dense:X16}; Vector256 {Vector256.IsHardwareAccelerated} Vector128 {Vector128.IsHardwareAccelerated}");
        Assert.Equal(shared, dense);
    }
}
