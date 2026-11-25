using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Sv;
using Sv.Host;
using Sv.Kernels.Lego;
using Sv.Unity;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

public static class SvUnityBench
{
    static readonly string[] TemplateMix =
    {
        "dummy", "shielded", "dummy", "vampire", "dummy", "thorny", "dummy", "regen_low", "dummy", "ward_charges",
        "dummy", "masked", "dummy", "crit_target", "vampire", "bloodmage", "shielded", "thorny", "dummy", "worked_example_player",
    };

    static readonly Channel[] SentChannels = { Channel.Physical, Channel.Fire, Channel.Poison, Channel.Healing, Channel.Tick, Channel.Physical, Channel.Healing, Channel.Fire };

    static readonly string[] PhaseNames = { "seal", "log", "stats", "collect", "decide", "commit", "events", "digest", "end" };

    public static void Run()
    {
        var shapes = Argument("-svBenchShapes", "1000000x1,1000000x4,20000x200,20000x500");
        var frames = int.Parse(Argument("-svBenchFrames", "32"), CultureInfo.InvariantCulture);
        var failures = 0;
        Report("SVBENCH unity " + Application.unityVersion + " jobWorkers " + JobsUtility.JobWorkerCount + " processors " + SystemInfo.processorCount + " cpu " + SystemInfo.processorType);
        foreach (var shape in shapes.Split(','))
        {
            var parts = shape.Split('x');
            try
            {
                Measure(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture), frames);
            }
            catch (Exception error)
            {
                failures++;
                Report("SVBENCH FAIL " + shape + " " + error);
            }
        }

        Report("SVBENCH done failures " + failures);
        if (Application.isBatchMode)
        {
            UnityEditor.EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }

    static void Measure(int entities, int recordsPerEntity, int frames)
    {
        var total = (long)entities * recordsPerEntity;
        var options = new HostOptions
        {
            EntityCapacity = entities + TemplateMix.Length * Slot.Rows,
            Workers = 1,
            RawCapacity = (int)Math.Min(int.MaxValue / 2, total + 4096),
            DigestEvery = 1,
            InputLogFrameLimit = total > 2_000_000 ? 0 : int.MaxValue,
        };
        var build = Stopwatch.StartNew();
        using var world = PackWorld.Create(options);
        var loop = world.Loop;
        var slots = new int[entities];
        var kindOfSlot = new byte[options.EntityCapacity];
        var templateValues = PackData.Create().TemplateValues;
        var spawned = 0;
        for (var chunk = 0; spawned < entities; chunk++)
        {
            var template = (int)Enum.Parse(typeof(Template), TemplateMix[chunk % TemplateMix.Length]);
            var kind = (byte)Math.Clamp(templateValues[template][(int)Intrinsic.Kind], 0, 255);
            for (var row = 0; row < Slot.Rows && spawned < entities; row++)
            {
                slots[spawned] = world.Spawn(template);
                kindOfSlot[slots[spawned]] = kind;
                spawned++;
            }
        }

        var keys = new ulong[total];
        var amounts = new int[total];
        var hits = new byte[total];
        var state = 0x9E3779B9u;
        var at = 0L;
        for (var entity = 0; entity < entities; entity++)
        {
            for (var op = 0; op < recordsPerEntity; op++)
            {
                var roll = Next(ref state);
                var pick = roll & 3u;
                var source = pick == 0u ? EffectKey.NoSource : pick == 1u ? slots[Next(ref state) % (uint)entities] : slots[(int)(((entity & ~63) + (Next(ref state) & 63u)) % entities)];
                var kind = source == EffectKey.NoSource ? (byte)0 : kindOfSlot[source];
                keys[at] = EffectKey.Pack(slots[entity], source, kind, (byte)SentChannels[(roll >> 2) & 7u], Origin.Direct, 0);
                amounts[at] = 1 + (int)((roll >> 5) & 7u);
                hits[at] = (byte)(1 + ((roll >> 8) & 1u));
                at++;
            }
        }

        var order = new int[total];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(keys, order);
        var sortedAmounts = new int[total];
        var sortedHits = new byte[total];
        for (var i = 0; i < order.Length; i++)
        {
            sortedAmounts[i] = amounts[order[i]];
            sortedHits[i] = hits[order[i]];
        }

        amounts = sortedAmounts;
        hits = sortedHits;
        var built = build.Elapsed.TotalSeconds;
        void Frame()
        {
            for (var r = 0L; r < total; r++)
            {
                var key = keys[r];
                loop.Send(EffectKey.Target(key), EffectKey.Source(key), EffectKey.Kind(key), EffectKey.Channel(key), amounts[r], hits[r], Origin.Direct);
            }
        }

        for (var warm = 0; warm < 16; warm++)
        {
            Frame();
            world.Step();
        }

        world.ClearProbes();
        var send = new double[frames];
        var step = new double[frames];
        var phases = new double[PhaseNames.Length][];
        for (var p = 0; p < phases.Length; p++)
        {
            phases[p] = new double[frames];
        }

        var scale = 1000.0 / Stopwatch.Frequency;
        var allocated = 0L;
        for (var f = 0; f < frames; f++)
        {
            var start = Stopwatch.GetTimestamp();
            Frame();
            var sent = Stopwatch.GetTimestamp();
            var before = GC.GetAllocatedBytesForCurrentThread();
            world.Step();
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            var marks = loop.Marks;
            send[f] = (sent - start) * scale;
            step[f] = (marks[FrameMarks.Ended] - marks[FrameMarks.Begin]) * scale;
            for (var p = 0; p < PhaseNames.Length; p++)
            {
                phases[p][f] = (marks[p + 1] - marks[p]) * scale;
            }
        }

        var line = new StringBuilder();
        var stepMedian = Median(step);
        line.Append(string.Format(CultureInfo.InvariantCulture,
            "SVBENCH entities {0} recordsPerEntity {1} records {2} built {3:0.0}s step median {4:0.000} ms ({5:0.00} ns/record) p90 {6:0.000} send(main thread) median {7:0.000} ms alloc(main) {8} B burstProbes {9} digest {10:X16}",
            entities, recordsPerEntity, total, built, stepMedian, stepMedian * 1e6 / total, Percentile(step, 0.9), Median(send), allocated, Probes(world), world.LastDigest));
        for (var p = 0; p < PhaseNames.Length; p++)
        {
            line.Append(string.Format(CultureInfo.InvariantCulture, " {0} {1:0.000}", PhaseNames[p], Median(phases[p])));
        }

        Report(line.ToString());
    }

    static string Probes(ISvWorld world)
    {
        var text = new StringBuilder();
        foreach (FramePhase phase in Enum.GetValues(typeof(FramePhase)))
        {
            text.Append(world.ProbeOf(phase));
        }

        return text.ToString();
    }

    static void Report(string line)
    {
        UnityEngine.Debug.Log(line);
        Console.WriteLine(line);
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

    static uint Next(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    static double Median(double[] values) => Percentile(values, 0.5);

    static double Percentile(double[] values, double at)
    {
        var copy = (double[])values.Clone();
        Array.Sort(copy);
        return copy[Math.Min(copy.Length - 1, (int)(copy.Length * at))];
    }
}
