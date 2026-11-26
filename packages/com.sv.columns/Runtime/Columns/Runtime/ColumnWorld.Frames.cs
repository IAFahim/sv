using System;

namespace Sv.Columns
{
    public sealed unsafe partial class ColumnWorld
    {
        ulong** frameRecords;
        int* frameCounts;
        RecordBucket* frameBuckets;
        long* writerLost;
        long* partLost;
        int[]? sortedCapacity;
        bool framed;

        public bool FrameOpen => framed;

        public ColumnView View
        {
            get
            {
                Live();
                return new ColumnView
                {
                    LaneData = (long)lanes,
                    AliveWords = (long)alive,
                    ChunkTemplates = (long)chunkTemplates,
                    Gates = (long)gates,
                    ChunkInts = chunkInts,
                    LaneCount = laneCount,
                    SlotCount = Capacity,
                    EventsLane = tables->EventsLane,
                    EnteredLane = tables->EnteredLane,
                    EventCount = tables->EventCount,
                    NoSourceFlags = NoSourceFlags,
                };
            }
        }

        public ColumnFrame OpenFrame(FrameKernel kernel = FrameKernel.Rows)
        {
            Live();
            if (framed || stepping)
            {
                throw new InvalidOperationException("a frame is already running: close it before opening the next");
            }

            if (logs != null)
            {
                throw new InvalidOperationException("the fused frame keeps no history: step a world opened with history through Step()");
            }

            if (frameRecords == null)
            {
                frameRecords = (ulong**)Native.Zeroed((long)writers.Length * sizeof(ulong*));
                frameCounts = Native.Zeroed<int>(writers.Length);
                frameBuckets = Native.Zeroed<RecordBucket>(writers.Length);
                writerLost = Native.Zeroed<long>(writers.Length * 8L);
                partLost = Native.Zeroed<long>(parts * 8L);
                sortedCapacity = new int[writers.Length];
                for (var w = 0; w < writers.Length; w++)
                {
                    frameBuckets[w].Starts = Native.Zeroed<int>(chunkCount + 2);
                }
            }

            for (var w = 0; w < writers.Length; w++)
            {
                var count = writers[w].Count;
                if (count > sortedCapacity![w])
                {
                    var grown = Math.Max(count, Math.Max(1024, sortedCapacity[w] * 2));
                    Native.Free(frameBuckets[w].Records);
                    frameBuckets[w].Records = Native.Zeroed<ulong>(grown);
                    sortedCapacity[w] = grown;
                }

                frameRecords[w] = writers[w].Records;
                frameCounts[w] = count;
                writerLost[w * 8] = 0;
            }

            for (var p = 0; p < parts; p++)
            {
                partLost[p * 8] = 0;
            }

            BeginFrame();
            framed = true;
            return new ColumnFrame
            {
                Tables = tables,
                Lanes = lanes,
                ChunkTemplates = chunkTemplates,
                Alive = alive,
                Gates = gates,
                ScratchArea = scratch,
                ScratchBytes = scratchBytes,
                ChunkCount = chunkCount,
                ChunkInts = chunkInts,
                ClassLongs = classLongs,
                PartCount = parts,
                SlotCount = Capacity,
                PartLost = partLost,
                WriterCount = writers.Length,
                WriterRecords = frameRecords,
                WriterCounts = frameCounts,
                WriterBuckets = frameBuckets,
                WriterLost = writerLost,
                Kernel = kernel,
                Number = frame,
            };
        }

        public void CloseFrame(long refusedElsewhere = 0)
        {
            Live();
            if (!framed)
            {
                throw new InvalidOperationException("no frame is open: call OpenFrame first");
            }

            var lost = refusedElsewhere;
            for (var p = 0; p < parts; p++)
            {
                lost += partLost[p * 8];
            }

            for (var w = 0; w < writers.Length; w++)
            {
                lost += writerLost[w * 8];
            }

            dropped[0] += lost;
            for (var w = 0; w < writers.Length; w++)
            {
                dropped[w * 8] += writers[w].TakeRefused();
                writers[w].Clear();
            }

            framed = false;
            EndFrame();
        }

        public void StepFused(FrameKernel kernel = FrameKernel.Rows)
        {
            var view = OpenFrame(kernel);
            for (var w = 0; w < view.Writers; w++)
            {
                view.BucketWriter(w);
            }

            for (var p = 0; p < view.Parts; p++)
            {
                view.Resolve(p, frameBuckets, view.Writers);
            }

            CloseFrame();
        }

        void FreeFrames()
        {
            if (frameRecords == null)
            {
                return;
            }

            for (var w = 0; w < writers.Length; w++)
            {
                Native.Free(frameBuckets[w].Records);
                Native.Free(frameBuckets[w].Starts);
            }

            Native.Free(frameRecords);
            Native.Free(frameCounts);
            Native.Free(frameBuckets);
            Native.Free(writerLost);
            Native.Free(partLost);
            frameRecords = null;
            frameCounts = null;
            frameBuckets = null;
            writerLost = null;
            partLost = null;
        }
    }
}
