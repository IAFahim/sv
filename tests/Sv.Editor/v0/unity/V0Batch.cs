using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace SvV0
{
    public static unsafe class V0Batch
    {
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
            UnityEngine.Debug.Log("V0 " + text);
        }

        public static void Compile()
        {
            UnityEditor.EditorApplication.Exit(0);
        }

        public static void Run()
        {
            var code = 0;
            output = new StreamWriter(Arg("-v0Out", "v0.txt"), true);
            try
            {
                BurstCompiler.Options.EnableBurstSafetyChecks = false;
                JobsUtility.JobDebuggerEnabled = false;
                NativeLeakDetection.Mode = NativeLeakDetectionMode.Disabled;
                var tag = Arg("-v0Tag", "run");
                Line("settings tag=" + tag + " burstEnabled=" + BurstCompiler.IsEnabled + " burstCompilation=" + BurstCompiler.Options.EnableBurstCompilation + " safety=" + BurstCompiler.Options.EnableBurstSafetyChecks + " jobDebugger=" + JobsUtility.JobDebuggerEnabled + " leak=" + NativeLeakDetection.Mode + " workers=" + JobsUtility.JobWorkerCount + " clr=" + Environment.Version + " unity=" + UnityEngine.Application.unityVersion);
                var probes = Arg("-v0Probes", "h,e,c,d,g").Split(',');
                foreach (var p in probes)
                {
                    if (p == "h") StatFold(Arg("-v0Dir", "."));
                    if (p == "e") Versions();
                    if (p == "c") Touch(1 << 20);
                    if (p == "c4") Touch(4 << 20);
                    if (p == "d") Scatter();
                    if (p == "g") InboxProbe();
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

        static void StatFold(string dir)
        {
            Line(StatFoldHost.Run("unity-coreclr", Path.Combine(dir, "statfold-unity-coreclr.bin")));
            var n = StatFoldSets.SetCount;
            var bases = (float*)UnsafeUtility.Malloc(n * 4L, 64, Allocator.Persistent);
            var counts = (byte*)UnsafeUtility.Malloc(n, 64, Allocator.Persistent);
            var entries = (ProbeStatEntry*)UnsafeUtility.Malloc((long)n * StatFoldSets.MaxEntries * sizeof(ProbeStatEntry), 64, Allocator.Persistent);
            var results = (uint*)UnsafeUtility.Malloc(n * 4L, 64, Allocator.Persistent);
            StatFoldSets.Fill(bases, counts, entries);
            for (var mode = 0; mode < 3; mode++)
            {
                V0Probe.Hits = 0;
                if (mode == 0) new V0StatFoldStrictJob { Bases = bases, Counts = counts, Entries = entries, Results = results }.Run();
                if (mode == 1) new V0StatFoldDefaultJob { Bases = bases, Counts = counts, Entries = entries, Results = results }.Run();
                if (mode == 2) new V0StatFoldFastJob { Bases = bases, Counts = counts, Entries = entries, Results = results }.Run();
                var name = mode == 0 ? "burst-strict" : mode == 1 ? "burst-default" : "burst-fast";
                var bytes = new byte[n * 4];
                fixed (byte* b = bytes) UnsafeUtility.MemCpy(b, results, n * 4L);
                File.WriteAllBytes(Path.Combine(dir, "statfold-" + name + ".bin"), bytes);
                Line(StatFoldHost.Report(name, results) + " burstProbe=" + (V0Probe.Hits == 0 ? 1 : 0));
            }
            UnsafeUtility.Free(bases, Allocator.Persistent);
            UnsafeUtility.Free(counts, Allocator.Persistent);
            UnsafeUtility.Free(entries, Allocator.Persistent);
            UnsafeUtility.Free(results, Allocator.Persistent);
        }

        static World Build(int rows, out EntityQuery query)
        {
            var world = new World("v0-" + rows);
            var em = world.EntityManager;
            var archetype = em.CreateArchetype(typeof(V0Slot), typeof(V0Hp), typeof(V0HpDelta), typeof(V0HpMax), typeof(V0Mana), typeof(V0ManaDelta), typeof(V0ManaMax), typeof(V0Flag), typeof(V0Filler));
            em.CreateEntity(archetype, rows);
            query = em.CreateEntityQuery(typeof(V0Hp));
            var chunks = query.ToArchetypeChunkArray(Allocator.Temp);
            var slotH = em.GetComponentTypeHandle<V0Slot>(false);
            var hpH = em.GetComponentTypeHandle<V0Hp>(false);
            var hpmH = em.GetComponentTypeHandle<V0HpMax>(false);
            var mpH = em.GetComponentTypeHandle<V0Mana>(false);
            var mpmH = em.GetComponentTypeHandle<V0ManaMax>(false);
            var slot = 0;
            for (var c = 0; c < chunks.Length; c++)
            {
                var chunk = chunks[c];
                var s = (int*)chunk.GetComponentDataPtrRW(ref slotH);
                var hp = (int*)chunk.GetComponentDataPtrRW(ref hpH);
                var hpm = (float*)chunk.GetComponentDataPtrRW(ref hpmH);
                var mp = (int*)chunk.GetComponentDataPtrRW(ref mpH);
                var mpm = (float*)chunk.GetComponentDataPtrRW(ref mpmH);
                for (var i = 0; i < chunk.Count; i++)
                {
                    s[i] = slot++;
                    hp[i] = 50;
                    hpm[i] = 100f;
                    mp[i] = 20;
                    mpm[i] = 40f;
                }
            }
            chunks.Dispose();
            return world;
        }

        static double Median(List<double> values)
        {
            values.Sort();
            return values[values.Count / 2];
        }

        static string Spread(List<double> values)
        {
            values.Sort();
            return Median(values).ToString("F4") + " [" + values[0].ToString("F4") + "-" + values[values.Count - 1].ToString("F4") + "]";
        }

        static string Measure(Action before, Action body, int ticks, int rounds)
        {
            var per = new List<double>();
            for (var r = 0; r < rounds; r++)
            {
                double total = 0;
                for (var t = 0; t < ticks; t++)
                {
                    before?.Invoke();
                    var start = Stopwatch.GetTimestamp();
                    body();
                    total += Stopwatch.GetTimestamp() - start;
                }
                if (r > 0) per.Add(total * 1000.0 / Stopwatch.Frequency / ticks);
            }
            return Spread(per);
        }

        static void Versions()
        {
            var world = Build(100000, out var query);
            var chunkCount = query.CalculateChunkCount();
            var fold = world.CreateSystem<V0FoldSystem>();
            var writer = world.CreateSystem<V0WriterSystem>();
            var visited = new NativeArray<int>(chunkCount, Allocator.Persistent);
            var flagChanged = new NativeArray<int>(chunkCount, Allocator.Persistent);
            var maxChanged = new NativeArray<int>(chunkCount, Allocator.Persistent);
            var targets = query.ToEntityArray(Allocator.Persistent);
            ref var f = ref world.Unmanaged.GetUnsafeSystemRef<V0FoldSystem>(fold);
            f.Filter = true;
            f.Visited = visited;
            f.FlagChanged = flagChanged;
            f.MaxChanged = maxChanged;
            ref var w = ref world.Unmanaged.GetUnsafeSystemRef<V0WriterSystem>(writer);
            w.Targets = targets;
            w.Count = 100;
            var modes = new[] { 0, 0, 0, 1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6, 0 };
            var names = new[] { "none(create)", "none", "none", "RW-ptr-even-chunks-no-write", "none", "RO-ptr-all", "none", "lookup-write-HpMax-100", "none", "lookup-SetComponentEnabled-100", "none", "chunk.SetComponentEnabled-1/4", "none", "EnabledMask[i]-1/4", "none" };
            Line("versions rows=100000 chunks=" + chunkCount + " capacity=" + query.ToArchetypeChunkArray(Allocator.Temp)[0].Capacity);
            for (var tick = 0; tick < modes.Length; tick++)
            {
                world.Unmanaged.GetUnsafeSystemRef<V0WriterSystem>(writer).Mode = modes[tick];
                V0Probe.Hits = 0;
                world.SetTime(new Unity.Core.TimeData(tick, 0.033f));
                writer.Update(world.Unmanaged);
                fold.Update(world.Unmanaged);
                var v = 0;
                var fc = 0;
                var mc = 0;
                for (var i = 0; i < chunkCount; i++)
                {
                    v += visited[i] != 0 ? 1 : 0;
                    fc += flagChanged[i];
                    mc += maxChanged[i];
                }
                Line("versions tick=" + (tick + 1) + " writer=" + names[tick] + " foldVisited=" + v + " flagDidChange=" + fc + " hpMaxDidChange=" + mc + " burstProbe=" + (V0Probe.Hits == 0 ? 1 : 0));
            }
            visited.Dispose();
            flagChanged.Dispose();
            maxChanged.Dispose();
            targets.Dispose();
            world.Dispose();
        }

        static void Touch(int rows)
        {
            var world = Build(rows, out var query);
            var chunkCount = query.CalculateChunkCount();
            var touch = world.CreateSystem<V0TouchSystem>();
            var fold = world.CreateSystem<V0FoldSystem>();
            var delta = world.CreateSystem<V0DeltaSystem>();
            var sums = new NativeArray<long>(chunkCount, Allocator.Persistent);
            var visited = new NativeArray<int>(chunkCount, Allocator.Persistent);
            var flagChanged = new NativeArray<int>(chunkCount, Allocator.Persistent);
            var maxChanged = new NativeArray<int>(chunkCount, Allocator.Persistent);
            world.Unmanaged.GetUnsafeSystemRef<V0TouchSystem>(touch).Sums = sums;
            ref var f = ref world.Unmanaged.GetUnsafeSystemRef<V0FoldSystem>(fold);
            f.Visited = visited;
            f.FlagChanged = flagChanged;
            f.MaxChanged = maxChanged;
            var u = world.Unmanaged;
            for (var i = 0; i < 32; i++)
            {
                touch.Update(u);
                delta.Update(u);
                fold.Update(u);
            }
            var bytes = (double)rows * 24;
            var size = UnsafeUtility.SizeOf<V0Filler>() + 7 * 4;
            Line("touch rows=" + rows + " chunks=" + chunkCount + " entityBytes~" + size);
            V0Probe.Hits = 0;
            Line("touch rows=" + rows + " job=touch-RO-6cols-24B ms=" + Measure(null, () => touch.Update(u), 32, 6) + " bytes=" + bytes);
            u.GetUnsafeSystemRef<V0FoldSystem>(fold).Filter = false;
            u.GetUnsafeSystemRef<V0FoldSystem>(fold).Intrinsics = 2;
            Line("touch rows=" + rows + " job=fold-Iw2-unfiltered ms=" + Measure(null, () => fold.Update(u), 32, 6));
            u.GetUnsafeSystemRef<V0FoldSystem>(fold).Intrinsics = 1;
            Line("touch rows=" + rows + " job=fold-Iw1-unfiltered ms=" + Measure(null, () => fold.Update(u), 32, 6));
            u.GetUnsafeSystemRef<V0FoldSystem>(fold).Intrinsics = 2;
            u.GetUnsafeSystemRef<V0FoldSystem>(fold).Filter = true;
            Line("touch rows=" + rows + " job=delta-writer-all-rows ms=" + Measure(null, () => delta.Update(u), 32, 6));
            Line("touch rows=" + rows + " job=fold-Iw2-filtered-all-changed ms=" + Measure(() => delta.Update(u), () => fold.Update(u), 32, 6));
            Line("touch rows=" + rows + " job=fold-Iw2-filtered-none-changed ms=" + Measure(null, () => fold.Update(u), 32, 6));
            var v = 0;
            for (var i = 0; i < chunkCount; i++) v += visited[i] != 0 ? 1 : 0;
            Line("touch rows=" + rows + " lastFilteredVisited=" + v + " burstProbe=" + (V0Probe.Hits == 0 ? 1 : 0));
            sums.Dispose();
            visited.Dispose();
            flagChanged.Dispose();
            maxChanged.Dispose();
            world.Dispose();
        }

        static void Scatter()
        {
            var rows = 1 << 20;
            var world = Build(rows, out var query);
            var writer = world.CreateSystem<V0WriterSystem>();
            var sorted = query.ToEntityArray(Allocator.Persistent);
            var random = new NativeArray<Entity>(rows, Allocator.Persistent);
            var state = 0x5CA77E5UL;
            var order = new int[rows];
            for (var i = 0; i < rows; i++) order[i] = i;
            for (var i = rows - 1; i > 0; i--)
            {
                var j = (int)(StatFoldSets.Next(ref state) % (ulong)(i + 1));
                var t = order[i];
                order[i] = order[j];
                order[j] = t;
            }
            for (var i = 0; i < rows; i++) random[i] = sorted[order[i]];
            var u = world.Unmanaged;
            ref var w = ref u.GetUnsafeSystemRef<V0WriterSystem>(writer);
            w.Mode = 3;
            foreach (var count in new[] { 128, 10000, 1 << 20 })
            {
                foreach (var shuffled in new[] { false, true })
                {
                    u.GetUnsafeSystemRef<V0WriterSystem>(writer).Targets = shuffled ? random : sorted;
                    u.GetUnsafeSystemRef<V0WriterSystem>(writer).Count = count;
                    for (var i = 0; i < 8; i++) writer.Update(u);
                    V0Probe.Hits = 0;
                    var ticks = count >= 1 << 20 ? 4 : 32;
                    var ms = Measure(null, () => writer.Update(u), ticks, 6);
                    Line("scatter rows=" + rows + " writes=" + count + " order=" + (shuffled ? "random" : "entity") + " ms=" + ms + " burstProbe=" + (V0Probe.Hits == 0 ? 1 : 0));
                }
            }
            sorted.Dispose();
            random.Dispose();
            world.Dispose();
        }

        static void InboxProbe()
        {
            var slots = 1 << 20;
            var inbox = (long*)UnsafeUtility.Malloc(slots * 8L, 64, Allocator.Persistent);
            Line("inbox slots=" + slots + " aligned64=" + (((long)inbox & 63) == 0));
            foreach (var sends in new[] { 500000, 1 << 20, 2500000 })
            {
                foreach (var mode in new[] { 0, 1, 2 })
                {
                    var name = mode == 0 ? "atomic-random" : mode == 1 ? "plain-random(racy-baseline)" : "atomic-one-hot";
                    var job = new V0InboxJob { Inbox = inbox, Slots = slots, Mode = mode };
                    UnsafeUtility.MemClear(inbox, slots * 8L);
                    for (var i = 0; i < 4; i++) job.Schedule(sends, 4096).Complete();
                    UnsafeUtility.MemClear(inbox, slots * 8L);
                    V0Probe.Hits = 0;
                    var ticks = 0;
                    var ms = Measure(null, () => { job.Schedule(sends, 4096).Complete(); ticks++; }, 16, 6);
                    long total = 0;
                    for (var i = 0; i < slots; i++) total += inbox[i];
                    var expected = (long)sends * ticks;
                    Line("inbox sends=" + sends + " mode=" + name + " ms=" + ms + " exact=" + (total == expected) + " burstProbe=" + (V0Probe.Hits == 0 ? 1 : 0));
                }
            }
            UnsafeUtility.Free(inbox, Allocator.Persistent);
        }
    }
}
