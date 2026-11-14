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
            return (T*)Memory.Alloc((nuint)count * (nuint)sizeof(T));
        }

        public void* AllocBytes(int bytes)
        {
            live += bytes;
            return Memory.Alloc((nuint)bytes);
        }

        public void* AllocZeroedBytes(int bytes)
        {
            live += bytes;
            return Memory.AllocZeroed((nuint)bytes);
        }

        public T* AllocZeroed<T>(int count) where T : unmanaged
        {
            live += (long)count * sizeof(T);
            return (T*)Memory.AllocZeroed((nuint)count * (nuint)sizeof(T));
        }

        public T* AllocAlignedZeroed<T>(int count) where T : unmanaged
        {
            var bytes = (nuint)count * (nuint)sizeof(T);
            live += (long)bytes;
#if NET
            var alignment = bytes >= (nuint)2 * 1024 * 1024 ? (nuint)2 * 1024 * 1024 : 64;
            var block = (T*)Memory.AllocAligned(bytes, alignment);
            Memory.Clear(block, bytes);
            AdviseHugePages(block, bytes);
#else
            var block = (T*)Memory.AllocAligned(bytes, 64);
            Memory.Clear(block, bytes);
#endif
            return block;
        }

        public void FreeAligned<T>(T* old, int count) where T : unmanaged
        {
            if (old == null)
            {
                return;
            }

            live -= (long)count * sizeof(T);
            Memory.FreeAligned(old);
        }

#if NET
        [DllImport("libc", SetLastError = true)]
        static extern int madvise(void* address, nuint length, int advice);

        static void AdviseHugePages(void* block, nuint bytes)
        {
            if (OperatingSystem.IsLinux() && bytes >= (nuint)2 * 1024 * 1024)
            {
                _ = madvise(block, bytes, 14);
            }
        }
#endif

        public T* Realloc<T>(T* old, int oldCount, int newCount) where T : unmanaged
        {
            live += (long)(newCount - oldCount) * sizeof(T);
            return (T*)Memory.Realloc(old, (nuint)newCount * (nuint)sizeof(T));
        }

        public void Free<T>(T* old, int count) where T : unmanaged
        {
            if (old == null)
            {
                return;
            }

            live -= (long)count * sizeof(T);
            Memory.Free(old);
        }

        public void FreeBytes(void* old, int bytes)
        {
            if (old == null)
            {
                return;
            }

            live -= bytes;
            Memory.Free(old);
        }

        public void FreeBytesAligned(void* old, int bytes)
        {
            if (old == null)
            {
                return;
            }

            live -= bytes;
            Memory.FreeAligned(old);
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
