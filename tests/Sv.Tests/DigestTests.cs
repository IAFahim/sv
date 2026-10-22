using System;
using System.Runtime.InteropServices;
using Sv;
using Xunit;

public unsafe class DigestTests
{
    static Schema Schema() => SchemaBuilder.Create()
        .Stat("power", 10f)
        .Stat("resolve", 5f)
        .Build();

    static void RunProgram(Board board, int[] slots)
    {
        var power = board.Schema.Stat("power");
        var resolve = board.Schema.Stat("resolve");
        for (var round = 0L; round < 5; round++)
        {
            for (var i = 0; i < slots.Length; i++)
            {
                board.AddModifier(slots[i], power, new Modifier(ModifierKind.Added, i % 3), 0, round + 1);
                if (i == round % slots.Length)
                {
                    board.AddModifier(slots[i], resolve, new Modifier(ModifierKind.Multi, 1f + round / 3));
                }
            }

            board.Recalculate();
            AssertDigestLaw(board);
            Assert.Equal(4, board.ExpireModifiers(round + 1));
            board.Recalculate();
            AssertDigestLaw(board);
        }
    }

    [Fact]
    public void IncrementalDigest_MatchesFromScratch_UnderChurn()
    {
        using var board = Board.Create(Schema(), 16);
        var slots = new int[4];
        for (var i = 0; i < 4; i++)
        {
            slots[i] = board.Acquire();
        }

        RunProgram(board, slots);
        var spawned = stackalloc int[3];
        board.Spawn(slots[0], 3, spawned);
        board.Recalculate();
        AssertDigestLaw(board);

        board.Release(slots[1]);
        board.Recalculate();
        AssertDigestLaw(board);

        using var saved = new SvState();
        board.SaveState(saved);
        board.AddModifier(slots[2], board.Schema.Stat("power"), new Modifier(ModifierKind.Added, 7f));
        board.Recalculate();
        AssertDigestLaw(board);
        board.LoadState(saved);
        AssertDigestLaw(board);
    }

    [Fact]
    public void BulkSpawnDigest_IsDeferredThenCorrect()
    {
        using var board = Board.Create(Schema(), 65536);
        var slots = stackalloc int[40000];
        var template = board.Acquire();
        var power = board.Schema.Stat("power");
        board.AddModifier(template, power, new Modifier(ModifierKind.Added, 2f));
        board.Recalculate();
        AssertDigestLaw(board);

        var column = (float*)NativeMemory.AllocZeroed(65536 * sizeof(float));
        try
        {
            board.BindOutput(power, column);
            board.Spawn(template, 40000, slots);
            AssertDigestLaw(board);
            for (var i = 0; i < 40000; i++)
            {
                Assert.Equal(12f, column[slots[i]]);
            }
        }
        finally
        {
            NativeMemory.Free(column);
        }
    }

    static void AssertDigestLaw(Board board)
    {
        var incremental = board.Digest;
        Assert.Equal(incremental, board.RecomputeDigest());
    }

    [Fact]
    public void IdenticalPrograms_DigestEqual()
    {
        using var a = Board.Create(Schema(), 16);
        using var b = Board.Create(Schema(), 16);
        var slotsA = new int[4];
        var slotsB = new int[4];
        for (var i = 0; i < 4; i++)
        {
            slotsA[i] = a.Acquire();
            slotsB[i] = b.Acquire();
        }

        RunProgram(a, slotsA);
        RunProgram(b, slotsB);
        Assert.Equal(a.Digest, b.Digest);

        a.AddModifier(slotsA[0], a.Schema.Stat("power"), new Modifier(ModifierKind.Added, 99f));
        a.Recalculate();
        Assert.NotEqual(a.Digest, b.Digest);
    }

    [Fact]
    public void PageDigests_LocalizeTheChangedSlot()
    {
        using var a = Board.Create(Schema(), 16);
        using var b = Board.Create(Schema(), 16);
        var slotsA = new int[4];
        var slotsB = new int[4];
        for (var i = 0; i < 4; i++)
        {
            slotsA[i] = a.Acquire();
            slotsB[i] = b.Acquire();
        }

        RunProgram(a, slotsA);
        RunProgram(b, slotsB);
        a.AddModifier(slotsA[2], a.Schema.Stat("resolve"), new Modifier(ModifierKind.Added, 3f));
        a.Recalculate();

        var resolve = 1;
        var pages = a.PageCount;
        Assert.Equal(resolve * pages + (slotsA[2] >> 8), a.FirstDivergentPage(b));
        Assert.Equal(resolve * pages + (slotsA[2] >> 8), b.FirstDivergentPage(a));
    }
}
