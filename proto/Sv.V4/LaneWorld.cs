using System;

namespace Sv.V4
{
    public readonly unsafe struct LaneView
    {
        public readonly int* Cells;
        public readonly int Chunks;
        public readonly int SlotLimit;

        public LaneView(int* cells, int chunks, int slotLimit)
        {
            Cells = cells;
            Chunks = chunks;
            SlotLimit = slotLimit;
        }

        public int* ChunkLanes(int chunk) => Cells + (long)chunk * Lane.Count * LaneWorld.Rows;
    }

    public sealed unsafe class LaneWorld : IDisposable
    {
        public const int Rows = 64;
        public readonly int Entities;
        public readonly int Chunks;
        public readonly int SlotLimit;
        public readonly int* Cells;

        public LaneWorld(int entities)
        {
            Entities = entities;
            Chunks = (entities + Rows - 1) / Rows;
            SlotLimit = Chunks * Rows;
            Cells = (int*)Memory.AllocZeroed((nuint)((long)Chunks * Lane.Count * Rows), sizeof(int));
        }

        public LaneView View => new LaneView(Cells, Chunks, SlotLimit);

        public int* LaneOf(int chunk, int lane) => Cells + ((long)chunk * Lane.Count + lane) * Rows;

        public int* CellOf(int slot, int lane) => LaneOf(slot >> 6, lane) + (slot & 63);

        public int Read(int slot, int lane) => *CellOf(slot, lane);

        public void Write(int slot, int lane, int value) => *CellOf(slot, lane) = value;

        public void CopyFrom(LaneWorld other) =>
            Buffer.MemoryCopy(other.Cells, Cells, (long)Chunks * Lane.Count * Rows * sizeof(int), (long)other.Chunks * Lane.Count * Rows * sizeof(int));

        public ulong Digest()
        {
            var hash = 0x9E3779B97F4A7C15ul;
            var cells = (long)Chunks * Lane.Count * Rows;
            for (long at = 0; at < cells; at++)
            {
                hash = (hash ^ (uint)Cells[at]) * 0x100000001B3ul;
                hash ^= hash >> 29;
            }

            return hash;
        }

        public void Dispose() => Memory.Free(Cells);
    }

    public static class Templates
    {
        public const int Plain = 0;
        public const int Shielded = 1;
        public const int FireHealsCard = 2;
        public const int FireImmune = 3;
        public const int BeastWard = 4;
        public const int PoisonedRegen = 5;
        public const int Executioner = 6;
        public const int Fortress = 7;
        public const int Count = 8;
        public const int BeastKind = 3;

        public static int OfChunk(int chunk) => chunk % Count;

        public static byte KindOf(int template) => (byte)(template % TargetContext.Kinds);

        public static void Spawn(LaneWorld world, int healthDivisor)
        {
            for (var slot = 0; slot < world.SlotLimit; slot++)
            {
                var template = OfChunk(slot >> 6);
                var maxHealth = (200_000 + slot % 997 * 100) / healthDivisor;
                world.Write(slot, Lane.MaxHealth, maxHealth);
                world.Write(slot, Lane.Health, (int)((long)maxHealth * (1 + slot % 10) / 10));
                world.Write(slot, Lane.Shield, template == Templates.Shielded ? 2_000 + slot % 5_000 : template == Templates.Fortress ? 50_000 : 0);
                world.Write(slot, Lane.ImmuneMask, template == Templates.FireImmune ? 1 << Channel.Fire : 0);
                world.Write(slot, Lane.KindMask, template == Templates.BeastWard ? 1 << BeastKind : 0);
                world.Write(slot, Lane.Execute, template == Templates.Executioner ? 1 : 0);
                world.Write(slot, Lane.Converter, template == Templates.FireHealsCard || template == Templates.Fortress ? 1 : 0);
                world.Write(slot, Lane.PoisonTicks, template == Templates.PoisonedRegen || template == Templates.Fortress ? 40 + slot % 200 : 0);
                world.Write(slot, Lane.RegenCard, template == Templates.PoisonedRegen ? 1 : 0);
                world.Write(slot, Lane.RegenRate, template == Templates.PoisonedRegen ? 3 : 0);
                world.Write(slot, Lane.Events, 0);
            }
        }
    }
}
