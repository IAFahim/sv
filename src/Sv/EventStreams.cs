using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sv
{
    /// <summary>
    /// Struct event maker: an instance interface (C# 9 / Unity portable) the JIT devirtualizes and
    /// inlines into the emit loop, so it pays no <c>UnmanagedCallersOnly</c> entry gate. Passed by
    /// <c>ref</c>, so a maker may carry state (counters, lookup pointers).
    /// </summary>
    public interface IEventMaker<T> where T : unmanaged
    {
        T Make(int row, int value);
    }

    public sealed unsafe class Channel<T> where T : unmanaged
    {
        internal EventStreams Owner = null!;

        public ushort Id;

        internal int Writer;

        public void Write(T evt)
        {
            Owner.Append(Id, Writer, &evt);
        }
    }

    public sealed unsafe class EventStreams : IDisposable
    {
        private const int HeaderBytes = 8;

        private readonly int writerCount;

        private readonly byte** arenas;

        private readonly int* arenaCapacity;

        private readonly int* arenaPeak;

        private readonly int* writeOffset;

        private readonly int* readOffset;

        private readonly int* readCursorOffset;

        private int active;

        private const int LockStride = 8;

        private const int BatchBytes = 256;

        private const int OffsetStride = LockStride * 2;

        private readonly long* locks;

        private int nextTypeId;

        private bool disposed;

        internal UndoLog? Undo;

        int[]? undoActiveHalves;

        readonly List<ArenaReplace> arenaReplaces = new();

        readonly struct ArenaReplace
        {
            public readonly int Index;

            public readonly IntPtr Old;

            public readonly int OldCapacity;

            public readonly IntPtr New;

            public readonly int Frame;

            public ArenaReplace(int index, IntPtr old, int oldCapacity, IntPtr @new, int frame)
            {
                Index = index;
                Old = old;
                OldCapacity = oldCapacity;
                New = @new;
                Frame = frame;
            }
        }

        EventStreams(int writerCount, int bytesPerWriter)
        {
            this.writerCount = writerCount;
            arenas = (byte**)Memory.Alloc((nuint)writerCount * 2 * (nuint)sizeof(byte*));
            arenaCapacity = (int*)Memory.AllocZeroed((nuint)writerCount * 2 * sizeof(int));
            arenaPeak = (int*)Memory.AllocZeroed((nuint)writerCount * 2 * sizeof(int));
            readOffset = (int*)Memory.AllocZeroed((nuint)writerCount * sizeof(int));
            readCursorOffset = (int*)Memory.AllocZeroed((nuint)writerCount * sizeof(int));
            locks = (long*)Memory.AllocAligned((nuint)(writerCount * LockStride * sizeof(long)), 64);
            Memory.Clear(locks, (nuint)(writerCount * LockStride * sizeof(long)));
            writeOffset = (int*)(locks + 1);
            var rounded = Round(bytesPerWriter);
            for (var w = 0; w < writerCount; w++)
            {
                for (var b = 0; b < 2; b++)
                {
                    var index = w * 2 + b;
                    arenas[index] = (byte*)Memory.Alloc((nuint)rounded);
                    arenaCapacity[index] = rounded;
                }
            }
        }

        public static EventStreams Create(int writerCount = 1, int bytesPerWriter = 4096)
        {
            if (writerCount < 1 || writerCount > 64)
            {
                throw new ArgumentOutOfRangeException(nameof(writerCount), "sv: 1..64 writers");
            }

            if (bytesPerWriter < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(bytesPerWriter), "sv: bytesPerWriter must be >= 1 (0 rounds to 0 and the growth loop never terminates)");
            }

            return new EventStreams(writerCount, bytesPerWriter);
        }

        static int Round(int bytes) => (bytes + 7) & ~7;

        public Channel<T> CreateChannel<T>(int writer = 0) where T : unmanaged
        {
            if (writer < 0 || writer >= writerCount)
            {
                throw new ArgumentOutOfRangeException(nameof(writer));
            }

            if (nextTypeId == ushort.MaxValue)
            {
                throw new InvalidOperationException("sv: channel type id exhaustion");
            }

            return new Channel<T> { Owner = this, Id = (ushort)nextTypeId++, Writer = writer };
        }

        internal void Append<T>(ushort id, int writer, T* payload) where T : unmanaged
        {
            AppendBatch(id, writer, payload, 1);
        }

        internal void AppendBatch<T>(ushort id, int writer, T* payload, int count) where T : unmanaged
        {
            var size = sizeof(T);
            var stride = HeaderBytes + Round(size);
            var need = stride * count;
            ref var gate = ref locks[writer * LockStride];
            while (Interlocked.CompareExchange(ref gate, 1, 0) != 0)
            {
                Thread.SpinWait(1);
            }

            try
            {
                var index = writer * 2 + active;
                var at = writeOffset[writer * OffsetStride];
                var required = (long)at + need;
                if (required > int.MaxValue / 2)
                {
                    throw new InvalidOperationException("sv: event arena exceeded 1 GiB per writer");
                }

                if (required > arenaCapacity[index])
                {
                    var capacity = arenaCapacity[index];
                    while (capacity < required)
                    {
                        capacity *= 2;
                    }

                    if (Undo != null)
                    {
                        var previous = arenas[index];
                        var fresh = (byte*)Memory.Alloc((nuint)capacity);
                        Buffer.MemoryCopy(previous, fresh, capacity, arenaCapacity[index]);
                        arenaReplaces.Add(new ArenaReplace(index, (IntPtr)previous, arenaCapacity[index], (IntPtr)fresh, Undo.Top));
                        arenas[index] = fresh;
                    }
                    else
                    {
                        arenas[index] = (byte*)Memory.Realloc(arenas[index], (nuint)capacity);
                    }

                    arenaCapacity[index] = capacity;
                }

                var dst = arenas[index] + at;
                Undo?.Note(dst, need);
                for (var i = 0; i < count; i++)
                {
                    *(ushort*)dst = id;
                    *(ushort*)(dst + 2) = 0;
                    *(int*)(dst + 4) = size;
                    *(T*)(dst + HeaderBytes) = payload[i];
                    dst += stride;
                }

                Undo?.Note(writeOffset + writer * OffsetStride, sizeof(int));
                writeOffset[writer * OffsetStride] = at + need;
            }
            finally
            {
                Volatile.Write(ref gate, 0);
            }
        }

        void EmitWord<T, TMaker>(Channel<T> channel, ulong mask, int rowBase, int* values, ref TMaker maker)
            where T : unmanaged
            where TMaker : struct, IEventMaker<T>
        {
            if (mask == 0)
            {
                return;
            }

            if (sizeof(T) > BatchBytes)
            {
                while (mask != 0)
                {
                    var bit = Bits.Tzcnt(mask);
                    mask &= mask - 1;
                    var row = rowBase + bit;
                    var evt = maker.Make(row, values == null ? 1 : values[row]);
                    Append(channel.Id, channel.Writer, &evt);
                }

                return;
            }

            var batch = stackalloc T[64];
            var count = 0;
            while (mask != 0)
            {
                var bit = Bits.Tzcnt(mask);
                mask &= mask - 1;
                var row = rowBase + bit;
                batch[count++] = maker.Make(row, values == null ? 1 : values[row]);
            }

            AppendBatch(channel.Id, channel.Writer, batch, count);
        }

#if NET
        readonly struct PointerMaker<T> : IEventMaker<T> where T : unmanaged
        {
            readonly delegate* unmanaged<int, int, T> make;

            public PointerMaker(delegate* unmanaged<int, int, T> make)
            {
                this.make = make;
            }

            public T Make(int row, int value) => make(row, value);
        }
#endif

        internal long ArenaBytes
        {
            get
            {
                long bytes = 0;
                for (var index = 0; index < writerCount * 2; index++)
                {
                    bytes += arenaCapacity[index];
                }

                return bytes;
            }
        }

        public unsafe void Swap()
        {
            var undo = Undo;
            for (var w = 0; w < writerCount; w++)
            {
                var used = writeOffset[w * OffsetStride];
                if (undo != null)
                {
                    undo.Note(readOffset + w, sizeof(int));
                    undo.Note(writeOffset + w * OffsetStride, sizeof(int));
                    undo.Note(readCursorOffset + w, sizeof(int));
                    undo.Note(arenaPeak + (w * 2 + active), sizeof(int));
                    undo.Note(arenaPeak + (w * 2 + (1 - active)), sizeof(int));
                }

                readOffset[w] = used;
                writeOffset[w * OffsetStride] = 0;
                readCursorOffset[w] = 0;
                DecayPeak(w * 2 + active, used);
                DecayPeak(w * 2 + (1 - active), used);
                if (undo == null)
                {
                    ShrinkArena(w * 2 + active);
                    ShrinkArena(w * 2 + (1 - active));
                }
            }

            active ^= 1;
        }

        void DecayPeak(int index, int used)
        {
            var decayed = arenaPeak[index] - (arenaPeak[index] >> 4);
            arenaPeak[index] = used > decayed ? used : decayed;
        }

        void ShrinkArena(int index)
        {
            var capacity = arenaCapacity[index];
            var peak = arenaPeak[index];
            if (capacity <= 4096 || (long)peak * 4 > (long)capacity * 3)
            {
                return;
            }

            var target = Round(peak + (peak >> 3));
            arenas[index] = (byte*)Memory.Realloc(arenas[index], (nuint)target);
            arenaCapacity[index] = target;
        }

        public bool TryReadNext(out ushort typeId, out void* payload, out int size)
        {
            var buffer = 1 - active;
            var writer = 0;
            while (writer < writerCount)
            {
                var offset = readCursorOffset[writer];
                if (offset < readOffset[writer])
                {
                    var src = arenas[writer * 2 + buffer] + offset;
                    typeId = *(ushort*)src;
                    size = *(int*)(src + 4);
                    payload = src + HeaderBytes;
                    Undo?.Note(readCursorOffset + writer, sizeof(int));
                    readCursorOffset[writer] = offset + HeaderBytes + Round(size);
                    return true;
                }

                writer++;
            }

            typeId = 0;
            size = 0;
            payload = null;
            return false;
        }

#if NET
        public void EmitFromMask<T>(Channel<T> channel, ulong enabled, int offset, delegate* unmanaged<int, int, T> make) where T : unmanaged
        {
            var maker = new PointerMaker<T>(make);
            EmitFromMask(channel, enabled, offset, ref maker);
        }
#endif

        public void EmitFromMask<T, TMaker>(Channel<T> channel, ulong enabled, int offset, ref TMaker maker)
            where T : unmanaged
            where TMaker : struct, IEventMaker<T>
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            EmitWord(channel, enabled, offset, null, ref maker);
        }

