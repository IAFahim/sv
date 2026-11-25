namespace Sv.Laws.Tests;

public sealed class SealRunLaws
{
    public static IEnumerable<object[]> Cases() =>
        new[] { 1, 2, 3, 15, 16, 24 }.SelectMany(runs =>
            new[] { 0, 3, 20, 45, 70 }.SelectMany(residue =>
                new[] { 1ul, 29ul }.Select(seed => new object[] { runs, residue, seed })));

    static Sent Draw(Gen gen, int chunk, bool narrow)
    {
        var target = chunk * Slot.Rows + gen.Below(Slot.Rows);
        var source = gen.Chance(15) ? EffectKey.NoSource : gen.Below(narrow ? 3 : 200);
        var kind = (byte)gen.Below(narrow ? 1 : 3);
        var channel = (byte)gen.Below(narrow ? 2 : 4);
        var origin = narrow ? Origin.Direct : (Origin)gen.Below(4);
        var depth = narrow ? 0 : gen.Below(4);
        var amount = gen.Chance(4) ? Gen.EdgeInts[gen.Below(Gen.EdgeInts.Length)] : gen.Between(-5, 5);
        var hits = gen.Chance(3) ? int.MaxValue : gen.Between(1, 3);
        return new Sent(EffectKey.Pack(target, source, kind, channel, origin, depth), amount == 0 ? 1 : amount, hits);
    }

    static List<Sent> Chunk(Gen gen, int chunk, int runs, int residuePercent, out int residueCount)
    {
        var pieces = new List<List<Sent>>();
        var narrow = gen.Chance(50);
        for (var run = 0; run < runs; run++)
        {
            var length = gen.Between(80, 400);
            var items = new List<Sent>(length);
            for (var at = 0; at < length; at++)
            {
                items.Add(Draw(gen, chunk, narrow));
            }

            items.Sort((first, second) => first.Key.CompareTo(second.Key));
            pieces.Add(items);
        }

        var total = pieces.Sum(piece => piece.Count);
        residueCount = total * residuePercent / Math.Max(1, 100 - residuePercent);
        var residue = new List<Sent>(residueCount);
        for (var at = 0; at < residueCount; at++)
        {
            residue.Add(Draw(gen, chunk, narrow));
        }

        var output = new List<Sent>();
        var taken = 0;
        foreach (var piece in pieces)
        {
            var slice = residueCount == 0 ? 0 : gen.Below(residueCount - taken + 1);
            output.AddRange(residue.Skip(taken).Take(slice));
            taken += slice;
            output.AddRange(piece);
        }

        output.AddRange(residue.Skip(taken));
        return output;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Seal_of_chunks_made_of_long_key_sorted_runs_and_a_residue_equals_the_reference(int runs, int residuePercent, ulong seed)
    {
        var gen = new Gen(seed * 1009ul + (ulong)(runs * 131 + residuePercent));
        var live = new ulong[SealHarness.Chunks];
        for (var chunk = 0; chunk < live.Length; chunk++)
        {
            live[chunk] = ulong.MaxValue;
        }

        var records = new List<Sent>();
        for (var chunk = 0; chunk < SealHarness.Chunks; chunk += 3)
        {
            records.AddRange(Chunk(gen, chunk, runs, residuePercent, out _));
        }

        var expected = SealHarness.Reference(records, live);
        foreach (var parts in new[] { 1, 4 })
        {
            var actual = SealHarness.SealOnce(gen, records, live, 1, parts, parts > 1);
            Assert.True(expected.SequenceEqual(actual.Records), $"{gen} runs {runs} residue {residuePercent} parts {parts}: {SealHarness.Describe(expected, actual.Records)}");
            for (var chunk = 0; chunk <= SealHarness.Chunks; chunk++)
            {
                Assert.Equal(expected.Count(record => EffectKey.Chunk(record.Key) < chunk), actual.Start[chunk]);
            }
        }
    }

    [Fact]
    public void Seal_of_one_sorted_run_with_a_small_unsorted_tail_sums_hits_across_both()
    {
        var live = new ulong[SealHarness.Chunks];
        live[2] = ulong.MaxValue;
        var records = new List<Sent>();
        for (var row = 0; row < Slot.Rows; row++)
        {
            for (var amount = 1; amount <= 3; amount++)
            {
                records.Add(new Sent(EffectKey.Pack(2 * Slot.Rows + row, 7, 0, 1, Origin.Direct, 0), amount, int.MaxValue));
            }
        }

        for (var row = Slot.Rows - 1; row >= 0; row -= 5)
        {
            records.Add(new Sent(EffectKey.Pack(2 * Slot.Rows + row, 7, 0, 1, Origin.Direct, 0), 2, 1));
            records.Add(new Sent(EffectKey.Pack(2 * Slot.Rows + row, 3, 0, 1, Origin.Direct, 0), -4, 2));
        }

        var expected = SealHarness.Reference(records, live);
        var actual = SealHarness.SealOnce(new Gen(5), records, live, 1, 1, false);
        Assert.True(expected.SequenceEqual(actual.Records), SealHarness.Describe(expected, actual.Records));
    }
}
