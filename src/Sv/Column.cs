using System;

namespace Sv;

/// <summary>
/// A (pointer, length) pair over unmanaged storage, validated at construction — the window guards
/// validate against <see cref="Length"/>, so a kernel needs no separate capacity. A null pointer is
/// only legal with length zero (the empty column).
/// </summary>
public readonly unsafe struct Column<T> where T : unmanaged
{
    public T* Pointer { get; }

    public int Length { get; }

    public Column(T* pointer, int length)
    {
        if (length < 0 || (pointer == null && length != 0))
        {
            throw new ArgumentException($"sv: column of {length} rows cannot wrap {(pointer == null ? "null" : "non-null")} storage");
        }

        Pointer = pointer;
        Length = length;
    }

    public static Column<T> Empty => new(null, 0);
}
