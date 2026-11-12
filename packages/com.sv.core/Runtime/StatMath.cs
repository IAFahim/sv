using System.Runtime.CompilerServices;

namespace Sv
{
    internal static unsafe class StatMath
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Add(float left, float right) => (float)(left + right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Multiply(float left, float right) => (float)(left * right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Subtract(float left, float right) => (float)(left - right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Divide(float left, float right) => (float)(left / right);

        public static float Fold(float @base, StatEntry* entries, int count)
        {
            var value = @base;
            var additive = 1f;
            for (var i = 0; i < count; i++)
            {
                if (entries[i].Kind == ModifierKind.Added)
                {
                    value = Add(value, entries[i].Value);
                }
            }

            for (var i = 0; i < count; i++)
            {
                if (entries[i].Kind == ModifierKind.Additive)
                {
                    additive = Add(additive, entries[i].Value);
                }
            }

            value = Multiply(value, additive);
            for (var i = 0; i < count; i++)
            {
                if (entries[i].Kind == ModifierKind.Multi)
                {
                    value = Multiply(value, Add(1f, entries[i].Value));
                }
            }

            for (var i = 0; i < count; i++)
            {
                if (entries[i].Kind == ModifierKind.Cap)
                {
                    value = value > entries[i].Value ? entries[i].Value : value;
                }
            }

            return value;
        }

        public static float Derived(float coefficient, float source) => Multiply(coefficient, source);

        public static float CurveCeiling(float min, int length, float step) => Add(min, Multiply(length - 1, step));

        public static int CurveBucket(float value, float min, float ceiling, float step, int length)
        {
            if (value <= min)
            {
                return 0;
            }

            if (value >= ceiling)
            {
                return length - 1;
            }

            return (int)Divide(Subtract(value, min), step);
        }
    }
}
