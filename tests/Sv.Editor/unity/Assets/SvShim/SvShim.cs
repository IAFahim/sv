// Sv.Editor test-project BCL shim (issue 0114, test tooling — managed freely).
// Supplies the surfaces the Unity 6000.7.0b5 editor-script compile lacks (empirical probes,
// tests/Sv.Editor/receipt/unity-probe.log + unity-measure.log):
//   - System.Runtime.Intrinsics Vector128/Vector256 (CS0234/CS0246 in Simd.cs / Simd.Chunk.cs).
//     IsHardwareAccelerated is false, so sv pins to its scalar kernel twins — the execution path
//     the Burst leg (issue 0111) proved bit-identical; the vector members below are a faithful
//     scalar re-expression kept for compilation.
//   - System.Runtime.CompilerServices.IsExternalInit (CS0518, Schema.cs init accessors).
//   - System.Numerics.BitOperations (CS0122 internal in Unity's profile — matches 0111's audit).
//   - System.Runtime.InteropServices.NativeMemory (CS0103 — libc malloc family).
//   - OperatingSystem.IsLinux (CS0117 — the type exists in the profile, the member does not;
//     rewritten to SvUnityBits.IsLinux in NativeArena.cs).
//   - System.SvUnityBits: twins for members Unity's profile lacks ON EXISTING BCL types, which
//     no compilation unit can supply: BitConverter.SingleToUInt32Bits/UInt32BitsToSingle
//     (CS0117, bit-identical via the profile's SingleToInt32Bits/Int32BitsToSingle),
//     Environment.TickCount64 (CS0117, Bulk.cs), ObjectDisposedException.ThrowIf (CS0117,
//     Drift.cs), and nint Interlocked/Volatile atomics (CS0452, EventStreams.cs). The SvCore
//     COPIES get those five member names rewritten to SvUnityBits.* by run-editor.sh (recorded).
// Not used by, and never merged into, src/Sv or packages/com.sv.unity.

using System.Runtime.InteropServices;

namespace System
{
    public static class SvUnityBits
    {
        public static bool IsLinux() => true;

        public static uint SingleToUInt32Bits(float value) => (uint)BitConverter.SingleToInt32Bits(value);

        public static float UInt32BitsToSingle(uint bits) => BitConverter.Int32BitsToSingle(unchecked((int)bits));

        public static long TickCount64 => (long)Environment.TickCount;

        public static void ThrowIf(bool disposed, object instance)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(instance?.GetType().FullName);
            }
        }

        public static nint CompareExchangeNint(ref nint location, nint value, nint comparand)
        {
            return (nint)System.Threading.Interlocked.CompareExchange(ref AsLong(ref location), value, comparand);
        }

        public static void WriteNint(ref nint location, nint value)
        {
            System.Threading.Volatile.Write(ref AsLong(ref location), value);
        }

        static ref long AsLong(ref nint value) => ref System.Runtime.CompilerServices.Unsafe.As<nint, long>(ref value);
    }
}

namespace System.Numerics
{
    public static class BitOperations
    {
        public static int TrailingZeroCount(uint value)
        {
            if (value == 0)
            {
                return 32;
            }

            return TrailingZeroCount((ulong)value);
        }

        public static int TrailingZeroCount(int value) => TrailingZeroCount((uint)value);

        public static int TrailingZeroCount(ulong value)
        {
            if (value == 0)
            {
                return 64;
            }

            var count = 0;
            if ((value & 0xFFFFFFFFul) == 0) { count += 32; value >>= 32; }
            if ((value & 0xFFFFul) == 0) { count += 16; value >>= 16; }
            if ((value & 0xFFul) == 0) { count += 8; value >>= 8; }
            if ((value & 0xFul) == 0) { count += 4; value >>= 4; }
            if ((value & 0x3ul) == 0) { count += 2; value >>= 2; }
            if ((value & 0x1ul) == 0) { count += 1; }
            return count;
        }

        public static int TrailingZeroCount(long value) => TrailingZeroCount((ulong)value);

