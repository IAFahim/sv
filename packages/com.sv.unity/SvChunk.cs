using System;
using Unity.Burst.Intrinsics;
using Unity.Entities;

namespace Sv.Unity
{
    /// <summary>
    /// The IJobChunk-shaped seam over Unity chunk storage: the chunk's SvRange (written at
    /// reconcile) resolves its slot window in O(1) — the column is re-based by the range's slot
    /// base (issue 0007's arithmetic — native storage needs no fixed scope) and the sv chunk verbs
    /// run through slot indexing. Dense chunks take RunIntrinsicsChunk; enabled chunks take the
    /// two-word window verb anchored at the range's slot base — any alignment, the tail word
    /// bounded by the range's row count (ULong0/ULong1 consumed directly). A missing SvRange
    /// (never reconciled) or a stale one (structure changed since) throws — reconcile at the
    /// structural boundary first.
    /// </summary>
    public unsafe struct SvChunk
    {
        public Board Board;

        public ComponentTypeHandle<SvSlot> SlotHandle;

        public ComponentTypeHandle<SvRange> RangeHandle;

        public SvChunk(Board board, ComponentTypeHandle<SvSlot> slotHandle, ComponentTypeHandle<SvRange> rangeHandle)
        {
            Board = board;
            SlotHandle = slotHandle;
            RangeHandle = rangeHandle;
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

            var rangeHandle = RangeHandle;
            if (!chunk.HasChunkComponent(rangeHandle))
            {
                throw new ArgumentException("sv.unity: chunk has no SvRange; reconcile at the structural boundary first");
            }

            var range = chunk.GetChunkComponentData(ref rangeHandle);
            if (range.Count == 0)
            {
                return;
            }

            var slotHandle = SlotHandle;
            var slots = chunk.GetComponentDataPtrRW(ref slotHandle);
            if (range.Count > chunk.Count || slots[0].Slot != range.Start || slots[range.Count - 1].Slot != range.Start + range.Count - 1)
            {
                throw new ArgumentException("sv.unity: stale SvRange; reconcile at the structural boundary first");
            }

            var startSlot = range.Start;
            var rows = chunk.GetComponentDataPtrRW(ref column);
            var value = (int*)rows - startSlot;
            fixed (int* appliedPin = applied)
            {
                var appliedView = applied.IsEmpty ? null : appliedPin - startSlot;
                if (!useEnabledMask)
                {
                    Board.RunIntrinsicsChunk(value, delta, clamp, startSlot, range.Count, appliedView);
                    return;
                }

                var words = stackalloc ulong[2];
                words[0] = chunkEnabledMask.ULong0;
                words[1] = chunkEnabledMask.ULong1;
                var wordCount = (range.Count + 63) >> 6;
                if (wordCount > 1)
                {
                    Board.RunEnabledChunk(value, delta, clamp, words, wordCount - 1, startSlot, appliedView);
                }

                Board.RunEnabledChunk(value, delta, clamp, words[wordCount - 1], startSlot + (wordCount - 1) * 64, appliedView, range.Count - (wordCount - 1) * 64);
            }
        }
    }
}
