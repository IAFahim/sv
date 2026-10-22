using System.Runtime.CompilerServices;
using Sv;
using Xunit;

public unsafe class SaturatingAddTests
{
    static readonly int[] Lattice = { int.MinValue, -1, 0, 1, int.MaxValue };
    static readonly int[][] BoundsList = { new[] { int.MinValue, int.MaxValue }, new[] { -10, 10 }, new[] { 0, 100 } };
    const ulong PartialWord = 0x0037_FFFF_FFFF_FF5Aul;

    static Schema TestSchema() =>
        SchemaBuilder.Create()
            .Intrinsic("mana")
            .Intrinsic("charges")
            .Build();

    static int Expected(int before, int delta, int min, int max) =>
        (int)Math.Clamp((long)before + delta, min, max);

    static void FillLattice(int* value, int count, int offset = 0)
    {
        for (var i = 0; i < count; i++)
        {
            value[i] = Lattice[(i + offset) % Lattice.Length];
        }
    }

    [Fact]
    public void Scalar_UniformLattice_SaturatesBeforeClamp()
    {
        var value = stackalloc int[Lattice.Length];
        var applied = stackalloc int[Lattice.Length];
        foreach (var bounds in BoundsList)
        {
            var clamp = Clamp.Constant(bounds[0], bounds[1]);
            foreach (var deltaValue in Lattice)
            {
                FillLattice(value, Lattice.Length);
                Simd.ChunkScalar(value, null, deltaValue, clamp, 0, Lattice.Length, applied);
                for (var i = 0; i < Lattice.Length; i++)
                {
                    var before = Lattice[i];
                    var expected = Expected(before, deltaValue, bounds[0], bounds[1]);
                    Assert.True(value[i] == expected && applied[i] == expected - before,
                        $"{before} + {deltaValue} in [{bounds[0]}, {bounds[1]}]: value {value[i]} applied {applied[i]} expected {expected}");
                }
            }
        }
    }

    [Fact]
    public void Scalar_VaryingDeltaLattice_SaturatesBeforeClamp()
    {
        var value = stackalloc int[Lattice.Length];
        var delta = stackalloc int[Lattice.Length];
        var applied = stackalloc int[Lattice.Length];
        foreach (var bounds in BoundsList)
        {
            var clamp = Clamp.Constant(bounds[0], bounds[1]);
            FillLattice(value, Lattice.Length);
            FillLattice(delta, Lattice.Length, 1);
            Simd.ChunkScalar(value, delta, 0, clamp, 0, Lattice.Length, applied);
            for (var i = 0; i < Lattice.Length; i++)
            {
                var before = Lattice[i];
                var d = Lattice[(i + 1) % Lattice.Length];
                var expected = Expected(before, d, bounds[0], bounds[1]);
                Assert.True(value[i] == expected && applied[i] == expected - before,
                    $"{before} + {d} in [{bounds[0]}, {bounds[1]}]: value {value[i]} applied {applied[i]} expected {expected}");
            }
        }
    }

    [Fact]
    public void EnabledScalar_Lattice_SaturatesAndZeroesDisabled()
    {
        var value = stackalloc int[64];
        var applied = stackalloc int[64];
        foreach (var bounds in BoundsList)
        {
            var clamp = Clamp.Constant(bounds[0], bounds[1]);
            foreach (var deltaValue in Lattice)
            {
                FillLattice(value, 64);
                for (var i = 0; i < 64; i++)
                {
                    applied[i] = int.MinValue;
                }

                Simd.EnabledWordScalar(value, null, deltaValue, clamp, PartialWord, 0, applied);
                for (var i = 0; i < 64; i++)
                {
                    var before = Lattice[i % Lattice.Length];
                    if ((PartialWord & (1ul << i)) != 0)
                    {
                        var expected = Expected(before, deltaValue, bounds[0], bounds[1]);
                        Assert.True(value[i] == expected && applied[i] == expected - before,
                            $"slot {i} {before} + {deltaValue} in [{bounds[0]}, {bounds[1]}]: value {value[i]} applied {applied[i]} expected {expected}");
                    }
                    else
                    {
                        Assert.True(value[i] == before && applied[i] == 0,
                            $"slot {i} disabled: value {value[i]} applied {applied[i]}");
                    }
                }
            }
        }
    }

    [Fact]
    public void Chunk_VectorWidths_UniformLattice()
    {
        using var board = Board.Create(TestSchema(), 64);
        var value = stackalloc int[64];
        var applied = stackalloc int[64];
        foreach (var bounds in BoundsList)
        {
            var clamp = Clamp.Constant(bounds[0], bounds[1]);
            foreach (var deltaValue in Lattice)
            {
                FillLattice(value, 64);
                board.RunIntrinsicsChunk(value, deltaValue, clamp, 0, 64, applied);
                for (var i = 0; i < 64; i++)
                {
                    var before = Lattice[i % Lattice.Length];
                    var expected = Expected(before, deltaValue, bounds[0], bounds[1]);
                    Assert.True(value[i] == expected && applied[i] == expected - before,
                        $"slot {i} {before} + {deltaValue} in [{bounds[0]}, {bounds[1]}]: value {value[i]} applied {applied[i]} expected {expected}");
                }
            }
        }
    }

