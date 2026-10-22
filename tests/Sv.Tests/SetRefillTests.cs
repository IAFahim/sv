using System;
using System.Runtime.InteropServices;
using Sv;
using Xunit;

public unsafe class SetRefillTests
{
    static Schema Schema() => SchemaBuilder.Create()
        .Stat("power", 10f)
        .Build();

    [Fact]
    public void BindOutput_AfterState_FillsColumnImmediately()
    {
        using var board = Board.Create(Schema(), 4);
        var power = board.Schema.Stat("power");
        var column = stackalloc float[4];
        NativeMemory.Clear(column, 4 * sizeof(float));
        var a = board.Acquire();
        var b = board.Acquire();
        board.AddModifier(a, power, new Modifier(ModifierKind.Added, 2f));
        board.AddModifier(b, power, new Modifier(ModifierKind.Added, 5f));
        board.Recalculate();

        board.BindOutput(power, column);
        Assert.Equal(12f, column[a]);
        Assert.Equal(15f, column[b]);
    }

    [Fact]
    public void LoadState_RefillsBoundColumnImmediately()
    {
        using var board = Board.Create(Schema(), 4);
        var power = board.Schema.Stat("power");
        var column = stackalloc float[4];
        NativeMemory.Clear(column, 4 * sizeof(float));
        var a = board.Acquire();
        board.AddModifier(a, power, new Modifier(ModifierKind.Added, 2f));
        board.Recalculate();
        board.BindOutput(power, column);
        Assert.Equal(12f, column[a]);

        using var saved = new SvState();
        board.SaveState(saved);
        var before = saved.Bytes.ToArray();

        board.AddModifier(a, power, new Modifier(ModifierKind.Added, 30f));
        board.Recalculate();
        Assert.Equal(42f, column[a]);

        board.LoadState(saved);
        Assert.Equal(12f, column[a]);

        using var resaved = new SvState();
        board.SaveState(resaved);
        Assert.True(before.AsSpan().SequenceEqual(resaved.Bytes), "save after load must be byte-identical with a bound column");
    }

    [Fact]
    public void BindRefill_LeavesChangedMaskClean()
    {
        using var board = Board.Create(Schema(), 4);
        var power = board.Schema.Stat("power");
        var column = stackalloc float[4];
        var a = board.Acquire();
        board.AddModifier(a, power, new Modifier(ModifierKind.Added, 2f));
        board.Recalculate();
        board.BindOutput(power, column);

        var words = board.ChangedWords(power);
        var snapshot = stackalloc ulong[1];
        snapshot[0] = words[0];

        board.BindOutput(power, column);
        var wordsAfter = board.ChangedWords(power);
        Assert.Equal(snapshot[0], wordsAfter[0]);
    }

    [Fact]
    public void BindOutput_BeforeAnyResolve_WritesNothing()
    {
        using var board = Board.Create(Schema(), 4);
        var power = board.Schema.Stat("power");
        var column = stackalloc float[4];
        NativeMemory.Clear(column, 4 * sizeof(float));
        var a = board.Acquire();
        board.BindOutput(power, column);
        Assert.Equal(0f, column[a]);

        board.AddModifier(a, power, new Modifier(ModifierKind.Added, 2f));
        board.Recalculate();
        Assert.Equal(12f, column[a]);
    }
}
