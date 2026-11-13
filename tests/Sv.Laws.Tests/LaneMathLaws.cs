using Sv.Rules.Reference;

namespace Sv.Laws.Tests;

public sealed unsafe class LaneMathLaws
{
    static readonly float[] EdgeFloats =
    {
        float.NaN, -float.NaN, BitConverter.Int32BitsToSingle(0x7FC00001), float.PositiveInfinity, float.NegativeInfinity, 0f, -0f, 0.5f, -0.5f, 0.99999994f,
        -0.99999994f, 1f, -1f, 2147483520f, -2147483520f, 2147483648f, -2147483648f, 2147483904f, -2147483904f, float.MaxValue, float.MinValue, float.Epsilon,
        -float.Epsilon, 16777217f, 1e10f, -1e10f,
    };

    static int[] IntLane(Gen gen)
    {
        var lane = new int[Lanes.Rows];
        for (var row = 0; row < lane.Length; row++)
        {
            lane[row] = gen.EdgeOrAny();
        }

        return lane;
    }

    static float[] FloatLane(Gen gen)
    {
        var lane = new float[Lanes.Rows];
        for (var row = 0; row < lane.Length; row++)
        {
            lane[row] = gen.Chance(40) ? EdgeFloats[gen.Below(EdgeFloats.Length)] : gen.Chance(50) ? BitConverter.Int32BitsToSingle(gen.AnyInt()) : gen.Between(-100000, 100000) / 7f;
        }

        return lane;
    }

    static ulong NaiveMask(Func<int, bool> rowIsSet)
    {
        var mask = 0ul;
        for (var row = 0; row < Lanes.Rows; row++)
        {
            mask |= rowIsSet(row) ? 1ul << row : 0ul;
        }

        return mask;
    }

