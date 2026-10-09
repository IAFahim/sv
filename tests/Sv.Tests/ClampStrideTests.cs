using System;
using Sv;
using Xunit;

public unsafe class ClampStrideTests
{
    [Fact]
    public void ClampStat_RejectsStridedBoundColumn()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("floor", 0f).Stat("ceiling", 10f).Intrinsic("hp").Build(), 64);
        var floor = board.Schema.Stat("floor");
        var ceiling = board.Schema.Stat("ceiling");
        var interleaved = stackalloc float[128];
        board.BindOutput(floor, interleaved, 2);
        board.BindOutput(ceiling, interleaved + 1, 2);
        Assert.Throws<ArgumentException>(() => Clamp.Stat(board, floor, ceiling));
        Assert.Throws<ArgumentException>(() => Clamp.StatMax(board, ceiling));
        Assert.Throws<ArgumentException>(() => Clamp.StatMin(board, floor));
        var contiguous = stackalloc float[64];
        board.BindOutput(ceiling, contiguous);
        Clamp.StatMax(board, ceiling);
    }
}
