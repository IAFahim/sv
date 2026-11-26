using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;

namespace Sv.Columns.Entities
{
    public struct ColumnsLaneSet
    {
        public int Slot;
        public int Lane;
        public int Value;
    }

    public unsafe struct ColumnsLaneSets : IDisposable
    {
        [NativeDisableUnsafePtrRestriction]
        internal UnsafeList<ColumnsLaneSet>* Lists;

        internal int ThreadCount;

        public static ColumnsLaneSets Create(int initialCapacity)
        {
            var count = JobsUtility.ThreadIndexCount;
            var sets = new ColumnsLaneSets
            {
                Lists = (UnsafeList<ColumnsLaneSet>*)UnsafeUtility.Malloc((long)sizeof(UnsafeList<ColumnsLaneSet>) * count, UnsafeUtility.AlignOf<UnsafeList<ColumnsLaneSet>>(), Allocator.Persistent),
                ThreadCount = count,
            };
            for (var t = 0; t < count; t++)
            {
                sets.Lists[t] = new UnsafeList<ColumnsLaneSet>(Math.Max(initialCapacity, 4), Allocator.Persistent);
            }

            return sets;
        }

        public bool IsCreated => Lists != null;

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

        public ParallelWriter AsParallelWriter() => new ParallelWriter { Lists = Lists };

        internal long Apply(ColumnWorld world)
        {
            var pending = Pending;
            if (pending == 0)
            {
                return 0;
            }

            var all = new NativeArray<ColumnsLaneSet>(pending, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var at = 0;
            for (var t = 0; t < ThreadCount; t++)
            {
                var list = Lists + t;
                for (var i = 0; i < list->Length; i++)
                {
                    all[at++] = (*list)[i];
                }

                list->Clear();
            }

            all.Sort(new SlotLaneValue());
            var view = world.View;
            var settable = view.Lanes - 2;
            var refused = 0L;
            for (var i = 0; i < all.Length; i++)
            {
                var set = all[i];
                if (!view.IsAlive(set.Slot) || (uint)set.Lane >= (uint)settable)
                {
                    refused++;
                    continue;
                }

                world.Set(set.Slot, set.Lane, set.Value);
            }

            all.Dispose();
            return refused;
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
            Lists = null;
        }

        struct SlotLaneValue : IComparer<ColumnsLaneSet>
        {
            public int Compare(ColumnsLaneSet x, ColumnsLaneSet y)
            {
                var bySlot = x.Slot.CompareTo(y.Slot);
                if (bySlot != 0)
                {
                    return bySlot;
                }

                var byLane = x.Lane.CompareTo(y.Lane);
                return byLane != 0 ? byLane : x.Value.CompareTo(y.Value);
            }
        }

        public struct ParallelWriter
        {
            [NativeDisableUnsafePtrRestriction]
            internal UnsafeList<ColumnsLaneSet>* Lists;

            [NativeSetThreadIndex]
            internal int ThreadIndex;

            public bool IsCreated => Lists != null;

            public void Set(int slot, int lane, int value) => Lists[ThreadIndex].Add(new ColumnsLaneSet { Slot = slot, Lane = lane, Value = value });
        }
    }
}
