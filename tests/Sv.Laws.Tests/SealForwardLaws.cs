namespace Sv.Laws.Tests;

public sealed unsafe class SealForwardLaws
{
    const int Slots = SealHarness.Chunks * Slot.Rows;
    const ulong ForwardedChannels = 0b0101ul;
    const int CycleStart = 3 * Slot.Rows;
    const int CycleLength = 3;

    struct TableForward : IForward
    {
        public int* Owner;

        public int Forward(int slot, int channel) => ((ForwardedChannels >> channel) & 1ul) != 0ul ? Owner[slot] : -1;
    }

    sealed class Forwarders
    {
        public readonly ulong[] Live;
        public readonly int[] Owner = new int[Slots];
        public readonly ulong[] Forwarding = new ulong[SealHarness.Chunks];

        public Forwarders(Gen gen)
        {
            Live = SealHarness.LiveWords(gen);
            for (var slot = 0; slot < Slots; slot++)
            {
                var forwards = IsLive(slot) && gen.Chance(20);
                Owner[slot] = forwards ? gen.Below(Slots) : -1;
                Forwarding[slot >> Slot.RowBits] |= forwards ? 1ul << (slot & Slot.RowMask) : 0ul;
            }

            for (var at = 0; at < CycleLength; at++)
            {
                Owner[CycleStart + at] = CycleStart + (at + 1) % CycleLength;
                Forwarding[CycleStart >> Slot.RowBits] |= 1ul << ((CycleStart + at) & Slot.RowMask);
            }
        }

        public bool IsLive(int slot) => slot < Slots && ((Live[slot >> Slot.RowBits] >> (slot & Slot.RowMask)) & 1ul) != 0ul;

        bool Forwards(int slot) => ((Forwarding[slot >> Slot.RowBits] >> (slot & Slot.RowMask)) & 1ul) != 0ul;

        public int Final(int slot, int channel)
        {
            if (!IsLive(slot))
            {
                return Route.Dead;
            }

            var at = slot;
            var hops = 0;
            while (Forwards(at) && ((ForwardedChannels >> channel) & 1ul) != 0ul)
            {
                if (hops == Route.MaxHops)
                {
                    return Route.Fault;
                }

                hops++;
                at = Owner[at];
                if (!IsLive(at))
                {
                    return Route.Dead;
                }
            }

            return at;
        }
    }

    static List<Sent> Records(Gen gen, int count, bool presorted)
    {
        var records = new List<Sent>(count);
        for (var at = 0; at < count; at++)
        {
            var target = gen.Chance(3) ? gen.Between(Slots, Slot.Mask) : gen.Below(Slots);
            var key = EffectKey.Pack(target, gen.Chance(10) ? EffectKey.NoSource : gen.Below(40), (byte)gen.Below(3), (byte)gen.Below(4), (Origin)gen.Below(4), gen.Below(4));
            records.Add(new Sent(key, gen.Chance(30) ? gen.Between(1, 3) : gen.Between(-6, 6) | 1, gen.Between(1, 3)));
        }

        if (presorted)
        {
            records.Sort(SealHarness.Order);
        }

        return records;
    }

    static SealResult SealForwarded(Forwarders table, IReadOnlyList<Sent> records, int writers, int parts, bool parallel, Gen gen)
    {
        using var raw = new RawArena(Math.Max(1, records.Count), writers, ArenaPolicy.Grow);
        var mark = raw.Mark();
        SealHarness.Send(raw, SealHarness.Distribute(gen, records, writers), parallel);
        Assert.Equal(LayerVerdict.Fits, raw.EndLayer(mark));
        using var canonical = new CanonicalArena(SealHarness.Chunks, parts, int.MaxValue, ArenaPolicy.Grow);
        fixed (ulong* live = table.Live)
        fixed (ulong* forwarding = table.Forwarding)
        fixed (int* owner = table.Owner)
        {
            var route = new RouteTable(live, forwarding, SealHarness.Chunks);
            var view = canonical.Begin(raw, route);
            var forward = new TableForward { Owner = owner };
            ForParts(parts, parallel, part =>
            {
                var local = forward;
                Sv.Seal.Count(view, ref local, part);
            });
            Sv.Seal.Plan(view);
            ForParts(parts, parallel, part => Sv.Seal.Scatter(view, part));
            ForParts(parts, parallel, part => Sv.Seal.Order(view, SealHarness.Chunks * part / parts, SealHarness.Chunks * (part + 1) / parts));
            Sv.Seal.Finish(view);
            var active = view.Totals[Sv.Seal.TotalActive];
            ForParts(parts, parallel, part => Sv.Seal.Compact(view, view.Scratch, view.Records, (int)((long)active * part / parts), (int)((long)active * (part + 1) / parts)));
            canonical.End(view, raw);
            var result = SealHarness.Snapshot(canonical);
            Faults = canonical.RouteFaults;
            return result;
        }
    }

