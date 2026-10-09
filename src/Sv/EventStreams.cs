using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sv;

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

    private readonly int* writeOffset;

    private readonly int* readOffset;

    private readonly int* readCursorOffset;

    private int active;

    private const int LockStride = 8;

    private const int BatchBytes = 256;

    private const int OffsetStride = LockStride * 2;

    private readonly nint* locks;

    private int nextTypeId;

    private bool disposed;

    EventStreams(int writerCount, int bytesPerWriter)
    {
        this.writerCount = writerCount;
        arenas = (byte**)NativeMemory.Alloc((nuint)writerCount * 2 * (nuint)sizeof(byte*), (nuint)sizeof(byte*));
        arenaCapacity = (int*)NativeMemory.AllocZeroed((nuint)writerCount * 2 * sizeof(int), sizeof(int));
        readOffset = (int*)NativeMemory.AllocZeroed((nuint)writerCount * sizeof(int), sizeof(int));
        readCursorOffset = (int*)NativeMemory.AllocZeroed((nuint)writerCount * sizeof(int), sizeof(int));
        locks = (nint*)NativeMemory.AlignedAlloc((nuint)(writerCount * LockStride * sizeof(nint)), 64);
        NativeMemory.Clear(locks, (nuint)(writerCount * LockStride * sizeof(nint)));
        writeOffset = (int*)(locks + 1);
        var rounded = Round(bytesPerWriter);
        for (var w = 0; w < writerCount; w++)
        {
            for (var b = 0; b < 2; b++)
            {
                var index = w * 2 + b;
                arenas[index] = (byte*)NativeMemory.Alloc((nuint)rounded, (nuint)sizeof(nint));
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

        var index = writer * 2 + active;
        var at = writeOffset[writer * OffsetStride];
        if (at + need > arenaCapacity[index])
        {
            var capacity = arenaCapacity[index];
            while (capacity < at + need)
            {
                capacity *= 2;
            }

            arenas[index] = (byte*)NativeMemory.Realloc(arenas[index], (nuint)capacity);
            arenaCapacity[index] = capacity;
        }

        var dst = arenas[index] + at;
        for (var i = 0; i < count; i++)
        {
            *(ushort*)dst = id;
            *(ushort*)(dst + 2) = 0;
            *(int*)(dst + 4) = size;
            *(T*)(dst + HeaderBytes) = payload[i];
            dst += stride;
        }

        writeOffset[writer * OffsetStride] = at + need;
        Volatile.Write(ref gate, 0);
    }

    void EmitWord<T>(Channel<T> channel, ulong mask, int rowBase, int* values, delegate* unmanaged<int, int, T> make) where T : unmanaged
    {
        if (mask == 0)
        {
            return;
        }

        if (sizeof(T) > BatchBytes)
        {
            while (mask != 0)
            {
                var bit = System.Numerics.BitOperations.TrailingZeroCount(mask);
                mask &= mask - 1;
                var row = rowBase + bit;
                var evt = make(row, values == null ? 1 : values[row]);
                Append(channel.Id, channel.Writer, &evt);
            }

            return;
        }

        var batch = stackalloc T[64];
        var count = 0;
        while (mask != 0)
        {
            var bit = System.Numerics.BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;
            var row = rowBase + bit;
            batch[count++] = make(row, values == null ? 1 : values[row]);
        }

        AppendBatch(channel.Id, channel.Writer, batch, count);
    }

    public void Swap()
    {
        for (var w = 0; w < writerCount; w++)
        {
            readOffset[w] = writeOffset[w * OffsetStride];
            writeOffset[w * OffsetStride] = 0;
            readCursorOffset[w] = 0;
        }

        active ^= 1;
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

    public void EmitFromMask<T>(Channel<T> channel, ulong enabled, int offset, delegate* unmanaged<int, int, T> make) where T : unmanaged
    {
        if (channel == null)
        {
            throw new ArgumentNullException(nameof(channel));
        }

        EmitWord(channel, enabled, offset, null, make);
    }

    public void EmitFromBools<T>(Channel<T> channel, byte* cond, int start, int count, delegate* unmanaged<int, int, T> make) where T : unmanaged
    {
        if (channel == null)
        {
            throw new ArgumentNullException(nameof(channel));
        }

        var row = start;
        var remaining = count;
        while (remaining >= 64)
        {
            EmitWord(channel, Simd.BoolNonZeroWord(cond, row), row, null, make);
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

        EmitWord(channel, tail, row, null, make);
    }

    public void EmitFromBools<T>(Channel<T> channel, byte* cond, ulong enabled, int offset, delegate* unmanaged<int, int, T> make) where T : unmanaged
    {
        if (channel == null)
        {
            throw new ArgumentNullException(nameof(channel));
        }

        var mask = enabled;
        ulong hits = 0;
        while (mask != 0)
        {
            var bit = System.Numerics.BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;
            if (cond[offset + bit] != 0)
            {
                hits |= 1ul << bit;
            }
        }

        EmitWord(channel, hits, offset, null, make);
    }

    public void EmitFromNonZero<T>(Channel<T> channel, int* values, int start, int count, delegate* unmanaged<int, int, T> make) where T : unmanaged
    {
        if (channel == null)
        {
            throw new ArgumentNullException(nameof(channel));
        }

        var row = start;
        var remaining = count;
        while (remaining >= 64)
        {
            EmitWord(channel, Simd.IntNonZeroWord(values, row), row, values, make);
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

        EmitWord(channel, tail, row, values, make);
    }

    public void EmitFromNonZero<T>(Channel<T> channel, int* values, ulong enabled, int offset, delegate* unmanaged<int, int, T> make) where T : unmanaged
    {
        if (channel == null)
        {
            throw new ArgumentNullException(nameof(channel));
        }

        var mask = enabled;
        ulong hits = 0;
        while (mask != 0)
        {
            var bit = System.Numerics.BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;
            if (values[offset + bit] != 0)
            {
                hits |= 1ul << bit;
            }
        }

        EmitWord(channel, hits, offset, values, make);
    }

    public static void WalkNonZero(int* values, ulong enabled, int offset, delegate* unmanaged<int, int, void> visit)
    {
        var mask = enabled;
        while (mask != 0)
        {
            var bit = System.Numerics.BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;
            var row = offset + bit;
            var value = values[row];
            if (value != 0)
            {
                visit(row, value);
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        for (var w = 0; w < writerCount; w++)
        {
            NativeMemory.Free(arenas[w * 2]);
            NativeMemory.Free(arenas[w * 2 + 1]);
        }

        NativeMemory.Free(arenas);
        NativeMemory.Free(arenaCapacity);
        NativeMemory.Free(readOffset);
        NativeMemory.Free(readCursorOffset);
        NativeMemory.AlignedFree(locks);
        disposed = true;
    }
}
