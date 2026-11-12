using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Sv;
using Sv.Unity;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs.LowLevel.Unsafe;

namespace SvBench
{
    public static unsafe class SvBenchRun
    {
        static StreamWriter output;

        static readonly Func<bool, long> TotalAllocated = (Func<bool, long>)Delegate.CreateDelegate(typeof(Func<bool, long>), typeof(GC).GetMethod("GetTotalAllocatedBytes", new[] { typeof(bool) }));

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
            UnityEngine.Debug.Log("SVBENCH " + text);
        }

        public static void Compile() => UnityEditor.EditorApplication.Exit(0);

        public static void Run()
        {
            var code = 0;
            output = new StreamWriter(Arg("-svOut", "svbench.txt"), true);
            try
            {
                BurstCompiler.Options.EnableBurstSafetyChecks = false;
                JobsUtility.JobDebuggerEnabled = false;
                NativeLeakDetection.Mode = NativeLeakDetectionMode.Disabled;
                var tag = Arg("-svTag", "run");
                Line($"settings tag={tag} burst={BurstCompiler.IsEnabled} safety={BurstCompiler.Options.EnableBurstSafetyChecks} jobDebugger={JobsUtility.JobDebuggerEnabled} leak={NativeLeakDetection.Mode} workers={JobsUtility.JobWorkerCount} unity={UnityEngine.Application.unityVersion} Iw=2 digestEvery=8 kinds=stand-in");
                foreach (var shape in Arg("-svShapes", "P1,P2,P3").Split(','))
                {
                    var rows = shape == "P1" ? 1 << 20 : shape == "P2" ? 1 << 18 : 10000;
                    var timelines = shape == "P1" ? 1 : shape == "P2" ? 4 : 150;
                    foreach (var w2 in new[] { 2, 5, 8 })
                    {
                        foreach (var cross in new[] { 0, 26, 128 })
                        {
                            Shape(shape, rows, timelines, w2, cross, false, false);
                        }
                    }

                    if (shape == "P1")
                    {
                        Shape(shape, rows, timelines, 5, 26, true, false);
                    }

                    Shape(shape, rows, timelines, 5, 0, false, true);
                }

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

        static void Shape(string shape, int rows, int timelines, int w2, int cross, bool hot, bool nullWindow)
        {
            var schema = SchemaBuilder.Create().Stat("max_hp", 100f).Intrinsic("hp").Intrinsic("mana").Build();
            var maxHp = schema.Stat("max_hp");
            var world = new World("svbench-" + shape);
            var board = Board.Create(schema, rows);
            var links = new SvLinkRegistry();
            links.Stat<BMaxHp>(maxHp);
            var hp = links.Intrinsic<BHp, BHpDelta>(schema.Intrinsic("hp"), SvBound.Of(0), SvBound.OfStat<BMaxHp>(), true);
            links.Condition<BLow>(hp, 30, 100, 40, 100);
            links.Intrinsic<BMana, BManaDelta>(schema.Intrinsic("mana"), SvBound.Of(0), SvBound.Of(1000));
            links.Link<BHpDelta>(1, LinkMode.Delta);
            links.Link<BHpDelta>(1, LinkMode.Delta, LinkScope.Remote);
            links.Link<BManaDelta>(1, LinkMode.Delta);
            links.Link<BMaxHp>(1, LinkMode.Read);
            var setup = Stopwatch.StartNew();
            var host = SvHost.Install(world, board, links, 1 << 16, 1 << 16);
            var manager = world.EntityManager;
            var archetype = manager.CreateArchetype(typeof(SvSlot), typeof(BHp), typeof(BHpDelta), typeof(BMaxHp), typeof(BMana), typeof(BManaDelta), typeof(BLow), typeof(BFiller));
            var entities = new NativeArray<Entity>(rows, Allocator.Persistent);
            manager.CreateEntity(archetype, entities);
            var query = manager.CreateEntityQuery(typeof(SvSlot));
            var chunks = query.ToArchetypeChunkArray(Allocator.Temp);
            var hpH = manager.GetComponentTypeHandle<BHp>(false);
            var maxH = manager.GetComponentTypeHandle<BMaxHp>(false);
            var manaH = manager.GetComponentTypeHandle<BMana>(false);
            for (var c = 0; c < chunks.Length; c++)
            {
                var chunk = chunks[c];
                var h = (int*)chunk.GetComponentDataPtrRW(ref hpH);
                var m = (float*)chunk.GetComponentDataPtrRW(ref maxH);
                var n = (int*)chunk.GetComponentDataPtrRW(ref manaH);
                for (var i = 0; i < chunk.Count; i++)
                {
                    h[i] = 60;
                    m[i] = 100f;
                    n[i] = 500;
                }
            }

            chunks.Dispose();
            for (var i = 0; i < rows; i++)
            {
                host.Bind(entities[i]);
            }

            var u = world.Unmanaged;
            var begin = world.GetExistingSystem<SvBeginSystem>();
            var end = world.GetExistingSystem<SvEndSystem>();
            var kinds = world.CreateSystem<StandInKindSystem>();
            u.GetUnsafeSystemRef<SvBeginSystem>(begin).Split = true;
            var sink = (long*)UnsafeUtility.Malloc(1024 * 8, 64, Allocator.Persistent);
            ref var k = ref u.GetUnsafeSystemRef<StandInKindSystem>(kinds);
            k.Job.Inbox = host.InboxWriter(0);
            k.Job.Timelines = timelines;
            k.Job.Rows = rows;
            k.Job.Sink = sink;
            begin.Update(u);
            end.Update(u);
            setup.Stop();
            Line($"shape={shape} rows={rows} timelines={timelines} chunks={query.CalculateChunkCount()} setupMs={setup.Elapsed.TotalMilliseconds:F1} boardBytes={board.MemoryBytes}");
            ref var job = ref u.GetUnsafeSystemRef<StandInKindSystem>(kinds).Job;
            job.WritesTimesTwo = w2;
            job.CrossThreshold = cross;
            job.HotTarget = hot;
            var statOps = nullWindow ? 0 : (int)Math.Round(0.05 * (w2 / 2.0) * rows * timelines * 2.0 / 300.0);
            Cell(shape, host, board, maxHp, u, begin, kinds, end, w2, cross, hot, statOps, rows, nullWindow);
            host.Dispose();
            UnsafeUtility.Free(sink, Allocator.Persistent);
            entities.Dispose();
            world.Dispose();
            board.Dispose();
        }

        static void Cell(string shape, SvHost host, Board board, StatId maxHp, WorldUnmanaged u, SystemHandle begin, SystemHandle kinds, SystemHandle end, int w2, int cross, bool hot, int statOps, int rows, bool nullWindow)
        {
            var source = 1;
            var live = new Queue<(int slot, int source)>(statOps * 7 * 32 + 16);
            var random = new System.Random(0x5B + w2 * 7 + cross);
            var stages = new[] { "svMain", "scatter", "fold", "kinds", "derive", "digest", "total", "expire", "apply", "recalc", "take" };
            var rounds = new List<double[]>();
            long mainBytes = 0;
            long allBytes = 0;
            SvProbe.ManagedRuns = 0;
            BenchProbe.ManagedRuns = 0;
            var freq = (double)Stopwatch.Frequency;
            for (var r = 0; r < 7; r++)
            {
                var sums = new double[stages.Length];
                const int ticks = 32;
                var mb = GC.GetAllocatedBytesForCurrentThread();
                var ab = TotalAllocated(true);
                for (var t = 0; t < ticks; t++)
                {
                    if (!nullWindow)
                    {
                        for (var o = 0; o < statOps; o++)
                        {
                            if ((o & 1) == 0 || live.Count == 0)
                            {
                                var slot = random.Next(rows);
                                host.Enqueue(StatOp.Add(slot, maxHp, ModifierKind.Added, (random.Next(9) - 4), source));
                                live.Enqueue((slot, source));
                                source++;
                            }
                            else
                            {
                                var old = live.Dequeue();
                                host.Enqueue(StatOp.RemoveSource(old.slot, old.source));
                            }
                        }
                    }

                    var s0 = Stopwatch.GetTimestamp();
                    if (!nullWindow) begin.Update(u);
                    var s1 = Stopwatch.GetTimestamp();
                    kinds.Update(u);
                    var s2 = Stopwatch.GetTimestamp();
                    if (!nullWindow) end.Update(u);
                    var s3 = Stopwatch.GetTimestamp();
                    var bt = u.GetUnsafeSystemRef<SvBeginSystem>(begin).Times;
                    var et = u.GetUnsafeSystemRef<SvEndSystem>(end).Times;
                    if (!nullWindow)
                    {
                        sums[0] += bt.Sparse;
                        sums[1] += bt.Scatter;
                        sums[2] += bt.Fold;
                        sums[4] += et.Derive;
                        sums[5] += et.Digest;
                        sums[7] += host.ExpireTicks;
                        sums[8] += host.ApplyTicks;
                        sums[9] += host.RecalcTicks;
                        sums[10] += host.TakeTicks;
                    }

                    sums[3] += s2 - s1;
                    sums[6] += s3 - s0;
                }

                var mbAfter = GC.GetAllocatedBytesForCurrentThread();
                var abAfter = TotalAllocated(true);
                if (r > 0)
                {
                    for (var i = 0; i < sums.Length; i++) sums[i] = sums[i] * 1000.0 / freq / ticks;
                    rounds.Add(sums);
                    mainBytes += mbAfter - mb;
                    allBytes += abAfter - ab;
                }
            }

            while (live.Count > 0)
            {
                var old = live.Dequeue();
                host.Enqueue(StatOp.RemoveSource(old.slot, old.source));
            }

            if (!nullWindow)
            {
                begin.Update(u);
                kinds.Update(u);
                end.Update(u);
            }

            var parts = new System.Text.StringBuilder();
            for (var i = 0; i < stages.Length; i++)
            {
                var v = new List<double>();
                foreach (var round in rounds) v.Add(round[i]);
                v.Sort();
                parts.Append(' ').Append(stages[i]).Append('=').Append(v[v.Count / 2].ToString("F3"));
            }

            var crossPct = cross == 0 ? 0 : cross == 26 ? 10 : 50;
            Line($"cell shape={shape} W={w2 / 2.0:F1} cross={crossPct}%{(hot ? "-hot1" : "")} window={(nullWindow ? "null(kinds-only)" : "sv+kinds")} statOps={statOps}{parts} mainBytes={mainBytes} allBytes={allBytes} svProbeManaged={SvProbe.ManagedRuns} kindProbeManaged={BenchProbe.ManagedRuns} events={host.Channel(0).Count} dense={host.Channel(0).Dense}");
        }
    }
}
