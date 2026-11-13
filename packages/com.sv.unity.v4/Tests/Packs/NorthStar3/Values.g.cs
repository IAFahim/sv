using Sv;

namespace Sv.Kernels.NorthStar3
{
    public enum Stat : ushort
    {
        MaxHealth = 0,
        MoveSpeed = 1,
    }

    public enum Intrinsic : ushort
    {
        Health = 2,
        Kind = 3,
        Owner = 4,
        SniperWard = 5,
        Thorns = 6,
        Leech = 7,
        NoPhysical = 9,
        DamageImmune = 10,
        Cursed = 11,
        DoubleJumping = 12,
        Jumping = 13,
        JumpHealer = 14,
        Immunity = 15,
        ImmuneCharges = 16,
        Spite = 17,
        MirrorWard = 18,
        Shield = 19,
        ChargeWard = 20,
        Target = 21,
    }

    public enum Event : ushort
    {
        LowHealth = 256,
    }

    public enum Channel : byte
    {
        Physical = 0,
        Poison = 1,
        Bleeding = 2,
        Healing = 3,
        GiveCharges = 4,
        Aim = 5,
    }

    public enum Kind : byte
    {
        None = 0,
        Sniper = 1,
        Rifle = 2,
    }

    public static class Layout
    {
        public const ulong CompiledAgainst = 0x1456B551FFCFA7CFul;

        public static readonly ulong PackHash = 0x1456B551FFCFA7CFul;

        public static readonly int LanesPerChunk = 22;

        public static readonly int Channels = 6;

        public static readonly int Kinds = 3;

        public static int LaneOf(Stat stat) => (int)stat;

        public static int LaneOf(Intrinsic intrinsic) => (int)intrinsic;

        public static int LaneOf(Event raised) => (int)raised >> 5;

        public static int WordOf(Event raised) => (int)raised & 31;

        public static bool Matches(ulong callerCompiledAgainst) => callerCompiledAgainst == PackHash;

        public static LaneClass[] Classes()
        {
            var classes = new LaneClass[22];
            classes[0] = LaneClass.Stat;
            classes[1] = LaneClass.Stat;
            classes[2] = LaneClass.Intrinsic;
            classes[3] = LaneClass.Intrinsic;
            classes[4] = LaneClass.Intrinsic;
            classes[5] = LaneClass.Intrinsic;
            classes[6] = LaneClass.Intrinsic;
            classes[7] = LaneClass.Intrinsic;
            classes[9] = LaneClass.Intrinsic;
            classes[10] = LaneClass.Intrinsic;
            classes[11] = LaneClass.Intrinsic;
            classes[12] = LaneClass.Intrinsic;
            classes[13] = LaneClass.Intrinsic;
            classes[14] = LaneClass.Intrinsic;
            classes[15] = LaneClass.Intrinsic;
            classes[16] = LaneClass.Intrinsic;
            classes[17] = LaneClass.Intrinsic;
            classes[18] = LaneClass.Intrinsic;
            classes[19] = LaneClass.Intrinsic;
            classes[20] = LaneClass.Intrinsic;
            classes[21] = LaneClass.Intrinsic;
            classes[8] = LaneClass.Event;
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
                case Stat.MoveSpeed: return 5;
                default: return 0;
            }
        }

        public static int DefaultOf(Intrinsic intrinsic)
        {
            switch (intrinsic)
            {
                case Intrinsic.Health: return 100;
                case Intrinsic.Owner: return -1;
                default: return 0;
            }
        }

        public static bool DefaultOf(Event raised)
        {
            return false;
        }
    }
}
