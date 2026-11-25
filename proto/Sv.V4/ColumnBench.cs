using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Sv.Host;
#if NET
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif

namespace Sv.V4
{
    public unsafe struct TemplateColumns
    {
        public ColumnOp* Raw;
        public int RawCount;
        public GateGroup* Groups;
        public int GroupCount;
        public LaneTerm* LaneTerms;
        public int LaneTermCount;
        public LaneTest* LaneAbove;
        public int LaneAboveCount;
        public LaneTest* HealthBelow;
        public int HealthBelowCount;
    }

    public sealed unsafe class CompiledColumns : IDisposable
    {
        public readonly TemplateColumns* Templates;
        public readonly int CompiledEntries;

        public CompiledColumns(ColumnOp[][] programs)
        {
            Templates = (TemplateColumns*)Memory.AllocZeroed((nuint)programs.Length, (nuint)sizeof(TemplateColumns));
            for (var template = 0; template < programs.Length; template++)
            {
                var groups = new List<GateGroup>();
                var laneTerms = new List<LaneTerm>();
                var laneAbove = new List<LaneTest>();
                var healthBelow = new List<LaneTest>();
                ColumnPrograms.Compile(programs[template], groups, laneTerms, laneAbove, healthBelow);
                var into = Templates + template;
                into->Raw = Copy(programs[template], out into->RawCount);
                into->Groups = Copy(groups.ToArray(), out into->GroupCount);
                into->LaneTerms = Copy(laneTerms.ToArray(), out into->LaneTermCount);
                into->LaneAbove = Copy(laneAbove.ToArray(), out into->LaneAboveCount);
                into->HealthBelow = Copy(healthBelow.ToArray(), out into->HealthBelowCount);
                CompiledEntries += into->GroupCount + into->LaneTermCount + into->LaneAboveCount + into->HealthBelowCount;
            }
        }

        static T* Copy<T>(T[] values, out int count) where T : unmanaged
        {
            count = values.Length;
            var copy = (T*)Memory.Alloc((nuint)Math.Max(1, values.Length), (nuint)sizeof(T));
            values.AsSpan().CopyTo(new Span<T>(copy, values.Length));
            return copy;
        }

        public void Dispose()
        {
            for (var template = 0; template < V4.Templates.Count; template++)
            {
                Memory.Free(Templates[template].Raw);
                Memory.Free(Templates[template].Groups);
                Memory.Free(Templates[template].LaneTerms);
                Memory.Free(Templates[template].LaneAbove);
                Memory.Free(Templates[template].HealthBelow);
            }

            Memory.Free(Templates);
        }
    }

    public static unsafe class ColumnLaw
    {
        const int Rows = LaneWorld.Rows;

        public static long TermOf(int* lanes, in ColumnOp op, int row)
        {
            switch (op.Kind)
            {
                case ColumnOpKind.AddConstant:
                    return op.Amount;
                case ColumnOpKind.AddLaneTimes:
                    return (long)lanes[op.Lane * Rows + row] * op.Amount;
                case ColumnOpKind.AddIfLaneAbove:
                    return lanes[op.Lane * Rows + row] > op.Threshold ? op.Amount : 0L;
                default:
                    return (long)lanes[Lane.Health * Rows + row] * 100 < (long)lanes[Lane.MaxHealth * Rows + row] * op.Threshold ? op.Amount : 0L;
            }
        }

        public static void AddChunk(int* lanes, uint* contexts, in TemplateColumns columns, long* totals)
        {
            for (var row = 0; row < Rows; row++)
            {
                var context = contexts[row];
                var below = (context >> TargetContext.BelowExecuteLine) & 1u;
                for (var at = 0; at < columns.RawCount; at++)
                {
                    var op = columns.Raw[at];
                    var admitted = (long)((context >> TargetContext.AdmitShift(op.Channel, op.SourceKind)) & 1u);
                    var doubles = (int)(below & (uint)op.Executes);
                    var term = (TermOf(lanes, op, row) << doubles) & -admitted;
                    var loss = (int)((ulong)term >> 63);
                    totals[SumClass.Of(op.Channel, loss) * Rows + row] += term;
                }
            }
        }
    }

#if NET
    public static unsafe class ColumnKernel
    {
        const int Rows = LaneWorld.Rows;
                public static bool Supported => Avx512F.IsSupported && Vector512.IsHardwareAccelerated;

