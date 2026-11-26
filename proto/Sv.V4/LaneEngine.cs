using System;
using System.Diagnostics;
using Sv.Host;
#if NET
using System.Runtime.Intrinsics;
#endif

namespace Sv.V4
{
    public unsafe struct EngineFrame
    {
        public LaneView World;
        public SlotTables Tables;
        public Effect** Lists;
        public int* Counts;
        public int Writers;
        public ClassSumBlocks** Sums;
        public long* Unsupported;
        public TemplateColumns* Columns;
        public long* EmittedLastFrame;
        public long* EmitNextFrame;
        public byte* Scratch;
        public int ScratchBytes;
        public int ResolveParts;
        public int Vectorized;
        public int Runs;
    }

    public sealed unsafe class LaneEngine : IDisposable
    {
        const int Rows = LaneWorld.Rows;
        const int LaneValuesAt = ClassSumBlocks.BlockLongs * sizeof(long);
        const int ScratchBytes = LaneValuesAt + Lane.Count * 64;
        readonly Workers workers;
        readonly EngineFrame* frame;
        readonly long* emitsA;
        readonly long* emitsB;
        public double ReduceMilliseconds;
        public double ResolveMilliseconds;

        public LaneEngine(LaneWorld world, RecordStream stream, CompiledColumns columns, Workers workers, bool vectorized, bool runs)
        {
            this.workers = workers;
            frame = (EngineFrame*)Memory.AllocZeroed(1, (nuint)sizeof(EngineFrame));
            frame->World = world.View;
            frame->Tables = SlotTables.Build(world);
            if (stream != null)
            {
                frame->Lists = stream.Lists;
                frame->Counts = stream.Counts;
                frame->Writers = stream.Writers;
                frame->Sums = (ClassSumBlocks**)Memory.AllocZeroed((nuint)stream.Writers, (nuint)sizeof(ClassSumBlocks*));
                frame->Unsupported = (long*)Memory.AllocZeroed((nuint)stream.Writers, sizeof(long));
                for (var w = 0; w < stream.Writers; w++)
                {
                    frame->Sums[w] = ClassSumBlocks.Create(world.Chunks);
                }
            }

            frame->Columns = columns != null ? columns.Templates : null;
            emitsA = (long*)Memory.AllocZeroed((nuint)((long)world.Chunks * EmitClass.BlockLongs), sizeof(long));
            emitsB = (long*)Memory.AllocZeroed((nuint)((long)world.Chunks * EmitClass.BlockLongs), sizeof(long));
            frame->EmittedLastFrame = emitsA;
            frame->EmitNextFrame = emitsB;
            frame->ResolveParts = WorkRange.PartsFor(world.Chunks, workers.Count * 8);
            frame->ScratchBytes = ScratchBytes;
            frame->Scratch = (byte*)Memory.AllocAligned((nuint)((long)frame->ResolveParts * ScratchBytes), 64);
            frame->Vectorized = vectorized ? 1 : 0;
            frame->Runs = runs ? 1 : 0;
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
            if (frame->Writers > 0)
            {
                workers.Run(&ReduceWriter, frame, frame->Writers);
            }

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
            var frame = (EngineFrame*)state;
            var sums = frame->Sums[writer];
            ClassSumBlocks.Forget(sums);
            frame->Unsupported[writer] = RecordReducer.ReduceIntoClassSums((uint)frame->World.SlotLimit, frame->Tables, frame->Lists[writer], frame->Counts[writer], sums, frame->Runs != 0);
        }

        static void ResolveChunks(void* state, int part)
        {
            var frame = (EngineFrame*)state;
            WorkRange.Of(frame->World.Chunks, part, frame->ResolveParts, out var from, out var to);
            var scratch = frame->Scratch + (long)part * frame->ScratchBytes;
            var totals = (long*)scratch;
            for (var chunk = from; chunk < to; chunk++)
            {
                var emitted = frame->EmittedLastFrame + (long)chunk * EmitClass.BlockLongs;
                var emitNext = frame->EmitNextFrame + (long)chunk * EmitClass.BlockLongs;
                RecordFold.GatherClassSums(totals, frame->Sums, frame->Writers, chunk);
                var lanes = frame->World.ChunkLanes(chunk);
                var firstSlot = chunk << EffectKey.RowBits;
                var contexts = frame->Tables.Contexts + firstSlot;
#if NET
                if (frame->Vectorized != 0)
                {
                    if (frame->Columns != null)
                    {
                        ColumnKernel.AddChunk(lanes, contexts, frame->Columns[Templates.OfChunk(chunk)], totals, (Vector512<long>*)(scratch + LaneValuesAt));
                    }

                    RecordFold.ResolveChunkVector(lanes, totals, emitted, emitNext, frame->Tables, firstSlot);
                    continue;
                }
#endif
                if (frame->Columns != null)
                {
                    ColumnLaw.AddChunk(lanes, contexts, frame->Columns[Templates.OfChunk(chunk)], totals);
                }

                RecordFold.ResolveChunkScalar(lanes, totals, emitted, emitNext, frame->Tables, firstSlot);
            }
        }

        public void Dispose()
        {
            for (var w = 0; w < frame->Writers; w++)
            {
                ClassSumBlocks.Free(frame->Sums[w]);
            }

            if (frame->Writers > 0)
            {
                Memory.Free(frame->Sums);
                Memory.Free(frame->Unsupported);
            }

            frame->Tables.Free();
            Memory.FreeAligned(frame->Scratch);
            Memory.Free(emitsA);
            Memory.Free(emitsB);
            Memory.Free(frame);
        }
    }
}