#if NET
        public void EmitFromBools<T>(Channel<T> channel, byte* cond, int start, int count, delegate* unmanaged<int, int, T> make) where T : unmanaged
        {
            var maker = new PointerMaker<T>(make);
            EmitFromBools(channel, cond, start, count, ref maker);
        }
#endif

        public void EmitFromBools<T, TMaker>(Channel<T> channel, byte* cond, int start, int count, ref TMaker maker)
            where T : unmanaged
            where TMaker : struct, IEventMaker<T>
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            var row = start;
            var remaining = count;
            while (remaining >= 64)
            {
                EmitWord(channel, Simd.BoolNonZeroWord(cond, row), row, null, ref maker);
                row += 64;
                remaining -= 64;
            }

            ulong tail = 0;
            for (var i = 0; i < remaining; i++)
            {
                if (cond[row + i] != 0)
                {
                    tail |= 1ul << i;
                }
            }

            EmitWord(channel, tail, row, null, ref maker);
        }

#if NET
        public void EmitFromBools<T>(Channel<T> channel, byte* cond, ulong enabled, int offset, delegate* unmanaged<int, int, T> make) where T : unmanaged
        {
            var maker = new PointerMaker<T>(make);
            EmitFromBools(channel, cond, enabled, offset, ref maker);
        }
