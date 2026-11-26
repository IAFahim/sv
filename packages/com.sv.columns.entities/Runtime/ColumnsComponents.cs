using System;
using System.Text;
using Unity.Collections;
using Unity.Entities;

namespace Sv.Columns.Entities
{
    public struct ColumnsPackBlob
    {
        public BlobArray<byte> Json;
    }

    public struct ColumnsWorldConfig : IComponentData
    {
        public BlobAssetReference<ColumnsPackBlob> Pack;
        public int Capacity;
        public int Writers;
        public int ResolveParts;
        public FrameKernel Kernel;
    }

    public struct ColumnsTemplate : IComponentData
    {
        public int Value;
    }

    [InternalBufferCapacity(4)]
    public struct ColumnsLaneValue : IBufferElementData
    {
        public int Lane;
        public int Value;
    }

    public struct ColumnsSlot : IComponentData
    {
        public int Value;
    }

    public struct ColumnsSlotCleanup : ICleanupComponentData
    {
        public int Value;
    }

    public struct ColumnsState : IComponentData
    {
        public ColumnView View;
        public ColumnsRecords.ParallelWriter Sender;
        public int Frame;
        public long Dropped;
        public bool BurstBucket;
        public bool BurstResolve;
    }

    public struct ColumnsHostHandle : IComponentData
    {
        public IntPtr Value;
    }

    public static unsafe class ColumnsPacks
    {
        public static BlobAssetReference<ColumnsPackBlob> Create(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            var bytes = Encoding.UTF8.GetBytes(json);
            using var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<ColumnsPackBlob>();
            var array = builder.Allocate(ref root.Json, bytes.Length);
            for (var i = 0; i < bytes.Length; i++)
            {
                array[i] = bytes[i];
            }

            return builder.CreateBlobAssetReference<ColumnsPackBlob>(Allocator.Persistent);
        }

        public static string JsonOf(ref ColumnsPackBlob blob) =>
            blob.Json.Length == 0 ? string.Empty : Encoding.UTF8.GetString((byte*)blob.Json.GetUnsafePtr(), blob.Json.Length);
    }
}
