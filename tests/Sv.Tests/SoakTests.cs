using System.Runtime.InteropServices;
using Sv;
using Xunit;
using Xunit.Abstractions;

[Collection(HeavyAllocationCollection.Name)]
public unsafe class SoakTests
{
    const int Capacity = 2048;

    const int Words = Capacity / 64;

    const ulong Pinned50k = 0x6D6C602BF07E9366UL;

    readonly ITestOutputHelper output;

    public SoakTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    static int Ticks
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("SV_SOAK_TICKS");
            return string.IsNullOrEmpty(env) ? 50_000 : int.Parse(env);
        }
    }

    struct LowHp
    {
        public int Slot;

        public int Entered;
    }

    struct LowMaker : IEventMaker<LowHp>
    {
        public LowHp Make(int row, int value) => new LowHp { Slot = row, Entered = value };
    }

    static ulong Mix(ulong x)
    {
        unchecked
        {
            x += 0x9E3779B97F4A7C15ul;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9ul;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBul;
            return x ^ (x >> 31);
        }
    }

    sealed class Soak : IDisposable
    {
        readonly int workers;
        readonly Schema schema;
        readonly StatId maxHp;
        readonly StatId armor;
        readonly Board board;
        readonly Board mirror;
        readonly SvState keyframe = new SvState();
        readonly InboxBuffer inbox = new InboxBuffer(Capacity);
        readonly InboxLanes lanes;
        readonly EventStreams streams = EventStreams.Create(1, 1 << 16);
        readonly Channel<LowHp> low;
        readonly Channel<DenseEvent> dense;
        readonly EventDerive derive;
        readonly int* hp;
        readonly int* delta;
        readonly int* floor;
        readonly int* ceiling;
        readonly ulong* changed;
        readonly ulong* state;
        readonly ulong* edges;
        readonly StatOp[] ops = new StatOp[64];
        readonly StatOp[] scratch = new StatOp[64];
        readonly StatWrite[] writes = new StatWrite[256];
        readonly ParallelOptions parallel;
        readonly Action<int> sendBody;
        readonly Action<int> foldBody;
        readonly DigestCadence cadence = DigestCadence.Release;
        long tick;
        int source = 1;

        public Soak(int workers)
        {
            this.workers = workers;
            parallel = new ParallelOptions { MaxDegreeOfParallelism = workers };
            schema = SchemaBuilder.Create().Stat("max_hp", 100f).Stat("armor", 10f).Build();
            maxHp = schema.Stat("max_hp");
            armor = schema.Stat("armor");
            board = Board.Create(schema, Capacity);
            mirror = Board.Create(schema, Capacity);
            board.BindSink(maxHp, true);
            board.BindSink(armor, true);
            lanes = new InboxLanes(inbox, workers, 64);
            low = streams.CreateChannel<LowHp>();
            dense = streams.CreateChannel<DenseEvent>();
            derive = new EventDerive(low.Id, 256);
            hp = Alloc<int>(Capacity);
            delta = Alloc<int>(Capacity);
            floor = Alloc<int>(Capacity);
            ceiling = Alloc<int>(Capacity);
            changed = Alloc<ulong>(Words);
            state = Alloc<ulong>(Words);
            edges = Alloc<ulong>(Words);
            for (var i = 0; i < Capacity * 3 / 4; i++)
            {
                hp[board.Acquire()] = 100;
            }

            board.Recalculate();
            DrainSink();
            using var baseline = new SvState();
            board.SaveState(baseline);
            mirror.LoadState(baseline);
            board.SaveKeyframe(keyframe);
            board.BeginUndoWindow(8);
            sendBody = SendPart;
            foldBody = FoldPart;
        }

        static T* Alloc<T>(int count) where T : unmanaged
        {
            var at = (T*)NativeMemory.AlignedAlloc((nuint)(count * sizeof(T)), 64);
            NativeMemory.Clear(at, (nuint)(count * sizeof(T)));
            return at;
        }

        public long BoardBytes => board.MemoryBytes + mirror.MemoryBytes;

        public long Events { get; private set; }

        public long DenseTicks { get; private set; }

        public int Rollbacks { get; private set; }

        int LiveSlot(ulong h)
        {
            var slot = (int)(h % Capacity);
            while ((board.LiveWord(slot >> 6) & (1ul << (slot & 63))) == 0)
            {
                slot = (slot + 1) % Capacity;
            }

            return slot;
        }

        int FreeSlot()
        {
            return board.Acquire();
        }

        void DrainSink()
        {
            while (true)
            {
                var taken = board.TakeBroadcast(writes);
                if (taken == 0)
                {
                    return;
                }

                for (var i = 0; i < taken; i++)
                {
                    var w = writes[i];
                    if (w.Stat == maxHp.Index)
                    {
                        var bits = w.Bits;
                        ceiling[w.Slot] = Clamp.BoundOf(*(float*)&bits);
                    }
                }
            }
        }

        public ulong Step()
        {
            tick++;
            var seed = Mix((ulong)tick);
            board.BeginUndoFrame();
            for (var i = 0; i < 4; i++)
            {
                var slot = LiveSlot(Mix(seed + (ulong)i));
                board.Release(slot);
                hp[slot] = 0;
                var spawned = FreeSlot();
                hp[spawned] = 100;
            }

            var n = 0;
            for (var i = 0; i < 12; i++)
            {
                var h = Mix(seed ^ (0x100ul + (ulong)i));
                var slot = LiveSlot(h);
                var stat = (h & 1) == 0 ? maxHp : armor;
                var value = (int)((h >> 8) % 61) - 30;
                ops[n++] = StatOp.Add(slot, stat, ModifierKind.Added, value, source++, tick + 1 + (long)((h >> 20) % 40));
            }

            for (var i = 0; i < 4; i++)
            {
                var h = Mix(seed ^ (0x200ul + (ulong)i));
                ops[n++] = StatOp.RemoveSource(LiveSlot(h), source - 1 - (int)((h >> 8) % 300));
            }

            StatOps.Canonicalize(ops.AsSpan(0, n), scratch.AsSpan(0, n));
            board.Apply(ops.AsSpan(0, n));
            board.ExpireModifiers(tick);
            board.Recalculate();
            if (tick % 101 == 0)
            {
                board.Rollback(1 + (int)(tick % 4));
                Rollbacks++;
                board.Recalculate();
            }

            DrainSink();
            if (tick % 64 == 0)
            {
                board.SaveKeyframe(keyframe);
                mirror.LoadKeyframe(keyframe);
                if (mirror.Digest != board.Digest)
                {
                    throw new InvalidOperationException($"soak: keyframe mirror digest differs at tick {tick}");
                }
            }

            if (workers == 1)
            {
                SendPart(0);
                FoldPart(0);
            }
            else
            {
                Parallel.For(0, workers, parallel, sendBody);
                Parallel.For(0, workers, parallel, foldBody);
            }

            derive.Begin();
            derive.AddCount(EventDerive.CountEdges(edges, Words));
            var maker = new LowMaker();
            Events += derive.Derive(streams, low, dense, tick, edges, state, Words, ref maker);
            DenseTicks += derive.LastDense ? 1 : 0;
            streams.Swap();
            if (!cadence.Due(tick))
            {
                return 0;
            }

            return unchecked(board.Digest * 31 + CellDigest.Sum(1, hp, 0, Capacity));
        }

        (int First, int Last) Part(int part)
        {
            var first = (int)((long)part * Words / workers);
            var last = (int)((long)(part + 1) * Words / workers);
            return (first, last);
        }

        void SendPart(int part)
        {
            var (first, last) = Part(part);
            var lane = lanes.Lane(part);
            var seed = Mix((ulong)tick * 7919);
            var wave = tick % 5000 == 0;
            for (var w = first; w < last; w++)
            {
                var live = board.LiveWord(w);
                while (live != 0)
                {
                    var bit = Bits.Tzcnt(live);
                    live &= live - 1;
                    var row = (w << 6) + bit;
                    var h = Mix(seed ^ (ulong)row);
                    if (wave)
                    {
                        delta[row] -= 1_000_000;
                    }
                    else if ((h & 7) == 0)
                    {
                        var target = (h >> 8) % 10 == 0 ? 0 : (int)((h >> 16) % Capacity);
                        lane->Send(target, -(int)((h >> 32) % 9));
                    }
                    else
                    {
                        delta[row] += (int)((h >> 40) % 5) - 1;
                    }
                }
            }

            lane->Flush();
        }

        void FoldPart(int part)
        {
            var (first, last) = Part(part);
            if (first == last)
            {
                return;
            }

            var start = first << 6;
            var count = (last - first) << 6;
            Kernel.Fold(new Column<int>(hp, Capacity), Window.Range(start, count), new Column<int>(delta, Capacity), Bounds.Rows(floor, ceiling), new Column<ulong>(changed + first, last - first), inbox.Column);
            for (var w = first; w < last; w++)
            {
                ulong enter = 0;
                ulong stay = 0;
                for (var r = 0; r < 64; r++)
                {
                    var slot = (w << 6) + r;
                    enter |= (Condition.Below(hp[slot], ceiling[slot], 3, 10) ? 1ul : 0ul) << r;
                    stay |= (Condition.Below(hp[slot], ceiling[slot], 4, 10) ? 1ul : 0ul) << r;
                }

                var next = Condition.Next(state[w], enter, stay);
                edges[w] = Condition.Edges(state[w], next);
                state[w] = next;
            }
        }

        public void Dispose()
        {
            derive.Dispose();
            streams.Dispose();
            lanes.Dispose();
            inbox.Dispose();
            keyframe.Dispose();
            board.Dispose();
            mirror.Dispose();
            NativeMemory.AlignedFree(hp);
            NativeMemory.AlignedFree(delta);
            NativeMemory.AlignedFree(floor);
            NativeMemory.AlignedFree(ceiling);
            NativeMemory.AlignedFree(changed);
            NativeMemory.AlignedFree(state);
            NativeMemory.AlignedFree(edges);
        }
    }

    (ulong Digest, long Alloc, long BytesWarm, long BytesEnd, Soak Stats) Run(int workers, int ticks)
    {
        var soak = new Soak(workers);
        var samples = new List<long>(16);
        var digest = 0ul;
        var warm = ticks / 10;
        var flatFrom = ticks * 3 / 10;
        long allocStart = 0;
        long bytesWarm = 0;
        for (var t = 1; t <= ticks; t++)
        {
            if (t == warm)
            {
                allocStart = GC.GetAllocatedBytesForCurrentThread();
            }

            if (t == flatFrom)
            {
                bytesWarm = soak.BoardBytes;
            }

            if (t % (ticks / 10) == 0)
            {
                samples.Add(soak.BoardBytes);
            }

            var d = soak.Step();
            if (d != 0)
            {
                digest = unchecked(digest * 1_000_003 + d);
            }
        }

        var alloc = GC.GetAllocatedBytesForCurrentThread() - allocStart;
        output.WriteLine($"workers={workers} board bytes per 10 %: {string.Join(" ", samples)}");
        return (digest, alloc, bytesWarm, soak.BoardBytes, soak);
    }

    [Fact]
    public void Soak_ChurnModifiersDamageRollbackKeyframes_IsDeterministic_ZeroAlloc_FlatMemory()
    {
        var ticks = Ticks;
        var first = Run(1, ticks);
        using (first.Stats)
        {
            output.WriteLine($"soak ticks={ticks} digest={first.Digest:X16} alloc={first.Alloc} B boardBytes at 30 %={first.BytesWarm} end={first.BytesEnd} events={first.Stats.Events} denseTicks={first.Stats.DenseTicks} rollbacks={first.Stats.Rollbacks}");
            Assert.Equal(0, first.Alloc);
            if (ticks == 50_000)
            {
                Assert.Equal(Pinned50k, first.Digest);
            }

            Assert.Equal(first.BytesWarm, first.BytesEnd);
            Assert.True(first.Stats.Events > 0);
            Assert.True(first.Stats.Rollbacks > 0);
            Assert.True(first.Stats.DenseTicks >= ticks / 5000);
        }

        var second = Run(1, ticks);
        using (second.Stats)
        {
            Assert.Equal(first.Digest, second.Digest);
        }

        foreach (var workers in new[] { 4, 11 })
        {
            var run = Run(workers, ticks);
            using (run.Stats)
            {
                output.WriteLine($"workers={workers} digest={run.Digest:X16}");
                Assert.Equal(first.Digest, run.Digest);
            }
        }
    }
}
