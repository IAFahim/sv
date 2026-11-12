using System.Numerics;
using System.Runtime.Intrinsics;
using Sv.Host.Net;
using Sv.Rules.Reference;

namespace Sv.Laws.Tests;

public sealed unsafe class FoldLaws
{
    static readonly int[] Lattice = { int.MinValue, -1, 0, 1, int.MaxValue };

    static int ClampOnce(BigInteger moved, int floor, int ceiling)
    {
        var low = Math.Min(floor, ceiling);
        return moved < low ? low : moved > ceiling ? ceiling : (int)moved;
    }

    [Fact]
    public void StepClampsBeforePlusDeltaOnceOnTheA6Lattice()
    {
        foreach (var before in Lattice)
        {
            foreach (var delta in Lattice)
            {
                foreach (var floor in Lattice)
                {
                    foreach (var ceiling in Lattice)
                    {
                        var expected = ClampOnce((BigInteger)before + delta, floor, ceiling);
                        Assert.Equal(expected, FoldLaw.Step(before, delta, floor, ceiling));
                        Assert.Equal(expected, ReferenceLaw.Step(before, delta, floor, ceiling));
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Many), MemberType = typeof(Seeds))]
    public void StepEqualsOneClampOfTheExactSumOnRandomValues(ulong seed)
    {
        var gen = new Gen(seed);
        for (var trial = 0; trial < 5000; trial++)
        {
            int before = gen.EdgeOrAny(), delta = gen.EdgeOrAny(), floor = gen.EdgeOrAny(), ceiling = gen.EdgeOrAny();
            Assert.True(ClampOnce((BigInteger)before + delta, floor, ceiling) == FoldLaw.Step(before, delta, floor, ceiling), $"{gen}: {before} {delta} {floor} {ceiling}");
        }
    }

    static int FoldFrame(int before, IEnumerable<(int Amount, int Hits)> records, int floor, int ceiling)
    {
        var total = 0L;
        foreach (var (amount, hits) in records)
        {
            total += EffectLaw.Term(amount, hits);
        }

        return FoldLaw.Step(before, EffectLaw.StoredDelta(total), floor, ceiling);
    }

    [Fact]
    public void AFrameSumsItsDeltasFirstAndClampsOnce()
    {
        var records = new[] { (50, 1), (-50, 1) };
        Assert.Equal(100, FoldFrame(100, records, 0, 100));
        Assert.Equal(100, FoldFrame(100, records.Reverse(), 0, 100));
        Assert.Equal(50, FoldLaw.Step(FoldLaw.Step(100, 50, 0, 100), -50, 0, 100));
        Assert.Equal(5, FoldFrame(10, new[] { (-30, 1), (25, 1) }, 0, 100));
        Assert.Equal(25, FoldLaw.Step(FoldLaw.Step(10, -30, 0, 100), 25, 0, 100));
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void AFrameFoldDoesNotDependOnTheOrderOfItsRecords(ulong seed)
    {
        var gen = new Gen(seed);
        for (var trial = 0; trial < 2000; trial++)
        {
            var records = Enumerable.Range(0, gen.Below(12)).Select(_ => (gen.EdgeOrAny(), gen.Chance(20) ? int.MaxValue : gen.Between(1, 5))).ToList();
            int before = gen.EdgeOrAny(), floor = gen.EdgeOrAny(), ceiling = gen.EdgeOrAny();
            var expected = FoldFrame(before, records, floor, ceiling);
            gen.Shuffle(records);
            Assert.True(expected == FoldFrame(before, records, floor, ceiling), $"{gen} trial {trial}");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void AFrameFoldEqualsOneClampOfTheSaturatedTotal(ulong seed)
    {
        var gen = new Gen(seed);
        for (var trial = 0; trial < 2000; trial++)
        {
            var records = Enumerable.Range(0, gen.Below(12)).Select(_ => (gen.Between(-1000000, 1000000), gen.Between(1, 1000))).ToList();
            int before = gen.EdgeOrAny(), floor = gen.EdgeOrAny(), ceiling = gen.EdgeOrAny();
            var exact = records.Aggregate(BigInteger.Zero, (sum, record) => sum + (BigInteger)record.Item1 * record.Item2);
            var delta = exact > int.MaxValue ? int.MaxValue : exact < int.MinValue ? int.MinValue : (int)exact;
            Assert.True(ClampOnce((BigInteger)before + delta, floor, ceiling) == FoldFrame(before, records, floor, ceiling), $"{gen} trial {trial}");
        }
    }

    [Fact]
    public void RecordTermSaturatesAtTwoToTheThirtyTwo()
    {
        Assert.Equal(1L << 32, EffectLaw.Term(int.MaxValue, int.MaxValue));
        Assert.Equal(-(1L << 32), EffectLaw.Term(int.MinValue, int.MaxValue));
        Assert.Equal(-6L, EffectLaw.Term(-3, 2));
        Assert.Equal(ReferenceLaw.Term(int.MinValue, 3), EffectLaw.Term(int.MinValue, 3));
    }

    static int[] Lane(Gen gen)
    {
        var lane = new int[Lanes.Rows];
        for (var row = 0; row < lane.Length; row++)
        {
            lane[row] = gen.EdgeOrAny();
        }

        return lane;
    }

    public static IEnumerable<object[]> WidthsAndSeeds() =>
        from width in new[] { Width.Vector128, Width.Vector256 }
        from seed in new[] { 1ul, 2ul, 3ul, 17ul }
        select new object[] { width, seed };

    [Theory]
    [MemberData(nameof(WidthsAndSeeds))]
    public void CommitFoldVectorWidthEqualsTheScalarFoldOnSelectedRowsAndLeavesTheRestAlone(Width width, ulong seed)
    {
        if (!(width == Width.Vector128 ? Vector128.IsHardwareAccelerated : Vector256.IsHardwareAccelerated))
        {
            Assert.True(Widths.Widest() < width, $"{width} is not accelerated but the host would choose it");
            return;
        }

        var gen = new Gen(seed);
        for (var trial = 0; trial < 500; trial++)
        {
            var value = Lane(gen);
            var delta = Lane(gen);
            var floor = Lane(gen);
            var ceiling = Lane(gen);
            var rows = gen.Chance(10) ? ulong.MaxValue : gen.Chance(10) ? 0ul : gen.Next() & gen.Next();
            var scalar = (int[])value.Clone();
            var vector = (int[])value.Clone();
            ulong scalarChanged, vectorChanged;
            fixed (int* s = scalar)
            fixed (int* v = vector)
            fixed (int* d = delta)
            fixed (int* f = floor)
            fixed (int* c = ceiling)
            {
                scalarChanged = CommitFold.Scalar(s, d, f, c, rows);
                vectorChanged = width == Width.Vector256 ? CommitFold.Lane256(v, d, f, c, rows) : CommitFold.Lane128(v, d, f, c, rows);
            }

            Assert.True(scalar.SequenceEqual(vector), $"{gen} {width} trial {trial}");
            Assert.Equal(scalarChanged, vectorChanged);
            for (var row = 0; row < Lanes.Rows; row++)
            {
                var selected = ((rows >> row) & 1ul) != 0ul;
                var expected = selected ? FoldLaw.Step(value[row], delta[row], floor[row], ceiling[row]) : value[row];
                Assert.Equal(expected, scalar[row]);
                Assert.Equal(expected != value[row], ((scalarChanged >> row) & 1ul) != 0ul);
            }
        }
    }

    [Fact]
    public void LevelEntersOnEnterAndStaysOnStay()
    {
        Assert.False(ReferenceLaw.NextLevel(false, false, false));
        Assert.False(ReferenceLaw.NextLevel(false, false, true));
        Assert.True(ReferenceLaw.NextLevel(false, true, false));
        Assert.True(ReferenceLaw.NextLevel(false, true, true));
        Assert.False(ReferenceLaw.NextLevel(true, false, false));
        Assert.True(ReferenceLaw.NextLevel(true, false, true));
        Assert.False(ReferenceLaw.NextLevel(true, true, false));
        Assert.True(ReferenceLaw.NextLevel(true, true, true));
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void HysteresisBandHoldsTheLevelBetweenEnterAndStayThresholds(ulong seed)
    {
        var gen = new Gen(seed);
        const int max = 1000;
        var level = false;
        var edges = 0;
        for (var frame = 0; frame < 5000; frame++)
        {
            var health = gen.Chance(5) ? gen.Between(0, max) : gen.Between(300, 349);
            var enter = ReferenceLaw.Below(health, max, 30, 100);
            var stay = ReferenceLaw.Below(health, max, 35, 100);
            var next = ReferenceLaw.NextLevel(level, enter, stay);
            if (health < 300)
            {
                Assert.True(next, $"{gen} frame {frame}: {health} must be low");
            }
            else if (health >= 350)
            {
                Assert.False(next, $"{gen} frame {frame}: {health} must not be low");
            }
            else
            {
                Assert.True(next == level, $"{gen} frame {frame}: {health} inside the band must hold the level");
            }

            edges += next != level ? 1 : 0;
            level = next;
        }

        Assert.True(edges > 0);
    }

    [Fact]
    public void BelowSaturatesItsProductsInsteadOfWrapping()
    {
        Assert.True(ReferenceLaw.Below(long.MinValue, 1, 1, 2));
        Assert.False(ReferenceLaw.Below(long.MaxValue, long.MaxValue, 1, 2));
        Assert.True(ReferenceLaw.Below(-1, long.MaxValue, long.MaxValue, 1));
        Assert.Equal(long.MaxValue, ReferenceLaw.Multiply(long.MaxValue, 2));
        Assert.Equal(long.MinValue, ReferenceLaw.Multiply(long.MinValue, 2));
        Assert.Equal(long.MaxValue, ReferenceLaw.Multiply(long.MinValue, -1));
    }
}
