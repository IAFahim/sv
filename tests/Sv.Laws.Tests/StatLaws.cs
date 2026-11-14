using System.Numerics;
using System.Runtime.Intrinsics;
using Sv.Rules.Reference;

namespace Sv.Laws.Tests;

public sealed unsafe class StatLaws
{
    static readonly int[] Scales = { 1, 10, 100, 1000, 10000, 100000, 1000000 };

    static int Clamp32(BigInteger value) => value > int.MaxValue ? int.MaxValue : value < int.MinValue ? int.MinValue : (int)value;

    static int ContractFold(int start, IEnumerable<ShapeTerm> terms)
    {
        var ordered = terms.OrderBy(term => term.Kind).ThenBy(term => term.Value).ToList();
        var value = Clamp32((BigInteger)start + ordered.Where(term => term.Kind == ShapeKind.Added).Aggregate(BigInteger.Zero, (sum, term) => sum + term.Value));
        var factor = Clamp32(10000 + ordered.Where(term => term.Kind == ShapeKind.Additive).Aggregate(BigInteger.Zero, (sum, term) => sum + term.Value));
        value = Clamp32(BigInteger.Divide((BigInteger)value * factor, 10000));
        foreach (var multi in ordered.Where(term => term.Kind == ShapeKind.Multi))
        {
            value = Clamp32(BigInteger.Divide((BigInteger)value * Clamp32((BigInteger)10000 + multi.Value), 10000));
        }

        foreach (var cap in ordered.Where(term => term.Kind == ShapeKind.Cap))
        {
            value = Math.Min(value, cap.Value);
        }

        return value;
    }

    static ShapeTerm[] RandomTerms(Gen gen)
    {
        var terms = new ShapeTerm[gen.Below(9)];
        for (var at = 0; at < terms.Length; at++)
        {
            terms[at] = new ShapeTerm((ShapeKind)gen.Below(4), gen.EdgeOrAny());
        }

        return terms;
    }

