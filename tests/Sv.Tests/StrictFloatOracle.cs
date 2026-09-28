using System;
using System.Runtime.CompilerServices;

namespace Kernels.Testing
{
    public static class StrictFloat
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static float Add(float left, float right) => (float)((double)left + right);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static float Subtract(float left, float right) => (float)((double)left - right);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static float Multiply(float left, float right) => (float)((double)left * right);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static float Divide(float left, float right) => (float)((double)left / right);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static float Sqrt(float value) => (float)Math.Sqrt(value);
    }
}
