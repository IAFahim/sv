namespace Sv.Laws.Tests;

public sealed unsafe class InputLaws
{
    public static IEnumerable<object[]> ShapesAndSeeds() =>
        from shape in Enum.GetValues<SendShape>()
        from seed in new[] { 1ul, 2ul, 3ul }
        select new object[] { shape, seed };

    [Fact]
    public void IdenticalHitsMergeIntoOneRecordWithTheirHitsAdded()
    {
        var key = EffectKey.Pack(70, 3, 1, 2, Origin.Direct, 0);
        var sorted = new[] { new Effect(key, 5, 1), new Effect(key, 5, 2), new Effect(key, 5, 4), new Effect(key, 6, 1) };
        var canonical = new Effect[sorted.Length];
        fixed (Effect* input = sorted)
        fixed (Effect* output = canonical)
        {
            Assert.Equal(2, Seal.Merge(input, sorted.Length, output));
        }

        Assert.Equal(new Effect(key, 5, 7), canonical[0]);
        Assert.Equal(new Effect(key, 6, 1), canonical[1]);
    }

    [Fact]
    public void MergedHitsAreAddedIn64BitsAndSaturatedOnce()
    {
        var key = EffectKey.Pack(70, 3, 1, 2, Origin.Direct, 0);
        var sorted = new[] { new Effect(key, 5, int.MaxValue), new Effect(key, 5, int.MaxValue), new Effect(key, 5, 1) };
        var canonical = new Effect[sorted.Length];
        fixed (Effect* input = sorted)
        fixed (Effect* output = canonical)
        {
            Assert.Equal(1, Seal.Merge(input, sorted.Length, output));
        }

        Assert.Equal(int.MaxValue, canonical[0].Hits);
    }

    [Fact]
    public void RecordsThatDifferInAnyKeyFieldOrAmountDoNotMerge()
    {
        var gen = new Gen(9);
        var live = Enumerable.Repeat(ulong.MaxValue, SealHarness.Chunks).ToArray();
        var baseKey = EffectKey.Pack(70, 3, 1, 2, Origin.Direct, 1);
        var records = new List<Sent>
        {
            new(baseKey, 5, 1),
            new(baseKey, 6, 1),
            new(EffectKey.Pack(71, 3, 1, 2, Origin.Direct, 1), 5, 1),
            new(EffectKey.Pack(70, 4, 1, 2, Origin.Direct, 1), 5, 1),
            new(EffectKey.Pack(70, 3, 2, 2, Origin.Direct, 1), 5, 1),
            new(EffectKey.Pack(70, 3, 1, 3, Origin.Direct, 1), 5, 1),
            new(EffectKey.Pack(70, 3, 1, 2, Origin.Timeline, 1), 5, 1),
            new(EffectKey.Pack(70, 3, 1, 2, Origin.Direct, 2), 5, 1),
        };
        var sealedRecords = SealHarness.SealOnce(gen, records, live, 1, 1, false).Records;
        Assert.Equal(records.Count, sealedRecords.Length);
    }

    [Fact]
    public void KeyPacksAndUnpacksEveryFieldAtItsContractBits()
    {
        var gen = new Gen(11);
        for (var trial = 0; trial < 10000; trial++)
        {
            var fields = new EffectFields(gen.Below(Slot.Capacity), gen.Below(Slot.Capacity), (byte)gen.Below(256), (byte)gen.Below(256), (Origin)gen.Below(4), gen.Below(4), gen.AnyInt(), gen.AnyInt());
            var packed = EffectKey.Pack(fields);
            var key = packed.Key;
            Assert.Equal((ulong)(uint)fields.Target, key >> 42);
            Assert.Equal((ulong)(uint)fields.Source, (key >> 20) & 0x3FFFFFul);
            Assert.Equal(fields.Kind, (byte)(key >> 12));
            Assert.Equal(fields.Channel, (byte)(key >> 4));
            Assert.Equal((ulong)fields.Origin, (key >> 2) & 3ul);
            Assert.Equal((ulong)fields.Depth, key & 3ul);
            Assert.Equal(fields.Target >> 6, (int)(key >> 48));
            Assert.Equal(fields, EffectKey.Unpack(packed));
        }
    }

    [Fact]
    public void PackedOrderEqualsTheFieldOrderOfThePlainForm()
    {
        var gen = new Gen(12);
        for (var trial = 0; trial < 20000; trial++)
        {
            var first = RandomFields(gen);
            var second = gen.Chance(30) ? WithAmount(first, gen.Between(-3, 3)) : RandomFields(gen);
            Assert.Equal(Math.Sign(EffectLaw.Compare(first, second)), Math.Sign(EffectLaw.Compare(EffectKey.Pack(first), EffectKey.Pack(second))));
        }
    }

