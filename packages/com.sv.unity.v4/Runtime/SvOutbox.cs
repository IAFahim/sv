using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Sv.Unity
{
    public interface ISvOutboxCell
    {
        int Channel { get; }

        int KindLane { get; }

        int TargetLane { get; }

        int Amount { get; set; }
    }

    public static unsafe class SvOutbox
    {
        public const int NoLane = -1;

        public static bool Send(in SvFrame frame, int slot, int amount, int channel, int kindLane, int targetLane)
        {
            var kind = kindLane < 0 ? 0 : Lanes.Read(frame.World, slot, kindLane);
            var aimed = targetLane < 0 ? 0 : Lanes.Read(frame.World, slot, targetLane);
            var target = aimed <= 0 ? slot : aimed - 1;
            kind = kind < 0 ? 0 : kind > 255 ? 255 : kind;
            return frame.Sender->Send(target, slot, kind, channel, Origin.Timeline, amount, 1);
        }
    }

    [BurstCompile]
    [UpdateInGroup(typeof(SvSendGroup))]
    public partial struct SvOutboxSystem<TCell> : ISystem
        where TCell : unmanaged, IComponentData, ISvOutboxCell
    {
        EntityQuery cells;
        EntityQuery frames;
        ComponentTypeHandle<TCell> cellHandle;
        ComponentTypeHandle<SvSlot> slotHandle;

        public int Probe;

        public long Sent;

        public long Refused;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            cells = new EntityQueryBuilder(Allocator.Temp).WithAllRW<TCell>().WithAll<SvSlot>().Build(ref state);
            frames = new EntityQueryBuilder(Allocator.Temp).WithAll<SvFrame, SvFixedStep>().Build(ref state);
            cellHandle = state.GetComponentTypeHandle<TCell>(false);
            slotHandle = state.GetComponentTypeHandle<SvSlot>(true);
            state.RequireForUpdate(frames);
            state.RequireForUpdate(cells);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            SvProbe.Witness(ref Probe);
            var frame = frames.GetSingleton<SvFrame>();
            if (!frame.IsOpen)
            {
                return;
            }

            cells.CompleteDependency();
            cellHandle.Update(ref state);
            slotHandle.Update(ref state);
            var shape = default(TCell);
            var channel = shape.Channel;
            var kindLane = shape.KindLane;
            var targetLane = shape.TargetLane;
            var chunks = cells.ToArchetypeChunkArray(Allocator.Temp);
            for (var c = 0; c < chunks.Length; c++)
            {
                var outbox = chunks[c].GetNativeArray(ref cellHandle);
                var slots = chunks[c].GetNativeArray(ref slotHandle);
                for (var row = 0; row < outbox.Length; row++)
                {
                    var cell = outbox[row];
                    var amount = cell.Amount;
                    if (amount == 0)
                    {
                        continue;
                    }

                    if (SvOutbox.Send(frame, slots[row].Value, amount, channel, kindLane, targetLane))
                    {
                        Sent++;
                    }
                    else
                    {
                        Refused++;
                    }

                    cell.Amount = 0;
                    outbox[row] = cell;
                }
            }
        }
    }
}
