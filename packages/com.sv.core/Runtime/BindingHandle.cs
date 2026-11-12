namespace Sv
{
    /// <summary>
    /// Typed handle over an external output binding. The captured pointer is valid from its bind
    /// generation until the next invalidate/rebind; <see cref="IsStale"/> reports that boundary.
    /// Hosts owning growable storage must rebind at reconcile.
    /// </summary>
    public readonly unsafe struct BindingHandle
    {
        public StatId Stat { get; }

        public float* Column { get; }

        public int Stride { get; }

        public int Generation { get; }

        internal BindingHandle(StatId stat, float* column, int stride, int generation)
        {
            Stat = stat;
            Column = column;
            Stride = stride;
            Generation = generation;
        }

        public bool IsStale(Board board) => board.BoundGenerationAt(Stat.Index) != Generation;
    }
}
