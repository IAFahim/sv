using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Sv;

internal static unsafe class Derive
{
    const int Slots = 1 << 20;

    const int Words = Slots >> 6;

    const int ChunkRows = 48;

    const int Reps = 41;

    struct EdgeEvent
    {
        public int Slot;

        public int Entered;
    }

    struct Maker : IEventMaker<EdgeEvent>
    {
        public EdgeEvent Make(int row, int value) => new EdgeEvent { Slot = row, Entered = value };
    }

    internal static int Run(string scenario, int ms)
    {
        if (scenario != "all" && scenario != "derive")
        {
            return 0;
        }

        var failures = 0;
        using var streams = EventStreams.Create(1, 1 << 20);
        var events = streams.CreateChannel<EdgeEvent>();
        var dense = streams.CreateChannel<DenseEvent>();
        using var derive = new EventDerive(events.Id);
        var edges = (ulong*)NativeMemory.AllocZeroed(Words, sizeof(ulong));
        var state = (ulong*)NativeMemory.AllocZeroed(Words, sizeof(ulong));
        var slotMap = (int*)NativeMemory.Alloc(Slots, sizeof(int));
        var ordered = (int*)NativeMemory.Alloc(Slots, sizeof(int));
        var random = new Random(65_536);
        for (var i = 0; i < Slots; i++)
        {
            slotMap[i] = i;
            ordered[i] = i;
        }

        for (var i = Slots - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (slotMap[i], slotMap[j]) = (slotMap[j], slotMap[i]);
        }

        var unitCount = (Slots + ChunkRows - 1) / ChunkRows;
        var shuffled = new EdgeUnit[unitCount];
        var straight = new EdgeUnit[unitCount];
        foreach (var count in new[] { 0, 1_000, 65_536, Slots })
        {
            new Span<ulong>(edges, Words).Clear();
            new Span<ulong>(state, Words).Clear();
            var pick = new Random(count);
            var placed = 0;
            while (placed < count)
            {
                var slot = count == Slots ? placed : pick.Next(Slots);
                if ((edges[slot >> 6] & (1ul << (slot & 63))) != 0)
                {
                    continue;
                }

                edges[slot >> 6] |= 1ul << (slot & 63);
                if ((slot & 1) == 0)
                {
                    state[slot >> 6] |= 1ul << (slot & 63);
                }

                placed++;
            }

            for (var u = 0; u < unitCount; u++)
            {
                shuffled[u] = Unit(slotMap, u, edges, state);
                straight[u] = Unit(ordered, u, edges, state);
            }

            for (var i = unitCount - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            var wordSamples = new double[Reps];
            var straightSamples = new double[Reps];
            var shuffledSamples = new double[Reps];
            var alloc = 0L;
            var delivered = new int[3];
            var maker = new Maker();
            for (var rep = 0; rep < Reps; rep++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var t0 = Stopwatch.GetTimestamp();
                derive.Begin();
                derive.AddCount(EventDerive.CountEdges(edges, Words));
                delivered[0] = derive.Derive(streams, events, dense, rep, edges, state, Words, ref maker);
                var t1 = Stopwatch.GetTimestamp();
                streams.Swap();
                var t2 = Stopwatch.GetTimestamp();
                derive.Begin();
                derive.AddCount(EventDerive.CountEdges(straight));
                delivered[1] = derive.Derive(streams, events, dense, rep, straight, ref maker);
                var t3 = Stopwatch.GetTimestamp();
                streams.Swap();
                var t4 = Stopwatch.GetTimestamp();
                derive.Begin();
                derive.AddCount(EventDerive.CountEdges(shuffled));
                delivered[2] = derive.Derive(streams, events, dense, rep, shuffled, ref maker);
                var t5 = Stopwatch.GetTimestamp();
                streams.Swap();
                alloc += rep == 0 ? 0 : GC.GetAllocatedBytesForCurrentThread() - before;
                wordSamples[rep] = (t1 - t0) * 1e3 / Stopwatch.Frequency;
                straightSamples[rep] = (t3 - t2) * 1e3 / Stopwatch.Frequency;
                shuffledSamples[rep] = (t5 - t4) * 1e3 / Stopwatch.Frequency;
            }

            var expected = count <= derive.Capacity ? count : 0;
            var verify = delivered[0] == expected && delivered[1] == expected && delivered[2] == expected && derive.LastDense == count > derive.Capacity ? "ok" : $"FAIL delivered {delivered[0]}/{delivered[1]}/{delivered[2]} expected {expected}";
            if (alloc != 0)
            {
                verify = $"FAIL allocated {alloc} managed bytes";
            }

            var word = Median(wordSamples);
            var line = Median(straightSamples);
            var sorted = Median(shuffledSamples);
            var sort = sorted - line;
            var kill = count == derive.Capacity ? $"  kill(sort>0.5ms at cap):{(sort > 0.5 ? "TRIPPED" : "clear")}" : string.Empty;
            Console.WriteLine($"derive {count,9:N0} edges  words {word:0.000} ms | units slot-ordered {line:0.000} ms | units shuffled (gather+radix) {sorted:0.000} ms (sort ~{sort:0.000})  branch={(count > derive.Capacity ? "dense" : "events")}  [2^20 slots, cap {derive.Capacity:N0}, {ChunkRows}-row units, 1 thread, median of {Reps - 1}]{kill}  {verify}");
            failures += verify == "ok" ? 0 : 1;
        }

        NativeMemory.Free(edges);
        NativeMemory.Free(state);
        NativeMemory.Free(slotMap);
        NativeMemory.Free(ordered);
        return failures;
    }

    static EdgeUnit Unit(int* map, int unit, ulong* edges, ulong* state)
    {
        var at = unit * ChunkRows;
        var rows = Math.Min(ChunkRows, Slots - at);
        ulong e = 0;
        ulong s = 0;
        for (var r = 0; r < rows; r++)
        {
            var slot = map[at + r];
            e |= ((edges[slot >> 6] >> (slot & 63)) & 1ul) << r;
            s |= ((state[slot >> 6] >> (slot & 63)) & 1ul) << r;
        }

        return new EdgeUnit(map + at, e, s);
    }

    static double Median(double[] samples)
    {
        Array.Sort(samples, 1, Reps - 1);
        return samples[1 + (Reps - 1) / 2];
    }
}