    static int Fold(int start, ShapeTerm[] terms)
    {
        var copy = (ShapeTerm[])terms.Clone();
        fixed (ShapeTerm* pointer = copy)
        {
            StatLaw.Canonicalize(pointer, copy.Length);
            return StatLaw.Fold(start, pointer, copy.Length);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Many), MemberType = typeof(Seeds))]
    public void CanonicalizeThenFoldEqualsTheContractFoldOnEdgeValues(ulong seed)
    {
        var gen = new Gen(seed);
        for (var trial = 0; trial < 4000; trial++)
        {
            var start = gen.EdgeOrAny();
            var terms = RandomTerms(gen);
            Assert.True(ContractFold(start, terms) == Fold(start, terms), $"{gen} trial {trial}: start {start} terms {string.Join(" ", terms.Select(term => $"{term.Kind}:{term.Value}"))}");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void FoldEqualsTheReferenceInterpreterStatFold(ulong seed)
    {
        var gen = new Gen(seed);
        for (var trial = 0; trial < 4000; trial++)
        {
            var start = gen.EdgeOrAny();
            var terms = RandomTerms(gen);
            var entries = terms.Select(term => new ShapeEntry(term.Kind, term.Value)).ToList();
            Assert.True(ReferenceLaw.StatFold(start, entries) == Fold(start, terms), $"{gen} trial {trial}");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void FoldOfCanonicalTermsDoesNotDependOnTheOrderTheyArrived(ulong seed)
    {
        var gen = new Gen(seed);
        for (var trial = 0; trial < 2000; trial++)
        {
            var start = gen.EdgeOrAny();
            var terms = RandomTerms(gen);
            var expected = Fold(start, terms);
            gen.Shuffle(terms);
            Assert.True(expected == Fold(start, terms), $"{gen} trial {trial}");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void CanonicalizeSortsByKindThenValue(ulong seed)
    {
        var gen = new Gen(seed);
        for (var trial = 0; trial < 2000; trial++)
        {
            var terms = RandomTerms(gen);
            fixed (ShapeTerm* pointer = terms)
            {
                StatLaw.Canonicalize(pointer, terms.Length);
            }

            for (var at = 1; at < terms.Length; at++)
            {
                Assert.False(StatLaw.Precedes(terms[at], terms[at - 1]), $"{gen} trial {trial}");
            }
        }
    }

    [Fact]
    public void NoTermsGiveTheStartValue()
    {
        foreach (var start in Gen.EdgeInts)
        {
            Assert.Equal(start, StatLaw.Fold(start, null, 0));
        }
    }

    [Fact]
    public void MultiTermsTruncateStepByStepInAscendingOrder()
    {
        var terms = new[] { new ShapeTerm(ShapeKind.Multi, 5000), new ShapeTerm(ShapeKind.Multi, -5000) };
        Assert.Equal(1, Fold(3, terms));
        fixed (ShapeTerm* arrival = terms)
        {
            Assert.Equal(2, StatLaw.Fold(3, arrival, 2));
        }
    }

    [Fact]
    public void DivisionTruncatesTowardZero()
    {
        Assert.Equal(7, StatLaw.Percent(15, 5000));
        Assert.Equal(-7, StatLaw.Percent(-15, 5000));
        Assert.Equal(-1, StatLaw.Multi(-3, -5000));
        Assert.Equal(0, StatLaw.Percent(-9999, 1));
        Assert.Equal(3, StatLaw.Rescale(39, 10, 1));
        Assert.Equal(-3, StatLaw.Rescale(-39, 10, 1));
    }

    [Fact]
    public void BasisPointsTreatTenThousandAsOneHundredPercent()
    {
        Assert.Equal(StatLaw.Whole, 10000);
        Assert.Equal(20000, StatLaw.Factor(10000));
        Assert.Equal(200, StatLaw.Multi(100, 10000));
        Assert.Equal(0, StatLaw.Multi(100, -10000));
        Assert.Equal(-100, StatLaw.Multi(100, -20000));
        Assert.Equal(125, Fold(100, new[] { new ShapeTerm(ShapeKind.Additive, 2500) }));
        Assert.Equal(150, Fold(100, new[] { new ShapeTerm(ShapeKind.Additive, 2500), new ShapeTerm(ShapeKind.Additive, 2500) }));
    }

    [Fact]
    public void EveryStepSaturatesToInt32()
    {
        Assert.Equal(int.MaxValue, StatLaw.Added(int.MaxValue, 1));
        Assert.Equal(int.MinValue, StatLaw.Added(int.MinValue, -1));
        Assert.Equal(int.MaxValue, StatLaw.Added(0, long.MaxValue / 2));
        Assert.Equal(int.MaxValue, StatLaw.Factor(int.MaxValue));
        Assert.Equal(int.MaxValue, StatLaw.Percent(int.MaxValue, int.MaxValue));
        Assert.Equal(int.MinValue, StatLaw.Percent(int.MinValue, int.MaxValue));
        Assert.Equal(int.MaxValue, StatLaw.Multi(int.MaxValue, int.MaxValue));
        Assert.Equal(int.MinValue, StatLaw.Percent(int.MaxValue, int.MinValue));
        var addedTerms = Enumerable.Repeat(new ShapeTerm(ShapeKind.Added, int.MaxValue), 4).ToArray();
        Assert.Equal(int.MaxValue, Fold(0, addedTerms));
    }

    [Fact]
    public void StepsRunInTheContractOrderAddedAdditiveMultiCap()
    {
        var terms = new[]
        {
            new ShapeTerm(ShapeKind.Cap, 500), new ShapeTerm(ShapeKind.Multi, 10000), new ShapeTerm(ShapeKind.Additive, 5000), new ShapeTerm(ShapeKind.Added, 100),
        };
        Assert.Equal(500, Fold(100, terms));
        terms[0] = new ShapeTerm(ShapeKind.Cap, 1000);
        Assert.Equal(600, Fold(100, terms));
    }

    [Fact]
    public void OnlyPowersOfTenFromOneToOneMillionAreScales()
    {
        foreach (var scale in Scales)
        {
            Assert.True(StatLaw.IsScale(scale));
        }

        foreach (var scale in new[] { int.MinValue, -10, -1, 0, 2, 5, 11, 99, 1001, 10000000, int.MaxValue })
        {
            Assert.False(StatLaw.IsScale(scale), $"scale {scale}");
            Assert.Equal(1, StatLaw.ScaleOrOne(scale));
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void RescaleMultipliesWithSaturationOrDividesTruncatingBetweenEveryPairOfScales(ulong seed)
    {
        var gen = new Gen(seed);
        foreach (var from in Scales)
        {
            foreach (var to in Scales)
            {
                for (var trial = 0; trial < 300; trial++)
                {
                    var value = gen.EdgeOrAny();
                    var expected = from >= to ? Clamp32(BigInteger.Divide(value, from / to)) : Clamp32((BigInteger)value * (to / from));
                    Assert.True(expected == StatLaw.Rescale(value, from, to), $"{gen}: {value} from {from} to {to}");
                    Assert.Equal(expected, ReferenceLaw.Rescale(value, from, to));
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void DerivedEqualsTheContractFormulaWithOneTruncation(ulong seed)
    {
        var gen = new Gen(seed);
        foreach (var sourceScale in Scales)
        {
            foreach (var targetScale in Scales)
            {
                for (var trial = 0; trial < 200; trial++)
                {
                    var source = gen.EdgeOrAny();
                    var coefficient = gen.EdgeOrAny();
                    var expected = Clamp32(BigInteger.Divide((BigInteger)source * coefficient * targetScale, (BigInteger)10000 * sourceScale));
                    Assert.True(expected == StatLaw.Derived(source, coefficient, sourceScale, targetScale), $"{gen}: source {source} coefficient {coefficient} scales {sourceScale}->{targetScale}");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void ScaleByMultipliesThenTruncatesOnce(ulong seed)
    {
        var gen = new Gen(seed);
        foreach (var scale in Scales)
        {
            for (var trial = 0; trial < 500; trial++)
            {
                var amount = gen.EdgeOrAny();
                var value = gen.EdgeOrAny();
                var expected = Clamp32(BigInteger.Divide((BigInteger)amount * value, scale));
                Assert.True(expected == StatLaw.ScaleBy(amount, value, scale), $"{gen}: {amount} x {value} / {scale}");
                Assert.Equal(expected, ReferenceLaw.ScaleBy(amount, value, scale));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void CurveBucketClampsTheStepIndexIntoTheTable(ulong seed)
    {
        var gen = new Gen(seed);
        for (var trial = 0; trial < 5000; trial++)
        {
            var value = gen.EdgeOrAny();
            var min = gen.EdgeOrAny();
            var step = gen.Chance(10) ? gen.Between(-2, 0) : gen.Chance(20) ? int.MaxValue : gen.Between(1, 1000);
            var length = gen.Chance(10) ? 0 : gen.Between(1, 64);
            var expected = length < 1 || step < 1 || value <= min ? 0 : (int)BigInteger.Min(BigInteger.Divide((BigInteger)value - min, step), length - 1);
            Assert.True(expected == StatLaw.CurveBucket(value, min, step, length), $"{gen}: {value} min {min} step {step} length {length}");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void BelowComparesTheExactRatio(ulong seed)
    {
        var gen = new Gen(seed);
        for (var trial = 0; trial < 5000; trial++)
        {
            int value = gen.EdgeOrAny(), whole = gen.EdgeOrAny(), numerator = gen.EdgeOrAny(), denominator = gen.EdgeOrAny();
            var expected = (BigInteger)value * denominator < (BigInteger)whole * numerator;
            Assert.True(expected == StatLaw.Below(value, whole, numerator, denominator), $"{gen}");
            Assert.Equal(expected, ReferenceLaw.Below(value, whole, numerator, denominator));
        }
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

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void RescaleLaneEqualsTheScalarLawOnEveryRow(ulong seed)
    {
        var gen = new Gen(seed);
        var output = new int[Lanes.Rows];
        foreach (var from in Scales.Append(7))
        {
            foreach (var to in Scales.Append(0))
            {
                for (var trial = 0; trial < 20; trial++)
                {
                    var input = Lane(gen);
                    fixed (int* value = input)
                    fixed (int* result = output)
                    {
                        StatLaw.Rescale(value, from, to, result);
                    }

                    for (var row = 0; row < Lanes.Rows; row++)
                    {
                        Assert.True(StatLaw.Rescale(input[row], from, to) == output[row], $"{gen} vector {Vector256.IsHardwareAccelerated}: {input[row]} {from}->{to}");
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void CapLaneEqualsTheScalarLawOnEveryRow(ulong seed)
    {
        var gen = new Gen(seed);
        var output = new int[Lanes.Rows];
        for (var trial = 0; trial < 200; trial++)
        {
            var input = Lane(gen);
            var cap = Lane(gen);
            fixed (int* value = input)
            fixed (int* limit = cap)
            fixed (int* result = output)
            {
                StatLaw.Cap(value, limit, result);
            }

            for (var row = 0; row < Lanes.Rows; row++)
            {
                Assert.Equal(StatLaw.Cap(input[row], cap[row]), output[row]);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void PercentAndBelowLanesEqualTheScalarLawOnEveryRow(ulong seed)
    {
        var gen = new Gen(seed);
        var output = new int[Lanes.Rows];
        for (var trial = 0; trial < 200; trial++)
        {
            var input = Lane(gen);
            var whole = Lane(gen);
            var factor = gen.EdgeOrAny();
            int numerator = gen.EdgeOrAny(), denominator = gen.EdgeOrAny();
            ulong below;
            fixed (int* value = input)
            fixed (int* wholeLane = whole)
            fixed (int* result = output)
            {
                StatLaw.Percent(value, factor, result);
                below = StatLaw.Below(value, wholeLane, numerator, denominator);
            }

            for (var row = 0; row < Lanes.Rows; row++)
            {
                Assert.Equal(StatLaw.Percent(input[row], factor), output[row]);
                Assert.Equal(StatLaw.Below(input[row], whole[row], numerator, denominator), ((below >> row) & 1ul) != 0ul);
            }
        }
    }
}
