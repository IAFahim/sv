using System.Runtime.Intrinsics;
using Xunit;

public class IsaLegTests
{
    [Fact]
    public void Leg_ObservesTheFlagsItExpects()
    {
        var leg = Environment.GetEnvironmentVariable("SV_ISA_LEG");
        switch (leg)
        {
            case null:
            case "":
                return;
            case "default":
                Assert.True(Vector256.IsHardwareAccelerated);
                Assert.True(Sv.Simd.Has256);
                return;
            case "noavx2":
                Assert.False(Vector256.IsHardwareAccelerated);
                Assert.True(Vector128.IsHardwareAccelerated);
                Assert.False(Sv.Simd.Has256);
                Assert.True(Sv.Simd.Has128);
                return;
            case "scalar":
                Assert.False(Vector128.IsHardwareAccelerated);
                Assert.False(Vector256.IsHardwareAccelerated);
                Assert.False(Sv.Simd.Has128);
                return;
            default:
                Assert.Fail($"unknown SV_ISA_LEG {leg}");
                return;
        }
    }
}
