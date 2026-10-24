using System;
using Sv;
using Xunit;

public unsafe class ShrinkTests
{
    static Schema TestSchema() => SchemaBuilder.Create()
        .Stat("power", 10f)
        .Build();

    [Fact]
    public void ModifierBurst_RecalculateShrinksRecordPoolExactly()
    {
        using var board = Board.Create(TestSchema(), 256);
        var power = board.Schema.Stat("power");
        for (var i = 0; i < 256; i++)
        {
            board.Acquire();
        }

        for (var i = 0; i < 100; i++)
        {
            board.AddModifier(0, power, new Modifier(ModifierKind.Added, 1f));
        }

        board.Recalculate();
        for (var i = 0; i < 32769; i++)
        {
            board.AddModifier((i >> 7) & 255, power, new Modifier(ModifierKind.Added, 1f));
        }

        var before = board.MemoryBytes;
        board.Recalculate();
        var after = board.MemoryBytes;
        Assert.True(before - after is > 1352000 and <= 1365424, $"record-pool slack reclaimed: {before - after} (max 1365424, bounded first-touch growth below)");
    }

    [Fact]
    public void MassAcquire_RecalculateShrinksDirtyTouchedSlack()
    {
        using var board = Board.Create(TestSchema(), 65536);
        for (var i = 0; i < 65536; i++)
        {
            board.Acquire();
        }

        var before = board.MemoryBytes;
        board.Recalculate();
        var after = board.MemoryBytes;
        Assert.Equal((65536 - 64) * sizeof(int), before - after);
    }

    [Fact]
    public void LoadState_FreesExcessPages_MemoryReturnsToBaseline()
    {
        using var fresh = Board.Create(TestSchema(), 8192);
        using var touched = Board.Create(TestSchema(), 8192);
        var far = touched.Acquire();
        Assert.True(touched.MemoryBytes > fresh.MemoryBytes);

        using var empty = new SvState();
        fresh.SaveState(empty);
        touched.LoadState(empty);
        Assert.Equal(fresh.MemoryBytes, touched.MemoryBytes);
    }

    [Fact]
    public void EventWave_ThenQuietSwaps_ShrinksTheArena()
    {
        using var streams = EventStreams.Create(1, 4096);
        var channel = streams.CreateChannel<int>();
        for (var i = 0; i < 65536; i++)
        {
            channel.Write(i);
        }

        streams.Swap();
        while (streams.TryReadNext(out _, out _, out _))
        {
        }

        var grown = streams.ArenaBytes;
        for (var swap = 0; swap < 24; swap++)
        {
            streams.Swap();
            while (streams.TryReadNext(out _, out _, out _))
            {
            }
        }

        Assert.True(streams.ArenaBytes < grown / 2, $"arena {streams.ArenaBytes} did not shrink below half of {grown}");
    }
}
