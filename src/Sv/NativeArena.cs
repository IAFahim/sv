using System;
using System.Runtime.InteropServices;

namespace Sv;

internal sealed unsafe class NativeArena : IDisposable
{
    private long live;

    private bool disposed;

    public long Live => live;

    public T* Alloc<T>(int count) where T : unmanaged
    {
        live += (long)count * sizeof(T);
        return (T*)NativeMemory.Alloc((nuint)count * (nuint)sizeof(T));
    }

    public void* AllocBytes(int bytes)
    {
        live += bytes;
        return NativeMemory.Alloc((nuint)bytes);
    }

    public void* AllocZeroedBytes(int bytes)
    {
        live += bytes;
        return NativeMemory.AllocZeroed((nuint)bytes);
    }

    public T* AllocZeroed<T>(int count) where T : unmanaged
    {
        live += (long)count * sizeof(T);
        return (T*)NativeMemory.AllocZeroed((nuint)count * (nuint)sizeof(T));
    }

    public T* Realloc<T>(T* old, int oldCount, int newCount) where T : unmanaged
    {
        live += (long)(newCount - oldCount) * sizeof(T);
        return (T*)NativeMemory.Realloc(old, (nuint)newCount * (nuint)sizeof(T));
    }

    public void Free<T>(T* old, int count) where T : unmanaged
    {
        if (old == null)
        {
            return;
        }

        live -= (long)count * sizeof(T);
        NativeMemory.Free(old);
    }

    public void FreeBytes(void* old, int bytes)
    {
        if (old == null)
        {
            return;
        }

        live -= bytes;
        NativeMemory.Free(old);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
    }
}
