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
        public long Frame;
        public SourceChunk* Sources;
    }

    [StructLayout(LayoutKind.Sequential, Size = 24)]
    public readonly unsafe struct SourceChunk
    {
        public readonly ulong Present;
        public readonly ulong OnTemplate;
        public readonly int* Values;

        public SourceChunk(ulong present, ulong onTemplate, int* values)
        {
            Present = present;
            OnTemplate = onTemplate;
            Values = values;
        }
    }

    public static unsafe class SourceChunks
    {
        public const int TrackedLanes = 64;

        public static void TemplateValues(in WorldView world, int* values)
        {
            var lanes = world.Layout.ValueLanes;
            for (var template = 0; template < world.TemplateCount; template++)
            {
                var row = Lanes.TemplateRow(world, template);
                for (var lane = 0; lane < lanes; lane++)
                {
                    values[(long)template * lanes + lane] = ((int*)Lanes.At(world, row[lane]))[0];
                }
            }
        }

        public static ulong UniformLanes(in WorldView world)
        {
            var lanes = world.Layout.ValueLanes < TrackedLanes ? world.Layout.ValueLanes : TrackedLanes;
            var uniform = 0ul;
            for (var lane = 0; lane < lanes; lane++)
            {
                uniform |= world.Layout.Classes[lane] != LaneClass.Event ? 1ul << lane : 0ul;
            }

            return uniform;
        }

        public static SourceChunk Of(in WorldView world, int* values, ulong uniform, int chunk)
        {
            var meta = Lanes.WordsOf(world, chunk, world.Layout.MetaLane);
            var present = meta[ChunkMeta.Live] | meta[ChunkMeta.Dying];
            var template = ChunkMeta.TemplateOf(meta[ChunkMeta.Template]);
            if ((uint)template >= (uint)world.TemplateCount)
            {
                return new SourceChunk(present, 0ul, null);
            }

            var lanes = world.Layout.ValueLanes < TrackedLanes ? world.Layout.ValueLanes : TrackedLanes;
            var current = world.LaneOf + Lanes.IndexOf(world, chunk, 0);
            var defaults = Lanes.TemplateRow(world, template);
            var onTemplate = 0ul;
            for (var lane = 0; lane < lanes; lane++)
            {
                onTemplate |= current[lane] == defaults[lane] ? 1ul << lane : 0ul;
            }

            return new SourceChunk(present, onTemplate & uniform, values + (long)template * world.Layout.ValueLanes);
        }

        public static void Build(in WorldView world, int* values, SourceChunk* table, int chunkFrom, int chunkTo)
        {
            var uniform = UniformLanes(world);
            for (var chunk = chunkFrom; chunk < chunkTo; chunk++)
            {
                table[chunk] = Of(world, values, uniform, chunk);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Present(in WorldView world, int capacity, long slot)
        {
            if ((ulong)slot >= (ulong)capacity)
            {
                return false;
            }

            var at = (int)slot;
            if (world.Sources != null)
            {
                return ((world.Sources[at >> Slot.RowBits].Present >> (at & Slot.RowMask)) & 1ul) != 0ul;
            }

            var meta = Lanes.WordsOf(world, at >> Slot.RowBits, world.Layout.MetaLane);
            return (((meta[ChunkMeta.Live] | meta[ChunkMeta.Dying]) >> (at & Slot.RowMask)) & 1ul) != 0ul;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Read(in WorldView world, int capacity, long slot, int lane, int fallback)
        {
            if ((ulong)slot >= (ulong)capacity)
            {
                return fallback;
            }

            var at = (int)slot;
            if (world.Sources != null)
            {
                var source = world.Sources + (at >> Slot.RowBits);
                if (((source->Present >> (at & Slot.RowMask)) & 1ul) == 0ul)
                {
                    return fallback;
                }

                if ((uint)lane < TrackedLanes && ((source->OnTemplate >> lane) & 1ul) != 0ul)
                {
                    return source->Values[lane];
                }

                return Lanes.Read(world, at, lane);
            }

            return Present(world, capacity, slot) ? Lanes.Read(world, at, lane) : fallback;
        }
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