    [Fact]
    public void AddSaturateEqualsTheSumSaturatedTo32BitsOnTheEdgeLattice()
    {
        foreach (var first in Gen.EdgeInts)
        {
            foreach (var second in Gen.EdgeInts)
            {
                Assert.Equal(EffectLaw.Saturate32((long)first + second), LaneMath.AddSaturate(first, second));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void AddSaturateLaneEqualsTheScalarOnEveryRow(ulong seed)
    {
        var gen = new Gen(seed);
        var output = new int[Lanes.Rows];
        for (var trial = 0; trial < 300; trial++)
        {
            var first = IntLane(gen);
            var second = IntLane(gen);
            fixed (int* a = first)
            fixed (int* b = second)
            fixed (int* result = output)
            {
                LaneMath.AddSaturate(a, b, result);
            }

            for (var row = 0; row < Lanes.Rows; row++)
            {
                Assert.True(EffectLaw.Saturate32((long)first[row] + second[row]) == output[row], $"{gen}: {first[row]} + {second[row]}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void AddLaneWrapsLikeTheScalarSum(ulong seed)
    {
        var gen = new Gen(seed);
        var output = new int[Lanes.Rows];
        for (var trial = 0; trial < 100; trial++)
        {
            var first = IntLane(gen);
            var second = IntLane(gen);
            fixed (int* a = first)
            fixed (int* b = second)
            fixed (int* result = output)
            {
                LaneMath.Add(a, b, result);
            }

            for (var row = 0; row < Lanes.Rows; row++)
            {
                Assert.Equal(unchecked(first[row] + second[row]), output[row]);
            }
        }
    }

    [Fact]
    public void PackEightTurnsEveryPatternOfEightFlagBytesIntoItsBits()
    {
        for (var pattern = 0; pattern < 256; pattern++)
        {
            var bytes = 0ul;
            for (var bit = 0; bit < 8; bit++)
            {
                bytes |= (ulong)((pattern >> bit) & 1) << (bit * 8);
            }

            Assert.Equal((ulong)pattern, Mask.PackEight(bytes));
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void PackTurnsSixtyFourFlagBytesIntoTheRowMask(ulong seed)
    {
        var gen = new Gen(seed);
        var flags = new byte[Lanes.Rows];
        for (var trial = 0; trial < 500; trial++)
        {
            for (var row = 0; row < flags.Length; row++)
            {
                flags[row] = gen.Chance(50) ? (byte)1 : (byte)0;
            }

            fixed (byte* pointer = flags)
            {
                Assert.Equal(NaiveMask(row => flags[row] != 0), Mask.Pack(pointer));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void IntegerCompareMasksEqualTheirScalarRowTests(ulong seed)
    {
        var gen = new Gen(seed);
        for (var trial = 0; trial < 300; trial++)
        {
            var first = IntLane(gen);
            var second = gen.Chance(30) ? (int[])first.Clone() : IntLane(gen);
            second[gen.Below(Lanes.Rows)] = first[gen.Below(Lanes.Rows)];
            var probe = first[gen.Below(Lanes.Rows)];
            fixed (int* a = first)
            fixed (int* b = second)
            {
                Assert.Equal(NaiveMask(row => first[row] > 0), Mask.Positive(a));
                Assert.Equal(NaiveMask(row => first[row] != 0), Mask.NonZero(a));
                Assert.Equal(NaiveMask(row => first[row] == probe), Mask.EqualTo(a, probe));
                Assert.Equal(NaiveMask(row => first[row] != second[row]), Mask.Differ(a, b));
                Assert.Equal(NaiveMask(row => first[row] < second[row]), Mask.Less(a, b));
                Assert.Equal(NaiveMask(row => first[row] > second[row]), Mask.Greater(a, b));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void FloatCompareMasksEqualTheirScalarRowTestsIncludingNaN(ulong seed)
    {
        var gen = new Gen(seed);
        for (var trial = 0; trial < 300; trial++)
        {
            var first = FloatLane(gen);
            var second = FloatLane(gen);
            fixed (float* a = first)
            fixed (float* b = second)
            {
                Assert.Equal(NaiveMask(row => first[row] < second[row]), Mask.LessFloat(a, b));
                Assert.Equal(NaiveMask(row => first[row] > second[row]), Mask.GreaterFloat(a, b));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void SelectTakesTheFirstLaneWhereTheMaskIsSetAndTheSecondElsewhere(ulong seed)
    {
        var gen = new Gen(seed);
        var output = new int[Lanes.Rows];
        for (var trial = 0; trial < 300; trial++)
        {
            var whenSet = IntLane(gen);
            var whenClear = IntLane(gen);
            var mask = gen.Next() & gen.Next();
            fixed (int* set = whenSet)
            fixed (int* clear = whenClear)
            fixed (int* result = output)
            {
                LaneMath.Select(mask, set, clear, result);
            }

            for (var row = 0; row < Lanes.Rows; row++)
            {
                Assert.Equal(((mask >> row) & 1ul) != 0ul ? whenSet[row] : whenClear[row], output[row]);
            }
        }
    }

    [Fact]
    public void ClampUsesTheLowerOfTheBoundsAsFloorWhenTheBoundsCross()
    {
        foreach (var value in Gen.EdgeInts)
        {
            foreach (var floor in Gen.EdgeInts)
            {
                foreach (var ceiling in Gen.EdgeInts)
                {
                    var low = Math.Min(floor, ceiling);
                    Assert.Equal(Math.Min(Math.Max(value, low), ceiling), LaneMath.Clamp(value, floor, ceiling));
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void ClampLaneEqualsTheScalarOnEveryRow(ulong seed)
    {
        var gen = new Gen(seed);
        var output = new int[Lanes.Rows];
        for (var trial = 0; trial < 300; trial++)
        {
            var value = IntLane(gen);
            var floor = IntLane(gen);
            var ceiling = IntLane(gen);
            fixed (int* v = value)
            fixed (int* f = floor)
            fixed (int* c = ceiling)
            fixed (int* result = output)
            {
                LaneMath.Clamp(v, f, c, result);
            }

            for (var row = 0; row < Lanes.Rows; row++)
            {
                Assert.True(LaneMath.Clamp(value[row], floor[row], ceiling[row]) == output[row], $"{gen}: clamp {value[row]} {floor[row]} {ceiling[row]}");
            }
        }
    }

    [Fact]
    public void BoundOfGivesZeroForNaNSaturatesOutOfRangeAndTruncatesTheRest()
    {
        foreach (var value in EdgeFloats)
        {
            var expected = float.IsNaN(value) ? 0 : value >= 2147483648f ? int.MaxValue : value < -2147483648f ? int.MinValue : (int)Math.Truncate((double)value);
            Assert.Equal(expected, LaneMath.BoundOf(value));
            Assert.Equal(ReferenceLaw.BoundOf(value), LaneMath.BoundOf(value));
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void BoundOfLaneEqualsTheScalarOnEveryRow(ulong seed)
    {
        var gen = new Gen(seed);
        var output = new int[Lanes.Rows];
        for (var trial = 0; trial < 500; trial++)
        {
            var value = FloatLane(gen);
            fixed (float* v = value)
            fixed (int* result = output)
            {
                LaneMath.BoundOf(v, result);
            }

            for (var row = 0; row < Lanes.Rows; row++)
            {
                Assert.True(LaneMath.BoundOf(value[row]) == output[row], $"{gen}: BoundOf bits {BitConverter.SingleToInt32Bits(value[row]):X8}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void ToFloatLaneEqualsTheScalarConversionOnEveryRow(ulong seed)
    {
        var gen = new Gen(seed);
        var output = new float[Lanes.Rows];
        for (var trial = 0; trial < 300; trial++)
        {
            var value = IntLane(gen);
            fixed (int* v = value)
            fixed (float* result = output)
            {
                LaneMath.ToFloat(v, result);
            }

            for (var row = 0; row < Lanes.Rows; row++)
            {
                Assert.Equal(BitConverter.SingleToInt32Bits(value[row]), BitConverter.SingleToInt32Bits(output[row]));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void FloatArithmeticLanesEqualOneScalarIeeeOperationPerRowBitForBit(ulong seed)
    {
        var gen = new Gen(seed);
        var sum = new float[Lanes.Rows];
        var difference = new float[Lanes.Rows];
        var product = new float[Lanes.Rows];
        for (var trial = 0; trial < 300; trial++)
        {
            var first = FloatLane(gen);
            var second = FloatLane(gen);
            fixed (float* a = first)
            fixed (float* b = second)
            fixed (float* s = sum)
            fixed (float* d = difference)
            fixed (float* p = product)
            {
                LaneMath.AddFloat(a, b, s);
                LaneMath.SubtractFloat(a, b, d);
                LaneMath.MultiplyFloat(a, b, p);
            }

            for (var row = 0; row < Lanes.Rows; row++)
            {
                Assert.True(BitConverter.SingleToInt32Bits(first[row] + second[row]) == BitConverter.SingleToInt32Bits(sum[row]), $"{gen}: add row {row}");
                Assert.True(BitConverter.SingleToInt32Bits(first[row] - second[row]) == BitConverter.SingleToInt32Bits(difference[row]), $"{gen}: subtract row {row}");
                Assert.True(BitConverter.SingleToInt32Bits(first[row] * second[row]) == BitConverter.SingleToInt32Bits(product[row]), $"{gen}: multiply row {row}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void RowLevelsReadAndWriteOneRowOfAllThirtyTwoEventWords(ulong seed)
    {
        var gen = new Gen(seed);
        var lane = new ulong[Lanes.Words];
        for (var word = 0; word < lane.Length; word++)
        {
            lane[word] = gen.Next();
        }

        fixed (ulong* words = lane)
        {
            for (var trial = 0; trial < 500; trial++)
            {
                var row = gen.Below(Lanes.Rows);
                var levels = (uint)gen.Next();
                var before = (ulong[])lane.Clone();
                LaneMath.SetRowLevels(words, row, levels);
                Assert.Equal(levels, LaneMath.RowLevels(words, row));
                for (var word = 0; word < Lanes.Words; word++)
                {
                    Assert.Equal(before[word] & ~(1ul << row), lane[word] & ~(1ul << row));
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void RowLevelsOverUsedWordsEqualRowLevelsOverAllThirtyTwoWords(ulong seed)
    {
        var gen = new Gen(seed);
        var lane = new ulong[Lanes.Words];
        fixed (ulong* words = lane)
        {
            for (var trial = 0; trial < 200; trial++)
            {
                for (var word = 0; word < lane.Length; word++)
                {
                    lane[word] = gen.Below(3) == 0 ? gen.Next() : 0ul;
                }

                var used = LaneMath.UsedWords(words);
                for (var word = 0; word < lane.Length; word++)
                {
                    Assert.Equal(lane[word] != 0ul, ((used >> word) & 1u) != 0u);
                }

                for (var row = 0; row < Lanes.Rows; row++)
                {
                    Assert.Equal(LaneMath.RowLevels(words, row), LaneMath.RowLevels(words, row, used));
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void BlendAndDifferWordsWorkPerRowBit(ulong seed)
    {
        var gen = new Gen(seed);
        var first = new ulong[Lanes.Words];
        var second = new ulong[Lanes.Words];
        var output = new ulong[Lanes.Words];
        for (var trial = 0; trial < 200; trial++)
        {
            for (var word = 0; word < Lanes.Words; word++)
            {
                first[word] = gen.Next();
                second[word] = gen.Chance(50) ? first[word] : gen.Next();
            }

            var mask = gen.Next();
            ulong differ;
            fixed (ulong* a = first)
            fixed (ulong* b = second)
            fixed (ulong* o = output)
            {
                LaneMath.BlendWords(mask, a, b, o);
                differ = LaneMath.DifferWords(a, b);
            }

            var expectedDiffer = 0ul;
            for (var word = 0; word < Lanes.Words; word++)
            {
                Assert.Equal((first[word] & mask) | (second[word] & ~mask), output[word]);
                expectedDiffer |= first[word] ^ second[word];
            }

            Assert.Equal(expectedDiffer, differ);
        }
    }

    [Fact]
    public void FillWritesTheValueIntoEveryRow()
    {
        var lane = new int[Lanes.Rows];
        var words = new ulong[Lanes.Words];
        fixed (int* pointer = lane)
        fixed (ulong* wordPointer = words)
        {
            foreach (var value in Gen.EdgeInts)
            {
                LaneMath.Fill(pointer, value);
                Assert.All(lane, cell => Assert.Equal(value, cell));
                LaneMath.FillWords(wordPointer, (ulong)value);
                Assert.All(words, word => Assert.Equal((ulong)value, word));
            }
        }
    }
}
