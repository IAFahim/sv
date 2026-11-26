using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace Sv.Columns.Entities
{
    public sealed unsafe class ColumnsBurstHost : IDisposable
    {
        readonly ColumnWorld world;
        readonly CompiledPack? ownedPack;
        readonly FrameKernel kernel;
        readonly int bucketCount;
        readonly int startsLength;
        ColumnsRecords records;
        ColumnsLaneSets sets;
        ColumnFrame* frame;
        RecordBucket* buckets;
        RecordBucket* dense;
        UnsafeList<ulong>* sorted;
        int* starts;
        long* outside;
        int* probes;
        JobHandle pending;
        bool open;
        bool disposed;

        public ColumnsBurstHost(CompiledPack pack, int capacity, int writers = 1, int resolveParts = 0, FrameKernel kernel = FrameKernel.Rows, bool ownsPack = false)
        {
            if (pack == null)
            {
                throw new ArgumentNullException(nameof(pack));
            }

            var chunks = (capacity + 63) / 64;
            var parts = resolveParts > 0 ? resolveParts : Math.Max(1, Math.Min(chunks, (JobsUtility.JobWorkerCount + 1) * 4));
            world = new ColumnWorld(pack, capacity, writers, parts);
            ownedPack = ownsPack ? pack : null;
            this.kernel = kernel;
            records = ColumnsRecords.Create(world.View, 64 + capacity / Math.Max(1, JobsUtility.ThreadIndexCount));
            sets = ColumnsLaneSets.Create(16);
            var threads = records.ThreadCount;
            bucketCount = world.Writers + threads;
            startsLength = world.Chunks + 2;
            frame = Alloc<ColumnFrame>(1);
            buckets = Alloc<RecordBucket>(bucketCount);
            dense = Alloc<RecordBucket>((long)bucketCount * world.ResolveParts);
            sorted = Alloc<UnsafeList<ulong>>(threads);
            starts = Alloc<int>((long)threads * startsLength);
            outside = Alloc<long>(threads * 8L);
            probes = Alloc<int>(16);
            for (var t = 0; t < threads; t++)
            {
                sorted[t] = new UnsafeList<ulong>(64, Allocator.Persistent);
            }
        }

        public ColumnWorld World => world;

        public ColumnView View => world.View;

        public FrameKernel Kernel => kernel;

        public ColumnsRecords Records => records;

        public ColumnsRecords.ParallelWriter Sender => records.AsParallelWriter();

        public ColumnsLaneSets.ParallelWriter Sets => sets.AsParallelWriter();

        public int PendingSets => sets.Pending;

        public long RefusedSets { get; private set; }

        public bool Running => open;

        public bool BucketRanBurst => probes[0] == 1;

        public bool ResolveRanBurst => probes[1] == 1;

        public JobHandle Schedule(JobHandle dependsOn = default)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ColumnsBurstHost));
            }

            Complete();
            RefusedSets += sets.Apply(world);
            *frame = world.OpenFrame(kernel);
            open = true;
            var bucketed = new BucketJob
            {
                Frame = frame,
                Buckets = buckets,
                Lists = records.Lists,
                Sorted = sorted,
                Starts = starts,
                Outside = outside,
                Probe = probes,
                Writers = world.Writers,
                StartsLength = startsLength,
            }.Schedule(bucketCount, 1, dependsOn);
            pending = new ResolveJob
            {
                Frame = frame,
                Buckets = buckets,
                Dense = dense,
                Probe = probes + 1,
                BucketCount = bucketCount,
            }.Schedule(world.ResolveParts, 1, bucketed);
            return pending;
        }

        public void Complete()
        {
            pending.Complete();
            pending = default;
            if (!open)
            {
                return;
            }

            var refused = records.TakeRefused();
            for (var t = 0; t < records.ThreadCount; t++)
            {
                refused += outside[t * 8];
                outside[t * 8] = 0;
            }

            open = false;
            world.CloseFrame(refused);
        }

        public void Step()
        {
            Schedule();
            Complete();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            pending.Complete();
            if (open)
            {
                open = false;
                world.CloseFrame();
            }

            disposed = true;
            for (var t = 0; t < records.ThreadCount; t++)
            {
                sorted[t].Dispose();
            }

            records.Dispose();
            sets.Dispose();
            Free(frame);
            Free(buckets);
            Free(dense);
            Free(sorted);
            Free(starts);
            Free(outside);
            Free(probes);
            world.Dispose();
            ownedPack?.Dispose();
        }

        static T* Alloc<T>(long count) where T : unmanaged
        {
            var bytes = Math.Max(1L, count) * sizeof(T);
            var memory = UnsafeUtility.Malloc(bytes, 64, Allocator.Persistent);
            UnsafeUtility.MemClear(memory, bytes);
            return (T*)memory;
        }

        static void Free(void* memory) => UnsafeUtility.Free(memory, Allocator.Persistent);

        [BurstDiscard]
        static void Managed(ref int burst) => burst = 0;

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
        struct BucketJob : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction]
            public ColumnFrame* Frame;

            [NativeDisableUnsafePtrRestriction]
            public RecordBucket* Buckets;

            [NativeDisableUnsafePtrRestriction]
            public UnsafeList<ulong>* Lists;

            [NativeDisableUnsafePtrRestriction]
            public UnsafeList<ulong>* Sorted;

            [NativeDisableUnsafePtrRestriction]
            public int* Starts;

            [NativeDisableUnsafePtrRestriction]
            public long* Outside;

            [NativeDisableUnsafePtrRestriction]
            public int* Probe;

            public int Writers;

            public int StartsLength;

            public void Execute(int index)
            {
                if (index == 0)
                {
                    var burst = 1;
                    Managed(ref burst);
                    *Probe = burst;
                }

                if (index < Writers)
                {
                    Frame->BucketWriter(index);
                    Buckets[index] = Frame->WriterBucket(index);
                    return;
                }

                var thread = index - Writers;
                var list = Lists + thread;
                var into = Sorted + thread;
                if (into->Capacity < list->Length)
                {
                    into->SetCapacity(Math.Max(list->Length, into->Capacity * 2));
                }

                into->Length = list->Length;
                var at = Starts + (long)thread * StartsLength;
                Outside[thread * 8] = Frame->Bucket(list->Ptr, list->Length, into->Ptr, at);
                Buckets[index] = new RecordBucket { Records = into->Ptr, Starts = at };
                list->Clear();
            }
        }

        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
        struct ResolveJob : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction]
            public ColumnFrame* Frame;

            [NativeDisableUnsafePtrRestriction]
            public RecordBucket* Buckets;

            [NativeDisableUnsafePtrRestriction]
            public RecordBucket* Dense;

            [NativeDisableUnsafePtrRestriction]
            public int* Probe;

            public int BucketCount;

            public void Execute(int part)
            {
                if (part == 0)
                {
                    var burst = 1;
                    Managed(ref burst);
                    *Probe = burst;
                }

                var chunks = Frame->Chunks;
                var mine = Dense + (long)part * BucketCount;
                var count = 0;
                for (var b = 0; b < BucketCount; b++)
                {
                    if (Buckets[b].Starts[chunks] > 0)
                    {
                        mine[count++] = Buckets[b];
                    }
                }

                Frame->Resolve(part, mine, count);
            }
        }
    }
}