        public static void AddChunk(int* lanes, uint* contexts, in TemplateColumns columns, long* totals, Vector512<long>* laneValues)
        {
            var zero = Vector512<long>.Zero;
            var one = Vector512<long>.One;
            for (var row = 0; row < Rows; row += 8)
            {
                for (var lane = 0; lane < Lane.Count; lane++)
                {
                    laneValues[lane] = Avx512F.ConvertToVector512Int64(Vector256.Load(lanes + lane * Rows + row));
                }

                var context = Avx512F.ConvertToVector512Int64(Vector256.Load((int*)contexts + row));
                var below = Vector512.Equals(Vector512.ShiftRightLogical(context, TargetContext.BelowExecuteLine) & one, one);
                var health100 = Avx512F.Multiply(laneValues[Lane.Health].AsInt32(), Vector512.Create(100));
                var maxHealth = laneValues[Lane.MaxHealth].AsInt32();
                for (var g = 0; g < columns.GroupCount; g++)
                {
                    var group = columns.Groups[g];
                    var all = Vector512.Create(group.ConstantGain + group.ConstantLoss);
                    var loss = Vector512.Create(group.ConstantLoss);
                    var terms = columns.LaneTerms + group.TermFirst;
                    for (var at = 0; at < group.TermCount; at++)
                    {
                        var value = Avx512F.Multiply(laneValues[terms[at].Lane].AsInt32(), Vector512.Create((int)terms[at].Amount));
                        all += value;
                        loss += Vector512.Min(value, zero);
                    }

                    var above = columns.LaneAbove + group.AboveFirst;
                    for (var at = 0; at < group.AboveCount; at++)
                    {
                        var passes = Vector512.GreaterThan(laneValues[above[at].Lane], Vector512.Create(above[at].Threshold));
                        all += passes & Vector512.Create(above[at].Gain + above[at].Loss);
                        loss += passes & Vector512.Create(above[at].Loss);
                    }

                    var belowTests = columns.HealthBelow + group.BelowFirst;
                    for (var at = 0; at < group.BelowCount; at++)
                    {
                        var passes = Vector512.LessThan(health100, Avx512F.Multiply(maxHealth, Vector512.Create((int)belowTests[at].Threshold)));
                        all += passes & Vector512.Create(belowTests[at].Gain + belowTests[at].Loss);
                        loss += passes & Vector512.Create(belowTests[at].Loss);
                    }

                    var admitted = Vector512.Equals(Vector512.ShiftRightLogical(context, group.AdmitShift) & one, one);
                    var doubles = group.Doubles != 0 ? below : zero;
                    var gain = all - loss;
                    gain = (gain + (gain & doubles)) & admitted;
                    loss = (loss + (loss & doubles)) & admitted;
                    var gainAt = totals + SumClass.Of(group.Channel, 0) * Rows + row;
                    var lossAt = totals + SumClass.Of(group.Channel, 1) * Rows + row;
                    (Vector512.Load(gainAt) + gain).Store(gainAt);
                    (Vector512.Load(lossAt) + loss).Store(lossAt);
                }
            }
        }
    }
#endif

    public static class ColumnBench
    {
        public static int Run(int entities, int opsPerEntity, int recordsPerEntity, int threads, int frames, int checkFrames)
        {
            using var workers = new Workers(threads);
            var authored = ColumnPrograms.Author(opsPerEntity);
            using var columns = new CompiledColumns(authored);
            var equal = Check(Math.Min(entities, 4_000), recordsPerEntity, threads, checkFrames, workers, columns);
            using var world = new LaneWorld(entities);
            Templates.Spawn(world, 1);
            using var stream = recordsPerEntity > 0 ? new RecordStream(world, recordsPerEntity, threads, "mixed", false) : null;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "v4 columns: entities {0}, column ops per entity {1} (authored per template, {2:0.0} compiled entries per template), records per entity {3}, threads {4}, entity-ops per frame {5}",
                entities, opsPerEntity, columns.CompiledEntries / (double)Templates.Count, recordsPerEntity, threads, (long)entities * (opsPerEntity + recordsPerEntity)));
            var allocated = 0L;
            using (var scalar = new LaneEngine(world, stream, columns, workers, false, false))
            {
                allocated += Time("scalar law, one op at a time", scalar, world, entities, opsPerEntity + recordsPerEntity, Math.Min(frames, 4));
            }

            using (var vector = new LaneEngine(world, stream, columns, workers, true, true))
            {
                allocated += Time("compiled groups (AVX-512)", vector, world, entities, opsPerEntity + recordsPerEntity, frames);
            }

            return equal && allocated == 0 ? 0 : 1;
        }

        static bool Check(int entities, int recordsPerEntity, int threads, int checkFrames, Workers workers, CompiledColumns columns)
        {
            using var scalarWorld = new LaneWorld(entities);
            Templates.Spawn(scalarWorld, 20);
            using var vectorWorld = new LaneWorld(entities);
            vectorWorld.CopyFrom(scalarWorld);
            using var stream = recordsPerEntity > 0 ? new RecordStream(scalarWorld, recordsPerEntity, threads, "mixed", true) : null;
            using var scalar = new LaneEngine(scalarWorld, stream, columns, workers, false, false);
            using var vector = new LaneEngine(vectorWorld, stream, columns, workers, true, true);
            var equal = true;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "check: scalar law vs compiled AVX-512, {0} entities at health / 20, 1 in 8 column amounts negative, {1} records per entity, {2} frames", entities, recordsPerEntity, checkFrames));
            for (var f = 0; f < checkFrames; f++)
            {
                scalar.StepOneFrame();
                vector.StepOneFrame();
                var same = scalarWorld.Digest() == vectorWorld.Digest();
                equal &= same;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  frame {0}: scalar {1:X16} compiled {2:X16} {3}", f, scalarWorld.Digest(), vectorWorld.Digest(), same ? "equal" : "DIFFERENT"));
            }

            if (checkFrames > 0)
            {
                Console.WriteLine("  " + Program.Census(scalarWorld));
            }

            return equal;
        }

        static long Time(string label, LaneEngine engine, LaneWorld world, int entities, int opsPerEntity, int frames)
        {
            using var copy = new LaneWorld(entities);
            copy.CopyFrom(world);
            for (var f = 0; f < Math.Min(frames, 8); f++)
            {
                engine.StepOneFrame();
            }

            var samples = new double[frames];
            var allocated = 0L;
            for (var f = 0; f < frames; f++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                engine.StepOneFrame();
                samples[f] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            world.CopyFrom(copy);
            var median = Program.Median(samples);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0,-30} median {1,9:0.000} ms (p10 {2:0.000}, p90 {3:0.000}) = {4:0.0000} ns per entity-op; records {5:0.000} ms, columns and fold {6:0.000} ms; alloc(main) {7} B",
                label, median, Program.Percentile(samples, 10), Program.Percentile(samples, 90), median * 1e6 / ((double)entities * opsPerEntity), engine.ReduceMilliseconds, engine.ResolveMilliseconds, allocated));
            return allocated;
        }
    }
}
