extern alias v3;

using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using v3::Sv;
using v3::Sv.Host;
using v3::Sv.Host.Net;
using v3::Sv.Rules;

namespace SvV3Bench
{
    internal unsafe struct BenchKernels : IPackKernels
    {
        public const ulong Hash = 0x5356334245304E43ul;
        public const int HealthLane = 0;
        public const int KindLane = 1;
        public const int Damage = 0;
        public const int Healing = 1;

        public ulong PackHash => Hash;

        public ulong CompiledAgainst => Hash;

        public int ScratchLanes => 1;

        public int TotalLanes => 0;

        public int DeltaLanes => 1;

        public int EventCount => 0;

        public int LanesWrittenPerChunk => 1;

        public int DeltaTarget(int deltaLane) => HealthLane;

        public int FloorStat(int deltaLane) => -1;

        public int CeilingStat(int deltaLane) => -1;

        public void Bounds(in WorldView world, int chunk, int deltaLane, int* floor, int* ceiling)
        {
            LaneMath.Fill(floor, 0);
            LaneMath.Fill(ceiling, 1000);
        }

        public int EventId(int ordinal) => 0;

        public bool Forwards(int template) => false;

        public int Forward(in WorldView world, int slot, int channel) => -1;

        public ulong Decide(in WorldView world, int chunk, int capacity, Effect* effects, int count, int* scratch, long* totals, byte* records, ref EffectWriter next)
        {
            var touched = 0ul;
            var at = 0;
            while (at < count)
            {
                var target = EffectKey.Target(effects[at].Key);
                var sum = 0L;
                while (at < count && EffectKey.Target(effects[at].Key) == target)
                {
                    var term = EffectLaw.Term(effects[at].Amount, effects[at].Hits);
                    sum += EffectKey.Channel(effects[at].Key) == Healing ? term : -term;
                    at++;
                }

                var row = target & Slot.RowMask;
                scratch[row] = EffectLaw.StoredDelta(sum);
                touched |= sum != 0L ? 1ul << row : 0ul;
            }

            return touched;
        }

        public ulong Events(in WorldView world, ref LaneWriter writer, int chunk, ulong recheck, int* scratch, ulong* edges) => 0ul;

        public void EdgeEmits(in WorldView world, int chunk, int capacity, ulong* edges, int* scratch, ref EffectWriter next)
        {
        }

        public static HostPack Pack() =>
            new HostPack(new[] { LaneClass.Intrinsic, LaneClass.Intrinsic }, 2, new StatDef[0], new[] { new[] { 1000, 0 } }, new CardTerm[0], Hash);
    }

    internal abstract class V3Bench : IDisposable
    {
        public abstract string Name { get; }

        public abstract long Frame();

        public virtual void Dispose()
        {
        }
    }

    internal static unsafe class V3
    {
        public const int Entities = 1_000_000;
        public const int Records = 1_000_000;

        public static int Run(string scenario, int ms)
        {
            var failures = 0;
            failures += Measure(scenario, ms, "v3-seal-1m", () => new Seal1m());
            failures += Measure(scenario, ms, "v3-decide-1m", () => new Decide1m());
            failures += Measure(scenario, ms, "v3-commit-1m", () => new Commit1m());
            failures += Measure(scenario, ms, "v3-frame-1m", () => new Frame1m(Math.Max(1, Environment.ProcessorCount - 1)));
            failures += Measure(scenario, ms, "v3-rewind-1m", () => new Rewind1m());
            failures += Measure(scenario, ms, "v3-pack-compile-10k", () => new PackCompile10k());
            return failures;
        }

