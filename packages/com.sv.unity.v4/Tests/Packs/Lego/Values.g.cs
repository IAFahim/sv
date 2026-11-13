using Sv;

namespace Sv.Kernels.Lego
{
    public enum Stat : ushort
    {
        MaxHealth = 0,
        Power = 1,
        MaxJumps = 2,
        RegenRate = 3,
        ThornsRate = 4,
        ConvertRate = 5,
        LifestealRate = 6,
        Strength = 7,
        RageBonus = 43,
    }

    public enum Intrinsic : ushort
    {
        Health = 8,
        Kind = 9,
        Owner = 10,
        PoisonTicks = 11,
        RegenCard = 12,
        Shield = 13,
        Lifesteal = 14,
        Thorns = 15,
        ImmuneCharges = 16,
        ImmuneMask = 17,
        KindMask = 18,
        StunTicks = 19,
        Moves = 20,
        Jumps = 21,
        JumpsDone = 22,
        Cooldown = 23,
        Casts = 24,
        HitStreak = 25,
        CritCard = 26,
        Execute = 27,
        ReviveCharges = 28,
        Converter = 29,
        FireTaken = 30,
        RageStacks = 31,
        SnapPower = 32,
        HealPerHit = 33,
        OwnerOnlyHeal = 34,
        SniperWard = 35,
        Leech = 36,
        CoreTraining = 37,
        LeapsDone = 40,
        Fury = 41,
        CritRoll = 42,
    }

    public enum Event : ushort
    {
        Stunned = 1216,
        Dead = 1217,
        LowHealth = 1218,
    }

    public enum Channel : byte
    {
        Physical = 0,
        Poison = 1,
        Fire = 2,
        Healing = 3,
        Tick = 4,
        PoisonTime = 5,
        StunTime = 6,
        Move = 7,
        Jump = 8,
        Landed = 9,
        JumpRefill = 10,
        Cast = 11,
        CooldownTime = 12,
        Streak = 13,
        ReviveSpend = 14,
        RageGain = 15,
        CastPoison = 16,
        PoisonPower = 17,
        OwnerShift = 18,
        DamageDealt = 19,
        Land = 20,
        JumpSet = 21,
        StunRefresh = 22,
        Leap = 23,
        OwnerSet = 24,
        CastSnap = 25,
        PoisonSnap = 26,
        FuryGain = 27,
    }

    public enum Kind : byte
    {
        None = 0,
        Sniper = 1,
        Rifle = 2,
        Beast = 3,
    }

    public static class Layout
    {
        public const ulong CompiledAgainst = 0x9A043301EA06000Eul;

        public static readonly ulong PackHash = 0x9A043301EA06000Eul;

        public static readonly int LanesPerChunk = 44;

        public static readonly int Channels = 28;

        public static readonly int Kinds = 4;

        public static int LaneOf(Stat stat) => (int)stat;

        public static int LaneOf(Intrinsic intrinsic) => (int)intrinsic;

        public static int LaneOf(Event raised) => (int)raised >> 5;

        public static int WordOf(Event raised) => (int)raised & 31;

        public static bool Matches(ulong callerCompiledAgainst) => callerCompiledAgainst == PackHash;

        public static LaneClass[] Classes()
        {
            var classes = new LaneClass[44];
            classes[0] = LaneClass.Stat;
            classes[1] = LaneClass.Stat;
            classes[2] = LaneClass.Stat;
            classes[3] = LaneClass.Stat;
            classes[4] = LaneClass.Stat;
            classes[5] = LaneClass.Stat;
            classes[6] = LaneClass.Stat;
            classes[7] = LaneClass.Stat;
            classes[43] = LaneClass.Stat;
            classes[8] = LaneClass.Intrinsic;
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
            classes[22] = LaneClass.Intrinsic;
            classes[23] = LaneClass.Intrinsic;
            classes[24] = LaneClass.Intrinsic;
            classes[25] = LaneClass.Intrinsic;
            classes[26] = LaneClass.Intrinsic;
            classes[27] = LaneClass.Intrinsic;
            classes[28] = LaneClass.Intrinsic;
            classes[29] = LaneClass.Intrinsic;
            classes[30] = LaneClass.Intrinsic;
            classes[31] = LaneClass.Intrinsic;
            classes[32] = LaneClass.Intrinsic;
            classes[33] = LaneClass.Intrinsic;
            classes[34] = LaneClass.Intrinsic;
            classes[35] = LaneClass.Intrinsic;
            classes[36] = LaneClass.Intrinsic;
            classes[37] = LaneClass.Intrinsic;
            classes[40] = LaneClass.Intrinsic;
            classes[41] = LaneClass.Intrinsic;
            classes[42] = LaneClass.Intrinsic;
            classes[38] = LaneClass.Event;
            return classes;
        }

        public static int ScaleOf(Stat stat)
        {
            switch (stat)
            {
                case Stat.ThornsRate: return 100;
                case Stat.ConvertRate: return 100;
                case Stat.LifestealRate: return 100;
                case Stat.RageBonus: return 100;
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
                case Stat.Power: return 10;
                case Stat.MaxJumps: return 2;
                case Stat.ThornsRate: return 100;
                case Stat.ConvertRate: return 50;
                case Stat.LifestealRate: return 50;
                case Stat.Strength: return 100;
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