    static EffectFields RandomFields(Gen gen) =>
        new(gen.Below(130), gen.Chance(10) ? EffectKey.NoSource : gen.Below(5), (byte)gen.Below(3), (byte)gen.Below(3), (Origin)gen.Below(4), gen.Below(4), gen.Between(-3, 3), 1);

    static EffectFields WithAmount(EffectFields fields, int amount) =>
        new(fields.Target, fields.Source, fields.Kind, fields.Channel, fields.Origin, fields.Depth, amount, fields.Hits);

    [Theory]
    [MemberData(nameof(ShapesAndSeeds))]
    public void SealEqualsTheReferenceGroupAndSortOfTheMultiset(SendShape shape, ulong seed)
    {
        var gen = new Gen(seed);
        var live = SealHarness.LiveWords(gen);
        var records = SealHarness.Records(gen, shape, 6000);
        var expected = SealHarness.Reference(records, live);
        var actual = SealHarness.SealOnce(gen, records, live, 1, 1, false);
        Assert.True(expected.SequenceEqual(actual.Records), $"{gen} {shape}: {SealHarness.Describe(expected, actual.Records)}");
        for (var chunk = 0; chunk <= SealHarness.Chunks; chunk++)
        {
            Assert.Equal(expected.Count(record => EffectKey.Chunk(record.Key) < chunk), actual.Start[chunk]);
        }
    }

