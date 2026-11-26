using System;

namespace Sv.Columns
{
    public sealed unsafe partial class ColumnWorld
    {
        ulong** frameRecords;
        int* frameCounts;
        RecordBucket* frameBuckets;
        ulong** frameScratch;
        long* writerLost;
        long* partLost;
        int[]? scratchCapacity;
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

        public int FrameRecordSets => writers.Length + 1;

        public ColumnFrame OpenFrame(FrameKernel kernel = FrameKernel.Rows)
        {
            Live();
            if (framed || stepping)
            {
                throw new InvalidOperationException("a frame is already running: close it before opening the next");
            }

            var sets = writers.Length + 1;
            if (frameRecords == null)
            {
                frameRecords = (ulong**)Native.Zeroed((long)sets * sizeof(ulong*));
                frameScratch = (ulong**)Native.Zeroed((long)sets * sizeof(ulong*));
                frameCounts = Native.Zeroed<int>(sets);
                frameBuckets = Native.Zeroed<RecordBucket>(sets);
                writerLost = Native.Zeroed<long>(sets * 8L);
                partLost = Native.Zeroed<long>(parts * 8L);
                scratchCapacity = new int[sets];
                for (var s = 0; s < sets; s++)
                {
                    frameBuckets[s].Starts = Native.Zeroed<int>(chunkCount + 2);
                }
            }

            BeginFrame();
            framed = true;
            for (var w = 0; w < writers.Length; w++)
            {
                var count = writers[w].Count;
                SetRecords(w, writers[w].Records, count);
                LogRecords(w, writers[w].Records, count, 0);
            }

            if (resimulating)
            {
                var log = LogOf(frame - 1);
                SetRecords(writers.Length, log->Records, log->RecordCount);
            }
            else
            {
                SetRecords(writers.Length, null, 0);
            }

            for (var p = 0; p < parts; p++)
            {
                partLost[p * 8] = 0;
            }

            var kept = logs != null ? CheckpointOf(frame) : null;
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
                WriterCount = sets,
                WriterRecords = frameRecords,
                WriterCounts = frameCounts,
                WriterBuckets = frameBuckets,
                WriterScratch = frameScratch,
                WriterLost = writerLost,
                ReplayMask = replayMask,
                KeepLanes = kept != null ? kept->Lanes : null,
                KeepGates = kept != null ? kept->Gates : null,
                Kernel = kernel,
                Number = frame,
            };
        }

        void SetRecords(int set, ulong* records, int count)
        {
            if (count > scratchCapacity![set])
            {
                var grown = Math.Max(count, Math.Max(1024, scratchCapacity[set] * 2));
                Native.Free(frameScratch[set]);
                frameScratch[set] = Native.Zeroed<ulong>(grown);
                scratchCapacity[set] = grown;
            }

            frameRecords[set] = records;
            frameCounts[set] = count;
            frameBuckets[set].Records = frameScratch[set];
            frameBuckets[set].Starts[chunkCount] = 0;
            writerLost[set * 8] = 0;
        }

        public void KeepFrameRecords(ulong* records, int count)
        {
            Live();
            if (!framed)
            {
                throw new InvalidOperationException("no frame is open: call OpenFrame first");
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (logs == null || resimulating || count == 0)
            {
                return;
            }

            var log = LogOf(frame - 1);
            if ((long)log->RecordCount + count > int.MaxValue)
            {
                throw new InvalidOperationException("a frame holds at most " + int.MaxValue + " records");
            }

            EnsureRecords(log, log->RecordCount + count);
            Buffer.MemoryCopy(records, log->Records + log->RecordCount, (long)count * 8, (long)count * 8);
            log->RecordCount += count;
        }

        public void CloseFrame(long refusedElsewhere = 0, long lostElsewhere = 0)
        {
            Live();
            if (!framed)
            {
                throw new InvalidOperationException("no frame is open: call OpenFrame first");
            }

            var lost = refusedElsewhere + lostElsewhere;
            for (var p = 0; p < parts; p++)
            {
                lost += partLost[p * 8];
            }

            for (var s = 0; s <= writers.Length; s++)
            {
                lost += writerLost[s * 8];
            }

            dropped[0] += lost;
            var refused = refusedElsewhere;
            for (var w = 0; w < writers.Length; w++)
            {
                var writerRefused = writers[w].TakeRefused();
                dropped[w * 8] += writerRefused;
                refused += writerRefused;
                writers[w].Clear();
            }

            if (logs != null && !resimulating)
            {
                LogOf(frame - 1)->Refused += refused;
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

        public void ResimulateFused(FrameKernel kernel = FrameKernel.Rows)
        {
            BeginResimulate();
            while (resimulating)
            {
                StepFused(kernel);
            }
        }

        void FreeFrames()
        {
            if (frameRecords == null)
            {
                return;
            }

            for (var s = 0; s <= writers.Length; s++)
            {
                Native.Free(frameScratch[s]);
                Native.Free(frameBuckets[s].Starts);
            }

            Native.Free(frameRecords);
            Native.Free(frameScratch);
            Native.Free(frameCounts);
            Native.Free(frameBuckets);
            Native.Free(writerLost);
            Native.Free(partLost);
            frameRecords = null;
            frameScratch = null;
            frameCounts = null;
            frameBuckets = null;
            writerLost = null;
            partLost = null;
        }
    }
}
