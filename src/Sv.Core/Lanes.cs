using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sv
{
    public unsafe struct WorldView
    {
        public byte** Segments;
        public uint* LaneOf;
        public uint* TemplateLanes;
        public WorldLayout Layout;
        public int LanesPerChunk;
        public int TemplateCount;
        public uint Epoch;
        public StoragePolicy Policy;
    }

    [StructLayout(LayoutKind.Sequential, Size = 8)]
    public readonly struct JournalEntry
    {
        public readonly uint TableIndex;
        public readonly uint Saved;

        public JournalEntry(uint tableIndex, uint saved)
        {
            TableIndex = tableIndex;
            Saved = saved;
        }
    }

    public unsafe struct LaneWriter
    {
        public uint* Free;
        public int FreeCount;
        public int FreeCapacity;
        public JournalEntry* Journal;
        public int JournalCount;
        public int JournalCapacity;
    }

    public static unsafe class Lanes
    {
        public const int Rows = 64;
        public const int Bytes = 256;
        public const int Words = 32;
        public const int ByteShift = 8;
        public const int SegmentShift = 13;
        public const int SegmentLanes = 1 << SegmentShift;
        public const uint SegmentMask = SegmentLanes - 1u;
        public const int SegmentBytes = SegmentLanes * Bytes;
        public const int HeaderLanes = SegmentLanes * sizeof(uint) / Bytes;
        public const int UsableLanes = SegmentLanes - HeaderLanes;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte* At(in WorldView world, uint lane) =>
            world.Segments[lane >> SegmentShift] + ((long)(lane & SegmentMask) << ByteShift);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint* EpochOf(in WorldView world, uint lane) =>
            (uint*)world.Segments[lane >> SegmentShift] + (lane & SegmentMask);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint IndexOf(in WorldView world, int chunk, int lane) => (uint)(chunk * world.LanesPerChunk + lane);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint LaneAt(in WorldView world, int chunk, int lane) => world.LaneOf[IndexOf(world, chunk, lane)];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte* BytesOf(in WorldView world, int chunk, int lane) => At(world, LaneAt(world, chunk, lane));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int* Ints(in WorldView world, int chunk, int lane) => (int*)BytesOf(world, chunk, lane);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float* Floats(in WorldView world, int chunk, int lane) => (float*)BytesOf(world, chunk, lane);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong* WordsOf(in WorldView world, int chunk, int lane) => (ulong*)BytesOf(world, chunk, lane);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Read(in WorldView world, int slot, int lane) => Ints(world, slot >> Slot.RowBits, lane)[slot & Slot.RowMask];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ReadFloat(in WorldView world, int slot, int lane) => Floats(world, slot >> Slot.RowBits, lane)[slot & Slot.RowMask];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Level(in WorldView world, int slot, int raised) =>
            ((WordsOf(world, slot >> Slot.RowBits, EventId.Lane(raised))[EventId.Word(raised)] >> (slot & Slot.RowMask)) & 1ul) != 0ul;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint* TemplateRow(in WorldView world, int template) => world.TemplateLanes + (long)template * world.Layout.ValueLanes;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Owned(in WorldView world, int chunk, int lane) => *EpochOf(world, LaneAt(world, chunk, lane)) == world.Epoch;

        public static byte* Writable(in WorldView world, ref LaneWriter writer, int chunk, int lane)
        {
            var index = IndexOf(world, chunk, lane);
            var current = world.LaneOf[index];
            if (*EpochOf(world, current) == world.Epoch)
            {
                return At(world, current);
            }

            writer.FreeCount--;
            var fresh = writer.Free[writer.FreeCount];
            LaneMath.Copy(At(world, current), At(world, fresh));
            var inPlace = world.Policy == StoragePolicy.Dense;
            var kept = inPlace ? current : fresh;
            var saved = inPlace ? fresh : current;
            *EpochOf(world, fresh) = world.Epoch;
            *EpochOf(world, kept) = world.Epoch;
            writer.Journal[writer.JournalCount] = new JournalEntry(index, saved);
            writer.JournalCount++;
            world.LaneOf[index] = kept;
            return At(world, kept);
        }

        public static byte* Overwritable(in WorldView world, ref LaneWriter writer, int chunk, int lane)
        {
            if (world.Policy == StoragePolicy.Dense)
            {
                return Writable(world, ref writer, chunk, lane);
            }

            var index = IndexOf(world, chunk, lane);
            var current = world.LaneOf[index];
            if (*EpochOf(world, current) == world.Epoch)
            {
                return At(world, current);
            }

            writer.FreeCount--;
            var fresh = writer.Free[writer.FreeCount];
            *EpochOf(world, fresh) = world.Epoch;
            writer.Journal[writer.JournalCount] = new JournalEntry(index, current);
            writer.JournalCount++;
            world.LaneOf[index] = fresh;
            return At(world, fresh);
        }

        public static void Share(in WorldView world, ref LaneWriter writer, int chunk, int lane, uint templateLane)
        {
            var index = IndexOf(world, chunk, lane);
            var current = world.LaneOf[index];
            if (current == templateLane)
            {
                return;
            }

            if (world.Policy == StoragePolicy.Dense)
            {
                LaneMath.Copy(At(world, templateLane), Writable(world, ref writer, chunk, lane));
                return;
            }

            writer.Journal[writer.JournalCount] = new JournalEntry(index, current);
            writer.JournalCount++;
            world.LaneOf[index] = templateLane;
        }

        public const uint NoLane = uint.MaxValue;

        public static uint Restore(in WorldView world, JournalEntry entry)
        {
            var current = world.LaneOf[entry.TableIndex];
            if (world.Policy == StoragePolicy.Dense)
            {
                LaneMath.Copy(At(world, entry.Saved), At(world, current));
                *EpochOf(world, current) = 0u;
                return entry.Saved;
            }

            world.LaneOf[entry.TableIndex] = entry.Saved;
            return *EpochOf(world, current) != 0u ? current : NoLane;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Expired(in WorldView world, JournalEntry entry) => *EpochOf(world, entry.Saved) != 0u ? entry.Saved : NoLane;
    }
}
