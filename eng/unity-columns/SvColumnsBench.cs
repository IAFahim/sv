using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Sv.Columns;
using Sv.Columns.Bench;
using Sv.Columns.Unity;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

public static class SvColumnsBench
{
    sealed class Traffic
    {
        public ColumnWorld World = null!;
        public int[] Slots = Array.Empty<int>();
        public int PerEntity;
        public int Frame;
    }

    struct SendJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction]
        public IntPtr State;

        public void Execute(int part)
        {
            var traffic = (Traffic)GCHandle.FromIntPtr(State).Target!;
            SendSlice(traffic.World, traffic.Slots, traffic.PerEntity, traffic.Frame, part, traffic.World.Writers);
        }
    }

    public static void Run()
    {
        var shapes = Argument("-svColumnsShapes", "ops:20000x500,ops:1000000x500,records:20000x500,records:1000000x10,mixed:1000000x500+10");
        var frames = int.Parse(Argument("-svColumnsFrames", "24"), CultureInfo.InvariantCulture);
        var choices = Argument("-svColumnsKernels", "best");
        var failures = 0;
        Report("SVCOLUMNS unity " + Application.unityVersion + " runtime " + RuntimeInformation.FrameworkDescription + " jobWorkers " + JobsUtility.JobWorkerCount + " processors " + SystemInfo.processorCount + " cpu " + SystemInfo.processorType);
        foreach (var shape in shapes.Split(','))
        {
            try
            {
                var name = shape.Substring(0, shape.IndexOf(':'));
                var sizes = shape.Substring(shape.IndexOf(':') + 1).Split('x');
                var entities = int.Parse(sizes[0], CultureInfo.InvariantCulture);
                var counts = sizes[1].Split('+');
                var ops = name == "records" ? 0 : int.Parse(counts[0], CultureInfo.InvariantCulture);
                var records = name == "records" ? int.Parse(counts[0], CultureInfo.InvariantCulture) : counts.Length > 1 ? int.Parse(counts[1], CultureInfo.InvariantCulture) : 0;
                foreach (var choice in choices.Split(','))
                {
                    failures += Measure(name, entities, ops, records, frames, (KernelChoice)Enum.Parse(typeof(KernelChoice), choice, true));
                }
            }
            catch (Exception error)
            {
                failures++;
                Report("SVCOLUMNS FAIL " + shape + " " + error);
            }
        }

        Report("SVCOLUMNS done failures " + failures);
        if (Application.isBatchMode)
        {
            UnityEditor.EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }

    static int Measure(string name, int entities, int ops, int records, int frames, KernelChoice kernels)
    {
        const int templates = 8;
        var threads = JobsUtility.JobWorkerCount + 1;
        using var pack = BenchPack.Build(templates, ops, 12345).Compile();
        var same = Check(pack, Math.Min(entities, 3_000), records, threads, kernels);
        var capacity = ((entities + templates * 64 + 63) / 64) * 64;
        using var world = new ColumnWorld(pack, capacity, threads, threads * 8, kernels);
        using var stepper = new ColumnStepper(world);
        var traffic = new Traffic { World = world, Slots = Spawn(world, pack, entities, templates), PerEntity = records };
        var handle = GCHandle.Alloc(traffic);
        try
        {
            var state = GCHandle.ToIntPtr(handle);
            var warm = Stopwatch.StartNew();
            for (var f = 0; f < 16 || warm.ElapsedMilliseconds < 2000; f++)
            {
                Send(state, traffic, threads);
                stepper.Step();
            }

            var steps = new double[frames];
            var sends = new double[frames];
            var allocated = 0L;
            for (var f = 0; f < frames; f++)
            {
                var sent = Stopwatch.GetTimestamp();
                Send(state, traffic, threads);
                var before = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                stepper.Step();
                var end = Stopwatch.GetTimestamp();
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                sends[f] = (start - sent) * 1000.0 / Stopwatch.Frequency;
                steps[f] = (end - start) * 1000.0 / Stopwatch.Frequency;
            }

            var median = Percentile(steps, 50);
            Report(string.Format(CultureInfo.InvariantCulture,
                "SVCOLUMNS {0} entities {1} ops {2} records {3} threads {4} kernels {5}: step median {6:0.000} ms (p10 {7:0.000}, p90 {8:0.000}) = {9:0.000} ns per entity-op; send {10:0.000} ms; alloc {11} B; scalar twin {12}; digest {13:X16}",
                name, entities, ops, records, threads, world.KernelPath, median, Percentile(steps, 10), Percentile(steps, 90),
                median * 1e6 / ((double)entities * (ops + records)), Percentile(sends, 50), allocated, same ? "equal" : "DIFFERENT", world.Digest()));
            return same ? 0 : 1;
        }
        finally
        {
            handle.Free();
        }
    }

    static void Send(IntPtr state, Traffic traffic, int threads)
    {
        if (traffic.PerEntity > 0)
        {
            new SendJob { State = state }.Schedule(threads, 1).Complete();
        }

        traffic.Frame++;
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
                    writer.Send(target, slots[(int)((state >> 4) % (uint)slots.Length)], channel, amount);
                }
                else
                {
                    writer.Send(target, channel, amount);
                }
            }
        }
    }

    static bool Check(CompiledPack pack, int entities, int records, int threads, KernelChoice kernels)
    {
        var capacity = ((entities + 8 * 64 + 63) / 64) * 64;
        using var scalar = new ColumnWorld(pack, capacity, threads, threads, KernelChoice.Scalar);
        using var best = new ColumnWorld(pack, capacity, threads, threads * 3, kernels);
        using var stepper = new ColumnStepper(best);
        var scalarSlots = Spawn(scalar, pack, entities, 8);
        var bestSlots = Spawn(best, pack, entities, 8);
        for (var f = 0; f < 6; f++)
        {
            for (var part = 0; part < threads; part++)
            {
                SendSlice(scalar, scalarSlots, records, f, part, threads);
                SendSlice(best, bestSlots, records, f, threads - 1 - part, threads);
            }

            scalar.Step();
            stepper.Step();
            if (scalar.Digest() != best.Digest())
            {
                return false;
            }
        }

        return true;
    }

    static double Percentile(double[] values, int percent)
    {
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        return sorted[Math.Min(sorted.Length - 1, sorted.Length * percent / 100)];
    }

    static string Argument(string name, string fallback)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == name)
            {
                return args[i + 1];
            }
        }

        return fallback;
    }

    static void Report(string line)
    {
        Console.WriteLine(line);
        UnityEngine.Debug.Log(line);
    }
}
