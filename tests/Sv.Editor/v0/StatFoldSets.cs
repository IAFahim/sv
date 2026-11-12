namespace SvV0
{
    public enum ProbeModifierKind : byte
    {
        Added = 0,
        Additive = 1,
        Multi = 2,
        Cap = 3,
    }

    public struct ProbeStatEntry
    {
        public ProbeModifierKind Kind;
        public float Value;
    }

    public static unsafe class StatFoldSets
    {
        public const int SetCount = 1 << 20;
        public const int MaxEntries = 8;
        public const ulong Seed = 0x5EED5EED0115UL;

        public static ulong Next(ref ulong state)
        {
            state += 0x9E3779B97F4A7C15UL;
            var z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public static uint FloatBits(ref ulong state)
        {
            var r = Next(ref state);
            var pick = (uint)(r & 63);
            var sign = (uint)((r >> 6) & 1) << 31;
            var mantissa = (uint)(r >> 8) & 0x7FFFFF;
            if (pick == 0) return 0x7FC00000u;
            if (pick == 1) return sign | 0x7F800000u;
            if (pick == 2) return sign;
            if (pick == 3) return sign | mantissa;
            if (pick == 4) return sign | ((uint)(254 - (int)((r >> 40) & 3)) << 23) | mantissa;
            var exponent = 127 - 24 + (int)((r >> 32) % 49);
            return sign | ((uint)exponent << 23) | mantissa;
        }

        public static void Fill(float* bases, byte* counts, ProbeStatEntry* entries)
        {
            var state = Seed;
            for (var set = 0; set < SetCount; set++)
            {
                var b = FloatBits(ref state);
                bases[set] = *(float*)&b;
                var count = (byte)(Next(ref state) % (MaxEntries + 1));
                counts[set] = count;
                for (var i = 0; i < MaxEntries; i++)
                {
                    var e = entries + (long)set * MaxEntries + i;
                    if (i < count)
                    {
                        e->Kind = (ProbeModifierKind)(Next(ref state) & 3);
                        var v = FloatBits(ref state);
                        e->Value = *(float*)&v;
                    }
                    else
                    {
                        e->Kind = ProbeModifierKind.Added;
                        e->Value = 0f;
                    }
                }
            }
        }

        public static float Fold(float @base, ProbeStatEntry* entries, int count)
        {
            var value = @base;
            var additive = 1f;
            for (var i = 0; i < count; i++)
            {
                if (entries[i].Kind == ProbeModifierKind.Added)
                {
                    value = value + entries[i].Value;
                }
            }

            for (var i = 0; i < count; i++)
            {
                if (entries[i].Kind == ProbeModifierKind.Additive)
                {
                    additive = additive + entries[i].Value;
                }
            }

            value = value * additive;
            for (var i = 0; i < count; i++)
            {
                if (entries[i].Kind == ProbeModifierKind.Multi)
                {
                    value = value * (1f + entries[i].Value);
                }
            }

            for (var i = 0; i < count; i++)
            {
                if (entries[i].Kind == ProbeModifierKind.Cap)
                {
                    value = value > entries[i].Value ? entries[i].Value : value;
                }
            }

            return value;
        }

        public static void FoldAll(float* bases, byte* counts, ProbeStatEntry* entries, uint* results)
        {
            for (var set = 0; set < SetCount; set++)
            {
                var f = Fold(bases[set], entries + (long)set * MaxEntries, counts[set]);
                results[set] = *(uint*)&f;
            }
        }

        public static ulong Digest(uint* results)
        {
            var h = 0xCBF29CE484222325UL;
            for (var i = 0; i < SetCount; i++)
            {
                h = (h ^ results[i]) * 0x100000001B3UL;
            }
            return h;
        }
    }
}
