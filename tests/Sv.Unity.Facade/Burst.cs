using System;
using System.Numerics;

namespace Unity.Burst
{
    public enum FloatMode
    {
        Default = 0,
        Strict = 1,
        Deterministic = 2,
        Fast = 3,
    }

    public enum FloatPrecision
    {
        Standard = 0,
        High = 1,
        Medium = 2,
        Low = 3,
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Assembly)]
    public class BurstCompileAttribute : Attribute
    {
        public BurstCompileAttribute()
        {
        }

        public BurstCompileAttribute(FloatPrecision floatPrecision, FloatMode floatMode)
        {
            FloatPrecision = floatPrecision;
            FloatMode = floatMode;
        }

        public FloatMode FloatMode { get; set; }

        public FloatPrecision FloatPrecision { get; set; }

        public bool CompileSynchronously { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
    public class BurstDiscardAttribute : Attribute
    {
    }

    public sealed class BurstCompilerOptions
    {
        public bool EnableBurstCompilation { get; set; } = true;

        public bool EnableBurstCompileSynchronously { get; set; }
    }

    public static class BurstCompiler
    {
        public static readonly BurstCompilerOptions Options = new BurstCompilerOptions();
    }

    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Struct | AttributeTargets.ReturnValue)]
    public class NoAliasAttribute : Attribute
    {
    }

    namespace Intrinsics
    {
        public struct v128
        {
            public ulong ULong0;
            public ulong ULong1;

            public v128(ulong a, ulong b)
            {
                ULong0 = a;
                ULong1 = b;
            }
        }
    }

    namespace CompilerServices
    {
        public static class Loop
        {
            [ThreadStatic]
            private static int expectedVectorizedLoops;

            public static int ExpectedVectorizedLoops => expectedVectorizedLoops;

            public static void ExpectVectorized() => expectedVectorizedLoops++;
        }
    }
}

namespace Unity.Mathematics
{
    public static class math
    {
        public static int tzcnt(ulong x) => BitOperations.TrailingZeroCount(x);

        public static float select(float falseValue, float trueValue, bool test) => test ? trueValue : falseValue;

        public static int select(int falseValue, int trueValue, bool test) => test ? trueValue : falseValue;

        public static float abs(float x) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(x) & 0x7FFFFFFF);

        public static float sqrt(float x) => (float)Math.Sqrt(x);

        public static float asfloat(int x) => BitConverter.Int32BitsToSingle(x);

        public static int asint(float x) => BitConverter.SingleToInt32Bits(x);
    }
}
