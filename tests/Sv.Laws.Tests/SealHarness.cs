namespace Sv.Laws.Tests;

public enum SendShape
{
    Uniform,
    HotTarget,
    Duplicates,
    Presorted,
}

public readonly record struct Sent(ulong Key, int Amount, int Hits);

public sealed unsafe class SealResult
{
    public required Sent[] Records { get; init; }

    public required int[] Start { get; init; }

    public long DroppedDead { get; init; }

    public OverflowLevel Overflow { get; init; }

    public long DroppedOverflow { get; init; }
}

public static unsafe class SealHarness
{
    public const int Chunks = 16;

    public static readonly int[] PartCounts = { 1, 2, 4, 7, 11 };

    public static ulong[] LiveWords(Gen gen)
    {
        var live = new ulong[Chunks];
        for (var chunk = 0; chunk < Chunks; chunk++)
        {
            live[chunk] = gen.Next() | gen.Next();
        }

        live[3] = ulong.MaxValue;
        return live;
    }

    public static List<Sent> Records(Gen gen, SendShape shape, int count)
    {
        var records = new List<Sent>(count);
        var hot = gen.Below(Chunks * Slot.Rows);
        for (var at = 0; at < count; at++)
        {
            var target = shape == SendShape.HotTarget && gen.Chance(80) ? hot
                : gen.Chance(3) ? gen.Between(Chunks * Slot.Rows, Slot.Mask)
                : gen.Below(Chunks * Slot.Rows);
            var source = gen.Chance(10) ? EffectKey.NoSource : gen.Below(40);
            var kind = (byte)gen.Below(3);
            var channel = (byte)gen.Below(4);
            var origin = (Origin)gen.Below(4);
            var depth = gen.Below(4);
            if (shape == SendShape.Duplicates)
            {
                target = gen.Below(4) + 3 * Slot.Rows;
                source = gen.Below(2);
                kind = 0;
                channel = (byte)gen.Below(2);
                origin = Origin.Direct;
                depth = 0;
            }

            var amount = gen.Chance(2) ? 0 : gen.Chance(3) ? Gen.EdgeInts[gen.Below(Gen.EdgeInts.Length)] : gen.Between(-6, 6);
            var hits = gen.Chance(2) ? 0 : gen.Chance(2) ? int.MaxValue : gen.Between(1, 3);
            records.Add(new Sent(EffectKey.Pack(target, source, kind, channel, origin, depth), amount, hits));
        }

        if (shape == SendShape.Presorted)
        {
            records.Sort(Order);
        }

        return records;
    }

    public static int Order(Sent first, Sent second) =>
        first.Key != second.Key ? first.Key.CompareTo(second.Key) : first.Amount.CompareTo(second.Amount);

    public static bool IsLive(ulong[] live, ulong key)
    {
        var slot = EffectKey.Target(key);
        var chunk = slot >> Slot.RowBits;
        return chunk < live.Length && ((live[chunk] >> (slot & Slot.RowMask)) & 1ul) != 0ul;
    }

    public static bool Stored(Sent record) => record.Amount != 0 && record.Hits > 0;

    public static Sent[] Reference(IEnumerable<Sent> records, ulong[] live) =>
        records
            .Where(record => Stored(record) && IsLive(live, record.Key))
            .GroupBy(record => (record.Key, record.Amount))
            .Select(group => new Sent(group.Key.Key, group.Key.Amount, EffectLaw.Saturate32(group.Sum(record => (long)record.Hits))))
            .OrderBy(record => record.Key)
            .ThenBy(record => record.Amount)
            .ToArray();

    public static List<List<Sent>> Distribute(Gen gen, IReadOnlyList<Sent> records, int writers)
    {
        var lists = Enumerable.Range(0, writers).Select(_ => new List<Sent>()).ToList();
        var mode = gen.Below(3);
        for (var at = 0; at < records.Count; at++)
        {
            var writer = mode == 0 ? 0 : mode == 1 ? at % writers : gen.Below(writers);
            lists[writer].Add(records[at]);
        }

        return lists;
    }

    public static void Send(RawArena raw, List<List<Sent>> lists, bool parallel)
    {
        var writers = Enumerable.Range(0, lists.Count).Select(index => (IntPtr)raw.Writer(index)).ToArray();
        void SendFrom(int index)
        {
            var writer = (EffectWriter*)writers[index];
            foreach (var record in lists[index])
            {
                writer->Send(record.Key, record.Amount, record.Hits);
            }
        }

        if (parallel)
        {
            Parallel.For(0, lists.Count, SendFrom);
        }
        else
        {
            for (var index = 0; index < lists.Count; index++)
            {
                SendFrom(index);
            }
        }
    }

    public static SealResult Seal(RawArena raw, ulong[] live, int parts, bool parallel, int limit = int.MaxValue, ArenaPolicy policy = ArenaPolicy.Grow)
    {
        using var canonical = new CanonicalArena(Chunks, parts, limit, policy);
        fixed (ulong* words = live)
        {
            var route = new RouteTable(words, null, live.Length);
            var view = canonical.Begin(raw, route);
            ForParts(parts, parallel, part =>
            {
                var forward = default(NoForward);
                Sv.Seal.Count(view, ref forward, part);
            });
            Sv.Seal.Plan(view);
            ForParts(parts, parallel, part => Sv.Seal.Scatter(view, part));
            ForParts(parts, parallel, part =>
            {
                Sv.Seal.Order(view, Chunks * part / parts, Chunks * (part + 1) / parts);
            });
            Sv.Seal.Finish(view);
            var active = view.Totals[Sv.Seal.TotalActive];
            ForParts(parts, parallel, part =>
            {
                var from = (int)((long)active * part / parts);
                var to = (int)((long)active * (part + 1) / parts);
                Sv.Seal.Compact(view, view.Scratch, view.Records, from, to);
            });
            canonical.End(view, raw);
        }

        return Snapshot(canonical);
    }

    public static SealResult Snapshot(CanonicalArena canonical)
    {
        var records = new Sent[canonical.Count];
        for (var at = 0; at < records.Length; at++)
        {
            var record = canonical.Records[at];
            records[at] = new Sent(record.Key, record.Amount, record.Hits);
        }

        var start = new int[canonical.Chunks + 1];
        for (var chunk = 0; chunk <= canonical.Chunks; chunk++)
        {
            start[chunk] = canonical.Start[chunk];
        }

        return new SealResult
        {
            Records = records,
            Start = start,
            DroppedDead = canonical.DroppedDead,
            Overflow = canonical.Overflow,
            DroppedOverflow = canonical.DroppedOverflow,
        };
    }

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

    public static SealResult SealOnce(Gen gen, IReadOnlyList<Sent> records, ulong[] live, int writers, int parts, bool parallel)
    {
        using var raw = new RawArena(Math.Max(1, records.Count), writers, ArenaPolicy.Grow);
        var mark = raw.Mark();
        Send(raw, Distribute(gen, records, writers), parallel);
        Assert.Equal(LayerVerdict.Fits, raw.EndLayer(mark));
        return Seal(raw, live, parts, parallel);
    }

    public static string Describe(Sent[] expected, Sent[] actual)
    {
        if (expected.Length != actual.Length)
        {
            return $"count {actual.Length}, expected {expected.Length}";
        }

        for (var at = 0; at < expected.Length; at++)
        {
            if (expected[at] != actual[at])
            {
                return $"record {at}: {actual[at]}, expected {expected[at]}";
            }
        }

        return "equal";
    }
}
