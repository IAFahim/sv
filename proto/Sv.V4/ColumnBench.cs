using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Sv.Host;
#if NET
using System.Runtime.Intrinsics;
#endif

namespace Sv.V4
{
    public static class ColumnOpKind
    {
        public const int AddConstant = 0;
        public const int AddLaneTimes = 1;
        public const int AddIfLaneAbove = 2;
        public const int AddIfHealthBelowPercent = 3;
    }

    public struct ColumnOp
    {
        public int Kind;
        public int SumClass;
        public int Lane;
        public int Amount;
        public int Threshold;
    }

    public static class ColumnPrograms
    {
        static readonly int[] ReadableLanes = { Lane.PoisonTicks, Lane.RegenRate, Lane.Converter, Lane.Execute, Lane.RegenCard };

        public static ColumnOp[][] Author(int opsPerEntity)
        {
            var programs = new ColumnOp[Templates.Count][];
            for (var template = 0; template < Templates.Count; template++)
            {
                var state = 0x2545F491u ^ (uint)(template * 2654435761u) | 1u;
                var program = new ColumnOp[opsPerEntity];
                for (var at = 0; at < opsPerEntity; at++)
                {
                    var roll = Next(ref state);
                    var kindRoll = roll % 20u;
                    var kind = kindRoll < 8u ? ColumnOpKind.AddConstant : kindRoll < 14u ? ColumnOpKind.AddLaneTimes : kindRoll < 17u ? ColumnOpKind.AddIfLaneAbove : ColumnOpKind.AddIfHealthBelowPercent;
                    var channel = (int)((roll >> 5) % 5u);
                    var sourceKind = (int)((roll >> 9) & 3u);
                    var amount = 1 + (int)((roll >> 12) & 7u);
                    program[at] = new ColumnOp
                    {
                        Kind = kind,
                        SumClass = ClassOf(channel, sourceKind, (int)((roll >> 15) & 1u)),
                        Lane = ReadableLanes[(roll >> 16) % (uint)ReadableLanes.Length],
                        Amount = amount,
                        Threshold = kind == ColumnOpKind.AddIfHealthBelowPercent ? 10 + (int)((roll >> 20) % 40u) : (int)((roll >> 20) & 63u),
                    };
                }

                programs[template] = program;
            }

            return programs;
        }

        static int ClassOf(int channel, int sourceKind, int executes) =>
            channel == Channel.Physical ? SumClass.PhysicalOf(sourceKind, executes, 0)
            : channel == Channel.Poison ? SumClass.PoisonOf(sourceKind, 0)
            : channel == Channel.Fire ? SumClass.FireOf(sourceKind, 0)
            : channel == Channel.Healing ? SumClass.HealingOf(sourceKind)
            : SumClass.TickOf(sourceKind);

        public static ColumnOp[][] FoldEqualTerms(ColumnOp[][] programs)
        {
            var folded = new ColumnOp[programs.Length][];
            for (var template = 0; template < programs.Length; template++)
            {
                var merged = new Dictionary<(int, int, int, int), int>();
                var order = new List<(int, int, int, int)>();
                foreach (var op in programs[template])
                {
                    var shape = op.Kind == ColumnOpKind.AddConstant ? (op.Kind, op.SumClass, 0, 0)
                        : op.Kind == ColumnOpKind.AddLaneTimes ? (op.Kind, op.SumClass, op.Lane, 0)
                        : op.Kind == ColumnOpKind.AddIfLaneAbove ? (op.Kind, op.SumClass, op.Lane, op.Threshold)
                        : (op.Kind, op.SumClass, 0, op.Threshold);
                    if (!merged.ContainsKey(shape))
                    {
                        merged[shape] = 0;
                        order.Add(shape);
                    }

                    merged[shape] += op.Amount;
                }

                order.Sort();
                folded[template] = new ColumnOp[order.Count];
                for (var at = 0; at < order.Count; at++)
                {
                    var (kind, sumClass, lane, threshold) = order[at];
                    folded[template][at] = new ColumnOp { Kind = kind, SumClass = sumClass, Lane = lane, Amount = merged[order[at]], Threshold = threshold };
                }
            }

            return folded;
        }

        static uint Next(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }
    }

    public static unsafe class ColumnKernels
    {
        const int Rows = LaneWorld.Rows;

        public static long TermOfOneEntity(int* lanes, in ColumnOp op, int row)
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

