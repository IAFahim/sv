using System;
using Unity.Burst.Intrinsics;
using Unity.Entities;

namespace Sv.Unity
{
    /// <summary>
    /// The IJobChunk-shaped seam over Unity chunk storage: re-bases the chunk column by its
    /// slot base (issue 0007's arithmetic — native storage needs no fixed scope) and runs the
    /// sv chunk verbs through slot indexing. Dense chunks take RunIntrinsicsChunk; enabled
    /// chunks take the issue-0011 two-word span verb when the chunk's slot base is 64-aligned
    /// (ULong0/ULong1 consumed directly), otherwise ChunkEntityEnumerator batches drive
    /// per-range intrinsics. Non-slot-contiguous chunks throw — reconcile at the drain step.
    /// </summary>
    public unsafe struct SvChunk
    {
        public Board Board;

        public ComponentTypeHandle<SvSlot> SlotHandle;

        public SvChunk(Board board, ComponentTypeHandle<SvSlot> slotHandle)
        {
            Board = board;
            SlotHandle = slotHandle;
        }

        public void RunIntrinsics<TColumn>(
            ref ComponentTypeHandle<TColumn> column,
            in ArchetypeChunk chunk,
            bool useEnabledMask,
            in v128 chunkEnabledMask,
            int delta,
            in Clamp clamp,
            Span<int> applied) where TColumn : unmanaged, IComponentData
        {
            if (sizeof(TColumn) != sizeof(int))
            {
                throw new ArgumentException("sv.unity: column must be int-sized");
            }

            var slotHandle = SlotHandle;
            var slots = chunk.GetComponentDataPtrRW(ref slotHandle);
            var startSlot = slots[0].Slot;
            for (var row = 1; row < chunk.Count; row++)
            {
                if (slots[row].Slot != startSlot + row)
                {
                    throw new ArgumentException("sv.unity: chunk is not slot-contiguous; reconcile at the drain step first");
                }
            }

            var rows = chunk.GetComponentDataPtrRW(ref column);
            var value = (int*)rows - startSlot;
            fixed (int* appliedPin = applied)
            {
                var appliedView = applied.IsEmpty ? null : appliedPin - startSlot;
                if (!useEnabledMask)
                {
                    Board.RunIntrinsicsChunk(value, delta, clamp, startSlot, chunk.Count, appliedView);
                    return;
                }

                if ((startSlot & 63) == 0)
                {
                    var words = stackalloc ulong[2];
                    words[0] = chunkEnabledMask.ULong0;
                    words[1] = chunkEnabledMask.ULong1;
                    Board.RunEnabledChunk(value, delta, clamp, words, 2, startSlot, appliedView);
                    return;
                }

                applied.Slice(0, chunk.Count).Fill(0);
                var enumerator = new ChunkEntityEnumerator(true, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndexBatch(out var begin, out var count))
                {
                    Board.RunIntrinsicsChunk(value, delta, clamp, startSlot + begin, count, appliedView);
                }
            }
        }
    }
}
