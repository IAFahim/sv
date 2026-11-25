using System;
using System.Diagnostics;
using Sv.Host;

namespace Sv.V4
{
    public unsafe struct RecordFrame
    {
        public LaneView World;
        public byte* SourceFlags;
        public Effect** Lists;
        public int* Counts;
        public int Writers;
        public ClassSumBlocks** Sums;
        public long* Unsupported;
        public long* EmittedLastFrame;
        public long* EmitNextFrame;
        public long* TotalsScratch;
        public int ResolveParts;
        public int Vectorized;
    }

    public sealed unsafe class LaneEngine : IDisposable
    {
        readonly Workers workers;
        readonly RecordFrame* frame;
        readonly long* emitsA;
        readonly long* emitsB;
        public double ReduceMilliseconds;
        public double ResolveMilliseconds;

        public LaneEngine(LaneWorld world, RecordStream stream, Workers workers, bool vectorized)
        {
            this.workers = workers;
            frame = (RecordFrame*)Memory.AllocZeroed(1, (nuint)sizeof(RecordFrame));
            frame->World = world.View;
            frame->SourceFlags = SourceFlag.Build(world);
            frame->Lists = stream.Lists;
            frame->Counts = stream.Counts;
            frame->Writers = stream.Writers;
            frame->Sums = (ClassSumBlocks**)Memory.AllocZeroed((nuint)stream.Writers, (nuint)sizeof(ClassSumBlocks*));
            for (var w = 0; w < stream.Writers; w++)
            {
                frame->Sums[w] = ClassSumBlocks.Create(world.Chunks);
            }

            frame->Unsupported = (long*)Memory.AllocZeroed((nuint)stream.Writers, sizeof(long));
            emitsA = (long*)Memory.AllocZeroed((nuint)((long)world.Chunks * EmitClass.BlockLongs), sizeof(long));
            emitsB = (long*)Memory.AllocZeroed((nuint)((long)world.Chunks * EmitClass.BlockLongs), sizeof(long));
            frame->EmittedLastFrame = emitsA;
            frame->EmitNextFrame = emitsB;
            frame->ResolveParts = WorkRange.PartsFor(world.Chunks, workers.Count * 8);
            frame->TotalsScratch = (long*)Memory.Alloc((nuint)((long)frame->ResolveParts * ClassSumBlocks.BlockLongs), sizeof(long));
            frame->Vectorized = vectorized ? 1 : 0;
        }

        public long Unsupported
        {
            get
            {
                var sum = 0L;
                for (var w = 0; w < frame->Writers; w++)
                {
                    sum += frame->Unsupported[w];
                }

                return sum;
            }
        }

        public void StepOneFrame()
        {
            var start = Stopwatch.GetTimestamp();
            workers.Run(&ReduceWriter, frame, frame->Writers);
            var reduced = Stopwatch.GetTimestamp();
            workers.Run(&ResolveChunks, frame, frame->ResolveParts);
            var resolved = Stopwatch.GetTimestamp();
            var swap = frame->EmittedLastFrame;
            frame->EmittedLastFrame = frame->EmitNextFrame;
            frame->EmitNextFrame = swap;
            ReduceMilliseconds = (reduced - start) * 1000.0 / Stopwatch.Frequency;
            ResolveMilliseconds = (resolved - reduced) * 1000.0 / Stopwatch.Frequency;
        }

        static void ReduceWriter(void* state, int writer)
        {
            var frame = (RecordFrame*)state;
            var sums = frame->Sums[writer];
            ClassSumBlocks.Forget(sums);
            frame->Unsupported[writer] = RecordReducer.ReduceIntoClassSums(frame->World, frame->SourceFlags, frame->Lists[writer], frame->Counts[writer], sums);
        }

        static void ResolveChunks(void* state, int part)
        {
            var frame = (RecordFrame*)state;
            WorkRange.Of(frame->World.Chunks, part, frame->ResolveParts, out var from, out var to);
            var totals = frame->TotalsScratch + (long)part * ClassSumBlocks.BlockLongs;
            for (var chunk = from; chunk < to; chunk++)
            {
                var emitted = frame->EmittedLastFrame + (long)chunk * EmitClass.BlockLongs;
                var emitNext = frame->EmitNextFrame + (long)chunk * EmitClass.BlockLongs;
                ChunkFold.GatherClassSums(totals, frame->Sums, frame->Writers, chunk, emitted);
                var lanes = frame->World.ChunkLanes(chunk);
#if NET
                if (frame->Vectorized != 0)
                {
                    ChunkFold.ResolveChunkVector(lanes, totals, emitNext);
                    continue;
                }
#endif
                ChunkFold.ResolveChunkScalar(lanes, totals, emitNext);
            }
        }

        public void Dispose()
        {
            for (var w = 0; w < frame->Writers; w++)
            {
                ClassSumBlocks.Free(frame->Sums[w]);
            }

            Memory.Free(frame->Sums);
            Memory.Free(frame->SourceFlags);
            Memory.Free(frame->Unsupported);
            Memory.Free(frame->TotalsScratch);
            Memory.Free(emitsA);
            Memory.Free(emitsB);
            Memory.Free(frame);
        }
    }
}
