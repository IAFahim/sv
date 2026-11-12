using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Sv;

internal static unsafe class Frame1m
{
    const int WarmTicks = 32;

    const int Rounds = 6;

    const int TicksPerRound = 32;

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
        if (scenario != "frame1m")
        {
            return 0;
        }

        var workers = Math.Max(1, Environment.ProcessorCount / 2);
        var env = Environment.GetEnvironmentVariable("SV_FRAME_WORKERS");
        if (!string.IsNullOrEmpty(env))
        {
            workers = int.Parse(env);
        }

        var shapes = new[] { ("P1", 1 << 20, 1), ("P2", 1 << 18, 4), ("P3", 10_000, 150) };
        var writes = new[] { 1.0, 2.5, 4.0 };
        var crosses = new[] { 0, 10, 50 };
        var only = Environment.GetEnvironmentVariable("SV_FRAME_SHAPE");
        Console.WriteLine($"frame1m  workers={workers} isa=v256:{Simd.Has256}/v128:{Simd.Has128} intrinsics=2 (hp bounded by sink stat, energy constant) kinds=sv stand-in (no tl) warm={WarmTicks} rounds={Rounds}x{TicksPerRound} first dropped, median of round means, 1 process per cell");
        Console.WriteLine("shape  W    cross  total ms | main  scatter  fold  kinds  derive  digest | statops/tick  edges/tick  alloc B/tick");
        foreach (var (name, rows, perRow) in shapes)
        {
            if (!string.IsNullOrEmpty(only) && only != name)
            {
                continue;
            }

            foreach (var w in writes)
            {
                foreach (var cross in crosses)
                {
                    using var frame = new Frame(rows, perRow, w, cross, workers);
                    for (var t = 0; t < WarmTicks; t++)
                    {
                        frame.Tick();
                    }

                    var samples = new double[Rounds][];
                    long alloc = 0;
                    for (var round = 0; round < Rounds; round++)
                    {
                        frame.Reset();
                        var before = GC.GetAllocatedBytesForCurrentThread();
                        for (var t = 0; t < TicksPerRound; t++)
                        {
                            frame.Tick();
                        }

                        if (round > 0)
                        {
                            alloc += GC.GetAllocatedBytesForCurrentThread() - before;
                        }

                        samples[round] = frame.Mean(TicksPerRound);
                    }

                    var median = new double[8];
                    for (var stage = 0; stage < 8; stage++)
                    {
                        var values = new double[Rounds - 1];
                        for (var round = 1; round < Rounds; round++)
                        {
                            values[round - 1] = samples[round][stage];
                        }

                        Array.Sort(values);
                        median[stage] = values[values.Length / 2];
                    }

                    Console.WriteLine($"{name,-5} {w,3:0.0}  {cross,3}%   {median[0],7:0.000} | {median[1],5:0.000} {median[2],7:0.000} {median[3],6:0.000} {median[4],6:0.000} {median[5],6:0.000} {median[6],6:0.000} | {frame.OpsPerTick,8} {frame.EdgesPerTick,10:0} {alloc / ((Rounds - 1) * TicksPerRound),10}");
                }
            }
        }

