using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Sv.Host;

namespace Sv.Columns.Bench
{
    public static unsafe class Program
    {
        sealed class Traffic
        {
            public ColumnWorld World = null!;
            public int[] Slots = Array.Empty<int>();
            public int PerEntity;
            public int Frame;
        }

        public static int Main(string[] args)
        {
            var mode = args.Length > 0 ? args[0] : "all";
            var threads = Arg(args, 3, Environment.ProcessorCount);
            var frames = Arg(args, 4, 24);
            var failures = 0;
            if (mode == "stats")
            {
                using var compiled = BenchPack.Build(8, Arg(args, 2, 500), 12345).Compile();
                for (var t = 0; t < compiled.Tables->TemplateCount; t++)
                {
                    var table = compiled.Tables->Templates + t;
                    var lanesTerms = 0;
                    var conditional = 0;
                    for (var i = 0; i < table->TermCount; i++)
                    {
                        lanesTerms += table->Terms[i].Lane >= 0 ? 1 : 0;
                        conditional += table->Terms[i].Pred >= 0 ? 1 : 0;
                    }

                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "template {0}: groups {1}, terms {2} (lane {3}, conditional {4}), preds {5}", t, table->GroupCount, table->TermCount, lanesTerms, conditional, table->PredCount));
                }

                return 0;
            }

            using var workers = new Workers(threads);
            if (mode == "ops" || mode == "all")
            {
                failures += Run("ops", Arg(args, 1, 20_000), Arg(args, 2, 500), 0, threads, frames, workers);
                if (args.Length < 2)
                {
                    failures += Run("ops", 1_000_000, 500, 0, threads, frames, workers);
                }
            }

            if (mode == "records" || mode == "all")
            {
                failures += Run("records", Arg(args, 1, 20_000), 0, Arg(args, 2, 500), threads, frames, workers);
                if (args.Length < 2)
                {
                    failures += Run("records", 1_000_000, 0, 10, threads, frames, workers);
                }
            }

            if (mode == "mixed" || mode == "all")
            {
                failures += Run("mixed", Arg(args, 1, 1_000_000), Arg(args, 2, 500), 10, threads, frames, workers);
            }

            if (mode == "history")
            {
                var history = Arg(args, 5, 8);
                failures += RunHistory(Arg(args, 1, 20_000), Arg(args, 2, 500), 10, threads, frames, history, workers);
                if (args.Length < 2)
                {
                    failures += RunHistory(1_000_000, 500, 10, threads, frames, history, workers);
                }
            }

