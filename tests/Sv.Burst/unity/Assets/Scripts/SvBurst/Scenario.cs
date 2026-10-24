using System;

namespace Sv.Burst
{
    /// <summary>
    /// The cross-target program: one seeded op stream plus the columns it runs over, built
    /// identically on every host (same seed → same op order, same column bits). Hosts run it
    /// through <c>Sv.Kernel.Run</c> (the .NET vector path), through
    /// <see cref="Kernels.RunProgram"/> under plain .NET, and through the Burst-compiled
    /// function pointer, then compare <see cref="Digest"/> — bit-identical results are the
    /// atom 6.2 receipt.
    /// </summary>
    public static unsafe class Scenario
    {
        public const int Rows = 4096;

        public const int Ops = 512;

        public const ulong Seed = 0x9E3779B97F4A7C15ul;

        public static ulong Next(ref ulong state)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            return state;
        }

        public static int Below(ref ulong state, int n)
        {
            return (int)(Next(ref state) % (ulong)n);
        }

        public static ulong Mix(ulong hash, ulong value)
        {
            unchecked
            {
                hash ^= value;
                hash *= 0x100000001B3ul;
                return hash ^ (hash >> 29);
            }
        }

        /// <summary>Initializes every column and emits the op stream; returns the op count. Same seed → same bits and ops everywhere.</summary>
        public static int Build(
            KOp* ops,
            int* value,
            int* delta,
            int* applied,
            int* minRows,
            int* maxRows,
            float* minStats,
            float* maxStats,
            ulong* words)
        {
            var state = Seed;
            for (var i = 0; i < Rows; i++)
            {
                value[i] = i % 97 == 0 ? int.MinValue : i % 89 == 0 ? int.MaxValue : Below(ref state, 900_000) - 450_000;
                delta[i] = i % 83 == 0 ? int.MinValue : i % 79 == 0 ? int.MaxValue : Below(ref state, 41) - 20;
                minRows[i] = i % 71 == 0 ? int.MinValue : i % 67 == 0 ? int.MaxValue : Below(ref state, 200) - 100;
                maxRows[i] = i % 61 == 0 ? int.MinValue : i % 59 == 0 ? int.MaxValue : Below(ref state, 200) - 100;
                minStats[i] = BitsToFloat(SpecialOrSeeded(ref state, i));
                maxStats[i] = BitsToFloat(SpecialOrSeeded(ref state, i));
                applied[i] = 0;
            }

            for (var w = 0; w < Rows / 64; w++)
            {
                words[w] = w % 13 == 0 ? ulong.MaxValue : w % 7 == 0 ? 0 : Next(ref state) & Next(ref state);
            }

            for (var i = 0; i < Ops; i++)
            {
                var kindRoll = Below(ref state, 4);
                var boundRoll = Below(ref state, 10);
                ops[i].Kind = (KOpKind)kindRoll;
                ops[i].Bound = BuildBound(ref state, boundRoll, minRows, maxRows, minStats, maxStats);
                ops[i].Uniform = i % 23 == 0 ? int.MinValue : i % 19 == 0 ? int.MaxValue : Below(ref state, 51) - 25;
                ops[i].PerRow = delta;
                if (ops[i].Kind == KOpKind.ChunkUniform || ops[i].Kind == KOpKind.ChunkPerRow)
                {
                    ops[i].Start = Below(ref state, Rows - 1);
                    ops[i].Count = Math.Min(1 + Below(ref state, 1024), Rows - ops[i].Start);
                    ops[i].Words = null;
                    ops[i].WordCount = 0;
                }
                else
                {
                    ops[i].WordCount = 1 + Below(ref state, 16);
                    ops[i].Start = Below(ref state, Rows - ops[i].WordCount * 64 + 1);
                    ops[i].Count = 0;
                    ops[i].Words = words;
                }
            }

            return Ops;
        }

        /// <summary>FNV-style digest over the final value and applied bits — pure ulong arithmetic, exact on every host.</summary>
        public static ulong Digest(int* value, int* applied, int rows)
        {
            var hash = 0xCBF29CE484222325ul;
            for (var i = 0; i < rows; i++)
            {
                hash = Mix(hash, (uint)value[i]);
                hash = Mix(hash, (uint)applied[i]);
            }

            return hash;
        }

        static KBound BuildBound(ref ulong state, int roll, int* minRows, int* maxRows, float* minStats, float* maxStats)
        {
            var bound = new KBound();
            if (roll < 1)
            {
                bound.Kind = KBoundKind.None;
                return bound;
            }

            if (roll < 4)
            {
                bound.Kind = KBoundKind.Constant;
                bound.Min = Below(ref state, 200) - 100;
                bound.Max = bound.Min + Below(ref state, 200);
                return bound;
            }

            if (roll < 7)
            {
                bound.Kind = KBoundKind.Rows;
                bound.MinRows = Below(ref state, 8) == 0 ? null : minRows;
                bound.MaxRows = bound.MinRows == null ? maxRows : (Below(ref state, 8) == 0 ? null : maxRows);
                return bound;
            }

            bound.Kind = KBoundKind.Stats;
            bound.MinStats = Below(ref state, 8) == 0 ? null : minStats;
            bound.MaxStats = bound.MinStats == null ? maxStats : (Below(ref state, 8) == 0 ? null : maxStats);
            return bound;
        }

        static readonly uint[] SpecialFloatBits = new uint[]
        {
            0x7FC00000u, 0xFFC00000u, 0x7F800000u, 0xFF800000u, 0x4F000000u, 0xCF000000u,
            0x4EFFFFFFu, 0x4F000001u, 0xCEFFFFFFu, 0xCF000001u, 0x3F800000u, 0xBF800000u,
            0x7F7FFFFFu, 0xFF7FFFFFu, 0x00000000u, 0x80000000u, 0x36A12283u, 0xB6A12283u,
        };

        static uint SpecialOrSeeded(ref ulong state, int index)
        {
            return index % 37 == 0 ? SpecialFloatBits[index / 37 % SpecialFloatBits.Length] : (uint)Next(ref state);
        }

        static float BitsToFloat(uint bits)
        {
            return *(float*)&bits;
        }
    }
}
