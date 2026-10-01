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

    readonly Board? board;

    readonly int minStatIndex;

    readonly int maxStatIndex;

    readonly int minGeneration;

    readonly int maxGeneration;

    Clamp(ClampKind kind, int min, int max, int* minColumn, int* maxColumn, float* minStat = null, float* maxStat = null, Board? board = null, int minStatIndex = -1, int maxStatIndex = -1, int minGeneration = -1, int maxGeneration = -1)
    {
        Kind = kind;
        Min = min;
        Max = max;
        MinColumn = minColumn;
        MaxColumn = maxColumn;
        MinStatColumn = minStat;
        MaxStatColumn = maxStat;
        this.board = board;
        this.minStatIndex = minStatIndex;
        this.maxStatIndex = maxStatIndex;
        this.minGeneration = minGeneration;
        this.maxGeneration = maxGeneration;
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
        var min = ResolveColumn(board, minStat, out var minGeneration);
        var max = ResolveColumn(board, maxStat, out var maxGeneration);
        return new Clamp(ClampKind.StatBounds, 0, 0, null, null, min, max, board, minStat.Index, maxStat.Index, minGeneration, maxGeneration);
    }

    public static Clamp StatMax(Board board, StatId maxStat)
    {
        var max = ResolveColumn(board, maxStat, out var maxGeneration);
        return new Clamp(ClampKind.StatBounds, 0, 0, null, null, null, max, board, -1, maxStat.Index, -1, maxGeneration);
    }

    public static Clamp StatMin(Board board, StatId minStat)
    {
        var min = ResolveColumn(board, minStat, out var minGeneration);
        return new Clamp(ClampKind.StatBounds, 0, 0, null, null, min, null, board, minStat.Index, -1, minGeneration, -1);
    }

    static float* ResolveColumn(Board board, StatId stat, out int generation)
    {
        var external = board.OutputColumn(stat);
        if (external != null)
        {
            generation = board.BoundGenerationAt(stat.Index);
            Guard.BindingFresh(generation);
            return external;
        }

        generation = -1;
        return board.OutputPtr(stat);
    }

    [System.Diagnostics.Conditional("SV_CHECKED")]
    internal void CheckStatBounds()
    {
        if (Kind != ClampKind.StatBounds || board == null)
        {
            return;
        }

        if (minGeneration >= 0 && board.BoundGenerationAt(minStatIndex) != minGeneration)
        {
            throw new InvalidOperationException("sv: stale binding — storage moved; rebind");
        }

        if (maxGeneration >= 0 && board.BoundGenerationAt(maxStatIndex) != maxGeneration)
        {
            throw new InvalidOperationException("sv: stale binding — storage moved; rebind");
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public int Apply(int value, int slot)
    {
        CheckStatBounds();
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

    static int BoundOf(float value)
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
