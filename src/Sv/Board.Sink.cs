using System;

namespace Sv
{
    public sealed unsafe partial class Board
    {
        ulong[]? sinkStats;

        ushort[]? sinkList;

        int sinkCount;

        ulong[]? sinkUnion;

        bool sinkBuilt;

        int sinkTotal;

        int sinkTaken;

        int sinkSlot;

        int sinkStatAt;

        public void BindSink(StatId stat)
        {
            var index = StatIndex(stat);
            if (index > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(stat), $"sv: stat {index} does not fit a sink record");
            }

            if (sinkStats == null)
            {
                sinkStats = new ulong[(statCount + 63) >> 6];
                sinkList = new ushort[statCount];
                sinkUnion = new ulong[(LiveWordCount + 63) >> 6];
            }

            sinkStats[index >> 6] |= 1ul << (index & 63);
            sinkCount = 0;
            for (var s = 0; s < statCount; s++)
            {
                if ((sinkStats[s >> 6] & (1ul << (s & 63))) != 0)
                {
                    sinkList![sinkCount++] = (ushort)s;
                }
            }

            ResetSink();
        }

        public bool IsSinkBound(StatId stat)
        {
            var index = StatIndex(stat);
            return sinkStats != null && (sinkStats[index >> 6] & (1ul << (index & 63))) != 0;
        }

        public int PendingBroadcast
        {
            get
            {
                if (sinkCount == 0)
                {
                    return 0;
                }

                BuildSink();
                return sinkTotal - sinkTaken;
            }
        }

        public int TakeBroadcast(Span<StatWrite> destination)
        {
            if (sinkCount == 0 || destination.Length == 0)
            {
                return 0;
            }

            BuildSink();
            var union = sinkUnion!;
            var list = sinkList!;
            var words = LiveWordCount;
            var written = 0;
            var slot = sinkSlot;
            var statAt = sinkStatAt;
            while (written < destination.Length && slot < Capacity)
            {
                var word = slot >> 6;
                var next = NextUnionWord(union, word, words);
                if (next < 0)
                {
                    slot = Capacity;
                    statAt = 0;
                    break;
                }

                if (next != word)
                {
                    slot = next << 6;
                    statAt = 0;
                    word = next;
                }

                var combined = 0ul;
                for (var k = 0; k < sinkCount; k++)
                {
                    combined |= changed[(nint)list[k] * words + word];
                }

                combined &= ulong.MaxValue << (slot & 63);
                if (combined == 0)
                {
                    slot = (word + 1) << 6;
                    statAt = 0;
                    continue;
                }

                var first = (word << 6) + CellDigest.Tzcnt(combined);
                if (first != slot)
                {
                    slot = first;
                    statAt = 0;
                }

                var alive = (live[word] & (1ul << (slot & 63))) != 0;
                for (; statAt < sinkCount && written < destination.Length; statAt++)
                {
                    var stat = list[statAt];
                    if ((changed[(nint)stat * words + word] & (1ul << (slot & 63))) == 0)
                    {
                        continue;
                    }

                    sinkTaken++;
                    if (!alive)
                    {
                        continue;
                    }

                    var value = CanonicalNaN(SourceValue(slot, stat));
                    destination[written++] = new StatWrite(slot, stat, *(uint*)&value);
                }

                if (statAt == sinkCount)
                {
                    slot++;
                    statAt = 0;
                }
            }

            sinkSlot = slot;
            sinkStatAt = statAt;
            return written;
        }

        public int Apply(ReadOnlySpan<StatOp> canonical)
        {
            var applied = 0;
            for (var i = 0; i < canonical.Length; i++)
            {
                var op = canonical[i];
                if (!IsLive(op.Slot))
                {
                    continue;
                }

                if (op.Op == StatOpKind.RemoveSource)
                {
                    if (RemoveSource(op.Slot, op.Source) > 0)
                    {
                        applied++;
                    }

                    continue;
                }

                if (op.Op != StatOpKind.Add || op.Stat >= statCount || (byte)op.Kind > (byte)ModifierKind.Cap)
                {
                    continue;
                }

                var bits = op.ValueBits;
                var value = *(float*)&bits;
                if (!float.IsFinite(value))
                {
                    continue;
                }

                AddModifier(op.Slot, new StatId(op.Stat), new Modifier(op.Kind, value), op.Source, op.ExpiresAt);
                applied++;
            }

            return applied;
        }

        void ResetSink()
        {
            sinkBuilt = false;
            sinkTotal = 0;
            sinkTaken = 0;
            sinkSlot = 0;
            sinkStatAt = 0;
        }

        void BuildSink()
        {
            if (sinkBuilt)
            {
                return;
            }

            var union = sinkUnion!;
            Array.Clear(union, 0, union.Length);
            var words = LiveWordCount;
            var total = 0;
            for (var k = 0; k < sinkCount; k++)
            {
                var stat = sinkList![k];
                var @base = stat * words;
                var touched = changedTouchedCounts[stat];
                for (var t = 0; t < touched; t++)
                {
                    var word = changedTouched[@base + t];
                    union[word >> 6] |= 1ul << (word & 63);
                    total += PopCount(changed[(nint)@base + word]);
                }
            }

            sinkTotal = total;
            sinkBuilt = true;
        }

        static int NextUnionWord(ulong[] union, int word, int words)
        {
            var at = word >> 6;
            if (at >= union.Length)
            {
                return -1;
            }

            var bits = union[at] & (ulong.MaxValue << (word & 63));
            while (bits == 0)
            {
                at++;
                if (at >= union.Length)
                {
                    return -1;
                }

                bits = union[at];
            }

            var found = (at << 6) + CellDigest.Tzcnt(bits);
            return found < words ? found : -1;
        }

        static int PopCount(ulong value)
        {
            var count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }

            return count;
        }
    }
}
