using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Sv.NorthStar2;
using Tl;
using Tl.Gen.Tlb;
using Tl.Unity;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs.LowLevel.Unsafe;

namespace Sv.Unity.TlTests
{
    public static unsafe class SvTlBench
    {
        const int Duration = 1000;
        const int WarmTicks = 32;
        const int RoundTicks = 32;
        const int Rounds = 7;

        static StreamWriter output;

        static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }

            return fallback;
        }

        static void Line(string text)
        {
            output.WriteLine(text);
            output.Flush();
            UnityEngine.Debug.Log("SVTLBENCH " + text);
        }

        public static void Compile() => UnityEditor.EditorApplication.Exit(0);

        static TypedTrack Poison(int amount)
        {
            var track = new TypedTrack { Type = typeof(TlNoSettings<PoisonClip>) };
            var clip = new PoisonClip { Amount = amount };
            track.Clips.Add(new TypedClip { Type = typeof(PoisonClip), Start = 0u, End = Duration, Data = new ReadOnlySpan<byte>(&clip, sizeof(PoisonClip)).ToArray() });
            return track;
        }

        static byte[] WideLibrary()
        {
            var w2 = new TypedTimeline { Name = "w2", Duration = Duration };
            w2.Tracks.Add(Poison(1));
            w2.Tracks.Add(Poison(-1));
            var w3 = new TypedTimeline { Name = "w3", Duration = Duration };
            w3.Tracks.Add(Poison(2));
            w3.Tracks.Add(Poison(-1));
            w3.Tracks.Add(Poison(-1));
            return SvNorthStar2AdapterTests.Library(new List<TypedTimeline> { w2, w3 });
        }

        public static void Run()
        {
            var code = 0;
            output = new StreamWriter(Arg("-svOut", "svtlbench.txt"), true);
            try
            {
                BurstCompiler.Options.EnableBurstSafetyChecks = false;
                JobsUtility.JobDebuggerEnabled = false;
                NativeLeakDetection.Mode = NativeLeakDetectionMode.Disabled;
                var tag = Arg("-svTag", "run");
                var rows = int.Parse(Arg("-svRows", "1048576"));
                Line($"settings tag={tag} burst={BurstCompiler.IsEnabled} safety={BurstCompiler.Options.EnableBurstSafetyChecks} jobDebugger={JobsUtility.JobDebuggerEnabled} leak={NativeLeakDetection.Mode} workers={JobsUtility.JobWorkerCount} unity={UnityEngine.Application.unityVersion} rows={rows} W=2.5 cross=0% kinds=tl-generated(Damage<-Poison feeds) digestEvery=8 Iw=1");
                var library = WideLibrary();
                Cell(library, rows, false);
                Cell(library, rows, true);
                Line("done tag=" + tag);
            }
            catch (Exception e)
            {
                Line("exception " + e);
                code = 1;
            }

            output.Dispose();
            UnityEditor.EditorApplication.Exit(code);
        }

        static void Cell(byte[] library, int rows, bool nullWindow)
        {
            var setup = Stopwatch.StartNew();
            using var rig = new SvNorthStar2AdapterTests.Rig(library, rows, r => 40 + r % 50, r => r);
            rig.Host.DigestEvery = 8;
            var u = rig.World.Unmanaged;
            u.GetUnsafeSystemRef<SvBeginSystem>(rig.Begin).Split = true;
            for (var r = 0; r < rows; r++)
            {
                rig.Manager.GetBuffer<TimelineRequest>(rig.Entities[r]).Add(new TimelineRequest { Slot = -1, Timeline = (ushort)(1 + (r & 1)), At = 0, Op = RequestOp.Start });
            }

            rig.Frame();
            setup.Stop();
            var boardAfterSetup = rig.Board.MemoryBytes;
            Line($"setup rows={rows} ms={setup.Elapsed.TotalMilliseconds:F1} boardBytes={boardAfterSetup} hostNativeBytes={rig.Host.NativeBytes} svEcsBytesPerRow={sizeof(SvSlot) + sizeof(SvSlotCleanup) + sizeof(NsHp) + sizeof(HpDelta) + sizeof(NsMaxHp) + sizeof(NsArmor) + sizeof(NsTarget) + sizeof(Outgoing)}+1bit");
            var statOps = nullWindow ? 0 : (int)Math.Round(0.05 * 2.5 * rows * 2.0 / 300.0);
            var random = new Random(0x7B);
            var live = new Queue<(int slot, int source)>(statOps * Rounds * RoundTicks + 16);
            var source = 1;
            var stages = new[] { "svMain", "scatter", "fold", "kinds", "send", "derive", "digest", "total" };
            var rounds = new List<double[]>();
            long mainBytes = 0;
            SvProbe.ManagedRuns = 0;
            var freq = (double)Stopwatch.Frequency;
            for (var w = 0; w < WarmTicks; w++)
            {
                Tick(rig, nullWindow, statOps, random, live, ref source, null);
            }

            for (var round = 0; round < Rounds; round++)
            {
                var sums = new double[stages.Length];
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var t = 0; t < RoundTicks; t++)
                {
                    Tick(rig, nullWindow, statOps, random, live, ref source, sums);
                }

                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                if (round > 0)
                {
                    for (var i = 0; i < sums.Length; i++) sums[i] = sums[i] * 1000.0 / freq / RoundTicks;
                    rounds.Add(sums);
                    mainBytes += allocated;
                }
            }

            using var tickQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<TimelineKindTick>().WithOptions(EntityQueryOptions.IncludeSystems).Build(rig.Manager);
            var kindManaged = tickQuery.GetSingleton<TimelineKindTick>().ManagedRuns;
            var parts = new System.Text.StringBuilder();
            for (var i = 0; i < stages.Length; i++)
            {
                var v = new List<double>();
                foreach (var r in rounds) v.Add(r[i]);
                v.Sort();
                parts.Append(' ').Append(stages[i]).Append('=').Append(v[v.Count / 2].ToString("F3"));
            }

            Line($"cell shape=P1 W=2.5 cross=0% window={(nullWindow ? "null(tl-only)" : "sv+tl")} statOps={statOps}{parts} mainBytes={mainBytes} svProbeManaged={SvProbe.ManagedRuns} kindProbeManaged={kindManaged} hp0={rig.Manager.GetComponentData<NsHp>(rig.Entities[0]).Value} delta0={rig.Manager.GetComponentData<HpDelta>(rig.Entities[0]).Value}");
            if (!nullWindow)
            {
                Modifiers(rig, rows, live, ref source);
            }
        }

        static void Tick(SvNorthStar2AdapterTests.Rig rig, bool nullWindow, int statOps, Random random, Queue<(int slot, int source)> live, ref int source, double[] sums)
        {
            if (!nullWindow)
            {
                for (var o = 0; o < statOps; o++)
                {
                    if ((o & 1) == 0 || live.Count == 0)
                    {
                        var slot = random.Next(rig.Rows);
                        rig.Host.Enqueue(StatOp.Add(slot, rig.MaxHp, ModifierKind.Added, random.Next(9) - 4, source));
                        live.Enqueue((slot, source));
                        source++;
                    }
                    else
                    {
                        var old = live.Dequeue();
                        rig.Host.Enqueue(StatOp.RemoveSource(old.slot, old.source));
                    }
                }
            }

            var u = rig.World.Unmanaged;
            var s0 = Stopwatch.GetTimestamp();
            if (!nullWindow) rig.Begin.Update(u);
            var s1 = Stopwatch.GetTimestamp();
            rig.TlGroup.Update();
            rig.Manager.CompleteAllTrackedJobs();
            var s2 = Stopwatch.GetTimestamp();
            if (!nullWindow) rig.End.Update(u);
            rig.Manager.CompleteAllTrackedJobs();
            var s3 = Stopwatch.GetTimestamp();
            if (sums == null)
            {
                return;
            }

            if (!nullWindow)
            {
                var bt = u.GetUnsafeSystemRef<SvBeginSystem>(rig.Begin).Times;
                var et = u.GetUnsafeSystemRef<SvEndSystem>(rig.End).Times;
                sums[0] += bt.Sparse;
                sums[1] += bt.Scatter;
                sums[2] += bt.Fold;
                sums[4] += et.Send;
                sums[5] += et.Derive;
                sums[6] += et.Digest;
            }

            sums[3] += s2 - s1;
            sums[7] += s3 - s0;
        }

        static void Modifiers(SvNorthStar2AdapterTests.Rig rig, int rows, Queue<(int slot, int source)> live, ref int source)
        {
            while (live.Count > 0)
            {
                var old = live.Dequeue();
                rig.Host.Enqueue(StatOp.RemoveSource(old.slot, old.source));
            }

            rig.Frame();
            rig.Frame();
            var before = rig.Board.MemoryBytes;
            const int count = 100000;
            for (var i = 0; i < count; i++)
            {
                rig.Host.Enqueue(StatOp.Add((int)((long)i * 7919 % rows), rig.MaxHp, ModifierKind.Added, 1f, source++));
            }

            rig.Frame();
            var after = rig.Board.MemoryBytes;
            Line($"ram rows={rows} boardBytesPerRow={(double)before / rows:F2} hostNativeBytesPerRow={(double)rig.Host.NativeBytes / rows:F2} modifiers={count} boardBytesPerModifier={(double)(after - before) / count:F2} boardBefore={before} boardAfter={after}");
        }
    }
}
