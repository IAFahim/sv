using Unity.Entities;

namespace SvV0
{
    public struct V0Slot : IComponentData { public int Value; }
    public struct V0Hp : IComponentData { public int Value; }
    public struct V0HpDelta : IComponentData { public int Value; }
    public struct V0HpMax : IComponentData { public float Value; }
    public struct V0Mana : IComponentData { public int Value; }
    public struct V0ManaDelta : IComponentData { public int Value; }
    public struct V0ManaMax : IComponentData { public float Value; }
    public struct V0Flag : IComponentData, IEnableableComponent { }
    public unsafe struct V0Filler : IComponentData { public fixed byte Bytes[288]; }

    public static class V0Probe
    {
        public static int Hits;

        [Unity.Burst.BurstDiscard]
        public static void Mark()
        {
            Hits = 1;
        }
    }

    public static class V0Law
    {
        public static int BoundOf(float value)
        {
            if (float.IsNaN(value))
            {
                return 0;
            }

            if (value >= 2147483648.0f)
            {
                return int.MaxValue;
            }

            if (value < -2147483648.0f)
            {
                return int.MinValue;
            }

            return (int)value;
        }

        public static int Step(int before, int delta, int floor, int ceiling)
        {
            var low = floor < ceiling ? floor : ceiling;
            var moved = (long)before + delta;
            return moved < low ? low : moved > ceiling ? ceiling : (int)moved;
        }
    }
}