        public static void RunOneEntityAtATime(int* lanes, long* totals, ColumnOp* program, int length)
        {
            for (var row = 0; row < Rows; row++)
            {
                for (var at = 0; at < length; at++)
                {
                    totals[program[at].SumClass * Rows + row] += TermOfOneEntity(lanes, program[at], row);
                }
            }
        }

#if NET
        static Vector256<long> LoadWide(int* lanes, int lane, int row)
        {
            var four = Vector128.Load(lanes + lane * Rows + row);
            return Vector256.Create(Vector128.WidenLower(four), Vector128.WidenUpper(four));
        }

        public static void RunWholeChunkPerOp(int* lanes, long* totals, ColumnOp* program, int length)
        {
            for (var at = 0; at < length; at++)
            {
                var op = program[at];
                var into = totals + op.SumClass * Rows;
                var amount = Vector256.Create((long)op.Amount);
                switch (op.Kind)
                {
                    case ColumnOpKind.AddConstant:
                        for (var row = 0; row < Rows; row += 4)
                        {
                            (Vector256.Load(into + row) + amount).Store(into + row);
                        }

                        break;
                    case ColumnOpKind.AddLaneTimes:
                        for (var row = 0; row < Rows; row += 4)
                        {
                            (Vector256.Load(into + row) + LoadWide(lanes, op.Lane, row) * amount).Store(into + row);
                        }

                        break;
                    case ColumnOpKind.AddIfLaneAbove:
                        var threshold = Vector256.Create((long)op.Threshold);
                        for (var row = 0; row < Rows; row += 4)
                        {
                            (Vector256.Load(into + row) + (Vector256.GreaterThan(LoadWide(lanes, op.Lane, row), threshold) & amount)).Store(into + row);
                        }

                        break;
                    default:
                        var percent = Vector256.Create((long)op.Threshold);
                        for (var row = 0; row < Rows; row += 4)
                        {
                            var below = Vector256.LessThan(LoadWide(lanes, Lane.Health, row) * 100L, LoadWide(lanes, Lane.MaxHealth, row) * percent);
                            (Vector256.Load(into + row) + (below & amount)).Store(into + row);
                        }

                        break;
                }
            }
        }
#endif
    }

    public unsafe struct ColumnFrame
    {
        public LaneView World;
        public ColumnOp** Programs;
        public int* ProgramLengths;
        public long* TotalsScratch;
        public long* EmittedLastFrame;
        public long* EmitNextFrame;
        public int Parts;
        public int Mode;
    }

    public sealed unsafe class ColumnEngine : IDisposable
    {
        public const int OneEntityAtATime = 0;
        public const int WholeChunkPerOp = 1;
        readonly Workers workers;
        readonly ColumnFrame* frame;

        public ColumnEngine(LaneWorld world, ColumnOp[][] programs, Workers workers, int mode)
        {
            this.workers = workers;
            frame = (ColumnFrame*)Memory.AllocZeroed(1, (nuint)sizeof(ColumnFrame));
            frame->World = world.View;
            frame->Programs = (ColumnOp**)Memory.AllocZeroed((nuint)programs.Length, (nuint)sizeof(ColumnOp*));
            frame->ProgramLengths = (int*)Memory.AllocZeroed((nuint)programs.Length, sizeof(int));
            for (var template = 0; template < programs.Length; template++)
            {
                frame->ProgramLengths[template] = programs[template].Length;
                frame->Programs[template] = (ColumnOp*)Memory.Alloc((nuint)Math.Max(1, programs[template].Length), (nuint)sizeof(ColumnOp));
                for (var at = 0; at < programs[template].Length; at++)
                {
                    frame->Programs[template][at] = programs[template][at];
                }
            }

            frame->Parts = WorkRange.PartsFor(world.Chunks, workers.Count * 8);
            frame->TotalsScratch = (long*)Memory.Alloc((nuint)((long)frame->Parts * ClassSumBlocks.BlockLongs), sizeof(long));
            frame->EmittedLastFrame = (long*)Memory.AllocZeroed((nuint)((long)world.Chunks * EmitClass.BlockLongs), sizeof(long));
            frame->EmitNextFrame = (long*)Memory.AllocZeroed((nuint)((long)world.Chunks * EmitClass.BlockLongs), sizeof(long));
            frame->Mode = mode;
        }

        public void StepOneFrame()
        {
            workers.Run(&ResolveChunks, frame, frame->Parts);
            var swap = frame->EmittedLastFrame;
            frame->EmittedLastFrame = frame->EmitNextFrame;
            frame->EmitNextFrame = swap;
        }

