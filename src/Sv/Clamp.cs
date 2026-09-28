using System;

namespace Sv;

public enum ClampKind : byte
{
    None = 0,
    Constant = 1,
    Columns = 2,
}

public readonly unsafe struct Clamp
{
    public ClampKind Kind { get; }

    public int Min { get; }

    public int Max { get; }

    public int* MinColumn { get; }

    public int* MaxColumn { get; }

    Clamp(ClampKind kind, int min, int max, int* minColumn, int* maxColumn)
    {
        Kind = kind;
        Min = min;
        Max = max;
        MinColumn = minColumn;
        MaxColumn = maxColumn;
    }

    public static Clamp Constant(int min, int max)
    {
        if (min > max)
        {
            throw new ArgumentException($"sv: clamp constant min {min} > max {max}");
        }

        return new Clamp(ClampKind.Constant, min, max, null, null);
    }

    public static Clamp Columns(int* min, int* max)
    {
        if (min == null && max == null)
        {
            throw new ArgumentException("sv: clamp columns require at least one bound");
        }

        return new Clamp(ClampKind.Columns, 0, 0, min, max);
    }

    public int Apply(int value, int slot)
    {
        switch (Kind)
        {
            case ClampKind.Constant:
                return value < Min ? Min : value > Max ? Max : value;
            case ClampKind.Columns:
                if (MinColumn != null)
                {
                    var lo = MinColumn[slot];
                    if (value < lo)
                    {
                        return lo;
                    }
                }

                if (MaxColumn != null)
                {
                    var hi = MaxColumn[slot];
                    if (value > hi)
                    {
                        return hi;
                    }
                }

                return value;
            default:
                return value;
        }
    }
}
