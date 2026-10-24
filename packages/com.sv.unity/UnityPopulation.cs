using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Sv.Unity
{
    /// <summary>
    /// Entity↔slot population over a Unity world: bind/unbind at structural-change boundaries
    /// (main thread here; command-buffer variants mirror the same arithmetic), plus the
    /// drain-step reconcile (issue 0013 semantics): per-chunk ascending reassign with each
    /// chunk block packed contiguously — enabled windows work at any slot offset — the sv
    /// board renumbered once; the returned permutation applies to host-owned slot columns
    /// via Sv.Columns.Permute. Every chunk that carries SvSlot rows gets its SvRange chunk
    /// component written (the rows must be the chunk's row prefix), so SvChunk resolves each
    /// chunk's slot window in O(1) afterwards.
    /// </summary>
    public sealed unsafe class UnityPopulation : IDisposable
    {
        public Board Board { get; }

        public UnityPopulation(Schema schema, int capacity)
        {
            Board = Board.Create(schema, capacity);
        }

        public int Bind(EntityManager manager, Entity entity)
        {
            var slot = Board.Acquire();
            manager.AddComponentData(entity, new SvSlot { Slot = slot });
            return slot;
        }

        public void Unbind(EntityManager manager, Entity entity)
        {
            var slot = manager.GetComponentData<SvSlot>(entity).Slot;
            manager.RemoveComponent<SvSlot>(entity);
            Board.Release(slot);
        }

        public bool TrySlot(ComponentLookup<SvSlot> slots, Entity entity, out int slot)
        {
            if (!slots.HasComponent(entity))
            {
                slot = -1;
                return false;
            }

            slot = slots[entity].Slot;
            return true;
        }

        public NativeArray<int> Reconcile(EntityManager manager)
        {
            manager.AddChunkComponentData(manager.CreateEntityQuery(typeof(SvSlot)), new SvRange());
            var rangeHandle = manager.GetComponentTypeHandle<SvRange>(false);
            var map = new NativeArray<int>(Board.Capacity, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var mapPtr = (int*)map.GetUnsafePtr();
            for (var i = 0; i < Board.Capacity; i++)
            {
                mapPtr[i] = -1;
            }

            var handle = manager.GetComponentTypeHandle<SvSlot>(false);
            var entityType = manager.GetEntityTypeHandle();
            var chunks = manager.GetAllChunks();
            var next = 0;
            for (var c = 0; c < chunks.Length; c++)
            {
                var chunk = chunks[c];
                if (!chunk.Has<SvSlot>())
                {
                    continue;
                }

                var slotHandle = handle;
                var slots = chunk.GetComponentDataPtrRW(ref slotHandle);
                var chunkEntities = chunk.GetNativeArray(entityType);
                if (next + chunk.Count > Board.Capacity)
                {
                    map.Dispose();
                    throw new InvalidOperationException("sv.unity: reconcile exceeds board capacity");
                }

                var chunkStart = -1;
                var bound = 0;
                for (var row = 0; row < chunk.Count; row++)
                {
                    if (!manager.HasComponent<SvSlot>(chunkEntities[row]))
                    {
                        continue;
                    }

                    if (row != bound)
                    {
                        map.Dispose();
                        throw new InvalidOperationException("sv.unity: SvSlot rows must be the chunk's row prefix; rebind before reconcile");
                    }

                    if (chunkStart < 0)
                    {
                        chunkStart = next;
                    }

                    var oldSlot = slots[row].Slot;
                    slots[row] = new SvSlot { Slot = next };
                    mapPtr[oldSlot] = next;
                    next++;
                    bound++;
                }

                chunk.SetChunkComponentData(ref rangeHandle, new SvRange { Start = chunkStart < 0 ? 0 : chunkStart, Count = bound });
            }

            Board.RenumberSlots(new ReadOnlySpan<int>(mapPtr, Board.Capacity), Board.LiveCount);
            return map;
        }

        public void Dispose()
        {
            Board.Dispose();
        }
    }
}
