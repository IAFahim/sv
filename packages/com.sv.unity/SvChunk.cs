using System;
using Unity.Burst.Intrinsics;
using Unity.Entities;

namespace Sv.Unity
{
    /// <summary>
    /// The IJobChunk-shaped seam over Unity chunk storage: re-bases the chunk column by its
    /// slot base (issue 0007's arithmetic — native storage needs no fixed scope) and runs the
    /// sv chunk verbs through slot indexing. Dense chunks take RunIntrinsicsChunk; enabled
    /// chunks take the two-word window verb anchored at the chunk's slot base — any alignment,
    /// the tail word bounded by the chunk's row count (ULong0/ULong1 consumed directly).
    /// Non-slot-contiguous chunks throw — reconcile at the drain step.
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

                var words = stackalloc ulong[2];
                words[0] = chunkEnabledMask.ULong0;
                words[1] = chunkEnabledMask.ULong1;
                var wordCount = (chunk.Count + 63) >> 6;
                if (wordCount > 1)
                {
                    Board.RunEnabledChunk(value, delta, clamp, words, wordCount - 1, startSlot, appliedView);
                }

                Board.RunEnabledChunk(value, delta, clamp, words[wordCount - 1], startSlot + (wordCount - 1) * 64, appliedView, chunk.Count - (wordCount - 1) * 64);
            }
        }
    }
}
