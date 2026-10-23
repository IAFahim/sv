using System;
using Sv;
using Xunit;

public unsafe class KernelTests
{
    [Fact]
    public void StaticKernels_MatchTheBoardVerbs_UniformAndPerRow()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("cap", 100f).Build(), 128);
        var clamp = Clamp.Constant(0, 200);
        var a = stackalloc int[128];
        var b = stackalloc int[128];
        var da = stackalloc int[128];
        var db = stackalloc int[128];
        var appliedA = stackalloc int[128];
        var appliedB = stackalloc int[128];
        for (var i = 0; i < 128; i++)
        {
            a[i] = 100 + i;
            b[i] = 100 + i;
            da[i] = i & 7;
            db[i] = i & 7;
        }

        Kernel.IntrinsicsChunk(a, -3, clamp, 16, 96, board.Capacity, appliedA);
        board.RunIntrinsicsChunk(b, -3, clamp, 16, 96, appliedB);
        for (var i = 0; i < 128; i++)
        {
            Assert.Equal(b[i], a[i]);
            Assert.Equal(appliedB[i], appliedA[i]);
        }

        Kernel.IntrinsicsChunk(a, da, clamp, 0, 128, board.Capacity, appliedA);
        board.RunIntrinsicsChunk(b, db, clamp, 0, 128, appliedB);
        for (var i = 0; i < 128; i++)
        {
            Assert.Equal(b[i], a[i]);
            Assert.Equal(appliedB[i], appliedA[i]);
        }
    }

    [Fact]
    public void StaticKernels_MatchTheBoardVerbs_Enabled()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("cap", 100f).Build(), 128);
        var clamp = Clamp.Constant(0, 200);
        var words = stackalloc ulong[2];
        words[0] = 0x5555_5555_5555_5555ul;
        words[1] = ulong.MaxValue;
        var a = stackalloc int[128];
        var b = stackalloc int[128];
        var appliedA = stackalloc int[128];
        var appliedB = stackalloc int[128];
        for (var i = 0; i < 128; i++)
        {
            a[i] = 50;
            b[i] = 50;
        }

        Kernel.EnabledChunk(a, 7, clamp, words, 2, 0, board.Capacity, appliedA);
        board.RunEnabledChunk(b, 7, clamp, words, 2, 0, appliedB);
        for (var i = 0; i < 128; i++)
        {
            Assert.Equal(b[i], a[i]);
            Assert.Equal(appliedB[i], appliedA[i]);
        }

        Kernel.EnabledChunk(a, 7, clamp, 0x0F0F_0F0F_0F0F_0F0Ful, 64, board.Capacity, appliedA);
        board.RunEnabledChunk(b, 7, clamp, 0x0F0F_0F0F_0F0F_0F0Ful, 64, appliedB);
        for (var i = 0; i < 128; i++)
        {
            Assert.Equal(b[i], a[i]);
            Assert.Equal(appliedB[i], appliedA[i]);
        }
    }

    [Fact]
    public void StaticKernels_GuardAgainstTheExplicitCapacity()
    {
        var value = stackalloc int[64];
        Assert.Throws<ArgumentOutOfRangeException>(() => Kernel.IntrinsicsChunk(value, 1, Clamp.Constant(0, 10), 0, 65, 64));
        Assert.Throws<ArgumentOutOfRangeException>(() => Kernel.EnabledChunk(value, 1, Clamp.Constant(0, 10), ulong.MaxValue, 64, 64));
        var words = stackalloc ulong[2];
        Assert.Throws<ArgumentOutOfRangeException>(() => Kernel.EnabledChunk(value, 1, Clamp.Constant(0, 10), words, 2, 0, 64));
        Kernel.IntrinsicsChunk(value, 1, Clamp.Constant(0, 10), 0, 64, 64);
        Kernel.EnabledChunk(value, 1, Clamp.Constant(0, 10), words, 1, 0, 64);
    }
}
