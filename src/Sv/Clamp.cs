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

    readonly int* minGenerationSlot;

    readonly int* maxGenerationSlot;

    readonly int minGeneration;

    readonly int maxGeneration;

    Clamp(ClampKind kind, int min, int max, int* minColumn, int* maxColumn, float* minStat = null, float* maxStat = null, int* minGenerationSlot = null, int* maxGenerationSlot = null, int minGeneration = -1, int maxGeneration = -1)
    {
        Kind = kind;
        Min = min;
        Max = max;
        MinColumn = minColumn;
        MaxColumn = maxColumn;
        MinStatColumn = minStat;
        MaxStatColumn = maxStat;
        this.minGenerationSlot = minGenerationSlot;
        this.maxGenerationSlot = maxGenerationSlot;
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
        var min = ResolveColumn(board, minStat, out var minSlot, out var minGeneration);
        var max = ResolveColumn(board, maxStat, out var maxSlot, out var maxGeneration);
        return new Clamp(ClampKind.StatBounds, 0, 0, null, null, min, max, minSlot, maxSlot, minGeneration, maxGeneration);
    }

    public static Clamp StatMax(Board board, StatId maxStat)
    {
        var max = ResolveColumn(board, maxStat, out var maxSlot, out var maxGeneration);
        return new Clamp(ClampKind.StatBounds, 0, 0, null, null, null, max, null, maxSlot, -1, maxGeneration);
    }

    public static Clamp StatMin(Board board, StatId minStat)
    {
        var min = ResolveColumn(board, minStat, out var minSlot, out var minGeneration);
        return new Clamp(ClampKind.StatBounds, 0, 0, null, null, min, null, minSlot, null, minGeneration, -1);
    }

    /// <summary>
    /// Clamps against the stats' sv-owned int mirrors (<see cref="Board.MirrorBounds"/>): the
    /// clause-16 conversion runs once per broadcast instead of once per row per op. Same results
    /// as <see cref="Stat"/> whenever the bound columns are written only by broadcast.
    /// </summary>
    public static Clamp StatMirror(Board board, StatId minStat, StatId maxStat)
    {
        return Columns(board.MirrorBounds(minStat), board.MirrorBounds(maxStat));
    }

    public static Clamp StatMirrorMax(Board board, StatId maxStat)
    {
        return Columns(null, board.MirrorBounds(maxStat));
    }

    public static Clamp StatMirrorMin(Board board, StatId minStat)
    {
        return Columns(board.MirrorBounds(minStat), null);
    }

    static float* ResolveColumn(Board board, StatId stat, out int* generationSlot, out int generation)
    {
        var external = board.OutputColumn(stat);
        if (external != null)
        {
            if (board.OutputStrideAt(stat.Index) != 1)
            {
                throw new ArgumentException($"sv: Clamp.Stat reads its bound column contiguously; stat {stat.Index} is bound with stride {board.OutputStrideAt(stat.Index)}");
            }

            generationSlot = board.BoundGenerationPtrAt(stat.Index);
            generation = *generationSlot;
            Guard.BindingFresh(generation);
            return external;
        }

        generationSlot = null;
        generation = -1;
        return board.OutputPtr(stat);
    }

    [System.Diagnostics.Conditional("SV_CHECKED")]
    internal void CheckStatBounds()
    {
        if (Kind != ClampKind.StatBounds)
        {
            return;
        }

        if (minGeneration >= 0 && *minGenerationSlot != minGeneration)
        {
            throw new InvalidOperationException("sv: stale binding — storage moved; rebind");
        }

        if (maxGeneration >= 0 && *maxGenerationSlot != maxGeneration)
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
                {
                    var lo = MinColumn != null ? MinColumn[slot] : int.MinValue;
                    var hi = MaxColumn != null ? MaxColumn[slot] : int.MaxValue;
                    if (lo > hi)
                    {
                        lo = hi;
                    }

                    return value < lo ? lo : value > hi ? hi : value;
                }
            case ClampKind.StatBounds:
                {
                    var lo = MinStatColumn != null ? BoundOf(MinStatColumn[slot]) : int.MinValue;
                    var hi = MaxStatColumn != null ? BoundOf(MaxStatColumn[slot]) : int.MaxValue;
                    if (lo > hi)
                    {
                        lo = hi;
                    }

                    return value < lo ? lo : value > hi ? hi : value;
                }
            default:
                return value;
        }
    }

    public static int BoundOf(float value)
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
