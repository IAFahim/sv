namespace Sv.V4
{
    public static class Lane
    {
        public const int Health = 0;
        public const int MaxHealth = 1;
        public const int Shield = 2;
        public const int ImmuneMask = 3;
        public const int KindMask = 4;
        public const int Execute = 5;
        public const int Converter = 6;
        public const int PoisonTicks = 7;
        public const int RegenCard = 8;
        public const int RegenRate = 9;
        public const int Events = 10;
        public const int Count = 11;
    }

    public static class Channel
    {
        public const int Physical = 0;
        public const int Poison = 1;
        public const int Fire = 2;
        public const int Healing = 3;
        public const int Tick = 4;
        public const int Count = 5;
    }

    public static class EventBit
    {
        public const int Dead = 1;
        public const int LowHealth = 2;
    }

    public static class Threshold
    {
        public const long ExecuteBelowPercent = 20;
        public const long LowHealthBelowPercent = 25;
        public const int PoisonPerTickHit = 4;
    }

    public static class SumClass
    {
        public const int PhysicalGain = 0;
        public const int PhysicalLoss = 1;
        public const int PoisonGain = 2;
        public const int PoisonLoss = 3;
        public const int FireGain = 4;
        public const int FireLoss = 5;
        public const int HealingGain = 6;
        public const int HealingLoss = 7;
        public const int TickGain = 8;
        public const int TickLoss = 9;
        public const int Count = 10;

        public static int Of(int channel, int loss) => (channel << 1) | loss;
    }

    public static class TargetContext
    {
        public const int Kinds = 4;
        public const int BelowExecuteLine = 20;

        public static int AdmitShift(int channel, int kind) => (channel << 2) | kind;

        public static uint Admits(int immuneMask, int kindMask)
        {
            var channels = ~(uint)immuneMask & 31u;
            var kinds = ~(uint)kindMask & 15u;
            var spread = (channels & 1u) | ((channels & 2u) << 3) | ((channels & 4u) << 6) | ((channels & 8u) << 9) | ((channels & 16u) << 12);
            return kinds * spread;
        }

        public static uint Of(int health, int maxHealth, int immuneMask, int kindMask)
        {
            var below = (long)health * 100 < (long)maxHealth * Threshold.ExecuteBelowPercent ? 1u : 0u;
            return Admits(immuneMask, kindMask) | (below << BelowExecuteLine);
        }
    }
}
