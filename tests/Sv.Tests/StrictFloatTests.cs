using Kernels.Testing;
using System;
using Xunit;

public class StrictFloatTests
{
    public static readonly float[] Subjects =
    {
        0f, -0f, 1f, -1f, 0.5f, 3f, 1e-38f, -1e-38f, 1e38f, 16777217f, 0.1f, -0.3f,
    };

    [Fact]
    public void DivideMatchesDoubleEmulatedDefinition()
    {
        foreach (var a in Subjects)
        {
            foreach (var b in Subjects)
            {
                if (b == 0f)
                {
                    continue;
                }

                var expected = (float)((double)a / (double)b);
                var actual = StrictFloat.Divide(a, b);
                Assert.Equal(
                    BitConverter.SingleToUInt32Bits(expected),
                    BitConverter.SingleToUInt32Bits(actual));
            }
        }
    }

    [Fact]
    public void SqrtMatchesDoubleEmulatedDefinition()
    {
        foreach (var a in Subjects)
        {
            if (a < 0f)
            {
                continue;
            }

            var expected = (float)Math.Sqrt((double)a);
            var actual = StrictFloat.Sqrt(a);
            Assert.Equal(
                BitConverter.SingleToUInt32Bits(expected),
                BitConverter.SingleToUInt32Bits(actual));
        }
    }

    [Fact]
    public void DivideSqrtExactAnchors()
    {
        Assert.Equal(0.5f, StrictFloat.Divide(1f, 2f));
        Assert.Equal(-4f, StrictFloat.Divide(-8f, 2f));
        Assert.Equal(2f, StrictFloat.Sqrt(4f));
        Assert.True(float.IsNaN(StrictFloat.Divide(0f, 0f)));
    }
}