    [Fact]
    public void Chunk_VectorWidths_VaryingDeltaLattice()
    {
        using var board = Board.Create(TestSchema(), 64);
        var value = stackalloc int[64];
        var delta = stackalloc int[64];
        var applied = stackalloc int[64];
        foreach (var bounds in BoundsList)
        {
            var clamp = Clamp.Constant(bounds[0], bounds[1]);
            FillLattice(value, 64);
            FillLattice(delta, 64, 3);
            board.RunIntrinsicsChunk(value, delta, clamp, 0, 64, applied);
            for (var i = 0; i < 64; i++)
            {
                var before = Lattice[i % Lattice.Length];
                var d = Lattice[(i + 3) % Lattice.Length];
                var expected = Expected(before, d, bounds[0], bounds[1]);
                Assert.True(value[i] == expected && applied[i] == expected - before,
                    $"slot {i} {before} + {d} in [{bounds[0]}, {bounds[1]}]: value {value[i]} applied {applied[i]} expected {expected}");
            }
        }
    }

    [Fact]
    public void Enabled_FullWord_Lattice()
    {
        using var board = Board.Create(TestSchema(), 64);
        var value = stackalloc int[64];
        var applied = stackalloc int[64];
        foreach (var bounds in BoundsList)
        {
            var clamp = Clamp.Constant(bounds[0], bounds[1]);
            foreach (var deltaValue in Lattice)
            {
                FillLattice(value, 64);
                board.RunEnabledChunk(value, deltaValue, clamp, ulong.MaxValue, 0, applied);
                for (var i = 0; i < 64; i++)
                {
                    var before = Lattice[i % Lattice.Length];
                    var expected = Expected(before, deltaValue, bounds[0], bounds[1]);
                    Assert.True(value[i] == expected && applied[i] == expected - before,
                        $"slot {i} {before} + {deltaValue} in [{bounds[0]}, {bounds[1]}]: value {value[i]} applied {applied[i]} expected {expected}");
                }
            }
        }
    }

    [Fact]
    public void Enabled_PartialWord_MaskedLattice()
    {
        using var board = Board.Create(TestSchema(), 64);
        var value = stackalloc int[64];
        var applied = stackalloc int[64];
        foreach (var bounds in BoundsList)
        {
            var clamp = Clamp.Constant(bounds[0], bounds[1]);
            foreach (var deltaValue in Lattice)
            {
                FillLattice(value, 64);
                board.RunEnabledChunk(value, deltaValue, clamp, PartialWord, 0, applied);
                for (var i = 0; i < 64; i++)
                {
                    var before = Lattice[i % Lattice.Length];
                    if ((PartialWord & (1ul << i)) != 0)
                    {
                        var expected = Expected(before, deltaValue, bounds[0], bounds[1]);
                        Assert.True(value[i] == expected && applied[i] == expected - before,
                            $"slot {i} {before} + {deltaValue} in [{bounds[0]}, {bounds[1]}]: value {value[i]} applied {applied[i]} expected {expected}");
                    }
                    else
                    {
                        Assert.True(value[i] == before && applied[i] == 0,
                            $"slot {i} disabled: value {value[i]} applied {applied[i]}");
                    }
                }
            }
        }
    }

    [Fact]
    public void OverflowHeal_SaturatesAtClampMax_InsteadOfWrapDeath()
    {
        using var board = Board.Create(TestSchema(), 8);
        var value = stackalloc int[8];
        var applied = stackalloc int[8];
        value[0] = 2_000_000_000;

        board.RunIntrinsicsChunk(value, 2_000_000_000, Clamp.Constant(0, 100), 0, 1, applied);

        Assert.Equal(100, value[0]);
        Assert.Equal(-1_999_999_900, applied[0]);
    }

    [Fact]
    public void OverflowDamage_SaturatesAtClampMin_InsteadOfWrapHeal()
    {
        using var board = Board.Create(TestSchema(), 8);
        var value = stackalloc int[8];
        var applied = stackalloc int[8];
        value[0] = -2_000_000_000;

        board.RunIntrinsicsChunk(value, -2_000_000_000, Clamp.Constant(0, 100), 0, 1, applied);

        Assert.Equal(0, value[0]);
        Assert.Equal(2_000_000_000, applied[0]);
    }

    [Fact]
    public void OverflowHeal_EnabledWord_SaturatesAndZeroesSkipped()
    {
        using var board = Board.Create(TestSchema(), 64);
        var value = stackalloc int[64];
        var applied = stackalloc int[64];
        value[3] = 2_000_000_000;

        board.RunEnabledChunk(value, 2_000_000_000, Clamp.Constant(0, 100), 1ul << 3, 0, applied);

        Assert.Equal(100, value[3]);
        Assert.Equal(-1_999_999_900, applied[3]);
        Assert.Equal(0, applied[0]);
    }
}
