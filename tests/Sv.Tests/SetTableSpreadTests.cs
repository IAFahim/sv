using System;
using System.IO;
using Sv;
using Xunit;

public unsafe class SetTableSpreadTests
{
    static Schema TestSchema() => SchemaBuilder.Create()
        .Stat("power", 1f)
        .Intrinsic("hp")
        .Build();

    [Fact]
    public void ContentHash_FloatOnlyDifferences_SpreadAcrossLowBuckets()
    {
        using var table = new SetTable();
        var entries = stackalloc StatEntry[1];
        var buckets = new bool[32];
        for (var i = 1; i <= 200; i++)
        {
            entries[0] = new StatEntry(ModifierKind.Multi, 5f * i);
            var hash = table.HashOf(0, entries, 1);
            buckets[(int)(hash & 31ul)] = true;
        }

        var used = 0;
        foreach (var used_ in buckets)
        {
            if (used_)
            {
                used++;
            }
        }

        Assert.True(used >= 16, $"200 value-only-distinct sets landed in {used}/32 low buckets");
    }

    [Fact]
    public void Resolve_ManyDistinctValues_KeepsLoadAtMostHalf()
    {
        using var table = new SetTable();
        var entries = stackalloc StatEntry[1];
        var first = -1;
        for (var i = 1; i <= 300; i++)
        {
            entries[0] = new StatEntry(ModifierKind.Multi, 0.37f * i);
            var set = table.Resolve(0, entries, 1);
            if (first < 0)
            {
                first = set;
            }

            Assert.True(table.BucketCount >= i * 2, $"load exceeded 50% at {i} sets (buckets {table.BucketCount})");
        }

        entries[0] = new StatEntry(ModifierKind.Multi, 0.37f);
        Assert.Equal(first, table.Resolve(0, entries, 1));
    }

    [Fact]
    public void BoardLoadState_PreFormatV2State_IsRejectedLoudly()
    {
        using var board = Board.Create(TestSchema(), 64);
        using var state = new SvState();
        var slot = board.Acquire();
        board.AddModifier(slot, board.Schema.Stat("power"), new Modifier(ModifierKind.Added, 1f));
        board.Recalculate();
        board.SaveState(state);

        var bytes = state.Bytes.ToArray();
        Assert.Equal((byte)'S', bytes[0]);
        var shape = BitConverter.ToInt64(bytes, 4);
        BitConverter.TryWriteBytes(bytes.AsSpan(4), shape ^ 1L);
        using var stale = new SvState();
        unsafe
        {
            fixed (byte* pinned = bytes)
            {
                stale.BeginWrite();
                stale.Write(pinned, bytes.Length);
            }
        }

        Assert.Throws<InvalidDataException>(() => board.LoadState(stale));
    }
}
