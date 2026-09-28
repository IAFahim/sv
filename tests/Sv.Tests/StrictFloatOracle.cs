using System;
using System.Runtime.CompilerServices;

public static class StrictFloat
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static float Add(float a, float b) => (float)((double)a + (double)b);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static float Sub(float a, float b) => (float)((double)a - (double)b);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static float Mul(float a, float b) => (float)((double)a * (double)b);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static float Divide(float a, float b) => (float)((double)a / (double)b);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static float Sqrt(float a) => (float)Math.Sqrt(a);
}
