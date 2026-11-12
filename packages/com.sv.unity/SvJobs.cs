using System.Threading;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace Sv.Unity
{
    public static class SvProbe
    {
        public static int ManagedRuns;

        [BurstDiscard]
        public static void Mark()
        {
            ManagedRuns++;
        }
    }

    public struct SvEvent
    {
        public int Slot;
        public byte Entered;
    }

    public struct SvChannel
    {
        public int Count;
        public int Delivered;
        public byte Dense;
    }

    public unsafe struct SvInboxWriter
    {
        [NativeDisableUnsafePtrRestriction] public long* Cells;
        [NativeDisableUnsafePtrRestriction] public int* Sent;

        public bool IsCreated => Cells != null;

        public void Send(int targetSlot, int amount)
        {
            if (*Sent == 0)
            {
                *Sent = 1;
            }

            Inbox.Send(Cells, targetSlot, amount);
        }
    }

    public unsafe struct SvLane
    {
        public DynamicComponentTypeHandle Value;
        public DynamicComponentTypeHandle Delta;
        public DynamicComponentTypeHandle Ceiling;
        public DynamicComponentTypeHandle Floor;
        public DynamicComponentTypeHandle Gate;
        public int Column;
        public int CeilingConstant;
        public int FloorConstant;
        public bool HasCeiling;
        public bool HasFloor;
        public bool HasCondition;
        public bool HasGate;
        public int EnterNumerator;
        public int EnterDenominator;
        public int StayNumerator;
        public int StayDenominator;
        [NativeDisableUnsafePtrRestriction] public long* Inbox;
        [NativeDisableUnsafePtrRestriction] public int* Sent;
        [NativeDisableUnsafePtrRestriction] public ulong* State;
        [NativeDisableUnsafePtrRestriction] public ulong* Edges;
        [NativeDisableUnsafePtrRestriction] public int* EdgeCount;
    }

    [BurstCompile(FloatMode = FloatMode.Strict)]
    public unsafe struct SvScatterJob : IJob
    {
        public DynamicComponentTypeHandle S0;
        public DynamicComponentTypeHandle S1;
        public DynamicComponentTypeHandle S2;
        public DynamicComponentTypeHandle S3;
        public DynamicComponentTypeHandle S4;
        public DynamicComponentTypeHandle S5;
        public DynamicComponentTypeHandle S6;
        public DynamicComponentTypeHandle S7;
        [ReadOnly] public EntityStorageInfoLookup Storage;
        [NativeDisableUnsafePtrRestriction] public StatWrite* Writes;
        [NativeDisableUnsafePtrRestriction] public int* Count;
        [NativeDisableUnsafePtrRestriction] public Entity* SlotEntity;
        [NativeDisableUnsafePtrRestriction] public sbyte* HandleOfStat;

        public void Execute()
        {
            SvProbe.Mark();
            var count = *Count;
            for (var i = 0; i < count; i++)
            {
                var write = Writes[i];
                var entity = SlotEntity[write.Slot];
                if (entity == Entity.Null)
                {
                    continue;
                }

                var info = Storage[entity];
                switch (HandleOfStat[write.Stat])
                {
                    case 0: Store(ref S0, info, write.Bits); break;
                    case 1: Store(ref S1, info, write.Bits); break;
                    case 2: Store(ref S2, info, write.Bits); break;
                    case 3: Store(ref S3, info, write.Bits); break;
                    case 4: Store(ref S4, info, write.Bits); break;
                    case 5: Store(ref S5, info, write.Bits); break;
                    case 6: Store(ref S6, info, write.Bits); break;
                    case 7: Store(ref S7, info, write.Bits); break;
                }
            }
        }

        static void Store(ref DynamicComponentTypeHandle handle, EntityStorageInfo info, uint bits)
        {
            if (!info.Chunk.Has(ref handle))
            {
                return;
            }

            var column = (uint*)NativeArrayUnsafeUtility.GetUnsafePtr(info.Chunk.GetDynamicComponentDataArrayReinterpret<uint>(ref handle, 4));
            column[info.IndexInChunk] = bits;
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict)]
    public unsafe struct SvFoldJob : IJobChunk
    {
        public SvLane L0;
        public SvLane L1;
        public SvLane L2;
        public SvLane L3;
        public int Lanes;
        public bool Filter;
        public uint LastVersion;
        [ReadOnly] public ComponentTypeHandle<SvSlot> Slot;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            SvProbe.Mark();
            var visit0 = Lanes > 0 && Due(ref L0, chunk);
            var visit1 = Lanes > 1 && Due(ref L1, chunk);
            var visit2 = Lanes > 2 && Due(ref L2, chunk);
            var visit3 = Lanes > 3 && Due(ref L3, chunk);
            if (!visit0 && !visit1 && !visit2 && !visit3)
            {
                return;
            }

            var slots = (int*)chunk.GetRequiredComponentDataPtrRO(ref Slot);
            if (visit0) Run(ref L0, chunk, slots);
            if (visit1) Run(ref L1, chunk, slots);
            if (visit2) Run(ref L2, chunk, slots);
            if (visit3) Run(ref L3, chunk, slots);
        }

        bool Due(ref SvLane lane, in ArchetypeChunk chunk)
        {
            if (!chunk.Has(ref lane.Value) || !chunk.Has(ref lane.Delta))
            {
                return false;
            }

            if (!Filter || (lane.Sent != null && *lane.Sent != 0))
            {
                return true;
            }

            return chunk.DidChange(ref lane.Delta, LastVersion)
                || chunk.DidChange(ref lane.Value, LastVersion)
                || (lane.HasCeiling && chunk.DidChange(ref lane.Ceiling, LastVersion))
                || (lane.HasFloor && chunk.DidChange(ref lane.Floor, LastVersion));
        }

        static void Run(ref SvLane lane, in ArchetypeChunk chunk, int* slots)
        {
            var rows = chunk.Count;
            var word = new FoldWord
            {
                Value = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(chunk.GetDynamicComponentDataArrayReinterpret<int>(ref lane.Value, 4)),
                Delta = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(chunk.GetDynamicComponentDataArrayReinterpret<int>(ref lane.Delta, 4)),
                Ceiling = lane.HasCeiling && chunk.Has(ref lane.Ceiling) ? (float*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(chunk.GetDynamicComponentDataArrayReinterpret<float>(ref lane.Ceiling, 4)) : null,
                Floor = lane.HasFloor && chunk.Has(ref lane.Floor) ? (float*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(chunk.GetDynamicComponentDataArrayReinterpret<float>(ref lane.Floor, 4)) : null,
                CeilingConstant = lane.CeilingConstant,
                FloorConstant = lane.FloorConstant,
            };
            if (lane.Inbox != null)
            {
                for (var row = 0; row < rows; row++)
                {
                    word.Delta[row] = Inbox.Take(lane.Inbox, slots[row], word.Delta[row]);
                }
            }

            Fold.Word(in word, rows < 64 ? rows : 64);
            if (rows > 64)
            {
                var high = word;
                high.Value += 64;
                high.Delta += 64;
                high.Ceiling = word.Ceiling == null ? null : word.Ceiling + 64;
                high.Floor = word.Floor == null ? null : word.Floor + 64;
                Fold.Word(in high, rows - 64);
            }

            if (lane.HasCondition)
            {
                Conditions(ref lane, chunk, slots, in word, rows);
            }
        }

        static void Conditions(ref SvLane lane, in ArchetypeChunk chunk, int* slots, in FoldWord word, int rows)
        {
            var edges = 0;
            var gate = lane.HasGate && chunk.Has(ref lane.Gate);
            for (var row = 0; row < rows; row++)
            {
                var whole = word.Ceiling == null ? word.CeilingConstant : Clamp.BoundOf(word.Ceiling[row]);
                var value = word.Value[row];
                var enter = Condition.Below(value, whole, lane.EnterNumerator, lane.EnterDenominator) ? 1ul : 0ul;
                var stay = Condition.Below(value, whole, lane.StayNumerator, lane.StayDenominator) ? 1ul : 0ul;
                var slot = slots[row];
                var bit = 1ul << (slot & 63);
                var state = (lane.State[slot >> 6] & bit) != 0 ? 1ul : 0ul;
                var next = Condition.Next(state, enter, stay);
                if (Condition.Edges(state, next) == 0)
                {
                    continue;
                }

                Flip(lane.State + (slot >> 6), bit, next != 0);
                Flip(lane.Edges + (slot >> 6), bit, true);
                if (gate)
                {
                    chunk.SetComponentEnabled(ref lane.Gate, row, next != 0);
                }

                edges++;
            }

            if (edges != 0)
            {
                Interlocked.Add(ref *lane.EdgeCount, edges);
            }
        }

        static void Flip(ulong* cell, ulong bit, bool on)
        {
            while (true)
            {
                var old = *cell;
                var next = on ? old | bit : old & ~bit;
                if (Interlocked.CompareExchange(ref *(long*)cell, (long)next, (long)old) == (long)old)
                {
                    return;
                }
            }
        }
    }

    [BurstCompile]
    public unsafe struct SvLaneResetJob : IJob
    {
        [NativeDisableUnsafePtrRestriction] public int* Sent0;
        [NativeDisableUnsafePtrRestriction] public int* Sent1;
        [NativeDisableUnsafePtrRestriction] public int* Sent2;
        [NativeDisableUnsafePtrRestriction] public int* Sent3;

        public void Execute()
        {
            SvProbe.Mark();
            if (Sent0 != null) *Sent0 = 0;
            if (Sent1 != null) *Sent1 = 0;
            if (Sent2 != null) *Sent2 = 0;
            if (Sent3 != null) *Sent3 = 0;
        }
    }

    [BurstCompile]
    public unsafe struct SvDeriveJob : IJob
    {
        [NativeDisableUnsafePtrRestriction] public ulong* Edges;
        [NativeDisableUnsafePtrRestriction] public ulong* State;
        [NativeDisableUnsafePtrRestriction] public int* EdgeCount;
        [NativeDisableUnsafePtrRestriction] public SvEvent* Events;
        [NativeDisableUnsafePtrRestriction] public SvChannel* Channel;
        public int Capacity;
        public int Words;

        public void Execute()
        {
            SvProbe.Mark();
            var count = *EdgeCount;
            Channel->Count = count;
            Channel->Delivered = 0;
            Channel->Dense = (byte)(count > Capacity ? 1 : 0);
            if (count == 0 || count > Capacity)
            {
                return;
            }

            var written = 0;
            for (var w = 0; w < Words; w++)
            {
                var bits = Edges[w];
                while (bits != 0)
                {
                    var slot = (w << 6) + Bits.Tzcnt(bits);
                    bits &= bits - 1;
                    Events[written++] = new SvEvent { Slot = slot, Entered = (byte)((State[slot >> 6] >> (slot & 63)) & 1ul) };
                }
            }

            Channel->Delivered = written;
        }
    }

    [BurstCompile]
    public unsafe struct SvDigestJob : IJobChunk
    {
        public SvLane L0;
        public SvLane L1;
        public SvLane L2;
        public SvLane L3;
        public int Lanes;
        [ReadOnly] public ComponentTypeHandle<SvSlot> Slot;
        [NativeDisableUnsafePtrRestriction] public ulong* Partial;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            SvProbe.Mark();
            var slots = (int*)chunk.GetRequiredComponentDataPtrRO(ref Slot);
            var sum = 0ul;
            if (Lanes > 0) sum += Lane(ref L0, chunk, slots);
            if (Lanes > 1) sum += Lane(ref L1, chunk, slots);
            if (Lanes > 2) sum += Lane(ref L2, chunk, slots);
            if (Lanes > 3) sum += Lane(ref L3, chunk, slots);
            Partial[JobsUtility.ThreadIndex * 8] += sum;
        }

        static ulong Lane(ref SvLane lane, in ArchetypeChunk chunk, int* slots)
        {
            if (!chunk.Has(ref lane.Value))
            {
                return 0ul;
            }

            var values = (uint*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(chunk.GetDynamicComponentDataArrayReinterpret<uint>(ref lane.Value, 4));
            var sum = 0ul;
            var rows = chunk.Count;
            unchecked
            {
                for (var row = 0; row < rows; row++)
                {
                    sum += CellDigest.Key(lane.Column, slots[row], values[row]);
                }
            }

            return sum;
        }
    }

    [BurstCompile]
    public unsafe struct SvSlotWriteJob : IJob
    {
        public ComponentLookup<SvSlot> Slots;
        [ReadOnly] public NativeArray<Entity> Entities;
        [NativeDisableUnsafePtrRestriction] public int* Assigned;
        [NativeDisableUnsafePtrRestriction] public Entity* SlotEntity;

        public void Execute()
        {
            SvProbe.Mark();
            for (var i = 0; i < Entities.Length; i++)
            {
                var slot = Assigned[i];
                Slots[Entities[i]] = new SvSlot { Slot = slot };
                SlotEntity[slot] = Entities[i];
            }
        }
    }
}
