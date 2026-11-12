using System;
using System.Runtime.CompilerServices;

namespace Sv
{
    public enum LaneClass : byte
    {
        Retired = 0,
        Stat = 1,
        Intrinsic = 2,
        Event = 3,
        Meta = 4,
    }

    public enum StoragePolicy : byte
    {
        Shared = 0,
        Dense = 1,
    }

    public enum SlotState : byte
    {
        Free = 0,
        Live = 1,
        Dying = 2,
    }

    public static class Slot
    {
        public const int Bits = 22;
        public const int Capacity = 1 << Bits;
        public const int Mask = Capacity - 1;
        public const int None = Mask;
        public const int RowBits = 6;
        public const int Rows = 1 << RowBits;
        public const int RowMask = Rows - 1;
        public const int MaxChunks = Capacity >> RowBits;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Of(int chunk, int row) => (chunk << RowBits) | row;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Chunk(int slot) => slot >> RowBits;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Row(int slot) => slot & RowMask;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Bit(int slot) => 1ul << (slot & RowMask);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Reserved(int chunk) => chunk == MaxChunks - 1 ? 1ul << RowMask : 0ul;
    }

    public static class EventId
    {
        public const int PerLane = 32;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Lane(int raised) => raised >> 5;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Word(int raised) => raised & 31;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Of(int lane, int word) => (lane << 5) | (word & 31);
    }

    public static class ChunkMeta
    {
        public const int Live = 0;
        public const int Dying = 1;
        public const int Template = 2;
        public const ulong Unassigned = 0ul;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TemplateOf(ulong word) => (int)(uint)word - 1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong WordOf(int template) => (ulong)(uint)(template + 1);
    }

    public unsafe struct WorldLayout
    {
        public const int MetaLanes = 2;
        public const int MaxLanesPerChunk = 8064;

        public LaneClass* Classes;
        public int ValueLanes;
        public int MetaLane;
        public int DeathLane;
        public int LanesPerChunk;
        public int Channels;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LaneClass ClassOf(int lane) => (uint)lane < (uint)ValueLanes ? Classes[lane] : LaneClass.Meta;

        public static void Validate(ReadOnlySpan<LaneClass> classes, int channels)
        {
            if (channels < 0 || channels > 256)
            {
                throw new ArgumentOutOfRangeException(nameof(channels), "sv: a pack holds 0 to 256 channels");
            }

            if (classes.Length + MetaLanes > MaxLanesPerChunk)
            {
                throw new ArgumentOutOfRangeException(nameof(classes), "sv: too many lanes for one chunk table");
            }

            for (var lane = 0; lane < classes.Length; lane++)
            {
                if (classes[lane] == LaneClass.Meta || classes[lane] > LaneClass.Meta)
                {
                    throw new ArgumentException("sv: a pack lane is Retired, Stat, Intrinsic or Event", nameof(classes));
                }
            }
        }
    }
}
