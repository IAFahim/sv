using System;

namespace Sv;

/// <summary>
/// The chunk verbs as static kernels: the caller supplies the capacity the guards validate
/// against, so a context that holds only pointers, masks and an unmanaged <see cref="Clamp"/>
/// runs the whole surface — no <see cref="Board"/> instance, nothing managed on the path.
/// </summary>
public static unsafe class Kernel
{
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

    public static void EnabledChunk(int* value, int delta, in Clamp clamp, ulong enabled, int offset, int capacity, int* applied = null)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        Guard.EnabledRange(offset, capacity);
        Simd.EnabledWord(value, null, delta, clamp, enabled, offset, applied);
    }

    public static void EnabledChunk(int* value, int* delta, in Clamp clamp, ulong enabled, int offset, int capacity, int* applied = null)
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
        Simd.EnabledWord(value, delta, 0, clamp, enabled, offset, applied);
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
            Simd.EnabledWord(value, null, delta, clamp, words[w], offset + w * 64, applied);
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
            Simd.EnabledWord(value, delta, 0, clamp, words[w], offset + w * 64, applied);
        }
    }
}