#endif

        public void EmitFromBools<T, TMaker>(Channel<T> channel, byte* cond, ulong enabled, int offset, ref TMaker maker)
            where T : unmanaged
            where TMaker : struct, IEventMaker<T>
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            var mask = enabled;
            ulong hits = 0;
            while (mask != 0)
            {
                var bit = Bits.Tzcnt(mask);
                mask &= mask - 1;
                if (cond[offset + bit] != 0)
                {
                    hits |= 1ul << bit;
                }
            }

            EmitWord(channel, hits, offset, null, ref maker);
        }

#if NET
        public void EmitFromNonZero<T>(Channel<T> channel, int* values, int start, int count, delegate* unmanaged<int, int, T> make) where T : unmanaged
        {
            var maker = new PointerMaker<T>(make);
            EmitFromNonZero(channel, values, start, count, ref maker);
        }
#endif

        public void EmitFromNonZero<T, TMaker>(Channel<T> channel, int* values, int start, int count, ref TMaker maker)
            where T : unmanaged
            where TMaker : struct, IEventMaker<T>
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            var row = start;
            var remaining = count;
            while (remaining >= 64)
            {
                EmitWord(channel, Simd.IntNonZeroWord(values, row), row, values, ref maker);
                row += 64;
                remaining -= 64;
            }

            ulong tail = 0;
            for (var i = 0; i < remaining; i++)
            {
                if (values[row + i] != 0)
                {
                    tail |= 1ul << i;
                }
            }

            EmitWord(channel, tail, row, values, ref maker);
        }

