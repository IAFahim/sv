using System;

namespace Sv;

public enum ClampKind : byte
{
    None = 0,
    Constant = 1,
    Columns = 2,
    StatBounds = 3,
}

public readonly unsafe struct Clamp
{
    public ClampKind Kind { get; }

    public int Min { get; }

    public int Max { get; }

    public int* MinColumn { get; }

    public int* MaxColumn { get; }

    public float* MinStatColumn { get; }

    public float* MaxStatColumn { get; }

    Clamp(ClampKind kind, int min, int max, int* minColumn, int* maxColumn, float* minStat = null, float* maxStat = null)
    {
        Kind = kind;
        Min = min;
        Max = max;
        MinColumn = minColumn;
        MaxColumn = maxColumn;
        MinStatColumn = minStat;
        MaxStatColumn = maxStat;
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

    public static Clamp Stat(Board board, StatId minStat, StatId maxStat)
    {
        var min = board.OutputColumn(minStat);
        var max = board.OutputColumn(maxStat);
        if (min == null && max == null)
        {
            throw new ArgumentException("sv: Clamp.Stat requires bound output columns");
        }

        return new Clamp(ClampKind.StatBounds, 0, 0, null, null, min, max);
    }

    public static Clamp StatMax(Board board, StatId maxStat)
    {
        var max = board.OutputColumn(maxStat);
        if (max == null)
        {
            throw new ArgumentException("sv: Clamp.StatMax requires a bound output column");
        }

        return new Clamp(ClampKind.StatBounds, 0, 0, null, null, null, max);
    }

    public static Clamp StatMin(Board board, StatId minStat)
    {
        var min = board.OutputColumn(minStat);
        if (min == null)
        {
            throw new ArgumentException("sv: Clamp.StatMin requires a bound output column");
        }

        return new Clamp(ClampKind.StatBounds, 0, 0, null, null, min, null);
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
            case ClampKind.StatBounds:
                if (MinStatColumn != null)
                {
                    var lo = BoundOf(MinStatColumn[slot]);
                    if (value < lo)
                    {
                        return lo;
                    }
                }

                if (MaxStatColumn != null)
                {
                    var hi = BoundOf(MaxStatColumn[slot]);
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

    internal static int BoundOf(float value)
    {
        if (float.IsNaN(value))
        {
            return 0;
        }

        if (value >= 2147483648.0f)
        {
            return int.MaxValue;
        }

        if (value < -2147483648.0f)
        {
            return int.MinValue;
        }

        return unchecked((int)value);
    }
}
