using System.Runtime.CompilerServices;
using Sv;
using Xunit;

public unsafe class IntrinsicsTests
{
    static Schema TestSchema() =>
        SchemaBuilder.Create()
            .Intrinsic("mana", 0, 100)
            .Intrinsic("charges")
            .Build();

    [Fact]
    public void Uniform_Add_MidRange_AppliesFully()
    {
        using var board = Board.Create(TestSchema(), 8);
        var value = stackalloc int[8];
        var applied = stackalloc int[8];
        value[0] = 40;

        board.RunIntrinsicsChunk(value, 15, Clamp.Constant(0, 100), 0, 1, applied);

        Assert.Equal(55, value[0]);
        Assert.Equal(15, applied[0]);
    }

    [Fact]
    public void Uniform_Add_AtMax_ClampsAndReportsPartialDelta()
    {
        using var board = Board.Create(TestSchema(), 8);
        var value = stackalloc int[8];
        var applied = stackalloc int[8];
        value[0] = 95;

        board.RunIntrinsicsChunk(value, 10, Clamp.Constant(0, 100), 0, 1, applied);

        Assert.Equal(100, value[0]);
        Assert.Equal(5, applied[0]);
    }

    [Fact]
    public void Uniform_Subtract_AtMin_ClampsAndReportsPartialDelta()
    {
        using var board = Board.Create(TestSchema(), 8);
        var value = stackalloc int[8];
        var applied = stackalloc int[8];
        value[3] = 3;

        board.RunIntrinsicsChunk(value, -10, Clamp.Constant(0, 100), 0, 8, applied);

        Assert.Equal(0, value[3]);
        Assert.Equal(-3, applied[3]);
    }

    [Fact]
    public void VaryingDelta_PerEntityDamageAndHealing()
    {
        using var board = Board.Create(TestSchema(), 4);
        var value = stackalloc int[4] { 50, 50, 50, 50 };
        var delta = stackalloc int[4] { -20, -80, 30, 75 };
        var applied = stackalloc int[4];

        board.RunIntrinsicsChunk(value, delta, Clamp.Constant(0, 100), 0, 4, applied);

        Assert.Equal(new[] { 30, 0, 80, 100 }, new ReadOnlySpan<int>(value, 4).ToArray());
        Assert.Equal(new[] { -20, -50, 30, 50 }, new ReadOnlySpan<int>(applied, 4).ToArray());
    }

