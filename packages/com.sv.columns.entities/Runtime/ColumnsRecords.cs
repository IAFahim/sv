using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;

namespace Sv.Columns.Entities
{
    public unsafe struct ColumnsRecords : IDisposable
    {
        [NativeDisableUnsafePtrRestriction]
        internal UnsafeList<ulong>* Lists;

        [NativeDisableUnsafePtrRestriction]
        internal long* Refused;

        internal int ThreadCount;

        internal ColumnView View;

        public static ColumnsRecords Create(ColumnView view, int initialCapacity)
        {
            var count = JobsUtility.ThreadIndexCount;
            var records = new ColumnsRecords
            {
                Lists = (UnsafeList<ulong>*)UnsafeUtility.Malloc((long)sizeof(UnsafeList<ulong>) * count, UnsafeUtility.AlignOf<UnsafeList<ulong>>(), Allocator.Persistent),
                Refused = (long*)UnsafeUtility.Malloc(count * 64L, 64, Allocator.Persistent),
                ThreadCount = count,
                View = view,
            };
            UnsafeUtility.MemClear(records.Refused, count * 64L);
            for (var t = 0; t < count; t++)
            {
                records.Lists[t] = new UnsafeList<ulong>(Math.Max(initialCapacity, 16), Allocator.Persistent);
            }

            return records;
        }

        public bool IsCreated => Lists != null;

        public int ThreadSets => ThreadCount;

        public int Pending
        {
            get
            {
                var sum = 0;
                for (var t = 0; t < ThreadCount; t++)
                {
                    sum += Lists[t].Length;
                }

                return sum;
            }
        }

        public ParallelWriter AsParallelWriter() => new ParallelWriter { Lists = Lists, Refused = Refused, View = View };

        internal long TakeRefused()
        {
            var sum = 0L;
            for (var t = 0; t < ThreadCount; t++)
            {
                sum += Refused[t * 8];
                Refused[t * 8] = 0;
            }

            return sum;
        }

        public void Dispose()
        {
            if (Lists == null)
            {
                return;
            }

            for (var t = 0; t < ThreadCount; t++)
            {
                Lists[t].Dispose();
            }

            UnsafeUtility.Free(Lists, Allocator.Persistent);
            UnsafeUtility.Free(Refused, Allocator.Persistent);
            Lists = null;
            Refused = null;
        }

        public struct ParallelWriter
        {
            [NativeDisableUnsafePtrRestriction]
            internal UnsafeList<ulong>* Lists;

            [NativeDisableUnsafePtrRestriction]
            internal long* Refused;

            internal ColumnView View;

            [NativeSetThreadIndex]
            internal int ThreadIndex;

            public void Send(int target, int channel, long amount, int hits = 1)
            {
                if (View.TryRecord(target, channel, amount, hits, out var record))
                {
                    Lists[ThreadIndex].Add(record);
                }
                else
                {
                    Refused[ThreadIndex * 8]++;
                }
            }

            public void SendFrom(int source, int target, int channel, long amount, int hits = 1)
            {
                if (View.TryRecordFrom(source, target, channel, amount, hits, out var record))
                {
                    Lists[ThreadIndex].Add(record);
                }
                else
                {
                    Refused[ThreadIndex * 8]++;
                }
            }

            public void SendRecord(ulong record) => Lists[ThreadIndex].Add(record);
        }
    }
}
