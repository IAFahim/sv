using System.Reflection;
using System.Runtime.Versioning;
using Sv;
using Xunit;

public class PortableLegTests
{
    [Fact]
    public void Leg_RunsTheNetStandardAssetWithScalarKernels()
    {
        var framework = typeof(Board).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName;
        Assert.Equal(".NETStandard,Version=v2.1", framework);
        Assert.False(Simd.Has128);
        Assert.False(Simd.Has256);
    }
}