    [Theory]
    [MemberData(nameof(ShapesAndSeeds))]
    public void SealOutputDependsOnlyOnTheMultisetForAnyOrderWritersAndParts(SendShape shape, ulong seed)
    {
        var gen = new Gen(seed);
        var live = SealHarness.LiveWords(gen);
        var records = SealHarness.Records(gen, shape, 5000);
        var first = SealHarness.SealOnce(new Gen(seed), records, live, 1, 1, false);
        foreach (var writers in SealHarness.PartCounts)
        {
            foreach (var parts in SealHarness.PartCounts)
            {
                var shuffled = records.ToList();
                if (shape != SendShape.Presorted)
                {
                    gen.Shuffle(shuffled);
                }

                var other = SealHarness.SealOnce(gen, shuffled, live, writers, parts, true);
                Assert.True(first.Records.SequenceEqual(other.Records), $"{gen} {shape} writers {writers} parts {parts}: {SealHarness.Describe(first.Records, other.Records)}");
                Assert.Equal(first.Start, other.Start);
                Assert.Equal(first.DroppedDead, other.DroppedDead);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void SealIsIdempotent(ulong seed)
    {
        var gen = new Gen(seed);
        var live = SealHarness.LiveWords(gen);
        var once = SealHarness.SealOnce(gen, SealHarness.Records(gen, SendShape.Duplicates, 3000), live, 3, 4, false);
        var twice = SealHarness.SealOnce(gen, once.Records, live, 2, 7, false);
        Assert.Equal(once.Records, twice.Records);
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void SealOfAUnionEqualsSealOfTheUnionOfSeals(ulong seed)
    {
        var gen = new Gen(seed);
        var live = SealHarness.LiveWords(gen);
        var first = SealHarness.Records(gen, SendShape.Duplicates, 2000);
        var second = SealHarness.Records(gen, SendShape.Uniform, 2000);
        var whole = SealHarness.SealOnce(gen, first.Concat(second).ToList(), live, 4, 4, false);
        var parts = SealHarness.SealOnce(gen, first, live, 1, 1, false).Records.Concat(SealHarness.SealOnce(gen, second, live, 1, 1, false).Records).ToList();
        var ofSeals = SealHarness.SealOnce(gen, parts, live, 4, 4, false);
        Assert.Equal(whole.Records, ofSeals.Records);
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void RecordsForDeadOrFreeTargetsAreDroppedAndCounted(ulong seed)
    {
        var gen = new Gen(seed);
        var live = SealHarness.LiveWords(gen);
        var records = SealHarness.Records(gen, SendShape.Uniform, 4000);
        var expectedDead = records.Count(record => SealHarness.Stored(record) && !SealHarness.IsLive(live, record.Key));
        Assert.True(expectedDead > 0);
        Assert.Equal(expectedDead, SealHarness.SealOnce(gen, records, live, 3, 2, false).DroppedDead);
    }

    [Fact]
    public void CountCompactsTheLiveRecordsOfABlockInPlaceAndKeepsTheirOrder()
    {
        var gen = new Gen(21);
        var live = SealHarness.LiveWords(gen);
        var records = SealHarness.Records(gen, SendShape.Uniform, 200).Where(SealHarness.Stored).ToList();
        using var raw = new RawArena(256, 1, ArenaPolicy.Grow);
        using var canonical = new CanonicalArena(SealHarness.Chunks, 1, int.MaxValue, ArenaPolicy.Grow);
        var mark = raw.Mark();
        SealHarness.Send(raw, new List<List<Sent>> { records }, false);
        raw.EndLayer(mark);
        fixed (ulong* words = live)
        {
            var view = canonical.Begin(raw, new RouteTable(words, null, live.Length));
            var forward = default(NoForward);
            Seal.Count(view, ref forward, 0);
            var kept = records.Where(record => SealHarness.IsLive(live, record.Key)).ToList();
            Assert.Equal(kept.Count, raw.Core->Fill[0]);
            var block = ArenaCore.Block(raw.Core, 0);
            for (var at = 0; at < kept.Count; at++)
            {
                Assert.Equal(kept[at], new Sent(block[at].Key, block[at].Amount, block[at].Hits));
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(11)]
    public void FullArenaGrowsOnceAndTheRerunLayerSealsLikeTheReference(int writers)
    {
        var gen = new Gen(31);
        var live = SealHarness.LiveWords(gen);
        var early = SealHarness.Records(gen, SendShape.Uniform, 500);
        var flood = SealHarness.Records(gen, SendShape.Uniform, 200000);
        using var raw = new RawArena(1, writers, ArenaPolicy.Grow);
        var blocksBefore = raw.Blocks;
        var mark = raw.Mark();
        SealHarness.Send(raw, SealHarness.Distribute(gen, early, writers), false);
        Assert.Equal(LayerVerdict.Fits, raw.EndLayer(mark));

        mark = raw.Mark();
        SealHarness.Send(raw, SealHarness.Distribute(gen, flood, writers), true);
        Assert.Equal(LayerVerdict.Rerun, raw.EndLayer(mark));
        Assert.Equal(1, raw.Grows);
        Assert.True(raw.Blocks > blocksBefore);

        mark = raw.Mark();
        SealHarness.Send(raw, SealHarness.Distribute(gen, flood, writers), true);
        Assert.Equal(LayerVerdict.Fits, raw.EndLayer(mark));
        Assert.Equal(1, raw.Grows);
        Assert.Equal(0, raw.Missed);

        var expected = SealHarness.Reference(early.Concat(flood), live);
        var actual = SealHarness.Seal(raw, live, 4, true);
        Assert.True(expected.SequenceEqual(actual.Records), SealHarness.Describe(expected, actual.Records));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(11)]
    public void HardCapDiscardsTheWholeOverflowingLayerAndKeepsTheEarlierOnes(int writers)
    {
        var gen = new Gen(41);
        var live = SealHarness.LiveWords(gen);
        var early = SealHarness.Records(gen, SendShape.Uniform, 500);
        var flood = SealHarness.Records(gen, SendShape.Uniform, 200000);
        using var raw = new RawArena(1, writers, ArenaPolicy.HardCap);
        var mark = raw.Mark();
        SealHarness.Send(raw, SealHarness.Distribute(gen, early, writers), false);
        Assert.Equal(LayerVerdict.Fits, raw.EndLayer(mark));

        mark = raw.Mark();
        SealHarness.Send(raw, SealHarness.Distribute(gen, flood, writers), true);
        Assert.Equal(LayerVerdict.Discarded, raw.EndLayer(mark));
        Assert.Equal(0, raw.Grows);
        Assert.Equal(1, raw.DroppedLayers);
        Assert.Equal(flood.Count(SealHarness.Stored), raw.DroppedRecords);

        var expected = SealHarness.Reference(early, live);
        var actual = SealHarness.Seal(raw, live, 2, false);
        Assert.True(expected.SequenceEqual(actual.Records), SealHarness.Describe(expected, actual.Records));
    }

    [Fact]
    public void ArenaVerdictDependsOnTheRecordCountNotOnHowWritersShareIt()
    {
        var gen = new Gen(51);
        const int writers = 7;
        using var probe = new RawArena(1, writers, ArenaPolicy.HardCap);
        var capacity = probe.Capacity;
        foreach (var count in new[] { (int)capacity - 2 * writers * 256 - 600, (int)capacity - 2 * writers * 256 + 600 })
        {
            var records = Enumerable.Range(0, count).Select(at => new Sent(EffectKey.Pack(at & 1023, 0, 0, 0, Origin.Direct, 0), 1, 1)).ToList();
            var verdicts = new HashSet<LayerVerdict>();
            for (var trial = 0; trial < 6; trial++)
            {
                using var raw = new RawArena(1, writers, ArenaPolicy.HardCap);
                var mark = raw.Mark();
                SealHarness.Send(raw, SealHarness.Distribute(gen, records, writers), trial % 2 == 0);
                verdicts.Add(raw.EndLayer(mark));
            }

            Assert.Single(verdicts);
        }
    }

    static List<(int Limit, SealResult Result)> DegradedRuns(Gen gen, out Sent[] exact)
    {
        var live = Enumerable.Repeat(ulong.MaxValue, SealHarness.Chunks).ToArray();
        var records = SealHarness.Records(gen, SendShape.Uniform, 3000)
            .Select(record => new Sent(record.Key, Math.Clamp(record.Amount, -1000, 1000), Math.Clamp(record.Hits, 0, 3)))
            .ToList();
        exact = SealHarness.Reference(records, live);
        var runs = new List<(int, SealResult)>();
        foreach (var limit in new[] { exact.Length, exact.Length - 1, exact.Length * 3 / 4, exact.Length / 2, exact.Length / 3, 40 })
        {
            using var raw = new RawArena(records.Count, 3, ArenaPolicy.Grow);
            var mark = raw.Mark();
            SealHarness.Send(raw, SealHarness.Distribute(gen, records, 3), false);
            raw.EndLayer(mark);
            runs.Add((limit, SealHarness.Seal(raw, live, 3, false, limit, ArenaPolicy.HardCap)));
        }

        return runs;
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void OverflowMergeConservesTotalsPerTargetAndChannelAtEveryLevel(ulong seed)
    {
        var gen = new Gen(seed);
        var runs = DegradedRuns(gen, out var exact);
        var expectedTotals = Totals(exact);
        foreach (var (limit, degraded) in runs)
        {
            Assert.True(degraded.Records.Length <= limit, $"{gen}: limit {limit}, got {degraded.Records.Length}");
            if (degraded.Overflow != OverflowLevel.Truncated)
            {
                Assert.True(expectedTotals.SequenceEqual(Totals(degraded.Records)), $"{gen}: limit {limit} level {degraded.Overflow} totals differ");
            }
        }

        var levels = runs.Select(run => run.Result.Overflow).ToHashSet();
        Assert.Contains(OverflowLevel.None, levels);
        Assert.True(levels.Count >= 3, $"{gen}: levels reached {string.Join(",", levels)}");
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void OverflowMergeOutputIsStillCanonicalWithNoZeroAmounts(ulong seed)
    {
        var gen = new Gen(seed);
        foreach (var (limit, degraded) in DegradedRuns(gen, out _))
        {
            Assert.True(CanonicalFault(degraded.Records) == null, $"{gen}: limit {limit} level {degraded.Overflow}: {CanonicalFault(degraded.Records)}");
        }
    }

    static SortedDictionary<(int Target, int Channel), long> Totals(IEnumerable<Sent> records)
    {
        var totals = new SortedDictionary<(int, int), long>();
        foreach (var record in records)
        {
            var group = (EffectKey.Target(record.Key), (int)EffectKey.Channel(record.Key));
            totals[group] = totals.GetValueOrDefault(group) + (long)record.Amount * record.Hits;
        }

        foreach (var empty in totals.Where(pair => pair.Value == 0).Select(pair => pair.Key).ToList())
        {
            totals.Remove(empty);
        }

        return totals;
    }

    static string? CanonicalFault(Sent[] records)
    {
        for (var at = 0; at < records.Length; at++)
        {
            if (records[at].Amount == 0 || records[at].Hits <= 0)
            {
                return $"record {at} {Show(records[at])} has amount {records[at].Amount} hits {records[at].Hits}";
            }

            if (at > 0 && SealHarness.Order(records[at - 1], records[at]) >= 0)
            {
                return $"records {at - 1} {Show(records[at - 1])} and {at} {Show(records[at])} are not strictly ascending";
            }
        }

        return null;
    }

    static string Show(Sent record)
    {
        var fields = EffectKey.Unpack(new Effect(record.Key, record.Amount, record.Hits));
        return $"(target {fields.Target} source {fields.Source} kind {fields.Kind} channel {fields.Channel} origin {fields.Origin} depth {fields.Depth} amount {fields.Amount} hits {fields.Hits})";
    }
}
