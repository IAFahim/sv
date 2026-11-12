using System;

namespace Sv
{
    /// <summary>
    /// The rows a kernel run touches: <see cref="Range(int, int)"/> for a contiguous span, or
    /// <see cref="Masked(int, ulong*, int)"/> for a 64-row-word enable mask starting at any offset —
    /// bit i of word w marks row start + w*64 + i, and the window spans wordCount × 64 rows.
    /// <see langword="default"/> is the empty range.
    /// </summary>
    public readonly unsafe struct Window
    {
        public int Start { get; }

        public int Count { get; }

        public ulong* Words { get; }

        public int WordCount { get; }

        Window(int start, int count, ulong* words, int wordCount)
        {
            Start = start;
            Count = count;
            Words = words;
            WordCount = wordCount;
        }

        public static Window Range(int start, int count)
        {
            if (count < 0)
            {
                throw new ArgumentException($"sv: range window of {count} rows is negative");
            }

            return new Window(start, count, null, 0);
        }

        public static Window Masked(int start, ulong* words, int wordCount)
        {
            if (words == null)
            {
                throw new ArgumentNullException(nameof(words));
            }

            if (wordCount < 1)
            {
                throw new ArgumentException($"sv: masked window of {wordCount} words is empty");
            }

            return new Window(start, 0, words, wordCount);
        }
    }
}
