namespace Sv.Host
{
    public static unsafe class ChunkSet
    {
        public static int Words(int chunks) => (chunks + 63) >> 6;

        public static void Mark(ulong* bits, int chunk) => bits[chunk >> 6] |= 1ul << (chunk & 63);

        public static void MarkAll(ulong* bits, int* chunks, int count)
        {
            for (var at = 0; at < count; at++)
            {
                bits[chunks[at] >> 6] |= 1ul << (chunks[at] & 63);
            }
        }

        public static int Drain(ulong* bits, int words, int* ascending)
        {
            var count = 0;
            for (var word = 0; word < words; word++)
            {
                var set = bits[word];
                bits[word] = 0ul;
                while (set != 0ul)
                {
                    ascending[count] = (word << 6) + Bits.Tzcnt(set);
                    set &= set - 1ul;
                    count++;
                }
            }

            return count;
        }
    }

    public static unsafe class Collect
    {
        public static void StatPairRanges(ulong* pairs, int pairCount, int* pairFrom, int* pairTo)
        {
            var at = 0;
            while (at < pairCount)
            {
                var chunk = Slot.Chunk(StatMachine.SlotOfPair(pairs[at]));
                var end = at + 1;
                while (end < pairCount && Slot.Chunk(StatMachine.SlotOfPair(pairs[end])) == chunk)
                {
                    end++;
                }

                pairFrom[chunk] = at;
                pairTo[chunk] = end;
                at = end;
            }
        }

        public static ulong RowsOfLane(ulong* pairs, int from, int to, int lane)
        {
            var rows = 0ul;
            for (var at = from; at < to; at++)
            {
                var pair = pairs[at];
                rows |= StatMachine.LaneOfPair(pair) == lane ? Slot.Bit(StatMachine.SlotOfPair(pair)) : 0ul;
            }

            return rows;
        }

        public static int Touched(
            ulong* activeBits,
            int words,
            int* canonicalActive,
            int canonicalCount,
            int* statChunks,
            int statCount,
            ulong* pendingBits,
            int* ascending)
        {
            ChunkSet.MarkAll(activeBits, canonicalActive, canonicalCount);
            ChunkSet.MarkAll(activeBits, statChunks, statCount);
            for (var word = 0; word < words; word++)
            {
                activeBits[word] |= pendingBits[word];
                pendingBits[word] = 0ul;
            }

            return ChunkSet.Drain(activeBits, words, ascending);
        }

        public static long Outbox(
            Net.Width width,
            in WorldView world,
            ref EffectWriter writer,
            int* outbox,
            ulong* live,
            int chunkFrom,
            int chunkTo,
            int kindLane,
            int targetLane,
            int channel)
        {
            var sent = 0L;
            for (var chunk = chunkFrom; chunk < chunkTo; chunk++)
            {
                var cells = outbox + ((long)chunk << Slot.RowBits);
                var rows = Net.LaneScan.NonZero(width, cells) & live[chunk];
                if (rows == 0ul)
                {
                    continue;
                }

                var kinds = kindLane >= 0 ? Lanes.Ints(world, chunk, kindLane) : null;
                var targets = targetLane >= 0 ? Lanes.Ints(world, chunk, targetLane) : null;
                while (rows != 0ul)
                {
                    var row = Bits.Tzcnt(rows);
                    rows &= rows - 1ul;
                    var slot = Slot.Of(chunk, row);
                    var kind = kinds == null ? 0 : kinds[row];
                    var target = targets == null ? slot : targets[row];
                    var key = EffectKey.Pack(target, slot, (byte)(kind < 0 ? 0 : kind > 255 ? 255 : kind), (byte)channel, Origin.Timeline, 0);
                    sent += writer.Send(key, cells[row], 1) ? 1L : 0L;
                    cells[row] = 0;
                }
            }

            return sent;
        }
    }
}