    [Fact]
    public void Uniform_IsSpecialCaseOfVarying()
    {
        using var board = Board.Create(TestSchema(), 8);
        var a = stackalloc int[8];
        var b = stackalloc int[8];
        var uniformDelta = stackalloc int[8];
        var appliedA = stackalloc int[8];
        var appliedB = stackalloc int[8];
        for (var i = 0; i < 8; i++)
        {
            a[i] = 10 * i;
            b[i] = 10 * i;
            uniformDelta[i] = 33;
        }

        var clamp = Clamp.Constant(0, 1000);
        board.RunIntrinsicsChunk(a, 33, clamp, 0, 8, appliedA);
        board.RunIntrinsicsChunk(b, uniformDelta, clamp, 0, 8, appliedB);

        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(a[i], b[i]);
            Assert.Equal(appliedA[i], appliedB[i]);
        }
    }

    [Fact]
    public void AppliedOptional_SkipsWritesWhenNull()
    {
        using var board = Board.Create(TestSchema(), 8);
        var value = stackalloc int[8];
        var applied = stackalloc int[8];
        applied[0] = 777;
        value[0] = 10;

        board.RunIntrinsicsChunk(value, 5, Clamp.Constant(0, 100), 0, 1, null);

        Assert.Equal(15, value[0]);
        Assert.Equal(777, applied[0]);
    }

    [Fact]
    public void ChunkRange_OnlyTouchesItsRows()
    {
        using var board = Board.Create(TestSchema(), 8);
        var value = stackalloc int[8];
        var applied = stackalloc int[8];
        for (var i = 0; i < 8; i++)
        {
            value[i] = 1;
        }

        board.RunIntrinsicsChunk(value, 10, Clamp.Constant(0, 100), 2, 3, applied);

        Assert.Equal(1, value[1]);
        Assert.Equal(11, value[2]);
        Assert.Equal(11, value[3]);
        Assert.Equal(11, value[4]);
        Assert.Equal(1, value[5]);
    }

    [Fact]
    public void Enabled_MaskSelectsRows_OffsetAlignsBitZero()
    {
        using var board = Board.Create(TestSchema(), 128);
        var value = stackalloc int[128];
        var applied = stackalloc int[128];
        for (var i = 0; i < 128; i++)
        {
            value[i] = 10;
        }

        board.RunEnabledChunk(value, 5, Clamp.Constant(0, 100), 0b1010_0001ul, 64, applied);

        Assert.Equal(15, value[64]);
        Assert.Equal(10, value[65]);
        Assert.Equal(10, value[66]);
        Assert.Equal(10, value[67]);
        Assert.Equal(10, value[68]);
        Assert.Equal(15, value[69]);
        Assert.Equal(10, value[70]);
        Assert.Equal(15, value[71]);
        Assert.Equal(10, value[63]);
        Assert.Equal(0, applied[63]);
        Assert.Equal(5, applied[64]);
        Assert.Equal(0, applied[65]);
        Assert.Equal(5, applied[69]);
    }

    [Fact]
    public void Enabled_AllMask_TouchesAllSixtyFour()
    {
        using var board = Board.Create(TestSchema(), 128);
        var value = stackalloc int[128];
        var applied = stackalloc int[128];

        board.RunEnabledChunk(value, 1, Clamp.Constant(0, 100), ulong.MaxValue, 0, applied);

        for (var i = 0; i < 64; i++)
        {
            Assert.Equal(1, value[i]);
            Assert.Equal(1, applied[i]);
        }
    }

    [Fact]
    public void Enabled_ZeroMask_WritesZeroAppliedOnly()
    {
        using var board = Board.Create(TestSchema(), 128);
        var value = stackalloc int[128];
        var applied = stackalloc int[128];
        for (var i = 0; i < 128; i++)
        {
            value[i] = 9;
            applied[i] = -1;
        }

        board.RunEnabledChunk(value, 7, Clamp.Constant(0, 100), 0ul, 0, applied);

        for (var i = 0; i < 64; i++)
        {
            Assert.Equal(9, value[i]);
            Assert.Equal(0, applied[i]);
        }
    }

    [Fact]
    public void Enabled_ClampBoundsArePerEnabledRow()
    {
        using var board = Board.Create(TestSchema(), 128);
        var value = stackalloc int[128];
        var applied = stackalloc int[128];
        value[65] = 99;

        board.RunEnabledChunk(value, 50, Clamp.Constant(0, 100), 1ul << 1, 64, applied);

        Assert.Equal(100, value[65]);
        Assert.Equal(1, applied[65]);
    }

    [Fact]
    public void SpanTwins_MatchPointerCore()
    {
        using var board = Board.Create(TestSchema(), 16);
        var pointerValue = stackalloc int[16];
        var spanValue = new int[16];
        var pointerApplied = stackalloc int[16];
        var spanApplied = new int[16];
        var delta = new int[16];
        var rng = new Random(99);
        for (var i = 0; i < 16; i++)
        {
            pointerValue[i] = rng.Next(0, 100);
            spanValue[i] = pointerValue[i];
            delta[i] = rng.Next(-60, 60);
        }

        fixed (int* sd = delta)
        {
            board.RunIntrinsicsChunk(pointerValue, sd, Clamp.Constant(0, 100), 0, 16, pointerApplied);
        }

        board.RunIntrinsicsChunk(spanValue, delta, Clamp.Constant(0, 100), 0, 16, spanApplied);

        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(pointerValue[i], spanValue[i]);
            Assert.Equal(pointerApplied[i], spanApplied[i]);
        }
    }

    [Fact]
    public void EnabledSpan_VaryingDelta_WritesZeroForDisabled()
    {
        using var board = Board.Create(TestSchema(), 128);
        var value = new int[128];
        var applied = new int[128];
        var delta = new int[128];
        value[70] = 10;
        delta[70] = -40;
        value[71] = 10;
        delta[71] = -40;

        board.RunEnabledChunk(value, delta, Clamp.Constant(0, 100), 1ul << 6, 64, applied);

        Assert.Equal(0, value[70]);
        Assert.Equal(-10, applied[70]);
        Assert.Equal(10, value[71]);
        Assert.Equal(0, applied[71]);
    }

    [Fact]
    public void Overflow_SaturatesBeforeClamp()
    {
        using var board = Board.Create(TestSchema(), 4);
        var value = stackalloc int[4];
        var applied = stackalloc int[4];
        value[0] = int.MaxValue;

        board.RunIntrinsicsChunk(value, 1, Clamp.Constant(int.MinValue, int.MaxValue), 0, 1, applied);

        Assert.Equal(int.MaxValue, value[0]);
        Assert.Equal(0, applied[0]);
    }

    [Fact]
    public void NullValue_ThrowsAlways()
    {
        using var board = Board.Create(TestSchema(), 8);
        Assert.Throws<ArgumentNullException>(() => board.RunIntrinsicsChunk((int*)null, 1, Clamp.Constant(0, 1), 0, 1));
        var d = stackalloc int[1];
        Assert.Throws<ArgumentNullException>(() => board.RunIntrinsicsChunk((int*)null, d, Clamp.Constant(0, 1), 0, 1));
    }
}
