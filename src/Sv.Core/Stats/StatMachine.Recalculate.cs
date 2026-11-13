using System;

namespace Sv
{
    public sealed unsafe partial class StatMachine
    {
        const int RadixBits = 10;
        const int RadixBuckets = 1 << RadixBits;
        const int RadixPasses = 4;

        ShapeTerm* scratchTerms;
        int scratchTermCapacity;
        ShapeTerm* externalTerms;
        int externalTermCapacity;
        ulong* dirtyScratch;
        int dirtyScratchCapacity;
        ulong* rankMask;
        int rankWords;
        ulong* changedPairs;
        int changedPairCount;
        int changedPairCapacity;
        int* changedChunks;
        ulong* changedRows;
        int changedChunkCount;
        int changedChunkCapacity;
        int changedRowCapacity;

        public int ChangedPairCount => changedPairCount;

        public ulong* ChangedPairs => changedPairs;

        public int ChangedChunkCount => changedChunkCount;

        public int* ChangedChunks => changedChunks;

        public ulong* ChangedRows => changedRows;

        public static int SlotOfPair(ulong pair) => (int)(pair >> 16);

        public static int LaneOfPair(ulong pair) => (int)(pair & 0xFFFFul);

        void InitRecalculate()
        {
            rankWords = Math.Max(1, (statCount + 63) >> 6);
            rankMask = (ulong*)Memory.AllocZeroed((nuint)rankWords, sizeof(ulong));
            EnsureTerms(16);
            EnsureExternal(16);
        }

        void FreeRecalculate()
        {
            Memory.Free(rankMask);
            if (scratchTerms != null)
            {
                Memory.Free(scratchTerms);
            }

            if (externalTerms != null)
            {
                Memory.Free(externalTerms);
            }

            if (dirtyScratch != null)
            {
                Memory.Free(dirtyScratch);
            }

            if (changedPairs != null)
            {
                Memory.Free(changedPairs);
            }

            if (changedChunks != null)
            {
                Memory.Free(changedChunks);
            }

            if (changedRows != null)
            {
                Memory.Free(changedRows);
            }
        }

        void EnsureTerms(int needed)
        {
            if (needed > scratchTermCapacity)
            {
                scratchTerms = Grow(scratchTerms, scratchTermCapacity, ref scratchTermCapacity, needed);
            }
        }

        void EnsureExternal(int needed)
        {
            if (needed > externalTermCapacity)
            {
                externalTerms = Grow(externalTerms, externalTermCapacity, ref externalTermCapacity, needed);
            }
        }

        public void Recalculate()
        {
            changedPairCount = 0;
            changedChunkCount = 0;
            if (dirtyCount == 0)
            {
                return;
            }

            var count = SortDirty();
            var view = world.View;
            var at = 0;
            while (at < count)
            {
                var slot = (int)(dirty[at] >> 16);
                var lowest = statCount;
                while (at < count && (int)(dirty[at] >> 16) == slot)
                {
                    var ordinal = (int)(dirty[at] & 0xFFFFul);
                    rankMask[ordinal >> 6] |= 1ul << (ordinal & 63);
                    lowest = ordinal < lowest ? ordinal : lowest;
                    at++;
                }

                var live = world.StateOf(slot) == SlotState.Live;
                for (var word = lowest >> 6; word < rankWords; word++)
                {
                    while (rankMask[word] != 0ul)
                    {
                        var ordinal = (word << 6) + Bits.Tzcnt(rankMask[word]);
                        rankMask[word] &= rankMask[word] - 1ul;
                        if (live && Resolve(view, slot, ordinal))
                        {
                            for (var d = dependentStart[ordinal]; d < dependentStart[ordinal + 1]; d++)
                            {
                                var dependent = dependents[d];
                                rankMask[dependent >> 6] |= 1ul << (dependent & 63);
                            }
                        }
                    }
                }
            }

            dirtyCount = 0;
            world.Absorb(ref writer);
        }