            Console.WriteLine(failures == 0 ? "all scenarios ok" : failures + " scenario(s) failed");
            return failures == 0 ? 0 : 1;
        }

        static readonly KernelChoice Choice = Environment.GetEnvironmentVariable("SV_COLUMNS_KERNELS") switch
        {
            "scalar" => KernelChoice.Scalar,
            "vector" => KernelChoice.Vector,
            _ => KernelChoice.Best,
        };

        static int Arg(string[] args, int at, int fallback) =>
            args.Length > at && int.TryParse(args[at], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

        static int Run(string name, int entities, int ops, int records, int threads, int frames, Workers workers)
        {
            var templates = 8;
            using var pack = BenchPack.Build(templates, ops, 12345).Compile();
            var same = Check(pack, Math.Min(entities, 3_000), records, threads, workers);
            var capacity = ((entities + templates * 64 + 63) / 64) * 64;
            using var world = new ColumnWorld(pack, capacity, threads, threads * 8, Choice);
            var traffic = new Traffic { World = world, Slots = Spawn(world, pack, entities, templates), PerEntity = records };
            using var runner = new ColumnRunner(world, workers);
            var handle = GCHandle.Alloc(traffic);
            var state = (void*)GCHandle.ToIntPtr(handle);
            var warm = Stopwatch.StartNew();
            for (var f = 0; f < 16 || warm.ElapsedMilliseconds < 2000; f++)
            {
                Send(workers, state, records, threads);
                runner.Step();
            }

            var totals = new double[frames];
            var reduces = new double[frames];
            var resolves = new double[frames];
            var sends = new double[frames];
            var allocated = 0L;
            for (var f = 0; f < frames; f++)
            {
                var sent = Stopwatch.GetTimestamp();
                Send(workers, state, records, threads);
                var before = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                runner.Step();
                var end = Stopwatch.GetTimestamp();
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                sends[f] = (start - sent) * 1000.0 / Stopwatch.Frequency;
                totals[f] = (end - start) * 1000.0 / Stopwatch.Frequency;
                reduces[f] = runner.ReduceMilliseconds;
                resolves[f] = runner.ResolveMilliseconds;
            }

            handle.Free();
            var work = (double)entities * (ops + records);
            var median = Median(totals);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "v4-{0} {1:N0} entities x ({2} ops + {3} records), {4} threads, {5}: step median {6:0.000} ms (p10 {7:0.000}, p90 {8:0.000}) = {9:0.000} ns per entity-op; reduce {10:0.000} ms, resolve {11:0.000} ms; send {12:0.000} ms; alloc {13} B; scalar twin {14}; digest {15:X16}",
                name, entities, ops, records, threads, world.KernelPath, median, Percentile(totals, 10), Percentile(totals, 90), median * 1e6 / work,
                Median(reduces), Median(resolves), Median(sends), allocated, same ? "equal" : "DIFFERENT", world.Digest()));
            return same && allocated == 0 ? 0 : 1;
        }

        static double StepMedian(ColumnWorld world, ColumnRunner runner, Workers workers, void* state, int records, int threads, int frames, ref long allocated)
        {
            var warm = Stopwatch.StartNew();
            for (var f = 0; f < 16 || warm.ElapsedMilliseconds < 2000; f++)
            {
                Send(workers, state, records, threads);
                runner.Step();
            }

            var totals = new double[frames];
            for (var f = 0; f < frames; f++)
            {
                Send(workers, state, records, threads);
                var before = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                runner.Step();
                totals[f] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            return Median(totals);
        }

        static int RunHistory(int entities, int ops, int records, int threads, int frames, int history, Workers workers)
        {
            var templates = 8;
            using var pack = BenchPack.Build(templates, ops, 12345).Compile();
            var capacity = ((entities + templates * 64 + 63) / 64) * 64;
            var allocated = 0L;
            double plainStep;
            using (var plain = new ColumnWorld(pack, capacity, threads, threads * 8, Choice))
            using (var plainRunner = new ColumnRunner(plain, workers))
            {
                var plainTraffic = new Traffic { World = plain, Slots = Spawn(plain, pack, entities, templates), PerEntity = records };
                var plainHandle = GCHandle.Alloc(plainTraffic);
                plainStep = StepMedian(plain, plainRunner, workers, (void*)GCHandle.ToIntPtr(plainHandle), records, threads, frames, ref allocated);
                plainHandle.Free();
            }

            using var world = new ColumnWorld(pack, capacity, threads, threads * 8, Choice, history);
            using var runner = new ColumnRunner(world, workers);
            var traffic = new Traffic { World = world, Slots = Spawn(world, pack, entities, templates), PerEntity = records };
            var handle = GCHandle.Alloc(traffic);
            var state = (void*)GCHandle.ToIntPtr(handle);
            var keptStep = StepMedian(world, runner, workers, state, records, threads, frames, ref allocated);
            var exact = true;
            var line = new System.Text.StringBuilder();
            var one = new[] { traffic.Slots[0] };
            var everyChunk = OnePerChunk(traffic.Slots);
            foreach (var edited in new[] { one, everyChunk })
            {
                line.Append(edited == one ? " one entity edited:" : " every chunk edited:");
                foreach (var depth in new[] { 1, history / 2, history })
                {
                    var times = new double[5];
                    for (var i = 0; i < times.Length; i++)
                    {
                        Send(workers, state, records, threads);
                        runner.Step();
                        var before = world.Digest();
                        var inputs = world.Inputs(world.Frame - depth);
                        for (var e = 0; e < edited.Length; e++)
                        {
                            inputs.AddAction(ColumnAction.Send(edited[e], 0, 0));
                        }

                        var bytes = GC.GetAllocatedBytesForCurrentThread();
                        var start = Stopwatch.GetTimestamp();
                        runner.Resimulate();
                        times[i] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                        allocated += GC.GetAllocatedBytesForCurrentThread() - bytes;
                        exact &= world.Digest() == before;
                    }

                    line.Append(string.Format(CultureInfo.InvariantCulture, " {0} frames {1:0.000} ms;", depth, Median(times)));
                }
            }

            var rewinds = new double[5];
            for (var i = 0; i < rewinds.Length; i++)
            {
                Send(workers, state, records, threads);
                runner.Step();
                var start = Stopwatch.GetTimestamp();
                world.Rewind(world.Frame - 1);
                rewinds[i] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                traffic.Frame--;
            }

            handle.Free();
            var lanes = pack.LaneNames.Count;
            var checkpointBytes = (history + 1) * ((double)world.Chunks * (lanes * 64 * 4 + 64 * 16 + 12)) / capacity;
            var logBytes = (history + 1) * records * 8.0;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "v4-history {0:N0} entities x ({1} ops + {2} records), {3} lanes, history {4}, {5} threads, {6}: step {7:0.000} ms without history, {8:0.000} ms with (+{9:0.000} ms); memory {10:0} B per entity for checkpoints + {11:0} B for the record log; rewind {12:0.000} ms; resimulate:{13} alloc {14} B; resimulated digest {15}",
                entities, ops, records, lanes, history, threads, world.KernelPath, plainStep, keptStep, keptStep - plainStep, checkpointBytes, logBytes,
                Median(rewinds), line, allocated, exact ? "bit-identical" : "DIFFERENT"));
            return exact && allocated == 0 ? 0 : 1;
        }

        static int[] OnePerChunk(int[] slots)
        {
            var chunks = new System.Collections.Generic.SortedDictionary<int, int>();
            foreach (var slot in slots)
            {
                if (!chunks.ContainsKey(slot >> 6))
                {
                    chunks.Add(slot >> 6, slot);
                }
            }

            var picked = new int[chunks.Count];
            chunks.Values.CopyTo(picked, 0);
            return picked;
        }

        static int[] Spawn(ColumnWorld world, CompiledPack pack, int entities, int templates)
        {
            var slots = new int[entities];
            for (var i = 0; i < entities; i++)
            {
                slots[i] = world.Spawn(pack.Template("t" + (i * templates / entities)));
            }

            return slots;
        }

        static void Send(Workers workers, void* state, int records, int threads)
        {
            if (records > 0)
            {
                workers.Run(&SendPart, state, threads);
            }

            ((Traffic)GCHandle.FromIntPtr((IntPtr)state).Target!).Frame++;
        }

        static void SendPart(void* state, int part)
        {
            var traffic = (Traffic)GCHandle.FromIntPtr((IntPtr)state).Target!;
            SendSlice(traffic.World, traffic.Slots, traffic.PerEntity, traffic.Frame, part, traffic.World.Writers);
        }

        static void SendSlice(ColumnWorld world, int[] slots, int perEntity, int frame, int part, int parts)
        {
            var writer = world.Writer(part);
            var channels = world.Pack.ChannelNames.Count;
            var from = (int)((long)slots.Length * part / parts);
            var to = (int)((long)slots.Length * (part + 1) / parts);
            var state = (uint)(frame * 0x9E3779B9u) ^ (uint)(part * 0x85EBCA6Bu) | 1u;
            for (var i = from; i < to; i++)
            {
                var target = slots[i];
                for (var r = 0; r < perEntity; r++)
                {
                    state ^= state << 13;
                    state ^= state >> 17;
                    state ^= state << 5;
                    var channel = (int)(state % (uint)channels);
                    var amount = (int)((state >> 8) % 61) - 20;
                    if ((state & 3u) == 0)
                    {
                        writer.SendFrom(slots[(int)((state >> 4) % (uint)slots.Length)], target, channel, amount);
                    }
                    else
                    {
                        writer.Send(target, channel, amount);
                    }
                }
            }
        }

        static bool Check(CompiledPack pack, int entities, int records, int threads, Workers workers)
        {
            var capacity = ((entities + 8 * 64 + 63) / 64) * 64;
            using var scalar = new ColumnWorld(pack, capacity, threads, threads, KernelChoice.Scalar);
            using var best = new ColumnWorld(pack, capacity, threads, threads * 3, Choice);
            var scalarSlots = Spawn(scalar, pack, entities, 8);
            var bestSlots = Spawn(best, pack, entities, 8);
            using var runner = new ColumnRunner(best, workers);
            for (var f = 0; f < 6; f++)
            {
                for (var part = 0; part < threads; part++)
                {
                    SendSlice(scalar, scalarSlots, records, f, part, threads);
                    SendSlice(best, bestSlots, records, f, threads - 1 - part, threads);
                }

                scalar.Step();
                runner.Step();
                if (scalar.Digest() != best.Digest())
                {
                    return false;
                }
            }

            return true;
        }

        static double Median(double[] values) => Percentile(values, 50);

        static double Percentile(double[] values, int percent)
        {
            var sorted = (double[])values.Clone();
            Array.Sort(sorted);
            return sorted[Math.Min(sorted.Length - 1, sorted.Length * percent / 100)];
        }
    }
}
