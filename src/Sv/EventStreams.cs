using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sv;

public sealed unsafe class Channel<T> where T : unmanaged
{
    internal EventStreams Owner = null!;

    internal ushort Id;

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

    private nint appendLock;

    private int nextTypeId;

    private bool disposed;

    EventStreams(int writerCount, int bytesPerWriter)
    {
        this.writerCount = writerCount;
        arenas = (byte**)NativeMemory.Alloc((nuint)writerCount * 2 * (nuint)sizeof(byte*), (nuint)sizeof(byte*));
        arenaCapacity = (int*)NativeMemory.AllocZeroed((nuint)writerCount * 2 * (nuint)sizeof(int), (nuint)sizeof(int));
        writeOffset = (int*)NativeMemory.AllocZeroed((nuint)writerCount * (nuint)sizeof(int), (nuint)sizeof(int));
        readOffset = (int*)NativeMemory.AllocZeroed((nuint)writerCount * (nuint)sizeof(int), (nuint)sizeof(int));
        readCursorOffset = (int*)NativeMemory.AllocZeroed((nuint)writerCount * (nuint)sizeof(int), (nuint)sizeof(int));
        appendLock = 0;
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
        var size = sizeof(T);
        var need = HeaderBytes + Round(size);
        var index = writer * 2 + active;
        int at;
        while (true)
        {
            at = writeOffset[writer];
            if (at + need > arenaCapacity[index])
            {
                Grow(writer, index, at + need);
                continue;
            }

            if (Interlocked.CompareExchange(ref appendLock, 1, 0) != 0)
            {
                continue;
            }

            if (writeOffset[writer] != at)
            {
                Volatile.Write(ref appendLock, 0);
                continue;
            }

            break;
        }

        var dst = arenas[index] + at;
        *(ushort*)dst = id;
        *(ushort*)(dst + 2) = 0;
        *(int*)(dst + 4) = size;
        *(T*)(dst + HeaderBytes) = *payload;
        writeOffset[writer] = at + need;
        Volatile.Write(ref appendLock, 0);
    }

    void Grow(int writer, int index, int needed)
    {
        while (Interlocked.CompareExchange(ref appendLock, 1, 0) != 0)
        {
        }

        if (arenaCapacity[index] >= needed)
        {
            Volatile.Write(ref appendLock, 0);
            return;
        }

        var capacity = arenaCapacity[index];
        while (capacity < needed)
        {
            capacity *= 2;
        }

        arenas[index] = (byte*)NativeMemory.Realloc(arenas[index], (nuint)capacity);
        arenaCapacity[index] = capacity;
        Volatile.Write(ref appendLock, 0);
    }

    public void Swap()
    {
        for (var w = 0; w < writerCount; w++)
        {
            readOffset[w] = writeOffset[w];
            writeOffset[w] = 0;
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

        var mask = enabled;
        while (mask != 0)
        {
            var bit = System.Numerics.BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;
            var evt = make(offset + bit, 1);
            Append(channel.Id, channel.Writer, &evt);
        }
    }

    public void EmitFromBools<T>(Channel<T> channel, byte* cond, int start, int count, delegate* unmanaged<int, int, T> make) where T : unmanaged
    {
        if (channel == null)
        {
            throw new ArgumentNullException(nameof(channel));
        }

        for (var i = 0; i < count; i++)
        {
            var row = start + i;
            if (cond[row] != 0)
            {
                var evt = make(row, 1);
                Append(channel.Id, channel.Writer, &evt);
            }
        }
    }

    public void EmitFromBools<T>(Channel<T> channel, byte* cond, ulong enabled, int offset, delegate* unmanaged<int, int, T> make) where T : unmanaged
    {
        if (channel == null)
        {
            throw new ArgumentNullException(nameof(channel));
        }

        var mask = enabled;
        while (mask != 0)
        {
            var bit = System.Numerics.BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;
            var row = offset + bit;
            if (cond[row] != 0)
            {
                var evt = make(row, 1);
                Append(channel.Id, channel.Writer, &evt);
            }
        }
    }

    public void EmitFromNonZero<T>(Channel<T> channel, int* values, int start, int count, delegate* unmanaged<int, int, T> make) where T : unmanaged
    {
        if (channel == null)
        {
            throw new ArgumentNullException(nameof(channel));
        }

        for (var i = 0; i < count; i++)
        {
            var row = start + i;
            var value = values[row];
            if (value != 0)
            {
                var evt = make(row, value);
                Append(channel.Id, channel.Writer, &evt);
            }
        }
    }

    public void EmitFromNonZero<T>(Channel<T> channel, int* values, ulong enabled, int offset, delegate* unmanaged<int, int, T> make) where T : unmanaged
    {
        if (channel == null)
        {
            throw new ArgumentNullException(nameof(channel));
        }

        var mask = enabled;
        while (mask != 0)
        {
            var bit = System.Numerics.BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;
            var row = offset + bit;
            var value = values[row];
            if (value != 0)
            {
                var evt = make(row, value);
                Append(channel.Id, channel.Writer, &evt);
            }
        }
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
        NativeMemory.Free(writeOffset);
        NativeMemory.Free(readOffset);
        NativeMemory.Free(readCursorOffset);
        disposed = true;
    }
}