        public int Compute(int slot, int lane)
        {
            var ordinal = OrdinalOf(lane);
            if (ordinal < 0 || world.StateOf(slot) != SlotState.Live)
            {
                return 0;
            }

            Gather(slot, ordinal, out var coreCount, out var externalCount);
            return StatLaw.Fold(TemplateBaseOf(world.TemplateOf(Slot.Chunk(slot)), ordinal), scratchTerms, coreCount, externalTerms, externalCount);
        }

        void Gather(int slot, int ordinal, out int coreCount, out int externalCount)
        {
            coreCount = GatherDerived(ordinal, null, slot);
            externalCount = 0;
            GatherCards(ordinal, slot, ref coreCount, ref externalCount);
            for (var at = HeadPeek(slot); at != NoRecord; at = records[at].Next)
            {
                if (records[at].Stat != ordinal)
                {
                    continue;
                }

                var term = new ShapeTerm(records[at].Kind, records[at].Value);
                if (records[at].Layer == StatLayer.Core)
                {
                    EnsureTerms(coreCount + 1);
                    scratchTerms[coreCount++] = term;
                }
                else
                {
                    EnsureExternal(externalCount + 1);
                    externalTerms[externalCount++] = term;
                }
            }

            StatLaw.Canonicalize(scratchTerms, coreCount);
            if (externalCount > 0)
            {
                StatLaw.Canonicalize(externalTerms, externalCount);
            }
        }

        bool Resolve(in WorldView view, int slot, int ordinal)
        {
            var chunk = Slot.Chunk(slot);
            var row = Slot.Row(slot);
            var lane = laneOf[ordinal];
            Gather(slot, ordinal, out var coreCount, out var externalCount);
            var value = StatLaw.Fold(TemplateBaseOf(world.TemplateOf(chunk), ordinal), scratchTerms, coreCount, externalTerms, externalCount);
            if (Lanes.Ints(view, chunk, lane)[row] == value)
            {
                return false;
            }

            world.Reserve(ref writer, 1);
            ((int*)Lanes.Writable(view, ref writer, chunk, lane))[row] = value;
            changedPairs = Grow(changedPairs, changedPairCount, ref changedPairCapacity, changedPairCount + 1);
            changedPairs[changedPairCount++] = ((ulong)(uint)slot << 16) | (uint)lane;
            if (changedChunkCount == 0 || changedChunks[changedChunkCount - 1] != chunk)
            {
                changedChunks = Grow(changedChunks, changedChunkCount, ref changedChunkCapacity, changedChunkCount + 1);
                changedRows = Grow(changedRows, changedChunkCount, ref changedRowCapacity, changedChunkCount + 1);
                changedChunks[changedChunkCount] = chunk;
                changedRows[changedChunkCount] = 0ul;
                changedChunkCount++;
            }

            changedRows[changedChunkCount - 1] |= 1ul << row;
            return true;
        }

        int SortDirty()
        {
            var count = dirtyCount;
            dirtyScratch = Grow(dirtyScratch, 0, ref dirtyScratchCapacity, count);
            var buckets = stackalloc int[RadixBuckets];
            var from = dirty;
            var into = dirtyScratch;
            for (var pass = 0; pass < RadixPasses; pass++)
            {
                var shift = pass * RadixBits;
                for (var bucket = 0; bucket < RadixBuckets; bucket++)
                {
                    buckets[bucket] = 0;
                }

                for (var at = 0; at < count; at++)
                {
                    buckets[(int)((from[at] >> shift) & (RadixBuckets - 1))]++;
                }

                if (buckets[(int)((from[0] >> shift) & (RadixBuckets - 1))] == count)
                {
                    continue;
                }

                var total = 0;
                for (var bucket = 0; bucket < RadixBuckets; bucket++)
                {
                    var size = buckets[bucket];
                    buckets[bucket] = total;
                    total += size;
                }

                for (var at = 0; at < count; at++)
                {
                    into[buckets[(int)((from[at] >> shift) & (RadixBuckets - 1))]++] = from[at];
                }

                var swap = from;
                from = into;
                into = swap;
            }

            var unique = 0;
            for (var at = 0; at < count; at++)
            {
                if (unique == 0 || from[at] != dirty[unique - 1])
                {
                    dirty[unique++] = from[at];
                }
            }

            return unique;
        }
    }
}