        static void ResolveChunks(void* state, int part)
        {
            var frame = (ColumnFrame*)state;
            WorkRange.Of(frame->World.Chunks, part, frame->Parts, out var from, out var to);
            var totals = frame->TotalsScratch + (long)part * ClassSumBlocks.BlockLongs;
            for (var chunk = from; chunk < to; chunk++)
            {
                var template = Templates.OfChunk(chunk);
                var lanes = frame->World.ChunkLanes(chunk);
                var emitNext = frame->EmitNextFrame + (long)chunk * EmitClass.BlockLongs;
                ChunkFold.GatherClassSums(totals, null, 0, chunk, frame->EmittedLastFrame + (long)chunk * EmitClass.BlockLongs);
#if NET
                if (frame->Mode == WholeChunkPerOp)
                {
                    ColumnKernels.RunWholeChunkPerOp(lanes, totals, frame->Programs[template], frame->ProgramLengths[template]);
                    ChunkFold.ResolveChunkVector(lanes, totals, emitNext);
                    continue;
                }
#endif
                ColumnKernels.RunOneEntityAtATime(lanes, totals, frame->Programs[template], frame->ProgramLengths[template]);
                ChunkFold.ResolveChunkScalar(lanes, totals, emitNext);
            }
        }

        public void Dispose()
        {
            for (var template = 0; template < Templates.Count; template++)
            {
                Memory.Free(frame->Programs[template]);
            }

            Memory.Free(frame->Programs);
            Memory.Free(frame->ProgramLengths);
            Memory.Free(frame->TotalsScratch);
            Memory.Free(frame->EmittedLastFrame);
            Memory.Free(frame->EmitNextFrame);
            Memory.Free(frame);
        }
    }

    public static class ColumnBench
    {
        public static int Run(int entities, int opsPerEntity, int threads, int frames, int checkFrames)
        {
            using var workers = new Workers(threads);
            var authored = ColumnPrograms.Author(opsPerEntity);
            var folded = ColumnPrograms.FoldEqualTerms(authored);
            var foldedLengths = 0;
            foreach (var program in folded)
            {
                foldedLengths += program.Length;
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "v4 columns: entities {0}, ops per entity {1} (authored per template), {2:0.0} ops per entity after folding equal terms, threads {3}, entity-ops per frame {4}",
                entities, opsPerEntity, foldedLengths / (double)folded.Length, threads, (long)entities * opsPerEntity));

            using var reference = new LaneWorld(entities);
            Templates.Spawn(reference, 1);
            using var naiveWorld = new LaneWorld(entities);
            using var foldedWorld = new LaneWorld(entities);
            naiveWorld.CopyFrom(reference);
            foldedWorld.CopyFrom(reference);
            using var oneAtATime = new ColumnEngine(reference, authored, workers, ColumnEngine.OneEntityAtATime);
            using var naive = new ColumnEngine(naiveWorld, authored, workers, ColumnEngine.WholeChunkPerOp);
            using var foldedEngine = new ColumnEngine(foldedWorld, folded, workers, ColumnEngine.WholeChunkPerOp);
            var equal = true;
            for (var f = 0; f < checkFrames; f++)
            {
                oneAtATime.StepOneFrame();
                naive.StepOneFrame();
                foldedEngine.StepOneFrame();
                var expected = reference.Digest();
                var same = expected == naiveWorld.Digest() && expected == foldedWorld.Digest();
                equal &= same;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  frame {0}: one entity at a time {1:X16} whole chunk per op {2:X16} folded {3:X16} {4}",
                    f, expected, naiveWorld.Digest(), foldedWorld.Digest(), same ? "equal" : "DIFFERENT"));
            }

            Console.WriteLine("  " + Program.Census(reference));
            var allocated = 0L;
            allocated += Time("one entity at a time (scalar)", entities, opsPerEntity, threads, authored, ColumnEngine.OneEntityAtATime, Math.Min(frames, 4), workers);
            allocated += Time("whole chunk per op (Vector256)", entities, opsPerEntity, threads, authored, ColumnEngine.WholeChunkPerOp, frames, workers);
            allocated += Time("folded program (Vector256)", entities, opsPerEntity, threads, folded, ColumnEngine.WholeChunkPerOp, frames, workers);
            return equal && allocated == 0 ? 0 : 1;
        }

        static long Time(string label, int entities, int opsPerEntity, int threads, ColumnOp[][] programs, int mode, int frames, Workers workers)
        {
            using var world = new LaneWorld(entities);
            Templates.Spawn(world, 1);
            using var engine = new ColumnEngine(world, programs, workers, mode);
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

            var median = Program.Median(samples);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0,-32} median {1,9:0.000} ms (p10 {2:0.000}, p90 {3:0.000}) = {4:0.0000} ns per entity-op; alloc(main) {5} B",
                label, median, Program.Percentile(samples, 10), Program.Percentile(samples, 90), median * 1e6 / ((double)entities * opsPerEntity), allocated));
            return allocated;
        }
    }
}
