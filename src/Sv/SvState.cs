using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Sv;

/// <summary>
/// Reusable native buffer holding a saved state (<see cref="Board.SaveState"/>,
/// <see cref="InputState.SaveState"/>, <see cref="EventStreams.SaveState"/>). Grows on demand and
/// never shrinks, so warm save/load is managed-allocation-free. Owned by the caller.
/// </summary>
public sealed unsafe class SvState : IDisposable
{
    byte* data;

    int capacity;

    int length;

    int cursor;

    bool disposed;

    public int Length => length;

    public ReadOnlySpan<byte> Bytes => new(data, length);

    internal void BeginWrite()
    {
        length = 0;
    }

    internal void BeginRead()
    {
        cursor = 0;
    }

    internal void EndRead()
    {
        if (cursor != length)
        {
            throw new InvalidDataException($"sv: state has {length - cursor} unread bytes — shape mismatch");
        }
    }

    internal void Write(void* source, long bytes)
    {
        if (bytes == 0)
        {
            return;
        }

        var need = length + bytes;
        if (need > int.MaxValue)
        {
            throw new InvalidOperationException("sv: state exceeds 2 GiB");
        }

        if (need > capacity)
        {
            var next = Math.Max(capacity * 2L, Math.Max(need, 4096));
            capacity = (int)Math.Min(next, int.MaxValue);
            data = data == null ? (byte*)NativeMemory.Alloc((nuint)capacity) : (byte*)NativeMemory.Realloc(data, (nuint)capacity);
        }

        Buffer.MemoryCopy(source, data + length, capacity - length, bytes);
        length += (int)bytes;
    }

    internal void Write<T>(T value) where T : unmanaged
    {
        Write(&value, sizeof(T));
    }

    internal void Read(void* destination, long bytes)
    {
        if (bytes == 0)
        {
            return;
        }

        if (cursor + bytes > length)
        {
            throw new InvalidDataException("sv: state truncated — shape mismatch");
        }

        Buffer.MemoryCopy(data + cursor, destination, bytes, bytes);
        cursor += (int)bytes;
    }

    internal T Read<T>() where T : unmanaged
    {
        T value;
        Read(&value, sizeof(T));
        return value;
    }

    internal void Expect(uint magic, long shape, string what)
    {
        if (Read<uint>() != magic || Read<long>() != shape)
        {
            throw new InvalidDataException($"sv: state does not hold a {what} of this shape");
        }
    }

    internal void Header(uint magic, long shape)
    {
        Write(magic);
        Write(shape);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        if (data != null)
        {
            NativeMemory.Free(data);
        }

        disposed = true;
    }
}