        public static int LeadingZeroCount(uint value)
        {
            var count = 0;
            if (value == 0)
            {
                return 32;
            }

            if (value < 1u << 16) { count += 16; value <<= 16; }
            if (value < 1u << 24) { count += 8; value <<= 8; }
            if (value < 1u << 28) { count += 4; value <<= 4; }
            if (value < 1u << 30) { count += 2; value <<= 2; }
            if (value < 1u << 31) { count += 1; }
            return count;
        }

        public static int LeadingZeroCount(int value) => LeadingZeroCount((uint)value);

        public static int LeadingZeroCount(ulong value)
        {
            if (value == 0)
            {
                return 64;
            }

            return value >> 32 != 0 ? LeadingZeroCount((uint)(value >> 32)) : 32 + LeadingZeroCount((uint)value);
        }

        public static int LeadingZeroCount(long value) => LeadingZeroCount((ulong)value);

        public static uint RotateLeft(uint value, int offset) => (value << offset) | (value >> (32 - offset));

        public static int RotateLeft(int value, int offset) => unchecked((int)RotateLeft((uint)value, offset));

        public static ulong RotateLeft(ulong value, int offset) => offset == 0 ? value : (value << offset) | (value >> (64 - offset));

        public static long RotateLeft(long value, int offset) => unchecked((long)RotateLeft((ulong)value, offset));
    }
}

namespace System.Runtime.InteropServices
{
    public static unsafe class NativeMemory
    {
        public static void* Alloc(nuint byteCount) => malloc(byteCount);

        public static void* Alloc(nuint elementCount, nuint elementSize) => malloc(elementCount * elementSize);

        public static void* AllocZeroed(nuint byteCount) => calloc(byteCount, 1);

        public static void* AllocZeroed(nuint elementCount, nuint elementSize) => calloc(elementCount, elementSize);

        public static void* Realloc(void* ptr, nuint byteCount) => realloc(ptr, byteCount);

        public static void Free(void* ptr) => free(ptr);

        public static void Clear(void* ptr, nuint byteCount) => memset(ptr, 0, byteCount);

        public static void Copy(void* destination, void* source, nuint byteCount) => memcpy(destination, source, byteCount);

        public static void* AlignedAlloc(nuint byteCount, nuint alignment)
        {
            void* ptr = null;
            return posix_memalign(&ptr, alignment, byteCount) == 0 ? ptr : null;
        }

        public static void AlignedFree(void* ptr) => free(ptr);

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl)]
        static extern void* malloc(nuint size);

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl)]
        static extern void* calloc(nuint count, nuint size);

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl)]
        static extern void* realloc(void* ptr, nuint size);

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl)]
        static extern void free(void* ptr);

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl)]
        static extern void* memset(void* ptr, int value, nuint size);

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl)]
        static extern void* memcpy(void* destination, void* source, nuint size);

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl)]
        static extern int posix_memalign(void** memptr, nuint alignment, nuint size);
    }
}

namespace System.Runtime.CompilerServices
{
    public static class IsExternalInit
    {
    }
}

namespace System.Runtime.Intrinsics.X86
{
    internal static class X86NamespaceMarker
    {
    }
}

namespace System.Runtime.Intrinsics
{
    public unsafe struct Vector128<T> where T : unmanaged
    {
        long lo;
        long hi;

        public static Vector128<T> Zero => default;

        public static Vector128<T> One => CreateLane((byte)1);

        public static Vector128<T> operator &(Vector128<T> left, Vector128<T> right) => Bitwise(left, right, (a, b) => a & b);

        public static Vector128<T> operator |(Vector128<T> left, Vector128<T> right) => Bitwise(left, right, (a, b) => a | b);

        public static Vector128<T> operator ^(Vector128<T> left, Vector128<T> right) => Bitwise(left, right, (a, b) => a ^ b);

        public static Vector128<T> operator ~(Vector128<T> value)
        {
            value.lo = ~value.lo;
            value.hi = ~value.hi;
            return value;
        }

        public static Vector128<T> operator +(Vector128<T> left, Vector128<T> right) => AddLanes(left, right, (a, b) => a + b);

        public static Vector128<T> operator -(Vector128<T> left, Vector128<T> right) => AddLanes(left, right, (a, b) => a - b);

        public static bool operator ==(Vector128<T> left, Vector128<T> right) => left.lo == right.lo && left.hi == right.hi;

