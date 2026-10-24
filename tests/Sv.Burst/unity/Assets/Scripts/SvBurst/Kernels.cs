using System.Runtime.InteropServices;
using Unity.Burst;

namespace Sv.Burst
{
    public enum KOpKind : byte
    {
        ChunkUniform = 0,
        ChunkPerRow = 1,
        MaskedUniform = 2,
        MaskedPerRow = 3,
    }

    public enum KBoundKind : byte
    {
        None = 0,
        Constant = 1,
        Rows = 2,
        Stats = 3,
    }

    /// <summary>
    /// Flat bound descriptor mirroring <c>Sv.Clamp</c>: Constant carries <see cref="Min"/>/<see cref="Max"/>,
    /// Rows carries int bound columns, Stats carries float stat columns (converted per clause 16);
    /// a null side of any column pair means that bound is absent (int.MinValue/int.MaxValue).
    /// </summary>
    public unsafe struct KBound
    {
        public KBoundKind Kind;
        public int Min;
        public int Max;
        public int* MinRows;
        public int* MaxRows;
        public float* MinStats;
        public float* MaxStats;
    }

    /// <summary>
    /// One kernel invocation mirroring <c>Sv.Kernel.Run</c> over <c>Sv.Window</c>/<c>Sv.Delta</c>:
    /// Chunk kinds run the <see cref="Start"/>..Start+<see cref="Count"/> range, Masked kinds run
    /// <see cref="WordCount"/> enabled words of <see cref="Words"/> from <see cref="Start"/>.
    /// </summary>
    public unsafe struct KOp
    {
        public KOpKind Kind;
        public KBound Bound;
        public int Start;
        public int Count;
        public int Uniform;
        public int* PerRow;
        public ulong* Words;
        public int WordCount;
    }

    /// <summary>Value and applied columns, indexed by absolute slot.</summary>
    public unsafe struct KState
    {
        public int* Value;
        public int* Applied;
    }

    /// <summary>
    /// The sv chunk kernels in the Burst-compiled subset: the scalar formulation of
    /// <c>Sv.Simd.ChunkScalar</c>/<c>EnabledWordScalar</c> over <c>Sv.Clamp.Apply</c> — saturating
    /// add, ternary clamps, clause-16 stat bound conversion, word-complete applied semantics —
    /// with no <c>System.Runtime.Intrinsics</c> and no BCL call Burst cannot compile, so the same
    /// source text builds under .NET and under Burst at <see cref="FloatMode.Strict"/>.
    /// </summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Strict)]
    public static unsafe class Kernels
    {
        /// <summary>The function-pointer signature Unity hosts compile with <c>BurstCompiler.CompileFunctionPointer</c>.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public unsafe delegate void RunProgramDelegate(KState* state, KOp* ops, int opCount);

        [BurstCompile(FloatPrecision.Standard, FloatMode.Strict)]
        public static void RunProgram(KState* state, KOp* ops, int opCount)
        {
            for (var i = 0; i < opCount; i++)
            {
                ref var op = ref ops[i];
                if (op.Kind == KOpKind.ChunkUniform)
                {
                    Chunk(state, op.Start, op.Count, null, op.Uniform, ref op.Bound);
                }
                else if (op.Kind == KOpKind.ChunkPerRow)
                {
                    Chunk(state, op.Start, op.Count, op.PerRow, 0, ref op.Bound);
                }
                else if (op.Kind == KOpKind.MaskedUniform)
                {
                    Masked(state, op.Start, op.WordCount, op.Words, null, op.Uniform, ref op.Bound);
                }
                else
                {
                    Masked(state, op.Start, op.WordCount, op.Words, op.PerRow, 0, ref op.Bound);
                }
            }
        }

        static int SaturatingAdd(int a, int b)
        {
            var sum = a + b;
            return ((a ^ sum) & (b ^ sum)) < 0 ? (b < 0 ? int.MinValue : int.MaxValue) : sum;
        }

        static int SaturatingSubtract(int after, int before)
        {
            var diff = after - before;
            return ((after ^ before) & (after ^ diff)) < 0 ? (after < 0 ? int.MinValue : int.MaxValue) : diff;
        }

        static int Apply(ref KBound bound, int value, int slot)
        {
            if (bound.Kind == KBoundKind.Constant)
            {
                return value < bound.Min ? bound.Min : value > bound.Max ? bound.Max : value;
            }

            if (bound.Kind == KBoundKind.Rows)
            {
                var lo = bound.MinRows != null ? bound.MinRows[slot] : int.MinValue;
                var hi = bound.MaxRows != null ? bound.MaxRows[slot] : int.MaxValue;
                if (lo > hi)
                {
                    lo = hi;
                }

                return value < lo ? lo : value > hi ? hi : value;
            }

            if (bound.Kind == KBoundKind.Stats)
            {
                var lo = bound.MinStats != null ? Bcl.BoundOf(bound.MinStats[slot]) : int.MinValue;
                var hi = bound.MaxStats != null ? Bcl.BoundOf(bound.MaxStats[slot]) : int.MaxValue;
                if (lo > hi)
                {
                    lo = hi;
                }

                return value < lo ? lo : value > hi ? hi : value;
            }

            return value;
        }

        static void Chunk(KState* state, int start, int count, int* delta, int uniform, ref KBound bound)
        {
            for (var r = 0; r < count; r++)
            {
                var slot = start + r;
                var before = state->Value[slot];
                var sum = SaturatingAdd(before, delta != null ? delta[slot] : uniform);
                var after = Apply(ref bound, sum, slot);
                state->Value[slot] = after;
                if (state->Applied != null)
                {
                    state->Applied[slot] = SaturatingSubtract(after, before);
                }
            }
        }

        static void Masked(KState* state, int start, int wordCount, ulong* words, int* delta, int uniform, ref KBound bound)
        {
            for (var w = 0; w < wordCount; w++)
            {
                var word = words[w];
                var offset = start + w * 64;
                var mask = word;
                while (mask != 0)
                {
                    var bit = Bcl.Tzcnt(mask);
                    mask &= mask - 1;
                    var slot = offset + bit;
                    var before = state->Value[slot];
                    var sum = SaturatingAdd(before, delta != null ? delta[slot] : uniform);
                    var after = Apply(ref bound, sum, slot);
                    state->Value[slot] = after;
                    if (state->Applied != null)
                    {
                        state->Applied[slot] = SaturatingSubtract(after, before);
                    }
                }

                if (state->Applied == null)
                {
                    continue;
                }

                var skipped = ~word;
                while (skipped != 0)
                {
                    var bit = Bcl.Tzcnt(skipped);
                    skipped &= skipped - 1;
                    state->Applied[offset + bit] = 0;
                }
            }
        }
    }
}
