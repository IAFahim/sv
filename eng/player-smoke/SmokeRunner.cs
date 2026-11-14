using System;
using System.Globalization;
using System.IO;
using Sv;
using Sv.Host;
using Sv.Unity;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

public static class SmokeRunner
{
    static ulong Hex(string text) => ulong.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    static int Int(string text) => int.Parse(text, CultureInfo.InvariantCulture);

    static ISvWorld Create(string pack, HostOptions options) => pack switch
    {
        "reference" => Sv.Kernels.Reference.PackWorld.Create(options),
        "lego" => Sv.Kernels.Lego.PackWorld.Create(options),
        "northstar3" => Sv.Kernels.NorthStar3.PackWorld.Create(options),
        _ => throw new ArgumentException("unknown pack " + pack),
    };

    static ulong PackHash(string pack) => pack == "reference" ? Sv.Kernels.Reference.Layout.PackHash : pack == "lego" ? Sv.Kernels.Lego.Layout.PackHash : Sv.Kernels.NorthStar3.Layout.PackHash;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var folder = Path.Combine(Application.streamingAssetsPath, "twin");
        var files = Directory.GetFiles(folder, "*.twin.txt", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);
        var failures = 0;
        var frames = 0;
        var cases = 0;
        foreach (var file in files)
        {
            foreach (var workers in new[] { 1, 11 })
            {
                cases++;
                string? error;
                try
                {
                    error = Play(file, workers, ref frames);
                }
                catch (Exception exception)
                {
                    error = exception.GetType().Name + ": " + exception.Message;
                }

                var name = Path.GetRelativePath(folder, file).Replace('\\', '/');
                if (error != null)
                {
                    failures++;
                    Debug.Log("SVSMOKE FAIL " + name + " workers " + workers + ": " + error);
                }
                else
                {
                    Debug.Log("SVSMOKE ok " + name + " workers " + workers);
                }
            }
        }

        Debug.Log("SVSMOKE summary cases " + cases + " frames " + frames + " failures " + failures + " maxJobWorkers " + JobsUtility.JobWorkerMaximumCount + " managedRuns " + SvProbe.ManagedRuns);
        SvWorlds.DisposeAll();
        Application.Quit(failures == 0 && SvProbe.ManagedRuns == 0 ? 0 : 1);
    }

    static string? Play(string file, int workers, ref int frames)
    {
        JobsUtility.JobWorkerCount = Math.Min(workers, JobsUtility.JobWorkerMaximumCount);
        var lines = File.ReadAllLines(file);
        if (lines[0] != "twin 1")
        {
            return "not a twin journal";
        }

        var head = lines[1].Split(' ');
        var pack = head[1];
        if (Hex(head[2]) != PackHash(pack))
        {
            return "pack hash differs";
        }

        var capacity = Int(lines[3].Split(' ')[1]);
        using var world = Create(pack, new HostOptions { Workers = workers, Parts = 64, EntityCapacity = capacity });
        var frame = -1;
        for (var at = 5; at < lines.Length; at++)
        {
            var part = lines[at].Split(' ');
            switch (part[0])
            {
                case "frame":
                    frame = Int(part[1]);
                    if (world.Frame != frame)
                    {
                        return "frame " + frame + ": world is at " + world.Frame;
                    }

                    break;
                case "spawn":
                    if (world.Spawn(Int(part[1])) != Int(part[2]))
                    {
                        return "frame " + frame + ": spawn got another slot";
                    }

                    break;
                case "kill":
                    if (!world.Kill(Int(part[1])))
                    {
                        return "frame " + frame + ": kill refused";
                    }

                    break;
                case "add":
                    world.Modify(StatOp.Add(Int(part[1]), Int(part[2]), (ShapeKind)Int(part[3]), Int(part[4]), Int(part[5]), long.MaxValue, (StatLayer)Int(part[6])));
                    break;
                case "remove":
                    world.Modify(StatOp.RemoveSource(Int(part[1]), Int(part[2])));
                    break;
                case "send":
                    if (!world.Send(Int(part[1]), Int(part[2]), Int(part[3]), Int(part[4]), Int(part[5]), Int(part[6]), (Origin)Int(part[7])))
                    {
                        return "frame " + frame + ": send refused";
                    }

                    break;
                case "digest":
                    world.Step();
                    frames++;
                    if (world.LastDigest != Hex(part[1]))
                    {
                        return "frame " + frame + ": digest " + world.LastDigest.ToString("X16") + " expected " + part[1];
                    }

                    break;
                case "final":
                    if (world.Digest() != Hex(part[1]))
                    {
                        return "final digest differs";
                    }

                    break;
                default:
                    return "unknown journal line: " + lines[at];
            }
        }

        for (var phase = 0; phase < FramePhases.Count; phase++)
        {
            if (world.ProbeOf((FramePhase)phase) == SvProbe.Managed)
            {
                return (FramePhase)phase + " ran as managed code";
            }
        }

        return null;
    }
}
