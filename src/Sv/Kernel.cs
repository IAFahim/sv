using System;

namespace Sv
{
/// <summary>
/// The chunk verbs as static kernels: the caller supplies the capacity the guards validate
/// against, so a context that holds only pointers, masks and an unmanaged <see cref="Clamp"/>
/// runs the whole surface — no <see cref="Board"/> instance, nothing managed on the path.
/// <see cref="Run(Column{int}, Window, Delta, Bounds, Column{int})"/> is the one verb over the
/// vocabulary; the clause-5 <c>Run*</c> names survive as aliases.
/// </summary>
public static unsafe class Kernel
{
    /// <summary>
    /// The one kernel verb: adds <paramref name="delta"/> to every row of <paramref name="window"/> inside
    /// <paramref name="value"/>, clamps with <paramref name="bounds"/>, and writes the applied delta to
    /// <paramref name="applied"/> (indexed by absolute slot — masked windows write their full span,
    /// disabled rows get zero; an empty applied column skips the report).
    /// </summary>
    public static void Run(Column<int> value, Window window, Delta delta, Bounds bounds, Column<int> applied = default)
    {
        var clamp = bounds.Clamp;
        var appliedPtr = applied.Pointer;
        if (window.Words == null)
        {
            Guard.ChunkRange(window.Start, window.Count, value.Length);
            if (appliedPtr != null && applied.Length < (long)window.Start + window.Count)
            {
                throw new ArgumentException($"sv: applied column of {applied.Length} rows does not cover the window's {(long)window.Start + window.Count}");
            }

            if (delta.PerRow == null)
            {
                IntrinsicsChunk(value.Pointer, delta.Uniform, clamp, window.Start, window.Count, value.Length, appliedPtr);
            }
            else
            {
                IntrinsicsChunk(value.Pointer, delta.PerRow, clamp, window.Start, window.Count, value.Length, appliedPtr);
            }

            return;
        }

        Guard.EnabledRange(window.Start, window.WordCount, value.Length);
        if (appliedPtr != null && applied.Length < (long)window.Start + (long)window.WordCount * 64)
        {
            throw new ArgumentException($"sv: applied column of {applied.Length} rows does not cover the window's {(long)window.Start + (long)window.WordCount * 64}");
        }

        if (delta.PerRow == null)
        {
            EnabledChunk(value.Pointer, delta.Uniform, clamp, window.Words, window.WordCount, window.Start, value.Length, appliedPtr);
        }
        else
        {
            EnabledChunk(value.Pointer, delta.PerRow, clamp, window.Words, window.WordCount, window.Start, value.Length, appliedPtr);
        }
    }

    public static void IntrinsicsChunk(int* value, int delta, in Clamp clamp, int start, int count, int capacity, int* applied = null)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        Guard.ChunkRange(start, count, capacity);
        Simd.Chunk(value, null, delta, clamp, start, count, applied);
    }

    public static void IntrinsicsChunk(int* value, int* delta, in Clamp clamp, int start, int count, int capacity, int* applied = null)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (delta == null)
        {
            throw new ArgumentNullException(nameof(delta));
        }

        Guard.ChunkRange(start, count, capacity);
        Simd.Chunk(value, delta, 0, clamp, start, count, applied);
    }

    /// <param name="applied">Delta column indexed by absolute slot; written for all 64 rows of the word — disabled rows get zero.</param>
    /// <param name="validBits">Rows of the word the columns are sized for; set mask bits past them are rejected. Full 64 keeps the word-complete contract.</param>
    public static void EnabledChunk(int* value, int delta, in Clamp clamp, ulong enabled, int offset, int capacity, int* applied = null, int validBits = 64)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        Guard.EnabledRange(offset, capacity);
        Guard.EnabledTail(enabled, validBits);
        Simd.EnabledWord(value, null, delta, clamp, enabled, offset, applied, validBits);
    }

    /// <param name="applied">Delta column indexed by absolute slot; written for all 64 rows of the word — disabled rows get zero.</param>
    /// <param name="validBits">Rows of the word the columns are sized for; set mask bits past them are rejected. Full 64 keeps the word-complete contract.</param>
    public static void EnabledChunk(int* value, int* delta, in Clamp clamp, ulong enabled, int offset, int capacity, int* applied = null, int validBits = 64)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (delta == null)
        {
            throw new ArgumentNullException(nameof(delta));
        }

        Guard.EnabledRange(offset, capacity);
        Guard.EnabledTail(enabled, validBits);
        Simd.EnabledWord(value, delta, 0, clamp, enabled, offset, applied, validBits);
    }

    /// <param name="value">Stat column indexed by absolute slot; written for every enabled row in the window.</param>
    /// <param name="delta">Uniform delta added to every enabled row.</param>
    /// <param name="clamp">Bounds applied after the add.</param>
    /// <param name="words">Enabled-mask words; bit i of word w marks row offset + w*64 + i.</param>
    /// <param name="wordCount">Number of mask words; the window spans wordCount * 64 rows from <paramref name="offset"/>.</param>
    /// <param name="offset">Absolute slot index of the window's first row.</param>
    /// <param name="capacity">Capacity the range guards validate against.</param>
    /// <param name="applied">Delta column indexed by absolute slot; must be sized for the full window from <paramref name="offset"/> — the entire window is written, disabled rows get zero.</param>
    public static void EnabledChunk(int* value, int delta, in Clamp clamp, ulong* words, int wordCount, int offset, int capacity, int* applied = null)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (words == null)
        {
            throw new ArgumentNullException(nameof(words));
        }

        Guard.EnabledRange(offset, wordCount, capacity);
        for (var w = 0; w < wordCount; w++)
        {
            Simd.EnabledWord(value, null, delta, clamp, words[w], offset + w * 64, applied, 64);
        }
    }

    /// <param name="value">Stat column indexed by absolute slot; written for every enabled row in the window.</param>
    /// <param name="delta">Per-slot delta column indexed by absolute slot.</param>
    /// <param name="clamp">Bounds applied after the add.</param>
    /// <param name="words">Enabled-mask words; bit i of word w marks row offset + w*64 + i.</param>
    /// <param name="wordCount">Number of mask words; the window spans wordCount * 64 rows from <paramref name="offset"/>.</param>
    /// <param name="offset">Absolute slot index of the window's first row.</param>
    /// <param name="capacity">Capacity the range guards validate against.</param>
    /// <param name="applied">Delta column indexed by absolute slot; must be sized for the full window from <paramref name="offset"/> — the entire window is written, disabled rows get zero.</param>
    public static void EnabledChunk(int* value, int* delta, in Clamp clamp, ulong* words, int wordCount, int offset, int capacity, int* applied = null)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (delta == null)
        {
            throw new ArgumentNullException(nameof(delta));
        }

        if (words == null)
        {
            throw new ArgumentNullException(nameof(words));
        }

        Guard.EnabledRange(offset, wordCount, capacity);
        for (var w = 0; w < wordCount; w++)
        {
            Simd.EnabledWord(value, delta, 0, clamp, words[w], offset + w * 64, applied, 64);
        }
    }
}
}