        public static bool operator !=(Vector128<T> left, Vector128<T> right) => !(left == right);

        public override bool Equals(object obj) => obj is Vector128<T> other && this == other;

        public override int GetHashCode() => (int)(lo ^ hi);

        internal void ToBytes(byte* buffer)
        {
            *(long*)buffer = lo;
            *(long*)(buffer + 8) = hi;
        }

        internal Vector128(void* bytes)
        {
            lo = *(long*)bytes;
            hi = *(long*)((byte*)bytes + 8);
        }

        internal static Vector128<T> FromBytes(void* bytes) => new Vector128<T>(bytes);

        static Vector128<T> CreateLane(byte fill)
        {
            var size = sizeof(T);
            var lanes = 16 / size;
            var bytes = stackalloc byte[16];
            for (var i = 0; i < 16; i++)
            {
                bytes[i] = 0;
            }

            if (typeof(T) == typeof(float))
            {
                var one = 1.0f;
                var bits = *(uint*)&one;
                for (var lane = 0; lane < lanes; lane++)
                {
                    *(uint*)(bytes + lane * size) = bits;
                }
            }
            else
            {
                for (var lane = 0; lane < lanes; lane++)
                {
                    for (var b = 0; b < size && b < 8; b++)
                    {
                        bytes[lane * size + b] = fill;
                    }
                }
            }

            return FromBytes(bytes);
        }

        static Vector128<T> AddLanes(Vector128<T> left, Vector128<T> right, System.Func<long, long, long> op)
        {
            var size = sizeof(T);
            var lanes = 16 / size;
            var a = stackalloc byte[16];
            var b = stackalloc byte[16];
            var r = stackalloc byte[16];
            left.ToBytes(a);
            right.ToBytes(b);
            for (var lane = 0; lane < lanes; lane++)
            {
                *(long*)(r + lane * size) = op(*(long*)(a + lane * size), *(long*)(b + lane * size));
            }

            return FromBytes(r);
        }

        static Vector128<T> Bitwise(Vector128<T> left, Vector128<T> right, System.Func<long, long, long> op)
        {
            left.lo = op(left.lo, right.lo);
            left.hi = op(left.hi, right.hi);
            return left;
        }
    }

    public static unsafe class Vector128
    {
        public static bool IsHardwareAccelerated => false;

        public static Vector128<T> Load<T>(T* source) where T : unmanaged
        {
            var bytes = stackalloc byte[16];
            for (var i = 0; i < 16; i++)
            {
                bytes[i] = ((byte*)source)[i];
            }

            return Vector128<T>.FromBytes(bytes);
        }

        public static void Store<T>(Vector128<T> source, T* destination) where T : unmanaged
        {
            Store(destination, source);
        }

        public static void Store<T>(T* destination, Vector128<T> source) where T : unmanaged
        {
            var bytes = stackalloc byte[16];
            source.ToBytes(bytes);
            for (var i = 0; i < 16; i++)
            {
                ((byte*)destination)[i] = bytes[i];
            }
        }

        public static Vector128<T> Create<T>(T value) where T : unmanaged
        {
            var size = sizeof(T);
            var lanes = 16 / size;
            var bytes = stackalloc byte[16];
            for (var i = 0; i < 16; i++)
            {
                bytes[i] = 0;
            }

            for (var lane = 0; lane < lanes; lane++)
            {
                *(T*)(bytes + lane * size) = value;
            }

            return Vector128<T>.FromBytes(bytes);
        }

        public static Vector128<int> Create(int e0, int e1, int e2, int e3)
        {
            var bytes = stackalloc byte[16];
            *(int*)bytes = e0;
            *(int*)(bytes + 4) = e1;
            *(int*)(bytes + 8) = e2;
            *(int*)(bytes + 12) = e3;
            return Vector128<int>.FromBytes(bytes);
        }

        public static Vector128<T> ConditionalSelect<T>(Vector128<T> condition, Vector128<T> left, Vector128<T> right) where T : unmanaged => SelectByMsb(condition, left, right);

        public static Vector128<T> LessThan<T>(Vector128<T> left, Vector128<T> right) where T : unmanaged => Compare(left, right, (a, b) => a < b);

