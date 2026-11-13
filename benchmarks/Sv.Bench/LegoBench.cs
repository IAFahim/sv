extern alias v3;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using v3::Sv;
using v3::Sv.Host;
using v3::Sv.Rules;
using LegoKernels = v3::Sv.Kernels.Lego.HostKernels;

namespace SvV3Bench
{
    internal unsafe struct SendState
    {
        public Effect** Lists;
        public int* Counts;
        public EffectWriter** Writers;
    }

    internal static unsafe class LegoSend
    {
        public static void Part(void* state, int part)
        {
            var send = (SendState*)state;
            var list = send->Lists[part];
            var count = send->Counts[part];
            var writer = send->Writers[part];
            for (var at = 0; at < count; at++)
            {
                writer->Send(list[at].Key, list[at].Amount, list[at].Hits);
            }
        }
    }

    internal sealed class LegoShape
    {
        public string Name = "";
        public int Ticks;
        public int Timeline;
        public int Random;
        public int ActiveEvery = 1;
        public int Rotate = 1;
    }

    internal sealed unsafe class LegoWorld : IDisposable
    {
        public const int Entities = 1_000_000;
        public static readonly int Workers = int.TryParse(Environment.GetEnvironmentVariable("SV_BENCH_WORKERS"), out var w) && w > 0 ? w : 11;

        static readonly string[] Mix =
        {
            "dummy", "shielded", "dummy", "vampire", "dummy", "thorny", "dummy", "regen_low", "dummy", "ward_charges",
            "dummy", "masked", "dummy", "crit_target", "vampire", "bloodmage", "shielded", "thorny", "dummy", "worked_example_player",
        };

        public readonly PackPlan Plan;
        public readonly HostPack Pack;
        public readonly FrameLoop<LegoKernels> Host;
        public readonly int[] Slots;
        public readonly byte[] KindOfSlot;
        public readonly int Chunks;
        readonly v3::Sv.Host.Workers senders;
        readonly SendState* send;

        public LegoWorld(int digestEvery, int rawCapacity)
        {
            Plan = Compile(LegoFolder());
            Pack = HostPack.From(Plan);
            Host = new FrameLoop<LegoKernels>(Pack, new HostOptions
            {
                EntityCapacity = Entities + 2 * Slot.Rows,
                Workers = Workers,
                Parts = int.TryParse(Environment.GetEnvironmentVariable("SV_BENCH_PARTS"), out var parts) && parts > 0 ? parts : 64,
                ExternalWriters = Workers,
                RawCapacity = rawCapacity,
                DigestEvery = digestEvery,
            });
            var schema = Plan.Schema;
            var templateIndex = new Dictionary<string, int>();
            for (var t = 0; t < schema.Templates.Length; t++)
            {
                templateIndex[schema.Templates[t].Name] = t;
            }

            var kindLane = schema.KindIntrinsic;
            Slots = new int[Entities];
            KindOfSlot = new byte[Entities + 2 * Slot.Rows];
            Chunks = (Entities + Slot.Rows - 1) / Slot.Rows;
            var spawned = 0;
            for (var chunk = 0; spawned < Entities; chunk++)
            {
                var template = templateIndex[Mix[chunk % Mix.Length]];
                var kind = (byte)Math.Clamp(Pack.TemplateValues[template][kindLane], 0, 255);
                for (var row = 0; row < Slot.Rows && spawned < Entities; row++)
                {
                    var slot = Host.Spawn(template);
                    Slots[spawned] = slot;
                    KindOfSlot[slot] = kind;
                    spawned++;
                }
            }

            senders = new v3::Sv.Host.Workers(Workers);
            send = (SendState*)Memory.AllocZeroed(1, (nuint)sizeof(SendState));
            send->Lists = (Effect**)Memory.AllocZeroed((nuint)Workers, (nuint)sizeof(Effect*));
            send->Counts = (int*)Memory.AllocZeroed((nuint)Workers, sizeof(int));
            send->Writers = (EffectWriter**)Memory.AllocZeroed((nuint)Workers, (nuint)sizeof(EffectWriter*));
            for (var w = 0; w < Workers; w++)
            {
                send->Writers[w] = Host.Sender(w);
            }
        }

