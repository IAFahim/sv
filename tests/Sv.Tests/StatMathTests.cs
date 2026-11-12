using Kernels.Testing;
using Sv;
using Xunit;

public unsafe class StatMathTests
{
    static ulong Next(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return state;
    }

    static float RandomValue(ref ulong state)
    {
        var roll = Next(ref state);
        switch ((int)(roll % 8))
        {
            case 0:
                return BitConverter.UInt32BitsToSingle((uint)(Next(ref state) >> 32));
            case 1:
                return (int)(Next(ref state) % 2001) - 1000;
            default:
                return ((long)(Next(ref state) % 2_000_001) - 1_000_000) / 997f;
        }
    }

    static float Oracle(float @base, StatEntry* entries, int count)
    {
        var value = @base;
        var additive = 1f;
        for (var i = 0; i < count; i++)
        {
            if (entries[i].Kind == ModifierKind.Added)
            {
                value = StrictFloat.Add(value, entries[i].Value);
            }
        }

        for (var i = 0; i < count; i++)
        {
            if (entries[i].Kind == ModifierKind.Additive)
            {
                additive = StrictFloat.Add(additive, entries[i].Value);
            }
        }

        value = StrictFloat.Multiply(value, additive);
        for (var i = 0; i < count; i++)
        {
            if (entries[i].Kind == ModifierKind.Multi)
            {
                value = StrictFloat.Multiply(value, StrictFloat.Add(1f, entries[i].Value));
            }
        }

        for (var i = 0; i < count; i++)
        {
            if (entries[i].Kind == ModifierKind.Cap)
            {
                value = value > entries[i].Value ? entries[i].Value : value;
            }
        }

        return value;
    }

    [Fact]
    public void Fold_EqualsStrictFloatOracleOnOneMillionRandomSets()
    {
        var kinds = new[] { ModifierKind.Added, ModifierKind.Additive, ModifierKind.Multi, ModifierKind.Cap };
        var entries = stackalloc StatEntry[12];
        var state = 0x5DEECE66Dul;
        var mismatches = 0;
        for (var set = 0; set < 1_000_000; set++)
        {
            var count = (int)(Next(ref state) % 13);
            for (var i = 0; i < count; i++)
            {
                entries[i] = new StatEntry(kinds[Next(ref state) % 4], RandomValue(ref state));
            }

            var @base = RandomValue(ref state);
            var got = Board.CanonicalNaN(StatMath.Fold(@base, entries, count));
            var want = Board.CanonicalNaN(Oracle(@base, entries, count));
            if (BitConverter.SingleToUInt32Bits(got) != BitConverter.SingleToUInt32Bits(want))
            {
                mismatches++;
            }
        }

        Assert.Equal(0, mismatches);
    }

    [Theory]
    [InlineData("Add")]
    [InlineData("Subtract")]
    [InlineData("Multiply")]
    [InlineData("Divide")]
    public void Primitives_RoundEveryIntermediateToSingleInIl(string name)
    {
        var il = typeof(StatMath).GetMethod(name)!.GetMethodBody()!.GetILAsByteArray()!;
        Assert.Contains((byte)0x6B, il);
    }

    [Fact]
    public void DerivedAndCurve_EqualStrictFloatOracleOnOneMillionRandomInputs()
    {
        var state = 0xC0FFEE1234ul;
        for (var i = 0; i < 1_000_000; i++)
        {
            var coefficient = RandomValue(ref state);
            var source = RandomValue(ref state);
            Assert.Equal(BitConverter.SingleToUInt32Bits(StrictFloat.Multiply(coefficient, source)), BitConverter.SingleToUInt32Bits(StatMath.Derived(coefficient, source)));
            var min = ((long)(Next(ref state) % 20001) - 10000) / 7f;
            var step = 1 + (long)(Next(ref state) % 1000) / 13f;
            var length = 2 + (int)(Next(ref state) % 64);
            var ceiling = StrictFloat.Add(min, StrictFloat.Multiply(length - 1, step));
            Assert.Equal(BitConverter.SingleToUInt32Bits(ceiling), BitConverter.SingleToUInt32Bits(StatMath.CurveCeiling(min, length, step)));
            var value = min + ((long)(Next(ref state) % 100001) - 1000) / 101f;
            var want = value <= min ? 0 : value >= ceiling ? length - 1 : (int)StrictFloat.Divide(StrictFloat.Subtract(value, min), step);
            Assert.Equal(want, StatMath.CurveBucket(value, min, ceiling, step, length));
        }
    }
}