        public static Vector128<T> GreaterThan<T>(Vector128<T> left, Vector128<T> right) where T : unmanaged => Compare(left, right, (a, b) => a > b);

        public static Vector128<T> GreaterThanOrEqual<T>(Vector128<T> left, Vector128<T> right) where T : unmanaged => Compare(left, right, (a, b) => a >= b);

        public static Vector128<T> Equals<T>(Vector128<T> left, Vector128<T> right) where T : unmanaged => Compare(left, right, (a, b) => a == b);

        public static Vector128<T> Min<T>(Vector128<T> left, Vector128<T> right) where T : unmanaged => SelectByMsb(LessThan(left, right), left, right);

        public static Vector128<TTo> As<TFrom, TTo>(Vector128<TFrom> source) where TFrom : unmanaged where TTo : unmanaged
        {
            var bytes = stackalloc byte[16];
            source.ToBytes(bytes);
            return Vector128<TTo>.FromBytes(bytes);
        }

        public static Vector128<int> ConvertToInt32Native(Vector128<float> value)
        {
            var bytes = stackalloc byte[16];
            value.ToBytes(bytes);
            var result = stackalloc byte[16];
            for (var lane = 0; lane < 4; lane++)
            {
                var f = *(float*)(bytes + lane * 4);
                *(int*)(result + lane * 4) = f != f ? 0 : f < -2147483648.0f ? int.MinValue : f >= 2147483648.0f ? int.MaxValue : (int)f;
            }

            return Vector128<int>.FromBytes(result);
        }

        public static byte ExtractMostSignificantBits<T>(Vector128<T> vector) where T : unmanaged
        {
            var bytes = stackalloc byte[16];
            vector.ToBytes(bytes);
            byte bits = 0;
            var size = sizeof(T);
            var lanes = 16 / size;
            for (var lane = 0; lane < lanes && lane < 8; lane++)
            {
                if ((bytes[lane * size + size - 1] & 0x80) != 0)
                {
                    bits |= (byte)(1 << lane);
                }
            }

            return bits;
        }

        static Vector128<T> Compare<T>(Vector128<T> left, Vector128<T> right, System.Func<long, long, bool> op) where T : unmanaged
        {
            var a = stackalloc byte[16];
            var b = stackalloc byte[16];
            var r = stackalloc byte[16];
            left.ToBytes(a);
            right.ToBytes(b);
            var size = sizeof(T);
            var lanes = 16 / size;
            for (var lane = 0; lane < lanes; lane++)
            {
                var equal = op(*(long*)(a + lane * size), *(long*)(b + lane * size));
                *(long*)(r + lane * size) = equal ? -1L : 0L;
            }

            return Vector128<T>.FromBytes(r);
        }

        static Vector128<T> SelectByMsb<T>(Vector128<T> mask, Vector128<T> left, Vector128<T> right) where T : unmanaged
        {
            var m = stackalloc byte[16];
            var a = stackalloc byte[16];
            var b = stackalloc byte[16];
            var r = stackalloc byte[16];
            mask.ToBytes(m);
            left.ToBytes(a);
            right.ToBytes(b);
            for (var i = 0; i < 16; i++)
            {
                r[i] = (m[i] & 0x80) != 0 ? a[i] : b[i];
            }

            return Vector128<T>.FromBytes(r);
        }
    }

    public unsafe struct Vector256<T> where T : unmanaged
    {
        long lo;
        long mid;
        long hi;
        long top;

        public static Vector256<T> Zero => default;

        public static Vector256<T> One => CreateLane((byte)1);

        public static Vector256<T> operator &(Vector256<T> left, Vector256<T> right) => Bitwise(left, right, (a, b) => a & b);

        public static Vector256<T> operator |(Vector256<T> left, Vector256<T> right) => Bitwise(left, right, (a, b) => a | b);

        public static Vector256<T> operator ^(Vector256<T> left, Vector256<T> right) => Bitwise(left, right, (a, b) => a ^ b);

        public static Vector256<T> operator ~(Vector256<T> value)
        {
            value.lo = ~value.lo;
            value.mid = ~value.mid;
            value.hi = ~value.hi;
            value.top = ~value.top;
            return value;
        }

        public static Vector256<T> operator +(Vector256<T> left, Vector256<T> right) => AddLanes(left, right, (a, b) => a + b);