        public static string LegoFolder()
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder != null && !Directory.Exists(Path.Combine(folder.FullName, "packs", "lego")))
            {
                folder = folder.Parent;
            }

            return Path.Combine(folder?.FullName ?? throw new DirectoryNotFoundException("packs/lego"), "packs", "lego");
        }

        public static PackSource Read(string folder) => new PackSource(
            File.ReadAllText(Path.Combine(folder, PackSource.ValuesFile)),
            File.ReadAllText(Path.Combine(folder, PackSource.RulesFile)),
            File.ReadAllText(Path.Combine(folder, PackSource.CardsFile)),
            File.ReadAllText(Path.Combine(folder, PackSource.TemplatesFile)));

        static PackPlan Compile(string folder)
        {
            var compiled = PackCompiler.Compile(Read(folder));
            return compiled.Value ?? throw new InvalidOperationException("packs/lego: " + string.Join("\n", compiled.Diagnostics));
        }

        public int Channel(string name)
        {
            foreach (var channel in Plan.Schema.Channels)
            {
                if (channel.Name == name)
                {
                    return channel.Id;
                }
            }

            throw new ArgumentException(name);
        }

        public void Load(Effect*[] lists, int[] counts)
        {
            for (var w = 0; w < Workers; w++)
            {
                send->Lists[w] = lists[w];
                send->Counts[w] = counts[w];
            }
        }

        public void Send() => senders.Run(&LegoSend.Part, send, Workers);

        public void Dispose()
        {
            senders.Dispose();
            Host.Dispose();
            Memory.Free(send->Lists);
            Memory.Free(send->Counts);
            Memory.Free(send->Writers);
            Memory.Free(send);
        }
    }

    internal sealed unsafe class LegoRecords : IDisposable
    {
        public readonly Effect*[] Lists = new Effect*[LegoWorld.Workers];
        public readonly int[] Counts = new int[LegoWorld.Workers];
        public int Total;

        public LegoRecords(LegoWorld world, LegoShape shape, int seed)
        {
            var tick = world.Channel("Tick");
            var poison = world.Channel("Poison");
            var damage = new[] { world.Channel("Physical"), world.Channel("Physical"), world.Channel("Physical"), world.Channel("Physical"), world.Channel("Healing"), world.Channel("Healing"), world.Channel("Fire"), world.Channel("Poison") };
            var slots = world.Slots;
            var active = new List<int>();
            for (var at = 0; at < slots.Length; at++)
            {
                if (Slot.Chunk(slots[at]) % shape.ActiveEvery == 0)
                {
                    active.Add(slots[at]);
                }
            }

            var state = (uint)seed | 1u;
            var buckets = new List<Effect>[LegoWorld.Workers];
            for (var w = 0; w < LegoWorld.Workers; w++)
            {
                buckets[w] = new List<Effect>();
            }

            for (var at = 0; at < shape.Ticks; at++)
            {
                var target = active[(int)((long)at * active.Count / shape.Ticks)];
                buckets[(int)((long)at * LegoWorld.Workers / shape.Ticks)].Add(new Effect(EffectKey.Pack(target, EffectKey.NoSource, 0, (byte)tick, Origin.Direct, 0), 1, 1));
            }

            for (var at = 0; at < shape.Timeline; at++)
            {
                var target = active[(int)((long)at * active.Count / shape.Timeline)];
                buckets[(int)((long)at * LegoWorld.Workers / shape.Timeline)].Add(new Effect(EffectKey.Pack(target, EffectKey.NoSource, 0, (byte)poison, Origin.Direct, 0), 1, 1));
            }

            for (var w = 0; w < LegoWorld.Workers; w++)
            {
                buckets[w].Sort((a, b) => a.Key.CompareTo(b.Key));
            }

            for (var at = 0; at < shape.Random; at++)
            {
                var target = active[(int)(Next(ref state) % (uint)active.Count)];
                var source = slots[(int)(Next(ref state) % (uint)slots.Length)];
                var roll = Next(ref state);
                var channel = damage[roll & 7u];
                var amount = 1 + (int)((roll >> 3) & 7u);
                var hits = 1 + (int)((roll >> 6) & 1u);
                buckets[at % LegoWorld.Workers].Add(new Effect(EffectKey.Pack(target, source, world.KindOfSlot[source], (byte)channel, Origin.Direct, 0), amount, hits));
            }

            for (var w = 0; w < LegoWorld.Workers; w++)
            {
                Counts[w] = buckets[w].Count;
                Total += Counts[w];
                Lists[w] = (Effect*)Memory.Alloc((nuint)Math.Max(1, Counts[w]), (nuint)sizeof(Effect));
                for (var at = 0; at < Counts[w]; at++)
                {
                    Lists[w][at] = buckets[w][at];
                }
            }
        }

        static uint Next(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        public void Dispose()
        {
            for (var w = 0; w < LegoWorld.Workers; w++)
            {
                Memory.Free(Lists[w]);
            }
        }
    }

    internal static unsafe class Lego1m
    {
        public static readonly LegoShape[] Shapes =
        {
            new LegoShape { Name = "a", Random = 100_000, ActiveEvery = 10 },
            new LegoShape { Name = "b", Ticks = 1_000_000 },
            new LegoShape { Name = "c", Ticks = 1_000_000, Random = 1_000_000 },
            new LegoShape { Name = "d", Ticks = 1_000_000, Timeline = 500_000, Random = 1_500_000 },
            new LegoShape { Name = "r", Random = 1_000_000 },
            new LegoShape { Name = "c-rotate", Ticks = 1_000_000, Random = 1_000_000, Rotate = 2 },
        };

        static readonly string[] Phases = { "send", "seal", "log", "stats", "collect", "decide", "commit", "events", "digest", "end", "total" };

        public static int Run(string scenario, int ms)
        {
            var failures = 0;
            foreach (var shape in Shapes)
            {
                var name = "v3-lego-" + shape.Name;
                if (scenario == name || scenario == "lego" || scenario == name + "-nodigest")
                {
                    failures += Frames(shape, name, scenario.EndsWith("-nodigest", StringComparison.Ordinal) ? 0 : 1, ms);
                }
            }

            if (scenario == "v3-lego-memory" || scenario == "lego")
            {
                LegoMemory.Run();
            }

            if (scenario == "v3-lego-rewind" || scenario == "lego")
            {
                LegoRewind.Run();
            }

            if (scenario == "v3-pack-10k-full" || scenario == "lego")
            {
                Pack10k.Run();
            }

            if (scenario == "v3-lego-decide-shape" || scenario == "lego")
            {
                LegoDecideShape.Run();
            }

            if (scenario == "v3-pack-stability" || scenario == "lego")
            {
                PackStability.Run();
            }

            return failures;
        }

        static int Frames(LegoShape shape, string name, int digestEvery, int ms)
        {
            var build = Stopwatch.StartNew();
            using var world = new LegoWorld(digestEvery, 1 << 22);
            var sets = new LegoRecords[shape.Rotate];
            for (var at = 0; at < sets.Length; at++)
            {
                sets[at] = new LegoRecords(world, shape, 12345 + at);
            }

            var records = sets[0];
            world.Load(records.Lists, records.Counts);
            var host = world.Host;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0}: world built in {1:0.0} s, {2} records per frame sent, {3} chunks, workers {4}",
                name, build.Elapsed.TotalSeconds, records.Total, world.Host.World.ChunkCount, LegoWorld.Workers));
            for (var warm = 0; warm < 64; warm++)
            {
                world.Load(sets[warm % sets.Length].Lists, sets[warm % sets.Length].Counts);
                world.Send();
                host.Step();
            }

            var frames = Math.Max(30, Math.Min(400, ms / 20));
            var samples = new double[Phases.Length][];
            for (var p = 0; p < Phases.Length; p++)
            {
                samples[p] = new double[frames];
            }

            var activeChunks = new double[frames];
            var canonical = new double[frames];
            var allocated = 0L;
            var allocatedAll = 0L;
            var scale = 1000.0 / Stopwatch.Frequency;
            for (var f = 0; f < frames; f++)
            {
                world.Load(sets[f % sets.Length].Lists, sets[f % sets.Length].Counts);
                var before = GC.GetAllocatedBytesForCurrentThread();
                var beforeAll = GC.GetTotalAllocatedBytes(true);
                var start = Stopwatch.GetTimestamp();
                world.Send();
                var sent = Stopwatch.GetTimestamp();
                host.Step();
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                allocatedAll += GC.GetTotalAllocatedBytes(true) - beforeAll;
                var marks = host.Marks;
                samples[0][f] = (sent - start) * scale;
                for (var p = 1; p < FrameMarks.Count; p++)
                {
                    samples[p][f] = (marks[p] - marks[p - 1]) * scale;
                }

                samples[Phases.Length - 1][f] = (marks[FrameMarks.Ended] - start) * scale;
                activeChunks[f] = host.Canonical.ActiveCount;
                canonical[f] = host.Canonical.Count;
            }

            var line = new StringBuilder();
            line.Append(name).Append(digestEvery == 0 ? " (digest off)" : string.Empty).Append(" frames=").Append(frames);
            line.Append(string.Format(CultureInfo.InvariantCulture, " canonical={0:0} activeChunks={1:0}", Median(canonical), Median(activeChunks)));
            line.Append(" alloc(main)=").Append(allocated).Append("B alloc(all)=").Append(allocatedAll).Append('B');
            line.Append(string.Format(CultureInfo.InvariantCulture, " rawGrows={0} rawCapacity={1} inputLog={2:0.0}MB", host.Raw.Grows, host.Raw.Capacity, host.Log.Bytes / 1e6));
            line.Append(" digest=").Append(host.LastDigest.ToString("X16", CultureInfo.InvariantCulture)).Append('\n');
            for (var p = 0; p < Phases.Length; p++)
            {
                Array.Sort(samples[p]);
                line.Append(string.Format(CultureInfo.InvariantCulture, "  {0,-8} median {1,8:0.000} ms  p10 {2,8:0.000}  p90 {3,8:0.000}  max {4,8:0.000}\n",
                    Phases[p], Median(samples[p]), samples[p][frames / 10], samples[p][frames * 9 / 10], samples[p][frames - 1]));
            }

            Console.Write(line.ToString());
            foreach (var set in sets)
            {
                set.Dispose();
            }

            return allocated == 0L ? 0 : 1;
        }

        static double Median(double[] values)
        {
            var copy = (double[])values.Clone();
            Array.Sort(copy);
            return copy[copy.Length / 2];
        }
    }

    internal static unsafe class LegoMemory
    {
        public static void Run()
        {
            foreach (var rotate in new[] { 1, 3 })
            {
                Variant(rotate);
            }
        }

        static void Variant(int rotate)
        {
            var before = Settle();
            var world = new LegoWorld(1, 1 << 16);
            var rest = Settle();
            var w = world.Host.World;
            var bench = (long)world.Slots.Length * sizeof(int) + world.KindOfSlot.Length;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "v3-lego-memory at rest: private {0:0.0} MB, minus {1:0.0} MB bench arrays = {2:0.0} B/entity; {3}",
                (rest - before) / 1e6, bench / 1e6, (rest - before - bench) / (double)LegoWorld.Entities, Parts(world)));
            var sets = new LegoRecords[rotate];
            for (var at = 0; at < rotate; at++)
            {
                sets[at] = new LegoRecords(world, new LegoShape { Name = "history", Random = 100_000, ActiveEvery = 10 }, 777 + at);
            }

            var frames = w.RingEpochs * w.FramesPerEpoch + 4;
            for (var f = 0; f < frames; f++)
            {
                world.Load(sets[f % rotate].Lists, sets[f % rotate].Counts);
                world.Send();
                world.Host.Step();
            }

            var recordBytes = 0L;
            foreach (var set in sets)
            {
                recordBytes += (long)set.Total * sizeof(Effect);
            }

            var history = Settle();
            var entries = 0L;
            for (var e = w.OldestEpoch; e <= w.Epoch; e++)
            {
                entries += w.Journal.Count(e);
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "v3-lego-memory with history (ring {0} epochs x {1} frames, 10% chunks active, 100,000 records/frame, {2} rotating record set(s)): private {3:0.0} MB, minus {4:0.0} MB bench = {5:0.0} B/entity; journal entries over ring {6} ({7:0.0} MB of old lanes); {8}",
                w.RingEpochs, w.FramesPerEpoch, rotate, (history - before) / 1e6, (bench + recordBytes) / 1e6, (history - before - bench - recordBytes) / (double)LegoWorld.Entities,
                entries, entries * 256 / 1e6, Parts(world)));
            foreach (var set in sets)
            {
                set.Dispose();
            }

            world.Dispose();
        }

        static string Parts(LegoWorld world)
        {
            var host = world.Host;
            var w = host.World;
            var pool = w.Pool;
            var usedLanes = pool.Usable - pool.Available;
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var activeCapacity = (int)typeof(FrameLoop<LegoKernels>).GetField("activeCapacity", flags)!.GetValue(host)!;
            var scratchInts = (int)typeof(FrameLoop<LegoKernels>).GetField("scratchInts", flags)!.GetValue(host)!;
            return string.Format(CultureInfo.InvariantCulture,
                "table {0:0.0} MB ({1} lanes per chunk), pool segments {2:0.0} MB with {3} lanes in use ({4:0.0} MB), input log {5:0.0} MB, raw arena {6:0.0} MB, canonical arena {7:0.0} MB, decide scratch {8} active chunks x {9} B = {10:0.0} MB",
                w.TableBytes / 1e6, w.View.LanesPerChunk, pool.Bytes / 1e6, usedLanes, usedLanes * 256 / 1e6, host.Log.Bytes / 1e6, host.Raw.Capacity * sizeof(Effect) / 1e6,
                host.Canonical.Capacity * 2L * sizeof(Effect) / 1e6, activeCapacity, scratchInts * 4, (long)activeCapacity * scratchInts * 4 / 1e6);
        }

        static long Settle()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            return process.PrivateMemorySize64;
        }
    }

    internal static unsafe class LegoRewind
    {
        public static void Run()
        {
            using var world = new LegoWorld(0, 1 << 22);
            var host = world.Host;
            var w = host.World;
            foreach (var every in new[] { 10, 1 })
            {
                var shape = new LegoShape { Name = "rewind", Random = every == 10 ? 100_000 : 1_000_000, ActiveEvery = every };
                using var records = new LegoRecords(world, shape, 999);
                world.Load(records.Lists, records.Counts);
                foreach (var back in new[] { 1, 4, 16 })
                {
                    var times = new List<double>();
                    var lanes = new List<double>();
                    for (var sample = 0; sample < 9; sample++)
                    {
                        for (var f = 0; f < (back + 1) * w.FramesPerEpoch; f++)
                        {
                            world.Send();
                            host.Step();
                        }

                        var target = w.Epoch - (uint)back;
                        var changed = 0L;
                        for (var e = target; e <= w.Epoch; e++)
                        {
                            changed += w.Journal.Count(e);
                        }

                        var start = Stopwatch.GetTimestamp();
                        if (!host.Rewind(target))
                        {
                            throw new InvalidOperationException("rewind refused");
                        }

                        times.Add((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
                        lanes.Add(changed);
                    }

                    times.Sort();
                    lanes.Sort();
                    var median = times[times.Count / 2];
                    var medianLanes = lanes[lanes.Count / 2];
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "v3-lego-rewind activity 1/{0} chunks, {1} epochs back: median {2:0.000} ms (min {3:0.000} max {4:0.000}), journal entries {5:0}, {6:0.000} ms per 10,000 entries",
                        every, back, median, times[0], times[times.Count - 1], medianLanes, median * 10_000 / Math.Max(1, medianLanes)));
                }
            }
        }
    }

    internal static class Pack10k
    {
        public const int Rules = 10_000;

        public static void Run()
        {
            var folder = Path.Combine(Path.GetTempPath(), "sv-bench-pack10k");
            Directory.CreateDirectory(folder);
            var source = Source();
            File.WriteAllText(Path.Combine(folder, PackSource.ValuesFile), source.Values);
            File.WriteAllText(Path.Combine(folder, PackSource.RulesFile), source.Rules);
            File.WriteAllText(Path.Combine(folder, PackSource.CardsFile), source.Cards);
            File.WriteAllText(Path.Combine(folder, PackSource.TemplatesFile), source.Templates);
            var plans = new List<double>();
            var emits = new List<double>();
            long bytes = 0;
            var files = 0;
            for (var run = 0; run < 5; run++)
            {
                var clock = Stopwatch.StartNew();
                var compiled = PackCompiler.Compile(source);
                var plan = compiled.Value ?? throw new InvalidOperationException("10k pack: " + string.Join("\n", compiled.Diagnostics.Take(5)));
                plans.Add(clock.Elapsed.TotalMilliseconds);
                clock.Restart();
                var emitted = v3::Sv.Rules.Emit.PackEmitter.Emit(plan, "Bench.Pack10k");
                emits.Add(clock.Elapsed.TotalMilliseconds);
                bytes = emitted.Sum(file => (long)Encoding.UTF8.GetByteCount(file.Value));
                files = emitted.Count;
            }

            plans.Sort();
            emits.Sort();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "v3-pack-10k: rules {0}, plan median {1:0.0} ms (min {2:0.0} max {3:0.0}), emit median {4:0.0} ms (min {5:0.0} max {6:0.0}), {7} files, {8:0.00} MB of C#",
                Rules, plans[2], plans[0], plans[4], emits[2], emits[0], emits[4], files, bytes / 1e6));
            Console.WriteLine("v3-pack-10k folder: " + folder);
        }

        public static PackSource Source()
        {
            const int intrinsics = 200;
            const int stats = 40;
            const int channels = 60;
            const int kinds = 200;
            const int cards = 500;
            var values = new StringBuilder();
            values.Append("{\"schema\":1,\"nextLane\":").Append(stats + intrinsics + cards + 1).Append(",\"rows\":[\n");
            var lane = 0;
            for (var s = 0; s < stats; s++)
            {
                values.Append("{\"id\":").Append(lane++).Append(",\"name\":\"S").Append(s).Append("\",\"class\":\"stat\",\"default\":").Append(100 + s).Append("},\n");
            }

            values.Append("{\"id\":").Append(lane++).Append(",\"name\":\"Health\",\"class\":\"intrinsic\",\"default\":1000},\n");
            values.Append("{\"id\":").Append(lane++).Append(",\"name\":\"Kind\",\"class\":\"intrinsic\",\"default\":0,\"role\":\"kind\"},\n");
            for (var i = 2; i < intrinsics; i++)
            {
                values.Append("{\"id\":").Append(lane++).Append(",\"name\":\"I").Append(i).Append("\",\"class\":\"intrinsic\",\"default\":0},\n");
            }

            for (var c = 0; c < cards; c++)
            {
                values.Append("{\"id\":").Append(lane++).Append(",\"name\":\"CardCount").Append(c).Append("\",\"class\":\"intrinsic\",\"default\":0},\n");
            }

            for (var c = 0; c < channels; c++)
            {
                values.Append("{\"id\":").Append(c).Append(",\"name\":\"C").Append(c).Append("\",\"class\":\"channel\"},\n");
            }

            for (var k = 0; k < kinds; k++)
            {
                values.Append("{\"id\":").Append(k).Append(",\"name\":\"K").Append(k).Append("\",\"class\":\"kind\"}").Append(k + 1 < kinds ? ",\n" : "\n");
            }

            values.Append("]}\n");
            var card = new StringBuilder("{\"schema\":1,\"rows\":[\n");
            for (var c = 0; c < cards; c++)
            {
                card.Append("{\"id\":").Append(c + 1).Append(",\"name\":\"card").Append(c).Append("\",\"value\":\"CardCount").Append(c).Append("\"}").Append(c + 1 < cards ? ",\n" : "\n");
            }

            card.Append("]}\n");
            var rules = new StringBuilder("{\"schema\":1,\"rows\":[\n");
            rules.Append("{\"id\":1,\"name\":\"health_flow\",\"layer\":\"flow\",\"then\":[{\"subtract\":[\"Health\"");
            for (var c = 0; c < channels / 2; c++)
            {
                rules.Append(",\"C").Append(c).Append('"');
            }

            rules.Append("]},{\"add\":[\"Health\"");
            for (var c = channels / 2; c < channels; c++)
            {
                rules.Append(",\"C").Append(c).Append('"');
            }

            rules.Append("]}]},\n");
            rules.Append("{\"id\":2,\"name\":\"health_bounds\",\"layer\":\"fold\",\"value\":\"Health\",\"floor\":0,\"ceiling\":{\"stat\":\"S0\"}}");
            for (var r = 0; r < Rules - 2; r++)
            {
                var id = 100 + r;
                var channel = "C" + (r % (channels / 2)).ToString(CultureInfo.InvariantCulture);
                var self = "self.I" + (2 + r % (intrinsics - 2)).ToString(CultureInfo.InvariantCulture);
                var cardName = "card" + (r % cards).ToString(CultureInfo.InvariantCulture);
                rules.Append(",\n{\"id\":").Append(id).Append(",\"name\":\"r").Append(id).Append("\",\"card\":\"").Append(cardName).Append('"');
                switch (r % 5)
                {
                    case 0:
                        rules.Append(",\"layer\":\"gate\",\"priority\":").Append(r % 100).Append(",\"when\":{\"and\":[{\"eq\":[\"record.kind\",\"K").Append(r % kinds)
                            .Append("\"]},{\"eq\":[\"record.channel\",\"").Append(channel).Append("\"]}]},\"then\":[{\"deny\":{}}]}");
                        break;
                    case 1:
                        rules.Append(",\"layer\":\"gate\",\"priority\":0,\"when\":{\"and\":[{\"eq\":[\"record.channel\",\"").Append(channel)
                            .Append("\"]},{\"gt\":[\"").Append(self).Append("\",").Append(r % 7).Append("]}]},\"then\":[{\"additive\":0.").Append(1 + r % 9).Append("}]}");
                        break;
                    case 2:
                        rules.Append(",\"layer\":\"gate\",\"priority\":0,\"when\":{\"and\":[{\"eq\":[\"record.channel\",\"").Append(channel)
                            .Append("\"]},{\"ge\":[\"source.I").Append(2 + (r * 7) % (intrinsics - 2)).Append("\",1]}]},\"then\":[{\"added\":").Append(1 + r % 5).Append("}]}");
                        break;
                    case 3:
                        rules.Append(",\"layer\":\"emit\",\"when\":{\"and\":[{\"eq\":[\"record.channel\",\"").Append(channel)
                            .Append("\"]},{\"gt\":[\"record.taken\",0]}]},\"then\":[{\"emit\":{\"to\":\"Self\",\"channel\":\"C").Append(channels / 2 + r % (channels / 2)).Append("\",\"amount\":1}}]}");
                        break;
                    default:
                        rules.Append(",\"layer\":\"emit\",\"depthLimit\":1,\"when\":{\"eq\":[\"record.channel\",\"").Append(channel)
                            .Append("\"]},\"then\":[{\"emit\":{\"to\":\"Source\",\"channel\":\"same\",\"amount\":\"Taken\",\"scale\":\"S").Append(1 + r % (stats - 1)).Append("\"}}]}");
                        break;
                }
            }

            rules.Append("\n]}\n");
            var templates = "{\"schema\":1,\"rows\":[{\"id\":1,\"name\":\"plain\",\"values\":{}}]}\n";
            return new PackSource(values.ToString(), rules.ToString(), card.ToString(), templates);
        }
    }

    internal static class PackStability
    {
        public static void Run()
        {
            var lego = LegoWorld.Read(LegoWorld.LegoFolder());
            Check("lego", lego);
            Check("10k", Pack10k.Source());
        }

        static void Check(string name, PackSource source)
        {
            var first = Emit(source);
            var second = Emit(source);
            var same = first.Count == second.Count;
            for (var at = 0; same && at < first.Count; at++)
            {
                same = first[at].Key == second[at].Key && first[at].Value == second[at].Value;
            }

            var all = new StringBuilder();
            foreach (var file in first)
            {
                all.Append(file.Key).Append('\n').Append(file.Value);
            }

            var hash = BitConverter.ToUInt64(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(all.ToString())), 0);

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "v3-pack-stability {0}: two emissions equal = {1}, files {2}, output hash {3:X16}", name, same, first.Count, hash));
        }

        static List<KeyValuePair<string, string>> Emit(PackSource source)
        {
            var compiled = PackCompiler.Compile(source);
            var plan = compiled.Value ?? throw new InvalidOperationException(string.Join("\n", compiled.Diagnostics.Take(5)));
            return v3::Sv.Rules.Emit.PackEmitter.Emit(plan, "Bench.Stable");
        }
    }
}

