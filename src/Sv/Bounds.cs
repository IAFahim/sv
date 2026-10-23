namespace Sv;

/// <summary>
/// The bounds a kernel run clamps with, as factories over the unmanaged <see cref="Clamp"/>:
/// <see langword="default"/> is none. <see cref="Rows(int*, int*)"/> takes at least one bound column
/// indexed by absolute slot; <see cref="Stats(Board, StatId?, StatId?)"/> captures stat bounds at
/// construction (validated, generation-checked — atom 77's shape).
/// </summary>
public readonly unsafe struct Bounds
{
    public Clamp Clamp { get; }

    Bounds(Clamp clamp)
    {
        Clamp = clamp;
    }

    public static Bounds None => default;

    public static Bounds Constant(int min, int max) => new(Clamp.Constant(min, max));

    public static Bounds Rows(int* min, int* max) => new(Clamp.Columns(min, max));

    public static Bounds Stats(Board board, StatId? minStat, StatId? maxStat)
    {
        if (minStat.HasValue && maxStat.HasValue)
        {
            return new(Clamp.Stat(board, minStat.Value, maxStat.Value));
        }

        if (maxStat.HasValue)
        {
            return new(Clamp.StatMax(board, maxStat.Value));
        }

        if (minStat.HasValue)
        {
            return new(Clamp.StatMin(board, minStat.Value));
        }

        return default;
    }
}
