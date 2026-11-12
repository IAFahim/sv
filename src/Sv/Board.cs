using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Sv
{
    public sealed unsafe partial class Board : IDisposable
    {
        public Schema Schema { get; }

        public int Capacity { get; }

        public int RequestedCapacity { get; }

        public int LiveCount { get; private set; }

        public int LiveWordCount => (Capacity + 63) >> 6;

        public ulong LiveWord(int word)
        {
            if (word < 0 || word >= LiveWordCount)
            {
                throw new ArgumentOutOfRangeException(nameof(word), $"sv: live word {word} out of range (0..{LiveWordCount - 1})");
            }

            return live[word];
        }

        private readonly NativeArena arena;

        private readonly ulong* live;

        private int freeHint;

        private bool disposed;

        Board(Schema schema, int capacity, int requestedCapacity)
        {
            Schema = schema;
            Capacity = capacity;
            RequestedCapacity = requestedCapacity;
            arena = new NativeArena();
            var words = (capacity + 63) >> 6;
            live = arena.AllocZeroed<ulong>(words);

            InitStats(schema, capacity);
            StateShape = ComputeStateShape();
        }

        long ComputeStateShape()
        {
            var bytes = stackalloc byte[28];
            *(long*)bytes = SetTableFormatVersion;
            *(int*)(bytes + 8) = Capacity;
            *(int*)(bytes + 12) = statCount;
            *(int*)(bytes + 16) = Schema.Intrinsics.Count;
            *(ulong*)(bytes + 20) = Schema.ContentHash;
            return (long)XxHash64.Hash(bytes, 28);
        }

        public static Board Create(Schema schema, int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "sv: capacity must be positive");
            }

            return new Board(schema, (capacity + 63) & ~63, capacity);
        }

        /// <summary>
        /// Native bytes currently owned by this board — the tracked allocator's live total, so it is
        /// O(1) and exact: slot bookkeeping, per-slot state pages, the block table, the modifier record
        /// pool, the set table, dirty masks, scratch, mirrors and sv-owned output columns, all counted
        /// at their real allocation sizes. <see cref="Capacity"/> rounds up to a 64 boundary;
        /// <see cref="RequestedCapacity"/> is the pre-rounding value.
        /// </summary>
        public long MemoryBytes => arena.Live;

        const uint StateMagic = 0x44425654;

        const long SetTableFormatVersion = 11;

        internal long StateShape { get; }

        /// <summary>
        /// Saves the board's complete state into <paramref name="state"/> (overwriting it): slot space,
        /// modifier storage and insertion ids, sources and expiries, set identities and membership,
        /// interned blocks, sv-owned output columns, mirrors, changed masks and the dirty stat masks.
        /// External bindings are not state — host-owned columns are the host's to save. Warm saves are
        /// managed-allocation-free.
        /// </summary>
        public void SaveState(SvState state)
        {
            state.BeginWrite();
            var codec = new SaveCodec(state);
            VisitState(ref codec);
        }

        /// <summary>
        /// Restores a state saved from a board of the same schema shape and capacity, bit-exactly:
        /// resimulating the same inputs reproduces the same results. Bindings stay as they are;
        /// sv-owned columns and mirrors hold the saved broadcast. A mirror that exists only locally
        /// (the snapshot predates it) is kept — never freed, so cached <see cref="Clamp.StatMirror"/>
        /// pointers stay valid — refilled from the restored output, and bound columns are densely
        /// refilled from the restored rows immediately.
        /// </summary>
        public void LoadState(SvState state)
        {
            state.BeginRead();
            var codec = new LoadCodec(state);
            VisitState(ref codec);
            state.EndRead();
        }

        internal void VisitState<TCodec>(ref TCodec codec) where TCodec : struct, IStateCodec
        {
            if (TCodec.Reading)
            {
                InvalidateUndo();
                RebaseKeyframes();
            }

            codec.Header(StateMagic, StateShape, "board");
            var liveCount = LiveCount;
            codec.Value(ref liveCount);
            if (TCodec.Reading)
            {
                LiveCount = liveCount;
            }

            codec.Bytes(live, (long)LiveWordCount * sizeof(ulong));
            if (TCodec.Reading)
            {
                var word = 0;
                while (word < LiveWordCount && live[word] == ulong.MaxValue)
                {
                    word++;
                }

                freeHint = word;
            }

            if (statCount > 0)
            {
                VisitStats(ref codec);
            }
        }

        public int Acquire()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(Board));
            }

            var slot = TakeFreeSlot();
            if (statCount > 0)
            {
                EnsureSlotPages(slot);
                MarkSlotStatsDirty(slot);
                MarkSlotAcquired(slot);
            }

            return slot;
        }

        int TakeFreeSlot()
        {
            var word = freeHint;
            while (word < LiveWordCount && live[word] == ulong.MaxValue)
            {
                word++;
            }

            if (word == LiveWordCount)
            {
                throw new InvalidOperationException("sv: slot exhaustion");
            }

            var slot = (word << 6) + BitOperations.TrailingZeroCount(~live[word]);
            undo?.Note(live + word, sizeof(ulong));
            live[word] |= 1ul << (slot & 63);
            freeHint = live[word] == ulong.MaxValue ? word + 1 : word;
            LiveCount++;
            MarkKeyframeDirty(slot);
            return slot;
        }

        internal bool IsLive(int slot)
        {
            return slot >= 0 && slot < Capacity && (live[slot >> 6] & (1ul << (slot & 63))) != 0;
        }

        public void Release(int slot)
        {
            if (slot < 0 || slot >= Capacity || (live[slot >> 6] & (1ul << (slot & 63))) == 0)
            {
                throw new ArgumentException($"sv: release of non-live slot {slot}");
            }

            undo?.Note(live + (slot >> 6), sizeof(ulong));
            live[slot >> 6] &= ~(1ul << (slot & 63));
            if ((slot >> 6) < freeHint)
            {
                freeHint = slot >> 6;
            }

            MarkKeyframeDirty(slot);

            if (statCount > 0)
            {
                ClearSlotStats(slot);
                ZeroSlotRows(slot);
            }

            LiveCount--;
        }

        public void RunIntrinsicsChunk(int* value, int delta, in Clamp clamp, int start, int count, int* applied = null)
        {
            Kernel.IntrinsicsChunk(value, delta, clamp, start, count, Capacity, applied);
        }

        public void RunIntrinsicsChunk(int* value, int* delta, in Clamp clamp, int start, int count, int* applied = null)
        {
            Kernel.IntrinsicsChunk(value, delta, clamp, start, count, Capacity, applied);
        }

        public void RunEnabledChunk(int* value, int delta, in Clamp clamp, ulong enabled, int offset, int* applied = null, int validBits = 64)
        {
            Kernel.EnabledChunk(value, delta, clamp, enabled, offset, Capacity, applied, validBits);
        }

        public void RunEnabledChunk(int* value, int* delta, in Clamp clamp, ulong enabled, int offset, int* applied = null, int validBits = 64)
        {
            Kernel.EnabledChunk(value, delta, clamp, enabled, offset, Capacity, applied, validBits);
        }

        /// <param name="value">Stat column indexed by absolute slot; written for every enabled row in the window.</param>
        /// <param name="delta">Uniform delta added to every enabled row.</param>
        /// <param name="clamp">Bounds applied after the add.</param>
        /// <param name="words">Enabled-mask words; bit i of word w marks row offset + w*64 + i.</param>
        /// <param name="wordCount">Number of mask words; the window spans wordCount * 64 rows from <paramref name="offset"/>.</param>
        /// <param name="offset">Absolute slot index of the window's first row.</param>
        /// <param name="applied">Delta column indexed by absolute slot; must be sized for the full window from <paramref name="offset"/> — the entire window is written, disabled rows get zero.</param>
        public void RunEnabledChunk(int* value, int delta, in Clamp clamp, ulong* words, int wordCount, int offset, int* applied = null)
        {
            Kernel.EnabledChunk(value, delta, clamp, words, wordCount, offset, Capacity, applied);
        }

        /// <param name="value">Stat column indexed by absolute slot; written for every enabled row in the window.</param>
        /// <param name="delta">Per-slot delta column indexed by absolute slot.</param>
        /// <param name="clamp">Bounds applied after the add.</param>
        /// <param name="words">Enabled-mask words; bit i of word w marks row offset + w*64 + i.</param>
        /// <param name="wordCount">Number of mask words; the window spans wordCount * 64 rows from <paramref name="offset"/>.</param>
        /// <param name="offset">Absolute slot index of the window's first row.</param>
        /// <param name="applied">Delta column indexed by absolute slot; must be sized for the full window from <paramref name="offset"/> — the entire window is written, disabled rows get zero.</param>
        public void RunEnabledChunk(int* value, int* delta, in Clamp clamp, ulong* words, int wordCount, int offset, int* applied = null)
        {
            Kernel.EnabledChunk(value, delta, clamp, words, wordCount, offset, Capacity, applied);
        }

        /// <param name="value">Stat column indexed by absolute slot; written for every enabled row in the window.</param>
        /// <param name="delta">Uniform delta added to every enabled row.</param>
        /// <param name="clamp">Bounds applied after the add.</param>
        /// <param name="words">Enabled-mask words; bit i of word w marks row offset + w*64 + i.</param>
        /// <param name="wordCount">Number of mask words; the window spans wordCount * 64 rows from <paramref name="offset"/>.</param>
        /// <param name="offset">Absolute slot index of the window's first row.</param>
        /// <param name="applied">Delta column indexed by absolute slot; must be sized for the full window from <paramref name="offset"/> — the entire window is written, disabled rows get zero.</param>
        public void RunEnabledChunk(Span<int> value, int delta, in Clamp clamp, ulong* words, int wordCount, int offset, Span<int> applied = default)
        {
            if (words == null)
            {
                throw new ArgumentNullException(nameof(words));
            }

            Guard.EnabledRange(offset, wordCount, Capacity);
            var reach = Reach(words, wordCount, offset);
            Guard.SpanReach(value.Length, reach, nameof(value));
            Guard.SpanWindow(applied.Length, (long)offset + wordCount * 64, nameof(applied));
            fixed (int* v = value)
            fixed (int* a = applied)
            {
                for (var w = 0; w < wordCount; w++)
                {
                    Simd.EnabledWord(v, null, delta, clamp, words[w], offset + w * 64, a, Simd.ValidBits(value.Length, offset + w * 64));
                }
            }
        }

        /// <param name="value">Stat column indexed by absolute slot; written for every enabled row in the window.</param>
        /// <param name="delta">Per-slot delta column indexed by absolute slot.</param>
        /// <param name="clamp">Bounds applied after the add.</param>
        /// <param name="words">Enabled-mask words; bit i of word w marks row offset + w*64 + i.</param>
        /// <param name="wordCount">Number of mask words; the window spans wordCount * 64 rows from <paramref name="offset"/>.</param>
        /// <param name="offset">Absolute slot index of the window's first row.</param>
        /// <param name="applied">Delta column indexed by absolute slot; must be sized for the full window from <paramref name="offset"/> — the entire window is written, disabled rows get zero.</param>
        public void RunEnabledChunk(Span<int> value, ReadOnlySpan<int> delta, in Clamp clamp, ulong* words, int wordCount, int offset, Span<int> applied = default)
        {
            if (words == null)
            {
                throw new ArgumentNullException(nameof(words));
            }

            Guard.EnabledRange(offset, wordCount, Capacity);
            var reach = Reach(words, wordCount, offset);
            Guard.SpanReach(value.Length, reach, nameof(value));
            Guard.SpanReach(delta.Length, reach, nameof(delta));
            Guard.SpanWindow(applied.Length, (long)offset + wordCount * 64, nameof(applied));
            fixed (int* v = value)
            fixed (int* d = delta)
            fixed (int* a = applied)
            {
                for (var w = 0; w < wordCount; w++)
                {
                    Simd.EnabledWord(v, d, 0, clamp, words[w], offset + w * 64, a, Simd.ValidBits(Math.Min(value.Length, delta.Length), offset + w * 64));
                }
            }
        }

        static int Reach(ulong* words, int wordCount, int offset)
        {
            for (var w = wordCount - 1; w >= 0; w--)
            {
                if (words[w] != 0)
                {
                    return offset + w * 64 + 64 - BitOperations.LeadingZeroCount(words[w]);
                }
            }

            return 0;
        }

        static int Reach(ulong word, int offset) => word == 0 ? 0 : offset + 64 - BitOperations.LeadingZeroCount(word);

        public void RunIntrinsicsChunk(Span<int> value, int delta, in Clamp clamp, int start, int count, Span<int> applied = default)
        {
            Guard.ChunkRange(start, count, Capacity);
            Guard.SpanReach(value.Length, start + count, nameof(value));
            Guard.SpanWindow(applied.Length, start + count, nameof(applied));
            fixed (int* v = value)
            fixed (int* a = applied)
            {
                Simd.Chunk(v, null, delta, clamp, start, count, a);
            }
        }

        public void RunIntrinsicsChunk(Span<int> value, ReadOnlySpan<int> delta, in Clamp clamp, int start, int count, Span<int> applied = default)
        {
            Guard.ChunkRange(start, count, Capacity);
            Guard.SpanReach(value.Length, start + count, nameof(value));
            Guard.SpanReach(delta.Length, start + count, nameof(delta));
            Guard.SpanWindow(applied.Length, start + count, nameof(applied));
            fixed (int* v = value)
            fixed (int* d = delta)
            fixed (int* a = applied)
            {
                Simd.Chunk(v, d, 0, clamp, start, count, a);
            }
        }

        public void RunEnabledChunk(Span<int> value, int delta, in Clamp clamp, ulong enabled, int offset, Span<int> applied = default)
        {
            Guard.EnabledRange(offset, Capacity);
            Guard.SpanReach(value.Length, Reach(enabled, offset), nameof(value));
            Guard.SpanWindow(applied.Length, offset + 64, nameof(applied));
            fixed (int* v = value)
            fixed (int* a = applied)
            {
                Simd.EnabledWord(v, null, delta, clamp, enabled, offset, a, Simd.ValidBits(value.Length, offset));
            }
        }

        public void RunEnabledChunk(Span<int> value, ReadOnlySpan<int> delta, in Clamp clamp, ulong enabled, int offset, Span<int> applied = default)
        {
            Guard.EnabledRange(offset, Capacity);
            var reach = Reach(enabled, offset);
            Guard.SpanReach(value.Length, reach, nameof(value));
            Guard.SpanReach(delta.Length, reach, nameof(delta));
            Guard.SpanWindow(applied.Length, offset + 64, nameof(applied));
            fixed (int* v = value)
            fixed (int* d = delta)
            fixed (int* a = applied)
            {
                Simd.EnabledWord(v, d, 0, clamp, enabled, offset, a, Simd.ValidBits(Math.Min(value.Length, delta.Length), offset));
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            EndUndoWindow();
            FreeStats();
            arena.Free(live, LiveWordCount);
            if (keyframePages != null)
            {
                arena.Free(keyframePages, keyframePageWords);
            }

            arena.Dispose();
            disposed = true;
        }
    }
}
