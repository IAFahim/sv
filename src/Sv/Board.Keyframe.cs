using System;
using System.Runtime.InteropServices;

namespace Sv
{
    public sealed unsafe partial class Board
    {
        internal struct KeyframeRecord
        {
            public long Id;

            public int Stat;

            public ModifierKind Kind;

            public float Value;

            public int Source;

            public long Expiry;
        }

        const uint KeyframeMagic = 0x4B46564C;

        ulong* keyframePages;

        int keyframePageWords;

        int keyframeDirtyCount;

        KeyframeRecord* keyframeScratch;

        int keyframeScratchCap;

        /// <summary>
        /// Digest pages (256 slots) dirtied since the last <see cref="SaveKeyframe"/> — the next
        /// keyframe's cost is proportional to this count, never to <see cref="Capacity"/>. Reset by
        /// <see cref="SaveKeyframe"/> and re-based by loading state or a keyframe; renumbering slots
        /// or rolling an undo window back conservatively dirties every page. Lazily allocated on the
        /// first slot mutation, freed when a load re-bases the epoch.
        /// </summary>
        public int KeyframeDirtyPages => keyframeDirtyCount;

        internal void InitKeyframes()
        {
            keyframePageWords = (PageCount + 63) >> 6;
            keyframePages = arena.AllocZeroed<ulong>(keyframePageWords);
        }

        internal void MarkKeyframeDirty(int slot)
        {
            if (keyframePages == null)
            {
                InitKeyframes();
            }

            var page = slot >> 8;
            var bit = 1ul << (page & 63);
            if ((keyframePages[page >> 6] & bit) != 0)
            {
                return;
            }

            keyframePages[page >> 6] |= bit;
            keyframeDirtyCount++;
        }

        internal void MarkAllKeyframesDirty()
        {
            if (keyframePages == null)
            {
                InitKeyframes();
            }

            Memory.Clear(keyframePages, (nuint)keyframePageWords * sizeof(ulong));
            var full = PageCount >> 6;
            for (var word = 0; word < full; word++)
            {
                keyframePages[word] = ulong.MaxValue;
            }

            var tail = PageCount & 63;
            if (tail != 0)
            {
                keyframePages[full] = (1ul << tail) - 1;
            }

            keyframeDirtyCount = PageCount;
        }

        void ResetKeyframeDirty()
        {
            if (keyframePages == null)
            {
                return;
            }

            Memory.Clear(keyframePages, (nuint)keyframePageWords * sizeof(ulong));
            keyframeDirtyCount = 0;
        }

        void RebaseKeyframes()
        {
            if (keyframePages == null)
            {
                return;
            }

            arena.Free(keyframePages, keyframePageWords);
            keyframePages = null;
            keyframePageWords = 0;
            keyframeDirtyCount = 0;
        }

        void EnsureKeyframeScratch(int count)
        {
            if (count <= keyframeScratchCap)
            {
                return;
            }

            var previous = keyframeScratchCap;
            keyframeScratchCap = count * 2;
            keyframeScratch = arena.Realloc(keyframeScratch, previous, keyframeScratchCap);
        }

        int ChainLength(int slot)
        {
            var count = 0;
            for (var at = Slot(slot).Head; at >= 0; at = records[at].Next)
            {
                count++;
            }

            return count;
        }