        public static Vector256<T> operator -(Vector256<T> left, Vector256<T> right) => AddLanes(left, right, (a, b) => a - b);

        public static bool operator ==(Vector256<T> left, Vector256<T> right) => left.lo == right.lo && left.mid == right.mid && left.hi == right.hi && left.top == right.top;

        public static bool operator !=(Vector256<T> left, Vector256<T> right) => !(left == right);

        public override bool Equals(object obj) => obj is Vector256<T> other && this == other;

        public override int GetHashCode() => (int)(lo ^ mid ^ hi ^ top);

        internal void ToBytes(byte* buffer)
        {
            *(long*)buffer = lo;
            *(long*)(buffer + 8) = mid;
            *(long*)(buffer + 16) = hi;
            *(long*)(buffer + 24) = top;
        }

        internal Vector256(void* bytes)
        {
            lo = *(long*)bytes;
            mid = *(long*)((byte*)bytes + 8);
            hi = *(long*)((byte*)bytes + 16);
            top = *(long*)((byte*)bytes + 24);
        }

        internal static Vector256<T> FromBytes(void* bytes) => new Vector256<T>(bytes);

        static Vector256<T> CreateLane(byte fill)
        {
            var size = sizeof(T);
            var lanes = 32 / size;
            var bytes = stackalloc byte[32];
            for (var i = 0; i < 32; i++)
            {
                bytes[i] = 0;
            }

            if (typeof(T) == typeof(float))
            {
                var one = 1.0f;
                var bits = *(uint*)&one;
                for (var lane = 0; lane < lanes; lane++)
                {
                    *(uint*)(bytes + lane * size) = bits;
                }
            }
            else
            {
                for (var lane = 0; lane < lanes; lane++)
                {
                    for (var b = 0; b < size && b < 8; b++)
                    {
                        bytes[lane * size + b] = fill;
                    }
                }
            }

            return FromBytes(bytes);
        }

        static Vector256<T> AddLanes(Vector256<T> left, Vector256<T> right, System.Func<long, long, long> op)
        {
            var size = sizeof(T);
            var lanes = 32 / size;
            var a = stackalloc byte[32];
            var b = stackalloc byte[32];
            var r = stackalloc byte[32];
            left.ToBytes(a);
            right.ToBytes(b);
            for (var lane = 0; lane < lanes; lane++)
            {
                *(long*)(r + lane * size) = op(*(long*)(a + lane * size), *(long*)(b + lane * size));
            }

            return FromBytes(r);
        }

        static Vector256<T> Bitwise(Vector256<T> left, Vector256<T> right, System.Func<long, long, long> op)
        {
            left.lo = op(left.lo, right.lo);
            left.mid = op(left.mid, right.mid);
            left.hi = op(left.hi, right.hi);
            left.top = op(left.top, right.top);
            return left;
        }
    }

    public static unsafe class Vector256
    {
        public static bool IsHardwareAccelerated => false;

        public static Vector256<T> Load<T>(T* source) where T : unmanaged
        {
            var bytes = stackalloc byte[32];
            for (var i = 0; i < 32; i++)
            {
                bytes[i] = ((byte*)source)[i];
            }

            return Vector256<T>.FromBytes(bytes);
        }

        public static void Store<T>(Vector256<T> source, T* destination) where T : unmanaged
        {
            Store(destination, source);
        }

        public static void Store<T>(T* destination, Vector256<T> source) where T : unmanaged
        {
            var bytes = stackalloc byte[32];
            source.ToBytes(bytes);
            for (var i = 0; i < 32; i++)
            {
                ((byte*)destination)[i] = bytes[i];
            }
        }

        public static Vector256<T> Create<T>(T value) where T : unmanaged
        {
            var size = sizeof(T);
            var lanes = 32 / size;
            var bytes = stackalloc byte[32];
            for (var i = 0; i < 32; i++)
            {
                bytes[i] = 0;
            }

            for (var lane = 0; lane < lanes; lane++)
            {
                *(T*)(bytes + lane * size) = value;
            }

            return Vector256<T>.FromBytes(bytes);
        }

