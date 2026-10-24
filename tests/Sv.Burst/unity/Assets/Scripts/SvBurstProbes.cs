using System;
using System.Runtime.InteropServices;
using Unity.Burst;

namespace Sv.Burst
{
    /// <summary>
    /// Atom 6.2 BCL audit probes: each entry is compiled by the real Burst compiler from batch
    /// mode (compile-only where noted) to record, with evidence, which BCL patterns the
    /// unmanaged kernel path may use on the Burst host. System.Numerics.BitOperations is not
    /// probed: Unity 6.7's scripting profile keeps the type internal (CS0122), which is the
    /// audit datum itself and the reason <c>Bcl.Tzcnt</c> exists.
    /// </summary>
    public static unsafe class Probes
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public unsafe delegate void IntProbe(int* p, int n);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public unsafe delegate void FloatProbe(float* f, int n);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public unsafe delegate void ThrowProbe(int* p);

        [BurstCompile(FloatPrecision.Standard, FloatMode.Strict)]
        public static void MathClampProbe(int* p, int n)
        {
            for (var i = 0; i < n; i++)
            {
                p[i] = Math.Clamp(p[i], -5, 5);
                p[i] = Math.Min(p[i], 3);
                p[i] = Math.Max(p[i], -3);
            }
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Strict)]
        public static void FloatFoldProbe(float* f, int n)
        {
            for (var i = 0; i < n; i++)
            {
                f[i] = f[i] * 1.0000001f + 0.0000001f;
            }
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Strict)]
        public static void ThrowInterpolatedProbe(int* p)
        {
            throw new ArgumentException("sv: probe " + p[0] + " out of range");
        }
    }
}