        /// <summary>
        /// Writes an incremental keyframe into <paramref name="state"/> (overwriting it): the state of
        /// every digest page dirtied since the previous <see cref="SaveKeyframe"/> — live bits plus each
        /// live slot's modifier records — followed by the digest, and resets the dirty-page tracking.
        /// Cost is O(dirty pages), never O(<see cref="Capacity"/>). Take keyframes at frame boundaries
        /// (after <see cref="Recalculate"/>). Warm saves are managed-allocation-free; an open undo
        /// window stays open.
        /// </summary>
        public void SaveKeyframe(SvState state)
        {
            state.BeginWrite();
            var codec = new SaveCodec(state);
            codec.Header(KeyframeMagic, StateShape, "keyframe");
            var insertion = nextInsertionId;
            codec.Value(ref insertion);
            var generation = insertionGeneration;
            codec.Value(ref generation);
            var digest = Digest;
            codec.Value(ref digest);
            var pages = keyframeDirtyCount;
            codec.Value(ref pages);
            var written = 0;
            for (var word = 0; word < keyframePageWords && written < pages; word++)
            {
                var bits = keyframePages[word];
                while (bits != 0 && written < pages)
                {
                    var page = (word << 6) + Bits.Tzcnt(bits);
                    bits &= bits - 1;
                    written++;
                    codec.Value(ref page);
                    var first = page << 2;
                    var wordsInPage = Math.Min(4, LiveWordCount - first);
                    codec.Bytes(live + first, (long)wordsInPage * sizeof(ulong));
                    var slotBase = page << 8;
                    var slotsInPage = Math.Min(256, Capacity - slotBase);
                    for (var local = 0; local < slotsInPage; local++)
                    {
                        var slot = slotBase + local;
                        var count = 0;
                        if (statCount > 0 && (live[slot >> 6] & (1ul << (slot & 63))) != 0)
                        {
                            count = ChainLength(slot);
                        }

                        codec.Value(ref count);
                        if (count == 0)
                        {
                            continue;
                        }

                        EnsureKeyframeScratch(count);
                        var at = 0;
                        for (var record = Slot(slot).Head; record >= 0; record = records[record].Next)
                        {
                            keyframeScratch[at++] = new KeyframeRecord
                            {
                                Id = records[record].Id,
                                Stat = records[record].Stat,
                                Kind = records[record].Kind,
                                Value = records[record].Value,
                                Source = records[record].Source,
                                Expiry = records[record].Expiry,
                            };
                        }

                        codec.Bytes(keyframeScratch, (long)count * sizeof(KeyframeRecord));
                    }
                }
            }

            ResetKeyframeDirty();
        }