    [ThreadStatic]
    static long Faults;

    static void ForParts(int parts, bool parallel, Action<int> body)
    {
        if (parallel)
        {
            Parallel.For(0, parts, body);
            return;
        }

        for (var part = 0; part < parts; part++)
        {
            body(part);
        }
    }

    static Sent[] Retargeted(Forwarders table, IEnumerable<Sent> records)
    {
        var moved = new List<Sent>();
        foreach (var record in records)
        {
            var final = table.Final(EffectKey.Target(record.Key), EffectKey.Channel(record.Key));
            if (final >= 0)
            {
                moved.Add(record with { Key = EffectKey.WithTarget(record.Key, final) });
            }
        }

        return SealHarness.Reference(moved, table.Live);
    }

    public static IEnumerable<object[]> Cases() =>
        new[] { 1ul, 2ul, 3ul, 17ul }.SelectMany(seed => new[] { false, true }.Select(presorted => new object[] { seed, presorted }));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Seal_with_forwarding_equals_sealing_the_records_already_sent_to_their_final_owner(ulong seed, bool presorted)
    {
        var gen = new Gen(seed);
        var table = new Forwarders(gen);
        var records = Records(gen, 5000, presorted);
        var expected = Retargeted(table, records);
        var actual = SealForwarded(table, records, 1, 1, false, gen);
        Assert.True(expected.SequenceEqual(actual.Records), $"{gen} presorted {presorted}: {SealHarness.Describe(expected, actual.Records)}");
        for (var chunk = 0; chunk < SealHarness.Chunks; chunk++)
        {
            Assert.Equal(expected.Count(record => EffectKey.Target(record.Key) >> Slot.RowBits == chunk), actual.Start[chunk + 1] - actual.Start[chunk]);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void Forwarded_records_into_dead_owners_count_as_dead_and_chains_past_the_hop_limit_count_as_faults(ulong seed)
    {
        var gen = new Gen(seed);
        var table = new Forwarders(gen);
        var records = Records(gen, 5000, false);
        for (var at = 0; at < 20; at++)
        {
            records.Add(new Sent(EffectKey.Pack(CycleStart + at % CycleLength, at, 0, 0, Origin.Direct, 0), 1 + at, 1));
        }

        var dead = records.LongCount(record => table.Final(EffectKey.Target(record.Key), EffectKey.Channel(record.Key)) == Route.Dead);
        var faults = records.LongCount(record => table.Final(EffectKey.Target(record.Key), EffectKey.Channel(record.Key)) == Route.Fault);
        var actual = SealForwarded(table, records, 1, 1, false, gen);
        Assert.Equal(dead, actual.DroppedDead);
        Assert.Equal(faults, Faults);
        Assert.True(faults > 0, $"{gen}: the generated table has no chain past the hop limit");
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void Seal_with_forwarding_gives_the_same_records_for_any_writers_parts_and_threads(ulong seed)
    {
        var gen = new Gen(seed);
        var table = new Forwarders(gen);
        var records = Records(gen, 5000, false);
        var first = SealForwarded(table, records, 1, 1, false, new Gen(seed));
        foreach (var parts in SealHarness.PartCounts)
        {
            var shuffled = records.ToList();
            gen.Shuffle(shuffled);
            var other = SealForwarded(table, shuffled, 3, parts, true, gen);
            Assert.True(first.Records.SequenceEqual(other.Records), $"{gen} parts {parts}: {SealHarness.Describe(first.Records, other.Records)}");
            Assert.Equal(first.Start, other.Start);
            Assert.Equal(first.DroppedDead, other.DroppedDead);
        }
    }
}
