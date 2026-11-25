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
        public const int Kinds = 4;
        public const int Count = Channel.Count * 16;

        public static int Of(int channel, int kind, int executes, int loss) => (channel << 4) | (kind << 2) | (executes << 1) | loss;

        public static int PhysicalOf(int kind, int executes, int loss) => Of(Channel.Physical, kind, executes, loss);

        public static int PoisonOf(int kind, int loss) => Of(Channel.Poison, kind, 0, loss);

        public static int FireOf(int kind, int loss) => Of(Channel.Fire, kind, 0, loss);

        public static int HealingOf(int kind) => Of(Channel.Healing, kind, 0, 0);

        public static int HealingLossOf(int kind) => Of(Channel.Healing, kind, 0, 1);

        public static int TickOf(int kind) => Of(Channel.Tick, kind, 0, 0);

        public static int TickLossOf(int kind) => Of(Channel.Tick, kind, 0, 1);
    }
}