        return 0;
    }

    sealed class Frame : IDisposable
    {
        readonly int rows;
        readonly int perRow;
        readonly double writesPerTimeline;
        readonly int cross;
        readonly int workers;
        readonly int words;
        readonly int capacity;
        readonly Board board;
        readonly StatId maxHp;
        readonly StatId armor;
        readonly int* hp;
        readonly int* energy;
        readonly int* hpDelta;
        readonly int* energyDelta;
        readonly int* floor;
        readonly int* ceiling;
        readonly float* maxHpHost;
        readonly float* armorHost;
        readonly ulong* hpChanged;
        readonly ulong* energyChanged;
        readonly ulong* low;
        readonly ulong* edges;
        readonly InboxBuffer hpInbox;
        readonly InboxBuffer energyInbox;
        readonly EventStreams streams;
        readonly Channel<EdgeEvent> channel;
        readonly Channel<DenseEvent> dense;
        readonly EventDerive derive;
        readonly StatOp[] ops;
        readonly StatOp[] scratch;
        readonly int[] liveSlot;
        readonly int[] liveSource;
        readonly StatWrite[] sink;
        readonly ulong[] partial;
        readonly long[] stage = new long[8];
        readonly ParallelOptions parallel;
        readonly Action<int> foldBody;
        readonly Action<int> kindBody;
        readonly Action<int> digestBody;
        int live;
        int source = 1;
        long tick;
        long edgeTotal;

        public Frame(int rows, int perRow, double writesPerTimeline, int cross, int workers)
        {
            this.rows = rows;
            this.perRow = perRow;
            this.writesPerTimeline = writesPerTimeline;
            this.cross = cross;
            this.workers = workers;
            words = (rows + 63) >> 6;
            capacity = words << 6;
            parallel = new ParallelOptions { MaxDegreeOfParallelism = workers };
            var schema = SchemaBuilder.Create().Stat("max_hp", 1000f).Stat("armor", 10f).Build();
            maxHp = schema.Stat("max_hp");
            armor = schema.Stat("armor");
            board = Board.Create(schema, rows);
            board.BindSink(maxHp, true);
            board.BindSink(armor, true);
            for (var i = 0; i < rows; i++)
            {
                board.Acquire();
            }

            hp = Ints();
            energy = Ints();
            hpDelta = Ints();
            energyDelta = Ints();
            floor = Ints();
            ceiling = Ints();
            maxHpHost = (float*)NativeMemory.AllocZeroed((nuint)capacity, sizeof(float));
            armorHost = (float*)NativeMemory.AllocZeroed((nuint)capacity, sizeof(float));
            hpChanged = (ulong*)NativeMemory.AllocZeroed((nuint)words, sizeof(ulong));
            energyChanged = (ulong*)NativeMemory.AllocZeroed((nuint)words, sizeof(ulong));
            low = (ulong*)NativeMemory.AllocZeroed((nuint)words, sizeof(ulong));
            edges = (ulong*)NativeMemory.AllocZeroed((nuint)words, sizeof(ulong));
            hpInbox = new InboxBuffer(capacity);
            energyInbox = new InboxBuffer(capacity);
            streams = EventStreams.Create(1, 1 << 20);
            channel = streams.CreateChannel<EdgeEvent>();
            dense = streams.CreateChannel<DenseEvent>();
            derive = new EventDerive(channel.Id);
            var timelines = (long)rows * perRow;
            OpsPerTick = (int)Math.Max(2, Math.Round(0.05 * writesPerTimeline * timelines * 2 / 300)) & ~1;
            ops = new StatOp[OpsPerTick * 2];
            scratch = new StatOp[ops.Length];
            liveSlot = new int[OpsPerTick];
            liveSource = new int[OpsPerTick];
            sink = new StatWrite[4096];
            partial = new ulong[workers];
            for (var r = 0; r < rows; r++)
            {
                hp[r] = 500 + (r % 400);
                energy[r] = 50;
            }

            foldBody = FoldPart;
            kindBody = KindPart;
            digestBody = DigestPart;
        }

        public int OpsPerTick { get; }

        public double EdgesPerTick { get; private set; }

        int* Ints() => (int*)NativeMemory.AllocZeroed((nuint)capacity, sizeof(int));

        public void Reset()
        {
            Array.Clear(stage, 0, stage.Length);
            edgeTotal = 0;
        }

        public double[] Mean(int ticks)
        {
            var result = new double[8];
            for (var i = 0; i < 8; i++)
            {
                result[i] = stage[i] * 1e3 / Stopwatch.Frequency / ticks;
            }

            EdgesPerTick = (double)edgeTotal / ticks;
            return result;
        }

        (int Start, int Count) Partition(int part)
        {
            var first = (int)((long)part * words / workers);
            var last = (int)((long)(part + 1) * words / workers);
            var start = first << 6;
            var end = Math.Min(rows, last << 6);
            return (start, Math.Max(0, end - start));
        }

        public void Tick()
        {
            var t0 = Stopwatch.GetTimestamp();
            var n = 0;
            for (var i = 0; i < live; i++)
            {
                ops[n++] = StatOp.RemoveSource(liveSlot[i], liveSource[i]);
            }

            var adds = OpsPerTick / 2;
            for (var i = 0; i < adds; i++)
            {
                var h = Hash((ulong)tick * 0x9E37u + (ulong)i);
                var slot = (int)(h % (ulong)rows);
                ops[n++] = StatOp.Add(slot, (h & 1) == 0 ? maxHp : armor, ModifierKind.Added, (int)((h >> 20) % 9) - 4, source);
                liveSlot[i] = slot;
                liveSource[i] = source++;
            }

            live = adds;
            StatOps.Canonicalize(ops.AsSpan(0, n), scratch.AsSpan(0, n));
            board.Apply(ops.AsSpan(0, n));
            board.Recalculate();
            var t1 = Stopwatch.GetTimestamp();
            while (true)
            {
                var taken = board.TakeBroadcast(sink);
                if (taken == 0)
                {
                    break;
                }

                for (var i = 0; i < taken; i++)
                {
                    var w = sink[i];
                    var bits = w.Bits;
                    var value = *(float*)&bits;
                    if (w.Stat == maxHp.Index)
                    {
                        maxHpHost[w.Slot] = value;
                        ceiling[w.Slot] = Clamp.BoundOf(value);
                    }
                    else
                    {
                        armorHost[w.Slot] = value;
                    }
                }
            }

            var t2 = Stopwatch.GetTimestamp();
            derive.Begin();
            Parallel.For(0, workers, parallel, foldBody);
            var t3 = Stopwatch.GetTimestamp();
            Parallel.For(0, workers, parallel, kindBody);
            var t4 = Stopwatch.GetTimestamp();
            var maker = new Maker();
            edgeTotal += derive.Edges;
            derive.Derive(streams, channel, dense, tick, edges, low, words, ref maker);
            streams.Swap();
            var t5 = Stopwatch.GetTimestamp();
            Parallel.For(0, workers, parallel, digestBody);
            ulong digest = 0;
            for (var p = 0; p < workers; p++)
            {
                digest = unchecked(digest + partial[p]);
            }

            var t6 = Stopwatch.GetTimestamp();
            stage[0] += t6 - t0;
            stage[1] += t1 - t0;
            stage[2] += t2 - t1;
            stage[3] += t3 - t2;
            stage[4] += t4 - t3;
            stage[5] += t5 - t4;
            stage[6] += t6 - t5;
            tick++;
        }

        void FoldPart(int part)
        {
            var (start, count) = Partition(part);
            if (count == 0)
            {
                return;
            }

            var first = start >> 6;
            var wordCount = (count + 63) >> 6;
            Kernel.Fold(new Column<int>(hp, capacity), Window.Range(start, count), new Column<int>(hpDelta, capacity), Bounds.Rows(floor, ceiling), new Column<ulong>(hpChanged + first, wordCount), hpInbox.Column);
            Kernel.Fold(new Column<int>(energy, capacity), Window.Range(start, count), new Column<int>(energyDelta, capacity), Bounds.Constant(0, 100), new Column<ulong>(energyChanged + first, wordCount), energyInbox.Column);
            long flips = 0;
            for (var w = first; w < first + wordCount; w++)
            {
                if (hpChanged[w] == 0 && edges[w] == 0)
                {
                    continue;
                }

                var limit = Math.Min(64, rows - (w << 6));
                ulong enter = 0;
                ulong stay = 0;
                for (var r = 0; r < limit; r++)
                {
                    var slot = (w << 6) + r;
                    enter |= (Condition.Below(hp[slot], ceiling[slot], 3, 10) ? 1ul : 0ul) << r;
                    stay |= (Condition.Below(hp[slot], ceiling[slot], 4, 10) ? 1ul : 0ul) << r;
                }

                var state = low[w];
                var next = Condition.Next(state, enter, stay);
                edges[w] = Condition.Edges(state, next);
                low[w] = next;
                flips += Bits.PopCount(edges[w]);
            }

            derive.AddCount(flips);
        }

        void KindPart(int part)
        {
            var (start, count) = Partition(part);
            var hpCells = hpInbox.Pointer;
            var energyCells = energyInbox.Pointer;
            var whole = (int)writesPerTimeline;
            var half = writesPerTimeline - whole > 0;
            var seed = (ulong)tick * 0xD1B54A32D192ED03ul;
            var reads = 0;
            for (var row = start; row < start + count; row++)
            {
                var localHp = 0;
                var localEnergy = 0;
                for (var k = 0; k < perRow; k++)
                {
                    var h = Hash(seed ^ ((ulong)row << 8) ^ (ulong)k);
                    var n = whole + (half && (k & 1) == 0 ? 1 : 0);
                    for (var r = n; r < 5; r++)
                    {
                        reads += (int)maxHpHost[row];
                    }

                    for (var w = 0; w < n; w++)
                    {
                        var bits = h >> (w * 13);
                        var amount = (int)(bits % 7) - 3;
                        if (cross > 0 && (int)((bits >> 3) % 100) < cross)
                        {
                            var target = (int)((bits >> 10) % (ulong)rows);
                            Inbox.Send((w & 1) == 0 ? hpCells : energyCells, target, amount);
                        }
                        else if ((w & 1) == 0)
                        {
                            localHp += amount;
                        }
                        else
                        {
                            localEnergy += amount;
                        }
                    }
                }

                hpDelta[row] += localHp;
                energyDelta[row] += localEnergy;
            }

            partial[part] = (ulong)reads;
        }

        void DigestPart(int part)
        {
            var (start, count) = Partition(part);
            partial[part] = count == 0 ? 0 : unchecked(CellDigest.Sum(1, hp, start, count) + CellDigest.Sum(2, energy, start, count));
        }

        static ulong Hash(ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15ul;
                x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9ul;
                x = (x ^ (x >> 27)) * 0x94D049BB133111EBul;
                return x ^ (x >> 31);
            }
        }

        public void Dispose()
        {
            derive.Dispose();
            streams.Dispose();
            hpInbox.Dispose();
            energyInbox.Dispose();
            board.Dispose();
            NativeMemory.Free(hp);
            NativeMemory.Free(energy);
            NativeMemory.Free(hpDelta);
            NativeMemory.Free(energyDelta);
            NativeMemory.Free(floor);
            NativeMemory.Free(ceiling);
            NativeMemory.Free(maxHpHost);
            NativeMemory.Free(armorHost);
            NativeMemory.Free(hpChanged);
            NativeMemory.Free(energyChanged);
            NativeMemory.Free(low);
            NativeMemory.Free(edges);
        }
    }
}
