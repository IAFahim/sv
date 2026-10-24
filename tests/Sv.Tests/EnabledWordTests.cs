using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using Sv;
using Xunit;

public unsafe class EnabledWordTests
{
    const int Rows = 260;

    static readonly ulong[] Words =
    {
        0ul, ulong.MaxValue, 0x5555_5555_5555_5555ul, 0xAAAA_AAAA_AAAA_AAAAul, 1ul, 1ul << 63,
        0x00FF_00FF_00FF_00FFul, 0xFF00_0000_0000_00FFul, 0x8000_0001_0000_0080ul, 0x0123_4567_89AB_CDEFul,
    };

    [Fact]
    public void EnabledWord_MatchesScalar_AllClampKinds()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("floor", 0f).Stat("ceiling", 0f).Build(), 8);
        var minStat = board.Schema.Stat("floor");
        var maxStat = board.Schema.Stat("ceiling");
        var random = new Random(20261010);
        var minCol = stackalloc float[Rows];
        var maxCol = stackalloc float[Rows];
        var intMin = stackalloc int[Rows];
        var intMax = stackalloc int[Rows];
        for (var i = 0; i < Rows; i++)
        {
            minCol[i] = i % 11 == 0 ? -1e30f : i % 17 == 0 ? float.NaN : random.Next(-40, 5) - (float)random.NextDouble();
            maxCol[i] = i % 13 == 0 ? 1e30f : i % 19 == 0 ? float.NaN : random.Next(0, 45) + (float)random.NextDouble();
            intMin[i] = random.Next(-50, 0);
            intMax[i] = random.Next(0, 50);
        }

        board.BindOutput(minStat, minCol);
        board.BindOutput(maxStat, maxCol);
        var clamps = new Clamp[]
        {
            default,
            Clamp.Constant(-10, 10),
            Clamp.Columns(intMin, intMax),
            Clamp.Columns(intMin, null),
            Clamp.Columns(null, intMax),
            Clamp.Stat(board, minStat, maxStat),
            Clamp.StatMin(board, minStat),
            Clamp.StatMax(board, maxStat),
        };

        var words = new ulong[Words.Length + 8];
        Words.CopyTo(words, 0);
        for (var i = Words.Length; i < words.Length; i++)
        {
            words[i] = (ulong)random.NextInt64() & (ulong)random.NextInt64();
        }

        var valueA = stackalloc int[Rows];
        var valueB = stackalloc int[Rows];
        var appliedA = stackalloc int[Rows];
        var appliedB = stackalloc int[Rows];
        var delta = stackalloc int[Rows];
        foreach (var clamp in clamps)
        {
            foreach (var word in words)
            {
                foreach (var offset in new[] { 0, 3, 64, 130 })
                {
                    foreach (var varying in new[] { false, true })
                    {
                        foreach (var withApplied in new[] { false, true })
                        {
                            for (var i = 0; i < Rows; i++)
                            {
                                valueA[i] = valueB[i] = random.Next(-60, 60);
                                appliedA[i] = appliedB[i] = -99;
                                delta[i] = random.Next(-30, 30);
                            }

                            var uniform = random.Next(-30, 30);
                            Simd.EnabledWordScalar(valueA, varying ? delta : null, uniform, clamp, word, offset, withApplied ? appliedA : null);
                            Simd.EnabledWord(valueB, varying ? delta : null, uniform, clamp, word, offset, withApplied ? appliedB : null, 64);
                            for (var i = 0; i < Rows; i++)
                            {
                                Assert.True(valueA[i] == valueB[i], $"value row {i} kind={clamp.Kind} word={word:X} offset={offset}");
                                Assert.True(appliedA[i] == appliedB[i], $"applied row {i} kind={clamp.Kind} word={word:X} offset={offset}");
                            }
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void EnabledWord_ShortColumnMatchesScalar()
    {
        const int rows = 13;
        var random = new Random(20261024);
        var bound = stackalloc int[rows];
        var delta = stackalloc int[rows];
        for (var i = 0; i < rows; i++)
        {
            bound[i] = random.Next(-20, 0);
            delta[i] = random.Next(-40, 40);
        }

        var words = new ulong[] { 0x1F2Bul, 0ul, 1ul << 12, 0b101_0101_0101ul, (1ul << rows) - 1 };
        var clamps = new Clamp[] { default, Clamp.Constant(-30, 30), Clamp.Columns(bound, null) };
        var valueA = stackalloc int[rows];
        var valueB = stackalloc int[rows];
        var appliedA = stackalloc int[64];
        var appliedB = stackalloc int[64];
        foreach (var clamp in clamps)
        {
            foreach (var word in words)
            {
                for (var i = 0; i < rows; i++)
                {
                    valueA[i] = valueB[i] = random.Next(-60, 60);
                }

                for (var i = 0; i < 64; i++)
                {
                    appliedA[i] = appliedB[i] = -7;
                }

                var uniform = random.Next(-40, 40);
                Simd.EnabledWordScalar(valueA, delta, uniform, clamp, word, 0, appliedA);
                Simd.EnabledWord(valueB, delta, uniform, clamp, word, 0, appliedB, rows);
                for (var i = 0; i < rows; i++)
                {
                    Assert.True(valueA[i] == valueB[i], $"value row {i} kind={clamp.Kind} word={word:X}");
                }

                for (var i = 0; i < 64; i++)
                {
                    Assert.True(appliedA[i] == appliedB[i], $"applied row {i} kind={clamp.Kind} word={word:X}");
                }
            }
        }
    }

    [DllImport("libc", SetLastError = true)]
    static extern void* mmap(void* addr, nuint length, int prot, int flags, int fd, nint offset);

    [DllImport("libc", SetLastError = true)]
    static extern int mprotect(void* addr, nuint length, int prot);

    [DllImport("libc", SetLastError = true)]
    static extern int munmap(void* addr, nuint length);

    [Fact]
    public void EnabledWord_NeverTouchesDisabledRowsPastAGuardPage()
    {
        if (!OperatingSystem.IsLinux() || !Avx2.IsSupported)
        {
            return;
        }

        const int page = 4096;
        var region = (byte*)mmap(null, page * 2, 3, 0x22, -1, 0);
        Assert.True(region != (byte*)-1);
        try
        {
            Assert.Equal(0, mprotect(region + page, page, 0));
            var value = (int*)(region + page) - 3;
            value[0] = 5;
            value[1] = 6;
            value[2] = 7;
            using var board = Board.Create(SchemaBuilder.Create().Intrinsic("mana").Build(), 64);
            board.RunEnabledChunk(value, 1, Clamp.Constant(0, 6), 0b111ul, 0, null, 3);
            Assert.Equal(6, value[0]);
            Assert.Equal(6, value[1]);
            Assert.Equal(6, value[2]);
            Simd.EnabledWord(value, value, 0, Clamp.Columns(value, null), 0b101ul, 0, null, 3);
            Assert.Equal(12, value[0]);
            Assert.Equal(12, value[2]);
        }
        finally
        {
            munmap(region, page * 2);
        }
    }
}
