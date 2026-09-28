using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Sv;
using Xunit;

public unsafe class SimdTests
{
    static readonly int[] Counts = { 0, 1, 2, 3, 4, 5, 7, 8, 9, 15, 16, 31, 32, 63, 64, 65, 127, 128, 130 };

    static Schema SchemaWithStats()
    {
        return SchemaBuilder.Create()
            .Stat("floor", 0f)
            .Stat("ceiling", 0f)
            .Intrinsic("mana")
            .Build();
    }

    static int[] RandomInts(Random random, int count)
    {
        var specials = new[] { int.MinValue, int.MaxValue, -1, 0, 1, int.MinValue / 2, 46341 };
        var values = new int[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = random.Next(4) == 0 ? specials[random.Next(specials.Length)] : random.Next(int.MinValue, int.MaxValue);
        }

        return values;
    }

    static void AssertSameInts(int* expected, int* actual, int length, string what)
    {
        for (var i = 0; i < length; i++)
        {
            Assert.True(expected[i] == actual[i], $"{what} row {i}: expected {expected[i]} got {actual[i]}");
        }
    }

    [Fact]
    public void Chunk_WidthInvariance_AllClampKinds()
    {
        using var board = Board.Create(SchemaWithStats(), 8);
        var minStat = board.Schema.Stat("floor");
        var maxStat = board.Schema.Stat("ceiling");
        var minCol = stackalloc float[260];
        var maxCol = stackalloc float[260];
        var random = new Random(20260928);
        for (var i = 0; i < 260; i++)
        {
            minCol[i] = i % 11 == 0 ? -1e30f : i % 17 == 0 ? float.NaN : (float)(random.Next(-40, 5) - random.NextDouble());
            maxCol[i] = i % 13 == 0 ? 1e30f : i % 19 == 0 ? float.NaN : (float)(random.Next(0, 45) + random.NextDouble());
        }

        board.BindOutput(minStat, minCol);
        board.BindOutput(maxStat, maxCol);

        var intMin = stackalloc int[260];
        var intMax = stackalloc int[260];
        for (var i = 0; i < 260; i++)
        {
            intMin[i] = i % 5 == 0 ? 40 - i : i % 9 == 0 ? i - 20 : -20 + i / 3;
            intMax[i] = i % 6 == 0 ? i - 30 : i % 10 == 0 ? 30 - i : i / 3;
        }

        var clamps = new Clamp[]
        {
            default,
            Clamp.Constant(-10, 10),
            Clamp.Constant(int.MinValue, int.MaxValue),
            Clamp.Columns(intMin, intMax),
            Clamp.Columns(intMin, null),
            Clamp.Columns(null, intMax),
            Clamp.Stat(board, minStat, maxStat),
            Clamp.StatMin(board, minStat),
            Clamp.StatMax(board, maxStat),
        };

        var valueA = stackalloc int[260];
        var valueB = stackalloc int[260];
        var valueC = stackalloc int[260];
        var appliedA = stackalloc int[260];
        var appliedB = stackalloc int[260];
        var appliedC = stackalloc int[260];
        var delta = stackalloc int[260];
        foreach (var count in Counts)
        {
            foreach (var start in new[] { 0, 3 })
            {
                var total = start + count;
                if (total > 260)
                {
                    continue;
                }

                var values = RandomInts(random, total);
                var deltas = RandomInts(random, total);
                var uniform = random.Next(-1000, 1000);
                for (var i = 0; i < total; i++)
                {
                    valueA[i] = values[i];
                    valueB[i] = values[i];
                    valueC[i] = values[i];
                    delta[i] = deltas[i];
                }

                for (var i = total; i < 260; i++)
                {
                    appliedA[i] = -99;
                    appliedB[i] = -99;
                    appliedC[i] = -99;
                }

                foreach (var clamp in clamps)
                {
                    foreach (var varying in new[] { false, true })
                    {
                        Simd.ChunkScalar(valueA, varying ? delta : null, uniform, clamp, start, count, appliedA);
                        if (Vector128.IsHardwareAccelerated)
                        {
                            Simd.ChunkW128(valueB, varying ? delta : null, uniform, clamp, start, count, appliedB);
                            AssertSameInts(valueA, valueB, total, $"w128 value count={count} start={start} kind={clamp.Kind} varying={varying}");
                            AssertSameInts(appliedA, appliedB, total, $"w128 applied count={count} start={start} kind={clamp.Kind} varying={varying}");
                        }

                        if (Vector256.IsHardwareAccelerated)
                        {
                            Simd.ChunkW256(valueC, varying ? delta : null, uniform, clamp, start, count, appliedC);
                            AssertSameInts(valueA, valueC, total, $"w256 value count={count} start={start} kind={clamp.Kind} varying={varying}");
                            AssertSameInts(appliedA, appliedC, total, $"w256 applied count={count} start={start} kind={clamp.Kind} varying={varying}");
                        }

                        for (var i = 0; i < total; i++)
                        {
                            valueA[i] = values[i];
                            valueB[i] = values[i];
                            valueC[i] = values[i];
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void BoolNonZeroWord_WidthInvariance()
    {
        var random = new Random(4242);
        const int rows = 512;
        var cond = stackalloc byte[rows];
        var alphabet = new byte[] { 0, 1, 2, 0x7F, 0x80, 0xFF, 0x42 };
        for (var i = 0; i < rows; i++)
        {
            cond[i] = alphabet[random.Next(alphabet.Length)];
        }

        for (var row = 0; row + 64 <= rows; row += 64)
        {
            var scalar = Simd.BoolWordScalar(cond, row);
            Assert.Equal(scalar, Simd.BoolWord128(cond, row));
            if (Vector256.IsHardwareAccelerated)
            {
                Assert.Equal(scalar, Simd.BoolWord256(cond, row));
            }
        }
    }

    [Fact]
    public void IntNonZeroWord_WidthInvariance()
    {
        var random = new Random(1717);
        const int rows = 512;
        var values = stackalloc int[rows];
        for (var i = 0; i < rows; i++)
        {
            values[i] = i % 3 == 0 ? 0 : random.Next(4) == 0 ? int.MinValue : random.Next(int.MinValue, int.MaxValue);
        }

        for (var row = 0; row + 64 <= rows; row += 64)
        {
            var scalar = Simd.IntWordScalar(values, row);
            Assert.Equal(scalar, Simd.IntWord128(values, row));
            if (Vector256.IsHardwareAccelerated)
            {
                Assert.Equal(scalar, Simd.IntWord256(values, row));
            }
        }
    }

    [Fact]
    public void NormalizeBools_WidthInvariance_ExactZeroOneBytes()
    {
        var random = new Random(99);
        const int rows = 300;
        var src = stackalloc byte[rows];
        var alphabet = new byte[] { 0, 1, 2, 0x7F, 0x80, 0xFF };
        for (var i = 0; i < rows; i++)
        {
            src[i] = alphabet[random.Next(alphabet.Length)];
        }

        var a = stackalloc byte[rows];
        var b = stackalloc byte[rows];
        var c = stackalloc byte[rows];
        Simd.NormalizeBoolsScalar(src, a, rows);
        Simd.NormalizeBools128(src, b, rows);
        if (Vector256.IsHardwareAccelerated)
        {
            Simd.NormalizeBools256(src, c, rows);
        }

        for (var i = 0; i < rows; i++)
        {
            var expected = (byte)(src[i] != 0 ? 1 : 0);
            Assert.Equal(expected, a[i]);
            Assert.Equal(expected, b[i]);
            Assert.True(b[i] == 0 || b[i] == 1);
            if (Vector256.IsHardwareAccelerated)
            {
                Assert.Equal(expected, c[i]);
            }
        }
    }

    [Fact]
    public void ApplyButtonWords_WidthInvariance_AcrossSequences()
    {
        var random = new Random(31337);
        const int words = 7;
        var sequence = new ulong[5][];
        for (var t = 0; t < sequence.Length; t++)
        {
            sequence[t] = new ulong[words];
            for (var w = 0; w < words; w++)
            {
                sequence[t][w] = ulong.MaxValue;
                for (var b = 0; b < 64; b++)
                {
                    if (random.Next(2) == 0)
                    {
                        sequence[t][w] &= ~(1ul << b);
                    }
                }
            }
        }

        ulong* src = stackalloc ulong[words];
        ulong* buttonsS = stackalloc ulong[words];
        ulong* previousS = stackalloc ulong[words];
        ulong* pressedS = stackalloc ulong[words];
        ulong* releasedS = stackalloc ulong[words];
        ulong* buttonsV = stackalloc ulong[words];
        ulong* previousV = stackalloc ulong[words];
        ulong* pressedV = stackalloc ulong[words];
        ulong* releasedV = stackalloc ulong[words];
        ulong* buttonsW = stackalloc ulong[words];
        ulong* previousW = stackalloc ulong[words];
        ulong* pressedW = stackalloc ulong[words];
        ulong* releasedW = stackalloc ulong[words];

        foreach (var frame in sequence)
        {
            fixed (ulong* f = frame)
            {
                for (var w = 0; w < words; w++)
                {
                    src[w] = f[w];
                }

                Simd.ApplyButtonWordsScalar(src, buttonsS, previousS, pressedS, releasedS, words);
                Simd.ApplyButtonWords128(src, buttonsV, previousV, pressedV, releasedV, words);
                if (Vector256.IsHardwareAccelerated)
                {
                    Simd.ApplyButtonWords256(src, buttonsW, previousW, pressedW, releasedW, words);
                }

                for (var w = 0; w < words; w++)
                {
                    Assert.Equal(buttonsS[w], buttonsV[w]);
                    Assert.Equal(previousS[w], previousV[w]);
                    Assert.Equal(pressedS[w], pressedV[w]);
                    Assert.Equal(releasedS[w], releasedV[w]);
                    if (Vector256.IsHardwareAccelerated)
                    {
                        Assert.Equal(buttonsS[w], buttonsW[w]);
                        Assert.Equal(previousS[w], previousW[w]);
                        Assert.Equal(pressedS[w], pressedW[w]);
                        Assert.Equal(releasedS[w], releasedW[w]);
                    }
                }
            }
        }
    }

    [Fact]
    public void EmitFromBools_VectorizedScan_MatchesMaskWalk()
    {
        var random = new Random(5150);
        const int rows = 300;
        var cond = stackalloc byte[rows];
        for (var i = 0; i < rows; i++)
        {
            cond[i] = random.Next(3) == 0 ? (byte)0 : (byte)random.Next(1, 256);
        }

        using var scanStreams = EventStreams.Create();
        var scanChannel = scanStreams.CreateChannel<EventsSandbox.ManaGained>();
        scanStreams.EmitFromBools(scanChannel, cond, 0, rows, &EventsSandbox.MakeMana);
        scanStreams.Swap();

        using var maskStreams = EventStreams.Create();
        var maskChannel = maskStreams.CreateChannel<EventsSandbox.ManaGained>();
        for (var block = 0; block < rows; block += 64)
        {
            var take = Math.Min(64, rows - block);
            ulong enabled = 0;
            for (var i = 0; i < take; i++)
            {
                if (cond[block + i] != 0)
                {
                    enabled |= 1ul << i;
                }
            }

            maskStreams.EmitFromBools(maskChannel, cond, enabled, block, &EventsSandbox.MakeMana);
        }

        maskStreams.Swap();

        var scanned = Drain(scanStreams);
        var masked = Drain(maskStreams);
        Assert.Equal(masked.Count, scanned.Count);
        for (var i = 0; i < scanned.Count; i++)
        {
            Assert.Equal(masked[i].Item1, scanned[i].Item1);
            Assert.Equal(masked[i].Item2, scanned[i].Item2);
        }
    }

    [Fact]
    public void EmitFromNonZero_VectorizedScan_MatchesMaskWalk()
    {
        var random = new Random(8080);
        const int rows = 300;
        var values = stackalloc int[rows];
        for (var i = 0; i < rows; i++)
        {
            values[i] = i % 4 == 0 ? 0 : random.Next(-50, 50);
        }

        using var scanStreams = EventStreams.Create();
        var scanChannel = scanStreams.CreateChannel<EventsSandbox.ManaGained>();
        scanStreams.EmitFromNonZero(scanChannel, values, 0, rows, &EventsSandbox.MakeMana);
        scanStreams.Swap();

        using var maskStreams = EventStreams.Create();
        var maskChannel = maskStreams.CreateChannel<EventsSandbox.ManaGained>();
        for (var block = 0; block < rows; block += 64)
        {
            var take = Math.Min(64, rows - block);
            ulong enabled = 0;
            for (var i = 0; i < take; i++)
            {
                if (values[block + i] != 0)
                {
                    enabled |= 1ul << i;
                }
            }

            maskStreams.EmitFromNonZero(maskChannel, values, enabled, block, &EventsSandbox.MakeMana);
        }

        maskStreams.Swap();

        var scanned = Drain(scanStreams);
        var masked = Drain(maskStreams);
        Assert.Equal(masked.Count, scanned.Count);
        for (var i = 0; i < scanned.Count; i++)
        {
            Assert.Equal(masked[i].Item1, scanned[i].Item1);
            Assert.Equal(masked[i].Item2, scanned[i].Item2);
        }
    }

    static List<(int, int)> Drain(EventStreams streams)
    {
        var result = new List<(int, int)>();
        while (streams.TryReadNext(out _, out var payload, out _))
        {
            var evt = *(EventsSandbox.ManaGained*)payload;
            result.Add((evt.Slot, evt.Amount));
        }

        return result;
    }
}
