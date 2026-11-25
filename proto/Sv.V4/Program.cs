using System;
using System.Diagnostics;
using System.Globalization;
using Sv.Host;

namespace Sv.V4
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            var mode = args.Length > 0 ? args[0] : "records";
            var entities = Arg(args, 1, 20_000);
            var perEntity = Arg(args, 2, 500);
            var threads = Arg(args, 3, Environment.ProcessorCount);
            var frames = Arg(args, 4, 32);
            var checkFrames = Arg(args, 5, 4);
            if (mode == "stream")
            {
                return StreamFloor(entities, perEntity, threads, frames);
            }

            return mode == "columns" ? ColumnBench.Run(entities, perEntity, threads, frames, checkFrames) : RecordBench(entities, perEntity, threads, frames, checkFrames);
        }

        static int Arg(string[] args, int at, int fallback) =>
            args.Length > at && int.TryParse(args[at], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

        static int RecordBench(int entities, int recordsPerEntity, int threads, int frames, int checkFrames)
        {
            using var workers = new Workers(threads);
            var equal = CheckAgainstReference(Math.Min(entities, 4_000), recordsPerEntity, threads, checkFrames, workers);
            using var world = new LaneWorld(entities);
            Templates.Spawn(world, 1);
            using var stream = new RecordStream(world, recordsPerEntity, threads, "mixed", false);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "v4 records: entities {0}, records per entity {1} (grouped targets, mixed sources, canonical order), threads {2}, records per frame {3}",
                entities, recordsPerEntity, threads, stream.Total));
            using var timed = new LaneEngine(world, stream, workers, true);
            for (var f = 0; f < 16; f++)
            {
                timed.StepOneFrame();
            }

            var totals = new double[frames];
            var reduces = new double[frames];
            var resolves = new double[frames];
            var allocated = 0L;
            for (var f = 0; f < frames; f++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                timed.StepOneFrame();
                totals[f] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                reduces[f] = timed.ReduceMilliseconds;
                resolves[f] = timed.ResolveMilliseconds;
            }

            var median = Median(totals);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  total median {0:0.000} ms (p10 {1:0.000}, p90 {2:0.000}) = {3:0.000} ns per record; reduce {4:0.000} ms, resolve {5:0.000} ms; alloc(main) {6} B; unsupported {7}; digest {8:X16}",
                median, Percentile(totals, 10), Percentile(totals, 90), median * 1e6 / stream.Total, Median(reduces), Median(resolves), allocated, timed.Unsupported, world.Digest()));
            return equal && allocated == 0 && timed.Unsupported == 0 ? 0 : 1;
        }

        static bool CheckAgainstReference(int entities, int recordsPerEntity, int threads, int checkFrames, Workers workers)
        {
            using var reference = new LaneWorld(entities);
            Templates.Spawn(reference, 20);
            using var stream = new RecordStream(reference, recordsPerEntity, threads, "mixed", true);
            using var scalarWorld = new LaneWorld(entities);
            using var vectorWorld = new LaneWorld(entities);
            scalarWorld.CopyFrom(reference);
            vectorWorld.CopyFrom(reference);
            using var scalar = new LaneEngine(scalarWorld, stream, workers, false);
            using var vector = new LaneEngine(vectorWorld, stream, workers, true);
            var sent = checkFrames > 0 ? stream.Gather() : Array.Empty<Effect>();
            var emitted = Array.Empty<Effect>();
            var equal = true;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "check against the per-record reference: {0} entities at health / 20, {1} records per entity, 1 in 16 amounts negative, {2} frames", entities, recordsPerEntity, checkFrames));
            for (var f = 0; f < checkFrames; f++)
            {
                emitted = RecordReference.StepOneFrame(reference, sent, emitted);
                scalar.StepOneFrame();
                vector.StepOneFrame();
                var expected = reference.Digest();
                var same = expected == scalarWorld.Digest() && expected == vectorWorld.Digest();
                equal &= same;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  frame {0}: reference {1:X16} scalar {2:X16} vector {3:X16} {4}",
                    f, expected, scalarWorld.Digest(), vectorWorld.Digest(), same ? "equal" : "DIFFERENT"));
            }

            if (checkFrames > 0)
            {
                Console.WriteLine("  " + Census(reference));
            }

            return equal;
        }

        static unsafe int StreamFloor(int entities, int recordsPerEntity, int threads, int frames)
        {
            using var workers = new Workers(threads);
            using var world = new LaneWorld(entities);
            Templates.Spawn(world, 1);
            using var stream = new RecordStream(world, recordsPerEntity, threads, "mixed", false);
            var sums = (long*)Memory.AllocZeroed((nuint)threads, sizeof(long));
            var state = (StreamState*)Memory.AllocZeroed(1, (nuint)sizeof(StreamState));
            state->Lists = stream.Lists;
            state->Counts = stream.Counts;
            state->Sums = sums;
            var samples = new double[frames];
            for (var f = 0; f < frames + 8; f++)
            {
                var start = Stopwatch.GetTimestamp();
                workers.Run(&ReadEveryRecord, state, threads);
                if (f >= 8)
                {
                    samples[f - 8] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "stream floor: read {0} records ({1:0} MB) once: median {2:0.000} ms = {3:0.000} ns per record, {4:0.0} GB/s; checksum {5}",
                stream.Total, stream.Total * 16 / 1e6, Median(samples), Median(samples) * 1e6 / stream.Total, stream.Total * 16 / 1e6 / Median(samples), sums[0]));
            Memory.Free(sums);
            Memory.Free(state);
            return 0;
        }

        unsafe struct StreamState
        {
            public Effect** Lists;
            public int* Counts;
            public long* Sums;
        }

        static unsafe void ReadEveryRecord(void* raw, int writer)
        {
            var state = (StreamState*)raw;
            var records = state->Lists[writer];
            var sum = 0L;
            for (var at = 0; at < state->Counts[writer]; at++)
            {
                sum += records[at].Amount + (long)(records[at].Key >> 42);
            }

            state->Sums[writer] = sum;
        }

        public static string Census(LaneWorld world)
        {
            var dead = 0;
            var low = 0;
            var shieldsLeft = 0;
            for (var slot = 0; slot < world.Entities; slot++)
            {
                var events = world.Read(slot, Lane.Events);
                dead += (events & EventBit.Dead) != 0 ? 1 : 0;
                low += (events & EventBit.LowHealth) != 0 ? 1 : 0;
                shieldsLeft += world.Read(slot, Lane.Shield) > 0 ? 1 : 0;
            }

            return string.Format(CultureInfo.InvariantCulture, "after check: dead {0}, low health {1}, shields left {2}", dead, low, shieldsLeft);
        }

        public static double Median(double[] values) => Percentile(values, 50);

        public static double Percentile(double[] values, int percent)
        {
            var copy = (double[])values.Clone();
            Array.Sort(copy);
            return copy[Math.Min(copy.Length - 1, copy.Length * percent / 100)];
        }
    }
}
