using System.Runtime.CompilerServices;
using Sv;
using Xunit;

public unsafe class ClampTests
{
    [Theory]
    [InlineData(0, 100, -5, 0)]
    [InlineData(0, 100, 0, 0)]
    [InlineData(0, 100, 50, 50)]
    [InlineData(0, 100, 100, 100)]
    [InlineData(0, 100, 105, 100)]
    [InlineData(-10, -10, 7, -10)]
    [InlineData(int.MinValue, int.MaxValue, 0, 0)]
    public void Constant_Clamps_Ternary(int min, int max, int value, int expected)
    {
        var clamp = Clamp.Constant(min, max);
        Assert.Equal(expected, clamp.Apply(value, 0));
    }

    [Fact]
    public void Constant_RejectsInvertedBounds()
    {
        Assert.Throws<ArgumentException>(() => Clamp.Constant(5, 4));
    }

    [Fact]
    public void Constant_IntExtremeOverflow_WrapsDeterministically()
    {
        var clamp = Clamp.Constant(int.MinValue, int.MaxValue);
        var applied = int.MaxValue;
        var wrapped = unchecked(applied + 1);
        Assert.Equal(wrapped, clamp.Apply(wrapped, 0));
    }

    [Fact]
    public void Columns_PerSlotBounds_ReadSameSlotIndex()
    {
        var min = stackalloc int[2];
        var max = stackalloc int[2];
        min[0] = 0; max[0] = 10;
        min[1] = 100; max[1] = 200;
        var clamp = Clamp.Columns(min, max);

        Assert.Equal(5, clamp.Apply(5, 0));
        Assert.Equal(10, clamp.Apply(99, 0));
        Assert.Equal(150, clamp.Apply(150, 1));
        Assert.Equal(200, clamp.Apply(300, 1));
        Assert.Equal(100, clamp.Apply(-3, 1));
    }

    [Fact]
    public void Columns_NullSides_MeanUnbounded()
    {
        var max = stackalloc int[1];
        max[0] = 10;
        var clamp = Clamp.Columns(null, max);

        Assert.Equal(int.MinValue, clamp.Apply(int.MinValue, 0));
        Assert.Equal(10, clamp.Apply(11, 0));

        var min = stackalloc int[1];
        min[0] = -5;
        var lower = Clamp.Columns(min, null);

        Assert.Equal(-5, lower.Apply(-6, 0));
        Assert.Equal(int.MaxValue, lower.Apply(int.MaxValue, 0));
    }

    [Fact]
    public void Columns_RequiresAtLeastOneBound()
    {
        Assert.Throws<ArgumentException>(() => Clamp.Columns(null, null));
    }

    [Fact]
    public void None_PassesValueThrough()
    {
        var clamp = default(Clamp);
        Assert.Equal(42, clamp.Apply(42, 7));
    }

    [Fact]
    public void Columns_MatchesScalarTernaryChain()
    {
        var min = stackalloc int[64];
        var max = stackalloc int[64];
        var rng = new Random(1234);
        for (var i = 0; i < 64; i++)
        {
            min[i] = rng.Next(-50, 0);
            max[i] = rng.Next(0, 50);
        }

        var clamp = Clamp.Columns(min, max);
        for (var i = 0; i < 64; i++)
        {
            var v = rng.Next(-100, 100);
            var expected = v < min[i] ? min[i] : v > max[i] ? max[i] : v;
            Assert.Equal(expected, clamp.Apply(v, i));
        }
    }
}