        /// <summary>
        /// Folds one <see cref="SaveKeyframe"/> onto this board: the base must be the source board's
        /// state when that keyframe's epoch began. Per dirty page, acquired slots acquire, released
        /// slots release and live slots have their modifier records replaced (original insertion ids
        /// preserved); the board then recalculates the touched slots and verifies the digest, throwing
        /// <see cref="System.IO.InvalidDataException"/> when the keyframe does not fold onto this base.
        /// The restored board continues simulation: interned sets and blocks are re-derived
        /// content-addressed, so digests, columns and folds match the source board exactly while the
        /// saved-state bytes of the two tables may differ. Closes any open undo window.
        /// </summary>
        public void LoadKeyframe(SvState state)
        {
            state.BeginRead();
            var codec = new LoadCodec(state);
            codec.Header(KeyframeMagic, StateShape, "keyframe");
            var insertion = 0L;
            codec.Value(ref insertion);
            var generation = 0;
            codec.Value(ref generation);
            if (generation > insertionGeneration || (generation == insertionGeneration && insertion > nextInsertionId))
            {
                insertionGeneration = generation;
                nextInsertionId = insertion;
            }

            InvalidateUndo();
            var expected = 0ul;
            codec.Value(ref expected);
            var pages = 0;
            codec.Value(ref pages);
            if (pages < 0 || pages > PageCount)
            {
                throw new System.IO.InvalidDataException("sv: corrupt keyframe");
            }

            var pageLive = stackalloc ulong[4];
            for (var p = 0; p < pages; p++)
            {
                var page = 0;
                codec.Value(ref page);
                if (page < 0 || page >= PageCount)
                {
                    throw new System.IO.InvalidDataException("sv: corrupt keyframe");
                }

                var first = page << 2;
                var wordsInPage = Math.Min(4, LiveWordCount - first);
                Memory.Clear(pageLive, 4 * sizeof(ulong));
                codec.Bytes(pageLive, (long)wordsInPage * sizeof(ulong));
                var slotBase = page << 8;
                var slotsInPage = Math.Min(256, Capacity - slotBase);
                for (var local = 0; local < slotsInPage; local++)
                {
                    var slot = slotBase + local;
                    var bit = 1ul << (slot & 63);
                    var shippedLive = (pageLive[local >> 6] & bit) != 0;
                    var wasLive = (live[slot >> 6] & bit) != 0;
                    var count = 0;
                    codec.Value(ref count);
                    if (count < 0 || (count > 0 && (statCount == 0 || !shippedLive)))
                    {
                        throw new System.IO.InvalidDataException("sv: corrupt keyframe");
                    }

                    if (count > 0)
                    {
                        EnsureKeyframeScratch(count);
                        codec.Bytes(keyframeScratch, (long)count * sizeof(KeyframeRecord));
                    }

                    if (!shippedLive)
                    {
                        if (!wasLive)
                        {
                            continue;
                        }

                        live[slot >> 6] &= ~bit;
                        LiveCount--;
                        if ((slot >> 6) < freeHint)
                        {
                            freeHint = slot >> 6;
                        }

                        if (statCount > 0)
                        {
                            ClearSlotStats(slot);
                            ZeroSlotRows(slot);
                        }

                        continue;
                    }

                    if (!wasLive)
                    {
                        live[slot >> 6] |= bit;
                        LiveCount++;
                        if (statCount > 0)
                        {
                            EnsureSlotPages(slot);
                            MarkSlotAcquired(slot);
                        }
                    }

                    if (statCount == 0)
                    {
                        continue;
                    }

                    ref var slotState = ref Slot(slot);
                    var identical = wasLive && ChainLength(slot) == count;
                    if (identical)
                    {
                        var at = slotState.Head;
                        for (var r = 0; r < count; r++, at = records[at].Next)
                        {
                            var shipped = keyframeScratch[r];
                            if (records[at].Id == shipped.Id && records[at].Stat == shipped.Stat && records[at].Kind == shipped.Kind &&
                                Bits.OfSingle(records[at].Value) == Bits.OfSingle(shipped.Value) &&
                                records[at].Source == shipped.Source && records[at].Expiry == shipped.Expiry)
                            {
                                continue;
                            }

                            identical = false;
                            break;
                        }
                    }

                    if (identical)
                    {
                        continue;
                    }

                    for (var at = slotState.Head; at >= 0;)
                    {
                        var next = records[at].Next;
                        WheelUnlink(at);
                        FreeRecord(at);
                        at = next;
                    }

                    slotState.Head = -1;
                    for (var r = count - 1; r >= 0; r--)
                    {
                        var shipped = keyframeScratch[r];
                        if (shipped.Stat < 0 || shipped.Stat >= statCount)
                        {
                            throw new System.IO.InvalidDataException("sv: corrupt keyframe");
                        }

                        var record = AllocRecord();
                        records[record] = new ModRecord
                        {
                            Id = shipped.Id,
                            Kind = shipped.Kind,
                            Value = shipped.Value,
                            Source = shipped.Source,
                            Expiry = shipped.Expiry,
                            Next = slotState.Head,
                            Slot = slot,
                            Stat = shipped.Stat,
                            Wheel = -1
                        };
                        slotState.Head = record;
                        if (shipped.Expiry != long.MaxValue)
                        {
                            WheelInsert(record, shipped.Id, shipped.Expiry);
                        }
                    }

                    MarkSlotStatsDirty(slot);
                }
            }

            var word = 0;
            while (word < LiveWordCount && live[word] == ulong.MaxValue)
            {
                word++;
            }

            freeHint = word;
            if (statCount > 0)
            {
                Recalculate();
                EnsureDigestCurrent();
                if (Digest != expected)
                {
                    throw new System.IO.InvalidDataException($"sv: keyframe does not fold onto this base — digest {Digest:X16} != {expected:X16}");
                }
            }

            RebaseKeyframes();
            RefillSink();
            state.EndRead();
        }}
}
