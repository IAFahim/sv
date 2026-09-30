using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Sv.Unity
{
    /// <summary>
    /// Entity↔slot population over a Unity world: bind/unbind at structural-change boundaries
    /// (main thread here; command-buffer variants mirror the same arithmetic), plus the
    /// drain-step reconcile (issue 0013 semantics): per-chunk ascending reassign, each chunk
    /// block 64-aligned so enabled windows address chunk storage directly, sv board renumbered
    /// once; the returned permutation applies to host-owned slot columns via Sv.Columns.Permute.
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
            manager.AddComponent(entity, new SvSlot { Slot = slot });
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
            var map = new NativeArray<int>(Board.Capacity, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var mapPtr = (int*)map.GetUnsafePtr();
            for (var i = 0; i < Board.Capacity; i++)
            {
                mapPtr[i] = -1;
            }

            var handle = manager.GetComponentTypeHandle<SvSlot>();
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
                var chunkEntities = chunk.GetNativeArray(ref entityType);
                next = (next + 63) & ~63;
                if (next + chunk.Count > Board.Capacity)
                {
                    map.Dispose();
                    throw new InvalidOperationException("sv.unity: reconcile exceeds board capacity (64-alignment padding)");
                }

                for (var row = 0; row < chunk.Count; row++)
                {
                    if (!manager.HasComponent<SvSlot>(chunkEntities[row]))
                    {
                        continue;
                    }

                    var oldSlot = slots[row].Slot;
                    slots[row] = new SvSlot { Slot = next };
                    mapPtr[oldSlot] = next;
                    next++;
                }
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