        static int Measure(string scenario, int ms, string name, Func<V3Bench> factory)
        {
            if (scenario != "all" && scenario != name && scenario != "v3")
            {
                return 0;
            }

            using var bench = factory();
            for (var warm = 0; warm < 32; warm++)
            {
                bench.Frame();
            }

            var samples = new double[Math.Max(5, Math.Min(2000, ms / 10))];
            var allocated = 0L;
            var sink = 0L;
            for (var at = 0; at < samples.Length; at++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                sink += bench.Frame();
                samples[at] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            Array.Sort(samples);
            var median = samples[samples.Length / 2];
            var p99 = samples[Math.Min(samples.Length - 1, samples.Length * 99 / 100)];
            var max = samples[samples.Length - 1];
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-22} median {1,9:0.000} ms  p99 {2,9:0.000} ms  max {3,9:0.000} ms  alloc={4}B  sink={5}",
                name, median, p99, max, allocated, sink));
            return allocated == 0L || name == "v3-pack-compile-10k" ? 0 : 1;
        }

        public static ulong[] Keys(int count, int entities, int seed)
        {
            var keys = new ulong[count];
            var state = (uint)seed | 1u;
            for (var at = 0; at < count; at++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                var target = (int)(state % (uint)entities);
                var source = (int)((state >> 7) % (uint)entities);
                keys[at] = EffectKey.Pack(target, source, (byte)(state & 3u), (byte)((state >> 3) & 1u), Origin.Direct, 0);
            }

            return keys;
        }

        public static ulong* AllLive(int chunks)
        {
            var live = (ulong*)Memory.AllocZeroed((nuint)chunks, sizeof(ulong));
            for (var chunk = 0; chunk < chunks; chunk++)
            {
                live[chunk] = ulong.MaxValue;
            }

            return live;
        }
    }

    internal sealed unsafe class Seal1m : V3Bench
    {
        readonly ulong[] keys = V3.Keys(V3.Records, V3.Entities, 7);
        readonly RawArena raw = new RawArena(V3.Records, 1, ArenaPolicy.Grow);
        readonly CanonicalArena canonical;
        readonly ulong* live;
        readonly int chunks = (V3.Entities + Slot.Rows - 1) / Slot.Rows;

        public Seal1m()
        {
            canonical = new CanonicalArena(chunks, 1, int.MaxValue, ArenaPolicy.Grow);
            live = V3.AllLive(chunks);
        }

        public override string Name => "v3-seal-1m";

        public CanonicalArena Canonical => canonical;

        public override long Frame()
        {
            var writer = raw.Writer(0);
            for (var at = 0; at < keys.Length; at++)
            {
                writer->Send(keys[at], 1 + (at & 15), 1);
            }

            var route = new RouteTable(live, null, chunks);
            var forward = default(NoForward);
            canonical.Run(raw, route, ref forward);
            return canonical.Count;
        }

        public override void Dispose()
        {
            canonical.Dispose();
            raw.Dispose();
            Memory.Free(live);
        }
    }

    internal sealed unsafe class Decide1m : V3Bench
    {
        readonly Seal1m seal = new Seal1m();
        readonly int* scratch = (int*)Memory.AllocZeroed((nuint)Slot.Rows, sizeof(int));
        readonly RawArena next = new RawArena(1 << 12, 1, ArenaPolicy.Grow);
        readonly CanonicalArena canonical;

        public Decide1m()
        {
            seal.Frame();
            canonical = seal.Canonical;
        }

        public override string Name => "v3-decide-1m";

        public override long Frame()
        {
            var kernels = default(BenchKernels);
            var view = default(WorldView);
            var touched = 0L;
            var writer = next.Writer(0);
            for (var index = 0; index < canonical.ActiveCount; index++)
            {
                var chunk = canonical.Active[index];
                var first = canonical.Start[chunk];
                touched += Bits(kernels.Decide(view, chunk, 0, canonical.Records + first, canonical.Start[chunk + 1] - first, scratch, null, null, ref *writer));
            }

            return touched;
        }

        static long Bits(ulong mask) => System.Numerics.BitOperations.PopCount(mask);

        public override void Dispose()
        {
            seal.Dispose();
            next.Dispose();
            Memory.Free(scratch);
        }
    }

    internal sealed unsafe class Commit1m : V3Bench
    {
        readonly int chunks = (V3.Entities + Slot.Rows - 1) / Slot.Rows;
        readonly int* value;
        readonly int* delta;
        readonly int* floor;
        readonly int* ceiling;
        readonly Width width = Widths.FromEnvironment();

        public Commit1m()
        {
            var cells = (nuint)chunks * Slot.Rows;
            value = (int*)Memory.AllocAligned(cells * sizeof(int), 64);
            delta = (int*)Memory.AllocAligned(cells * sizeof(int), 64);
            floor = (int*)Memory.AllocAligned((nuint)Slot.Rows * sizeof(int), 64);
            ceiling = (int*)Memory.AllocAligned((nuint)Slot.Rows * sizeof(int), 64);
            for (nuint at = 0; at < cells; at++)
            {
                value[at] = 500;
                delta[at] = (int)(at % 7) - 3;
            }

            LaneMath.Fill(floor, 0);
            LaneMath.Fill(ceiling, 1000);
        }

        public override string Name => "v3-commit-1m";

        public override long Frame()
        {
            var changed = 0L;
            for (var chunk = 0; chunk < chunks; chunk++)
            {
                var at = (long)chunk * Slot.Rows;
                changed += System.Numerics.BitOperations.PopCount(CommitFold.Lane(width, value + at, delta + at, floor, ceiling, ulong.MaxValue));
            }

            return changed;
        }

        public override void Dispose()
        {
            Memory.FreeAligned(value);
            Memory.FreeAligned(delta);
            Memory.FreeAligned(floor);
            Memory.FreeAligned(ceiling);
        }
    }

    internal sealed unsafe class Frame1m : V3Bench
    {
        readonly FrameLoop<BenchKernels> host;
        readonly ulong[] keys = V3.Keys(V3.Records, V3.Entities, 11);

        public Frame1m(int workers)
        {
            host = new FrameLoop<BenchKernels>(BenchKernels.Pack(), new HostOptions
            {
                EntityCapacity = V3.Entities + Slot.Rows,
                Workers = workers,
                RawCapacity = V3.Records * 2,
            });
            for (var at = 0; at < V3.Entities; at++)
            {
                host.Spawn(0);
            }
        }

        public override string Name => "v3-frame-1m";

        public FrameLoop<BenchKernels> Host => host;

        public override long Frame()
        {
            var writer = host.Sender(0);
            for (var at = 0; at < keys.Length; at++)
            {
                writer->Send(keys[at], 1 + (at & 3), 1);
            }

            host.Step();
            return (long)(host.LastDigest & 0x7FFFFFFFul);
        }

        public override void Dispose() => host.Dispose();
    }

    internal sealed unsafe class Rewind1m : V3Bench
    {
        readonly FrameLoop<BenchKernels> host;
        readonly ulong[] keys = V3.Keys(V3.Records / 10, V3.Entities, 13);

        public Rewind1m()
        {
            host = new FrameLoop<BenchKernels>(BenchKernels.Pack(), new HostOptions
            {
                EntityCapacity = V3.Entities + Slot.Rows,
                Workers = Math.Max(1, Environment.ProcessorCount - 1),
                RawCapacity = V3.Records,
                DigestEvery = 0,
            });
            for (var at = 0; at < V3.Entities; at++)
            {
                host.Spawn(0);
            }

            host.Step();
            host.Step();
        }

        public override string Name => "v3-rewind-1m";

        public override long Frame()
        {
            var epoch = host.World.Epoch;
            for (var step = 0; step < host.World.FramesPerEpoch; step++)
            {
                var writer = host.Sender(0);
                for (var at = 0; at < keys.Length; at++)
                {
                    writer->Send(keys[at], 1, 1);
                }

                host.Step();
            }

            return host.Rewind(epoch) ? 1L : 0L;
        }

        public override void Dispose() => host.Dispose();
    }

    internal sealed class PackCompile10k : V3Bench
    {
        public const int Rules = 10_000;
        readonly PackSource source = Source();

        public override string Name => "v3-pack-compile-10k";

        public override long Frame()
        {
            var compiled = PackCompiler.Compile(source);
            return compiled.Ok ? compiled.Diagnostics.Length : -1L;
        }

        static PackSource Source()
        {
            var values = new StringBuilder();
            values.Append("{\"schema\":1,\"nextLane\":1,\"rows\":[\n");
            values.Append("{\"id\":0,\"name\":\"Health\",\"class\":\"intrinsic\",\"default\":100},\n");
            values.Append("{\"id\":0,\"name\":\"Physical\",\"class\":\"channel\"}");
            for (var kind = 0; kind < 200; kind++)
            {
                values.Append(",\n{\"id\":").Append(kind.ToString(CultureInfo.InvariantCulture)).Append(",\"name\":\"K")
                    .Append(kind.ToString(CultureInfo.InvariantCulture)).Append("\",\"class\":\"kind\"}");
            }

            values.Append("\n]}\n");
            var rules = new StringBuilder();
            rules.Append("{\"schema\":1,\"rows\":[\n");
            for (var rule = 0; rule < Rules; rule++)
            {
                rules.Append(rule == 0 ? string.Empty : ",\n");
                rules.Append("{\"id\":").Append((10_000 + rule).ToString(CultureInfo.InvariantCulture))
                    .Append(",\"name\":\"deny_").Append(rule.ToString(CultureInfo.InvariantCulture))
                    .Append("\",\"layer\":\"gate\",\"priority\":").Append((rule % 1000).ToString(CultureInfo.InvariantCulture))
                    .Append(",\"when\":{\"eq\":[\"record.kind\",\"K").Append((rule % 200).ToString(CultureInfo.InvariantCulture))
                    .Append("\"]},\"then\":[{\"deny\":{}}]}");
            }

            rules.Append("\n]}\n");
            const string empty = "{\"schema\":1,\"rows\":[]}\n";
            return new PackSource(values.ToString(), rules.ToString(), empty, empty);
        }
    }
}
