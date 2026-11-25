using System;
using System.Runtime.InteropServices;

namespace Sv.Columns
{
    internal static unsafe class Native
    {
        public const int Alignment = 64;

        public static void* Zeroed(long bytes)
        {
            var size = (nuint)Math.Max(bytes, 1L);
#if NET
            var memory = NativeMemory.AlignedAlloc(size, Alignment);
            NativeMemory.Clear(memory, size);
            return memory;
#else
            var raw = Marshal.AllocHGlobal((IntPtr)((long)size + Alignment + sizeof(IntPtr)));
            var aligned = ((long)raw + sizeof(IntPtr) + Alignment - 1) & ~(long)(Alignment - 1);
            ((IntPtr*)aligned)[-1] = raw;
            new Span<byte>((void*)aligned, (int)size).Clear();
            return (void*)aligned;
#endif
        }

        public static T* Zeroed<T>(long count) where T : unmanaged => (T*)Zeroed(count * sizeof(T));

        public static void Clear(void* memory, long bytes)
        {
            var at = (byte*)memory;
            while (bytes > 0)
            {
                var part = (int)Math.Min(bytes, int.MaxValue);
                new Span<byte>(at, part).Clear();
                at += part;
                bytes -= part;
            }
        }

        public static void Free(void* memory)
        {
            if (memory == null)
            {
                return;
            }
#if NET
            NativeMemory.AlignedFree(memory);
#else
            Marshal.FreeHGlobal(((IntPtr*)memory)[-1]);
#endif
        }
    }
}
