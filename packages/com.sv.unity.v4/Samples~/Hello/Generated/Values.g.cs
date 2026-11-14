using Sv;

namespace Hello.Sv
{
    public enum Stat : ushort
    {
        MaxHealth = 0,
    }

    public enum Intrinsic : ushort
    {
        Health = 1,
        Shield = 2,
        FireWard = 3,
        Kind = 4,
    }

    public enum Event : ushort
    {
        LowHealth = 160,
    }

    public enum Channel : byte
    {
        Physical = 0,
        Fire = 1,
        Healing = 2,
    }

    public enum Kind : byte
    {
        None = 0,
        Goblin = 1,
        Dragon = 2,
    }

    public enum Template : int
    {
        hero = 0,
        knight = 1,
        goblin = 2,
        dragon = 3,
    }

    public static class Layout
    {
        public const ulong CompiledAgainst = 0x0EB68FE438A2EF3Dul;

        public static readonly ulong PackHash = 0x0EB68FE438A2EF3Dul;

        public static readonly int LanesPerChunk = 6;

        public static readonly int Channels = 3;

        public static readonly int Kinds = 3;

        public static int LaneOf(Stat stat) => (int)stat;

        public static int LaneOf(Intrinsic intrinsic) => (int)intrinsic;

        public static int LaneOf(Event raised) => (int)raised >> 5;

        public static int WordOf(Event raised) => (int)raised & 31;

        public static bool Matches(ulong callerCompiledAgainst) => callerCompiledAgainst == PackHash;

        public static LaneClass[] Classes()
        {
            var classes = new LaneClass[6];
            classes[0] = LaneClass.Stat;
            classes[1] = LaneClass.Intrinsic;
            classes[2] = LaneClass.Intrinsic;
            classes[3] = LaneClass.Intrinsic;
            classes[4] = LaneClass.Intrinsic;
            classes[5] = LaneClass.Event;
            return classes;
        }

        public static int ScaleOf(Stat stat)
        {
            switch (stat)
            {
                default: return 1;
            }
        }

        public static int ScaleOf(Intrinsic intrinsic)
        {
            switch (intrinsic)
            {
                default: return 1;
            }
        }

        public static int DefaultOf(Stat stat)
        {
            switch (stat)
            {
                case Stat.MaxHealth: return 100;
                default: return 0;
            }
        }

        public static int DefaultOf(Intrinsic intrinsic)
        {
            switch (intrinsic)
            {
                case Intrinsic.Health: return 100;
                default: return 0;
            }
        }

        public static bool DefaultOf(Event raised)
        {
            return false;
        }
    }
}
