using System;
using System.Runtime.InteropServices;
using Sv;
using Xunit;

public unsafe class BindingParkTests
{
    [Fact]
    public void InvalidateBindings_ParksWritesUntilRebind()
    {
        var schema = SchemaBuilder.Create().Stat("power", 10f).Build();
        var power = schema.Stat("power");
        using var board = Board.Create(schema, 64);
        var stale = stackalloc float[64];
        for (var i = 0; i < 64; i++)
        {
            stale[i] = -999f;
        }

        board.BindOutput(power, stale, 1, 64);
        board.Acquire();
        board.Recalculate();

        board.InvalidateBindings();
        Assert.Equal(1, board.PendingRebind);
        Assert.True(board.OutputColumn(power) == null);

        board.AddModifier(0, power, new Modifier(ModifierKind.Added, 5f));
        board.Recalculate();
        Assert.Equal(10f, stale[0]);
        for (var i = 1; i < 64; i++)
        {
            Assert.Equal(-999f, stale[i]);
        }

        var fresh = stackalloc float[64];
        board.BindOutput(power, fresh, 1, 64);
        Assert.Equal(0, board.PendingRebind);
        Assert.Equal(15f, fresh[0]);
    }

    [Fact]
    public void PendingRebind_CountsEachParkedStat()
    {
        var schema = SchemaBuilder.Create().Stat("power", 10f).Stat("speed", 5f).Build();
        using var board = Board.Create(schema, 8);
        var cap = board.Capacity;
        var a = stackalloc float[cap];
        var b = stackalloc float[cap];
        board.BindOutput(schema.Stat("power"), a, 1, cap);
        board.BindOutput(schema.Stat("speed"), b, 1, cap);
        Assert.Equal(0, board.PendingRebind);
        board.InvalidateBindings();
        Assert.Equal(2, board.PendingRebind);
        board.BindOutput(schema.Stat("power"), a, 1, cap);
        Assert.Equal(1, board.PendingRebind);
        board.BindOutput(schema.Stat("speed"), b, 1, cap);
        Assert.Equal(0, board.PendingRebind);
    }

    [Fact]
    public void BindOutput_RejectsColumnsTooShortForCapacity()
    {
        var schema = SchemaBuilder.Create().Stat("power", 10f).Build();
        using var board = Board.Create(schema, 64);
        var column = stackalloc float[64];
        Assert.Throws<ArgumentException>(() => board.BindOutput(schema.Stat("power"), column, 1, 63));
        Assert.Throws<ArgumentException>(() => board.BindOutput(schema.Stat("power"), column, 2, 64));
        Assert.Throws<ArgumentException>(() => board.BindExternal(schema.Stat("power"), column, 1, 63));
        board.BindOutput(schema.Stat("power"), column, 1, 64);
        board.InvalidateBindings();
        board.BindOutput(schema.Stat("power"), column, 2, 127);
    }

    [DllImport("libc", SetLastError = true)]
    static extern void* mmap(void* addr, nuint length, int prot, int flags, int fd, nint offset);

    [DllImport("libc", SetLastError = true)]
    static extern int mprotect(void* addr, nuint length, int prot);

    [DllImport("libc", SetLastError = true)]
    static extern int munmap(void* addr, nuint length);

    [Fact]
    public void ParkedWrites_NeverReachAFreedColumn()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        const int page = 4096;
        var region = (float*)mmap(null, (nuint)(page * 2), 3, 0x22, -1, 0);
        Assert.True(region != (float*)-1);
        try
        {
            var schema = SchemaBuilder.Create().Stat("power", 10f).Build();
            var power = schema.Stat("power");
            using var board = Board.Create(schema, 64);
            board.BindOutput(power, region, 1, page / sizeof(float));
            board.Acquire();
            board.Recalculate();
            board.InvalidateBindings();
            Assert.Equal(0, mprotect(region, (nuint)page, 0));
            board.AddModifier(0, power, new Modifier(ModifierKind.Added, 5f));
            board.Recalculate();
            Assert.Equal(0, mprotect(region, (nuint)page, 3));
            Assert.Equal(15f, board.Stat(0, power));
        }
        finally
        {
            munmap(region, (nuint)(page * 2));
        }
    }
}
