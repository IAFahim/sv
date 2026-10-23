using System;

namespace Sv;

/// <summary>
/// What a kernel run adds to each row: <see cref="Delta(int)"/> for one value everywhere, or
/// <see cref="Delta(int*)"/> for a column indexed by absolute slot. <see langword="default"/> is uniform zero.
/// </summary>
public readonly unsafe struct Delta
{
    public int Uniform { get; }

    public int* PerRow { get; }

    Delta(int uniform, int* perRow)
    {
        Uniform = uniform;
        PerRow = perRow;
    }

    public Delta(int uniform) => this = new Delta(uniform, null);

    public Delta(int* perRow)
    {
        if (perRow == null)
        {
            throw new ArgumentNullException(nameof(perRow));
        }

        this = new Delta(0, perRow);
    }
}