#if NET
        public void EmitFromNonZero<T>(Channel<T> channel, int* values, ulong enabled, int offset, delegate* unmanaged<int, int, T> make) where T : unmanaged
        {
            var maker = new PointerMaker<T>(make);
            EmitFromNonZero(channel, values, enabled, offset, ref maker);
        }
#endif

        public void EmitFromNonZero<T, TMaker>(Channel<T> channel, int* values, ulong enabled, int offset, ref TMaker maker)
            where T : unmanaged
            where TMaker : struct, IEventMaker<T>
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            var mask = enabled;
            ulong hits = 0;
            while (mask != 0)
            {
                var bit = Bits.Tzcnt(mask);
                mask &= mask - 1;
                if (values[offset + bit] != 0)
                {
                    hits |= 1ul << bit;
                }
            }

            EmitWord(channel, hits, offset, values, ref maker);
        }

#if NET
        public static void WalkNonZero(int* values, ulong enabled, int offset, delegate* unmanaged<int, int, void> visit)
        {
            var mask = enabled;
            while (mask != 0)
            {
                var bit = Bits.Tzcnt(mask);
                mask &= mask - 1;
                var row = offset + bit;
                var value = values[row];
                if (value != 0)
                {
                    visit(row, value);
                }
            }
        }
#endif

        const uint StateMagic = 0x56455653;

        /// <summary>
        /// Saves both halves of the double buffer (this frame's writes and the published frame being
        /// drained) with every cursor. Channels are cold setup, not state: the loading streams must
        /// have created the same channels. Warm saves are managed-allocation-free.
        /// </summary>
        public void SaveState(SvState state)
        {
            state.BeginWrite();
            var codec = new SaveCodec(state);
            VisitState(ref codec);
        }

        public void LoadState(SvState state)
        {
            state.BeginRead();
            var codec = new LoadCodec(state);
            VisitState(ref codec);
            state.EndRead();
        }

        internal void VisitState<TCodec>(ref TCodec codec) where TCodec : struct, IStateCodec
        {
            if (codec.Reading)
            {
                EndUndoWindow();
            }

            codec.Header(StateMagic, ((long)writerCount << 32) | (uint)nextTypeId, "event streams");
            codec.Value(ref active);
            for (var w = 0; w < writerCount; w++)
            {
                var written = writeOffset[w * OffsetStride];
                var published = readOffset[w];
                var cursor = readCursorOffset[w];
                codec.Value(ref written);
                codec.Value(ref published);
                codec.Value(ref cursor);
                if (codec.Reading)
                {
                    if (written < 0 || published < 0)
                    {
                        throw new System.IO.InvalidDataException("sv: event stream state carries negative offsets");
                    }

                    readCursorOffset[w] = cursor;
                    EnsureArena(w * 2 + active, written);
                    EnsureArena(w * 2 + (1 - active), published);
                    writeOffset[w * OffsetStride] = written;
                    readOffset[w] = published;
                }

                codec.Bytes(arenas[w * 2 + active], written);
                codec.Bytes(arenas[w * 2 + (1 - active)], published);
            }
        }

        void EnsureArena(int index, int bytes)
        {
            if (arenaCapacity[index] >= bytes)
            {
                return;
            }

            if (bytes > int.MaxValue / 2)
            {
                throw new System.IO.InvalidDataException("sv: event stream state exceeds 1 GiB per writer");
            }

            var capacity = arenaCapacity[index];
            while (capacity < bytes)
            {
                capacity *= 2;
            }

            arenas[index] = (byte*)Memory.Realloc(arenas[index], (nuint)capacity);
            arenaCapacity[index] = capacity;
            arenaPeak[index] = capacity;
        }

        internal void BeginUndoWindow(int frames)
        {
            EndUndoWindow();
            Undo = new UndoLog(frames);
            undoActiveHalves = new int[frames];
            Undo.OpenFrame();
            CaptureUndoScalars();
        }

        internal void BeginUndoFrame()
        {
            var undo = Undo;
            if (undo == null)
            {
                return;
            }

            var previousBottom = undo.Bottom;
            undo.OpenFrame();
            CaptureUndoScalars();
            if (undo.Bottom > previousBottom)
            {
                for (var i = arenaReplaces.Count - 1; i >= 0; i--)
                {
                    if (arenaReplaces[i].Frame < undo.Bottom)
                    {
                        Memory.Free((void*)arenaReplaces[i].Old);
                        arenaReplaces.RemoveAt(i);
                    }
                }
            }
        }

        void CaptureUndoScalars()
        {
            undoActiveHalves![Undo!.Top % Undo.Window] = active;
        }

        internal void EndUndoWindow()
        {
            Undo?.Dispose();
            Undo = null;
            undoActiveHalves = null;
            for (var i = arenaReplaces.Count - 1; i >= 0; i--)
            {
                Memory.Free((void*)arenaReplaces[i].Old);
            }

            arenaReplaces.Clear();
        }

        internal void RollbackTo(int target)
        {
            var undo = Undo;
            if (undo == null)
            {
                throw new InvalidOperationException("sv: no undo window is open");
            }

            undo.UndoFrames(undo.Top, target);
            undo.TruncateTo(target);
            active = undoActiveHalves![target % undo.Window];
            for (var i = arenaReplaces.Count - 1; i >= 0; i--)
            {
                var replace = arenaReplaces[i];
                if (replace.Frame < target)
                {
                    continue;
                }

                Memory.Free((void*)replace.New);
                arenas[replace.Index] = (byte*)replace.Old;
                arenaCapacity[replace.Index] = replace.OldCapacity;
                arenaReplaces.RemoveAt(i);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            EndUndoWindow();
            for (var w = 0; w < writerCount; w++)
            {
                Memory.Free(arenas[w * 2]);
                Memory.Free(arenas[w * 2 + 1]);
            }

            Memory.Free(arenas);
            Memory.Free(arenaCapacity);
            Memory.Free(arenaPeak);
            Memory.Free(readOffset);
            Memory.Free(readCursorOffset);
            Memory.FreeAligned(locks);
            disposed = true;
        }
    }
}