        public static Vector256<int> Create(int e0, int e1, int e2, int e3, int e4, int e5, int e6, int e7)
        {
            var bytes = stackalloc byte[32];
            *(int*)bytes = e0;
            *(int*)(bytes + 4) = e1;
            *(int*)(bytes + 8) = e2;
            *(int*)(bytes + 12) = e3;
            *(int*)(bytes + 16) = e4;
            *(int*)(bytes + 20) = e5;
            *(int*)(bytes + 24) = e6;
            *(int*)(bytes + 28) = e7;
            return Vector256<int>.FromBytes(bytes);
        }

        public static Vector256<T> ConditionalSelect<T>(Vector256<T> condition, Vector256<T> left, Vector256<T> right) where T : unmanaged => SelectByMsb(condition, left, right);

        public static Vector256<T> LessThan<T>(Vector256<T> left, Vector256<T> right) where T : unmanaged => Compare(left, right, (a, b) => a < b);

        public static Vector256<T> GreaterThan<T>(Vector256<T> left, Vector256<T> right) where T : unmanaged => Compare(left, right, (a, b) => a > b);

        public static Vector256<T> GreaterThanOrEqual<T>(Vector256<T> left, Vector256<T> right) where T : unmanaged => Compare(left, right, (a, b) => a >= b);

        public static Vector256<T> Equals<T>(Vector256<T> left, Vector256<T> right) where T : unmanaged => Compare(left, right, (a, b) => a == b);

        public static Vector256<T> Min<T>(Vector256<T> left, Vector256<T> right) where T : unmanaged => SelectByMsb(LessThan(left, right), left, right);

        public static Vector256<TTo> As<TFrom, TTo>(Vector256<TFrom> source) where TFrom : unmanaged where TTo : unmanaged
        {
            var bytes = stackalloc byte[32];
            source.ToBytes(bytes);
            return Vector256<TTo>.FromBytes(bytes);
        }

        public static Vector256<int> ConvertToInt32Native(Vector256<float> value)
        {
            var bytes = stackalloc byte[32];
            value.ToBytes(bytes);
            var result = stackalloc byte[32];
            for (var lane = 0; lane < 8; lane++)
            {
                var f = *(float*)(bytes + lane * 4);
                *(int*)(result + lane * 4) = f != f ? 0 : f < -2147483648.0f ? int.MinValue : f >= 2147483648.0f ? int.MaxValue : (int)f;
            }

            return Vector256<int>.FromBytes(result);
        }

        public static ushort ExtractMostSignificantBits<T>(Vector256<T> vector) where T : unmanaged
        {
            var bytes = stackalloc byte[32];
            vector.ToBytes(bytes);
            ushort bits = 0;
            var size = sizeof(T);
            var lanes = 32 / size;
            for (var lane = 0; lane < lanes && lane < 16; lane++)
            {
                if ((bytes[lane * size + size - 1] & 0x80) != 0)
                {
                    bits |= (ushort)(1 << lane);
                }
            }

            return bits;
        }

        static Vector256<T> Compare<T>(Vector256<T> left, Vector256<T> right, System.Func<long, long, bool> op) where T : unmanaged
        {
            var a = stackalloc byte[32];
            var b = stackalloc byte[32];
            var r = stackalloc byte[32];
            left.ToBytes(a);
            right.ToBytes(b);
            var size = sizeof(T);
            var lanes = 32 / size;
            for (var lane = 0; lane < lanes; lane++)
            {
                var equal = op(*(long*)(a + lane * size), *(long*)(b + lane * size));
                *(long*)(r + lane * size) = equal ? -1L : 0L;
            }

            return Vector256<T>.FromBytes(r);
        }

        static Vector256<T> SelectByMsb<T>(Vector256<T> mask, Vector256<T> left, Vector256<T> right) where T : unmanaged
        {
            var m = stackalloc byte[32];
            var a = stackalloc byte[32];
            var b = stackalloc byte[32];
            var r = stackalloc byte[32];
            mask.ToBytes(m);
            left.ToBytes(a);
            right.ToBytes(b);
            for (var i = 0; i < 32; i++)
            {
                r[i] = (m[i] & 0x80) != 0 ? a[i] : b[i];
            }

            return Vector256<T>.FromBytes(r);
        }
    }
}