namespace SvV3Bench
{
    internal static unsafe class LegoDecideShape
    {
        public static void Run()
        {
            var plan = v3::Sv.Rules.PackCompiler.Compile(LegoWorld.Read(LegoWorld.LegoFolder())).Value!;
            var pack = HostPack.From(plan);
            var dummy = Array.FindIndex(plan.Schema.Templates, t => t.Name == "dummy");
            using var host = new FrameLoop<LegoKernels>(pack, new HostOptions { EntityCapacity = 4 * Slot.Rows, Workers = 1 });
            for (var at = 0; at < 2 * Slot.Rows; at++)
            {
                host.Spawn(dummy);
            }

            host.Step();
            var tick = Channel(plan, "Tick");
            var physical = Channel(plan, "Physical");
            using var raw = new RawArena(1 << 16, 1, ArenaPolicy.Grow);
            var scratchInts = Math.Max(1, v3::Sv.Kernels.Lego.Kernels.ScratchLanes) << Slot.RowBits;
            var scratch = (int*)Memory.AllocZeroed((nuint)scratchInts, sizeof(int));
            var buffer = (byte*)Memory.AllocZeroed(64, 32);
            var effects = (Effect*)Memory.AllocZeroed(64, (nuint)sizeof(Effect));
            foreach (var channel in new[] { tick, physical })
            {
                foreach (var targets in new[] { 64, 8, 1 })
                {
                    var per = 64 / targets;
                    var count = 0;
                    for (var row = 0; row < targets; row++)
                    {
                        for (var r = 0; r < per; r++)
                        {
                            var source = channel == tick ? EffectKey.NoSource : Slot.Rows + r;
                            effects[count++] = new Effect(EffectKey.Pack(Slot.Of(0, row), source, 0, (byte)channel, Origin.Direct, 0), 1, 1);
                        }
                    }

                    var times = new List<double>();
                    var clears = new List<double>();
                    for (var sample = 0; sample < 9; sample++)
                    {
                        const int calls = 20_000;
                        var clearTicks = 0L;
                        var decideTicks = 0L;
                        for (var call = 0; call < calls; call++)
                        {
                            var t0 = Stopwatch.GetTimestamp();
                            Memory.Clear(scratch, (nuint)scratchInts * sizeof(int));
                            var t1 = Stopwatch.GetTimestamp();
                            var writer = raw.Writer(0);
                            v3::Sv.Kernels.Lego.Kernels.Decide(host.World.View, 0, host.World.ChunkCapacity * Slot.Rows, effects, count, scratch, buffer, ref *writer);
                            var t2 = Stopwatch.GetTimestamp();
                            raw.Reset();
                            clearTicks += t1 - t0;
                            decideTicks += t2 - t1;
                        }

                        times.Add(decideTicks * 1e9 / Stopwatch.Frequency / calls);
                        clears.Add(clearTicks * 1e9 / Stopwatch.Frequency / calls);
                    }

                    times.Sort();
                    clears.Sort();
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "v3-lego-decide-shape {0,-8} {1,2} targets x {2,2} records: kernel {3,8:0} ns per chunk call ({4,6:0.0} ns per record, {5,6:0.0} ns per target), scratch clear {6,6:0} ns",
                        channel == tick ? "Tick" : "Physical", targets, per, times[4], times[4] / 64, times[4] / targets, clears[4]));
                }
            }

            Memory.Free(scratch);
            Memory.Free(buffer);
            Memory.Free(effects);
        }

        static int Channel(PackPlan plan, string name) => Array.Find(plan.Schema.Channels, c => c.Name == name)!.Id;
    }
}
