using System;
using Sv;
using Unity.Burst.Intrinsics;

namespace Sv.Unity
{
    /// <summary>
    /// Δ-sync view over the board's changed masks: after Board.Recalculate, ChunkMask funnels one
    /// stat's changed bits for a chunk's slot range [chunkStart, chunkStart + chunkCount) into the
    /// v128 word pair Unity hands to chunk jobs — bit i marks row chunkStart + i, at any alignment,
    /// the tail bounded by chunkCount. A host job re-reads only chunks whose mask is nonzero;
    /// unchanged chunks read zero.
    /// </summary>
    public static unsafe class SvChanged
    {
        /// <summary>Count of mask words covering <paramref name="chunkCount"/> rows; the v128 pair holds two.</summary>
        public static int WordCount(int chunkCount) => (chunkCount + 63) >> 6;

        /// <summary>
        /// The chunk's changed-rows mask: bit i is set iff row chunkStart + i of the stat was
        /// broadcast a value whose bits differ from what its output column held before the last
        /// Recalculate. Reads at most the three board words the range can span, any alignment.
        /// </summary>
        public static v128 ChunkMask(Board board, StatId stat, int chunkStart, int chunkCount)
        {
            if (chunkCount < 0 || chunkCount > 128)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkCount), "sv.unity: chunk mask covers [0, 128] rows");
            }

            if (chunkStart < 0 || (long)chunkStart + chunkCount > board.Capacity)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkStart), "sv.unity: chunk range outside the board");
            }

            var words = board.ChangedWords(stat);
            var wordCount = board.LiveWordCount;
            var first = chunkStart >> 6;
            var shift = chunkStart & 63;
            var word0 = words[first];
            var word1 = first + 1 < wordCount ? words[first + 1] : 0ul;
            var word2 = first + 2 < wordCount ? words[first + 2] : 0ul;
            ulong low;
            ulong high;
            if (shift == 0)
            {
                low = word0;
                high = word1;
            }
            else
            {
                low = (word0 >> shift) | (word1 << (64 - shift));
                high = (word1 >> shift) | (word2 << (64 - shift));
            }

            if (chunkCount < 64)
            {
                low &= (1ul << chunkCount) - 1;
                high = 0;
            }
            else if (chunkCount < 128)
            {
                high &= (1ul << (chunkCount - 64)) - 1;
            }

            return new v128(low, high);
        }
    }
}
