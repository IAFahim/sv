using System;

namespace Sv
{
    public sealed unsafe class LanePool : IDisposable
    {
        public const int MaxSegments = 1 << (32 - Lanes.SegmentShift);
        public const int SegmentAlignment = 4096;

        byte** segments;
        int segmentCount;
        uint* free;
        int freeCount;
        int freeCapacity;
        long usable;

        public LanePool()
        {
            segments = (byte**)Memory.AllocZeroed((nuint)MaxSegments, (nuint)sizeof(byte*));
        }

        public byte** Segments => segments;

        public int SegmentCount => segmentCount;

        public int Available => freeCount;

        public long Usable => usable;

        public long Bytes => (long)segmentCount * Lanes.SegmentBytes;

        public int AddSegment(int reserved)
        {
            if (segmentCount == MaxSegments)
            {
                throw new InvalidOperationException("sv: the lane pool reached 2^32 lanes");
            }

            if (reserved < 0 || reserved > Lanes.UsableLanes)
            {
                throw new ArgumentOutOfRangeException(nameof(reserved));
            }

            var segment = (byte*)Memory.AllocAligned((nuint)Lanes.SegmentBytes, SegmentAlignment);
            Memory.Clear(segment, (nuint)Lanes.SegmentBytes);
            var index = segmentCount;
            segments[index] = segment;
            segmentCount++;
            var given = Lanes.UsableLanes - reserved;
            usable += given;
            if (usable > freeCapacity)
            {
                var capacity = (int)Math.Min(int.MaxValue, Math.Max(usable, (long)freeCapacity * 2));
                free = (uint*)Memory.Realloc(free, (nuint)capacity * sizeof(uint));
                freeCapacity = capacity;
            }

            var first = ((uint)index << Lanes.SegmentShift) + (uint)(Lanes.HeaderLanes + reserved);
            for (var lane = Lanes.UsableLanes - reserved - 1; lane >= 0; lane--)
            {
                free[freeCount] = first + (uint)lane;
                freeCount++;
            }

            return index;
        }

        public uint Take()
        {
            if (freeCount == 0)
            {
                AddSegment(0);
            }

            freeCount--;
            return free[freeCount];
        }

        public void Give(uint lane)
        {
            free[freeCount] = lane;
            freeCount++;
        }

        public LaneWriter CreateWriter(int capacity)
        {
            var writer = default(LaneWriter);
            Grow(ref writer, capacity < 1 ? 1 : capacity);
            return writer;
        }

        public void Reserve(ref LaneWriter writer, int lanes)
        {
            if (writer.FreeCapacity < lanes || writer.JournalCapacity - writer.JournalCount < lanes)
            {
                Grow(ref writer, Math.Max(lanes + writer.JournalCount, writer.JournalCapacity * 2));
            }

            while (writer.FreeCount < lanes)
            {
                writer.Free[writer.FreeCount] = Take();
                writer.FreeCount++;
            }
        }

        public void DisposeWriter(ref LaneWriter writer)
        {
            for (var at = 0; at < writer.FreeCount; at++)
            {
                Give(writer.Free[at]);
            }

            Memory.Free(writer.Free);
            Memory.Free(writer.Journal);
            writer = default;
        }

        void Grow(ref LaneWriter writer, int capacity)
        {
            if (capacity > writer.FreeCapacity)
            {
                writer.Free = (uint*)Memory.Realloc(writer.Free, (nuint)capacity * sizeof(uint));
                writer.FreeCapacity = capacity;
            }

            if (capacity > writer.JournalCapacity)
            {
                writer.Journal = (JournalEntry*)Memory.Realloc(writer.Journal, (nuint)capacity * (nuint)sizeof(JournalEntry));
                writer.JournalCapacity = capacity;
            }
        }

        public void Dispose()
        {
            if (segments == null)
            {
                return;
            }

            for (var index = 0; index < segmentCount; index++)
            {
                Memory.FreeAligned(segments[index]);
            }

            Memory.Free(segments);
            Memory.Free(free);
            segments = null;
            free = null;
            segmentCount = 0;
            freeCount = 0;
            freeCapacity = 0;
            usable = 0;
        }
    }
}
