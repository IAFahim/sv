using System;
using System.Runtime.InteropServices;

namespace Sv;

public static unsafe class Columns
{
    /// <summary>
    /// Applies a reconcile permutation to a host-owned slot column in place:
    /// entries with a non-negative <paramref name="oldToNew"/> value scatter to their new index;
    /// negative entries (dead slots) are skipped. Call with the span returned by
    /// <c>Population.Reconcile</c> before reading the column again.
    /// </summary>
    public static void Permute<T>(T* column, ReadOnlySpan<int> oldToNew, int count) where T : unmanaged
    {
        if (column == null)
        {
            throw new ArgumentNullException(nameof(column));
        }

        if (count < 0 || count > oldToNew.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "sv: permute count must be in [0, oldToNew.Length]");
        }

        if (count == 0)
        {
            return;
        }

        var temp = (T*)NativeMemory.Alloc((nuint)count * (nuint)sizeof(T));
        for (var old = 0; old < oldToNew.Length; old++)
        {
            var next = oldToNew[old];
            if (next >= 0)
            {
                if (next >= count)
                {
                    NativeMemory.Free(temp);
                    throw new ArgumentOutOfRangeException(nameof(oldToNew), $"sv: permute maps slot {old} to {next} outside [0, {count})");
                }

                temp[next] = column[old];
            }
        }

        for (var i = 0; i < count; i++)
        {
            column[i] = temp[i];
        }

        NativeMemory.Free(temp);
    }
}
