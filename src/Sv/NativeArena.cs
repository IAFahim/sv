using System;
using System.Runtime.InteropServices;

namespace Sv
{
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

        public T* AllocAlignedZeroed<T>(int count) where T : unmanaged
        {
            var bytes = (nuint)count * (nuint)sizeof(T);
            live += (long)bytes;
            var alignment = bytes >= (nuint)2 * 1024 * 1024 ? (nuint)2 * 1024 * 1024 : 64;
            var block = (T*)NativeMemory.AlignedAlloc(bytes, alignment);
            NativeMemory.Clear(block, bytes);
            AdviseHugePages(block, bytes);
            return block;
        }

        public void FreeAligned<T>(T* old, int count) where T : unmanaged
        {
            if (old == null)
            {
                return;
            }

            live -= (long)count * sizeof(T);
            NativeMemory.AlignedFree(old);
        }

        [DllImport("libc", SetLastError = true)]
        static extern int madvise(void* address, nuint length, int advice);

        static void AdviseHugePages(void* block, nuint bytes)
        {
            if (OperatingSystem.IsLinux() && bytes >= (nuint)2 * 1024 * 1024)
            {
                _ = madvise(block, bytes, 14);
            }
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

        public void FreeBytesAligned(void* old, int bytes)
        {
            if (old == null)
            {
                return;
            }

            live -= bytes;
            NativeMemory.AlignedFree(old);
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
}
