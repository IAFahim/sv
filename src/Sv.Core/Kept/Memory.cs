using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sv
{
    public static unsafe class Memory
    {
#if NET
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* Alloc(nuint bytes) => NativeMemory.Alloc(bytes);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* Alloc(nuint count, nuint size) => NativeMemory.Alloc(count, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* AllocZeroed(nuint bytes) => NativeMemory.AllocZeroed(bytes);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* AllocZeroed(nuint count, nuint size) => NativeMemory.AllocZeroed(count, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* Realloc(void* block, nuint bytes) => NativeMemory.Realloc(block, bytes);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Free(void* block) => NativeMemory.Free(block);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* AllocAligned(nuint bytes, nuint alignment) => NativeMemory.AlignedAlloc(bytes, alignment);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FreeAligned(void* block) => NativeMemory.AlignedFree(block);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Clear(void* block, nuint bytes) => NativeMemory.Clear(block, bytes);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Copy(void* source, void* destination, nuint bytes) => NativeMemory.Copy(source, destination, bytes);
#else
        public static void* Alloc(nuint bytes) => (void*)Marshal.AllocHGlobal((IntPtr)(long)(bytes == 0 ? 1 : bytes));

        public static void* Alloc(nuint count, nuint size) => Alloc(checked(count * size));

        public static void* AllocZeroed(nuint bytes)
        {
            var block = Alloc(bytes);
            Clear(block, bytes);
            return block;
        }

        public static void* AllocZeroed(nuint count, nuint size) => AllocZeroed(checked(count * size));

        public static void* Realloc(void* block, nuint bytes)
        {
            if (block == null)
            {
                return Alloc(bytes);
            }

            return (void*)Marshal.ReAllocHGlobal((IntPtr)block, (IntPtr)(long)(bytes == 0 ? 1 : bytes));
        }

        public static void Free(void* block)
        {
            if (block != null)
            {
                Marshal.FreeHGlobal((IntPtr)block);
            }
        }

        public static void* AllocAligned(nuint bytes, nuint alignment)
        {
            if (alignment < (nuint)sizeof(void*) || (alignment & (alignment - 1)) != 0)
            {
                throw new ArgumentException("sv: alignment must be a power of two of at least pointer size", nameof(alignment));
            }

            var raw = (byte*)Alloc(checked(bytes + alignment + (nuint)sizeof(void*)));
            var aligned = (byte*)(((nuint)raw + (nuint)sizeof(void*) + alignment - 1) & ~(alignment - 1));
            ((void**)aligned)[-1] = raw;
            return aligned;
        }

        public static void FreeAligned(void* block)
        {
            if (block != null)
            {
                Free(((void**)block)[-1]);
            }
        }

        public static void Clear(void* block, nuint bytes)
        {
            var at = (byte*)block;
            while (bytes > 0)
            {
                var part = bytes > int.MaxValue ? int.MaxValue : (int)bytes;
                new Span<byte>(at, part).Clear();
                at += part;
                bytes -= (nuint)part;
            }
        }

        public static void Copy(void* source, void* destination, nuint bytes) => Buffer.MemoryCopy(source, destination, (long)bytes, (long)bytes);
#endif
    }
}
