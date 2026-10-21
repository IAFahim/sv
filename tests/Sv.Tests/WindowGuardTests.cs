using System;
using Frent;
using Sv;
using Sv.Frent;
using Xunit;

namespace Sv.Tests;

// Atom 1.1 (roadmap issues/0058, issue 0062): every window seam validates exactly —
// offset inside the chunk, wordCount spanning exactly the chunk's remaining rows,
// applied/deltas spans covering what the fast path writes, and range guards that
// cannot overflow in int. These pin the throws the guard-page fuzz proved missing.

[Collection("Frent")]
public unsafe class WindowGuardTests
{
    static Schema ManaSchema() => SchemaBuilder.Create().Intrinsic("mana").Build();

    static SvSlot[] Slots(int start, int count)
    {
        var slots = new SvSlot[count];
        for (var i = 0; i < count; i++)
        {
            slots[i] = new SvSlot(start + i);
        }

        return slots;
    }

    [Fact]
    public void ChunkSeam_RunChunk_ShortApplied_Throws()
    {
        using var board = Board.Create(ManaSchema(), 128);
        var rows = new int[64];
        Assert.Throws<ArgumentException>(() => ChunkSeam.RunChunk(board, rows, Slots(0, 64), 7, Clamp.Constant(0, 100), new int[32]));
    }

    [Fact]
    public void ChunkSeam_RunChunk_ShortDeltas_Throws()
    {
        using var board = Board.Create(ManaSchema(), 128);
        var rows = new int[64];
        var deltas = new int[32];
        Assert.Throws<ArgumentException>(() => ChunkSeam.RunChunk(board, rows, deltas, Slots(0, 64), Clamp.Constant(0, 100), default));
    }

    [Fact]
    public void ChunkSeam_RunEnabledChunk_NonExactWords_Throws()
    {
        using var board = Board.Create(ManaSchema(), 128);
        var rows = new int[64];
        var words = new ulong[3];
        var threw = false;
        fixed (ulong* w = words)
        {
            try
            {
                ChunkSeam.RunEnabledChunk(board, rows, Slots(0, 64), w, 3, 0, 7, Clamp.Constant(0, 100), default);
            }
            catch (ArgumentException)
            {
                threw = true;
            }
        }

        Assert.True(threw);
    }

    [Fact]
    public void ChunkSeam_RunEnabledChunk_ShortApplied_Throws()
    {
        using var board = Board.Create(ManaSchema(), 128);
        var rows = new int[64];
        var words = new ulong[1];
        var applied = new int[32];
        var threw = false;
        fixed (ulong* w = words)
        {
            try
            {
                ChunkSeam.RunEnabledChunk(board, rows, Slots(0, 64), w, 1, 0, 7, Clamp.Constant(0, 100), applied);
            }
            catch (ArgumentException)
            {
                threw = true;
            }
        }

        Assert.True(threw);
    }

    [Fact]
    public void Population_Apply_ShortApplied_Throws()
    {
        using var world = new World();
        using var population = new Population(world, ManaSchema(), 256);
        for (var i = 0; i < 64; i++)
        {
            population.Bind(world.Create(new Mana { Value = 40 }));
        }

        foreach (var window in population.Chunks<Mana>())
        {
            var threw = false;
            try
            {
                window.Apply(population.Board, 7, Clamp.Constant(0, 100), new int[1]);
            }
            catch (ArgumentException)
            {
                threw = true;
            }

            Assert.True(threw);
            return;
        }
    }

    [Fact]
    public void Population_ApplyEnabled_OffsetOutsideChunk_Throws()
    {
        using var world = new World();
        using var population = new Population(world, ManaSchema(), 512);
        for (var i = 0; i < 256; i++)
        {
            population.Bind(world.Create(new Mana { Value = 40 }));
        }

        var words = new ulong[8];
        foreach (var window in population.Chunks<Mana>())
        {
            var threwOutside = false;
            var threwUnaligned = false;
            fixed (ulong* w = words)
            {
                try
                {
                    window.ApplyEnabled(population.Board, w, 1, 256, 7, Clamp.Constant(0, 100), default);
                }
                catch (ArgumentException)
                {
                    threwOutside = true;
                }

                try
                {
                    window.ApplyEnabled(population.Board, w, 1, 1, 7, Clamp.Constant(0, 100), default);
                }
                catch (ArgumentException)
                {
                    threwUnaligned = true;
                }
            }

            Assert.True(threwOutside);
            Assert.True(threwUnaligned);
            return;
        }
    }

    [Fact]
    public void Population_ApplyEnabled_WordCountNotExact_Throws()
    {
        using var world = new World();
        using var population = new Population(world, ManaSchema(), 512);
        for (var i = 0; i < 256; i++)
        {
            population.Bind(world.Create(new Mana { Value = 40 }));
        }

        var words = new ulong[16];
        foreach (var chunk in world.Query<Mana, SvSlot>().EnumerateChunks<Mana, SvSlot>())
        {
            chunk.Deconstruct(out _, out var slots);
            Assert.True(ChunkSeam.TrySlotRange(slots, out var start, out var count));
            var exact = (count + 63) >> 6;
            var threw = false;
            foreach (var window in population.Chunks<Mana>())
            {
                fixed (ulong* w = words)
                {
                    try
                    {
                        window.ApplyEnabled(population.Board, w, exact + 1, start, 7, Clamp.Constant(0, 100), default);
                    }
                    catch (ArgumentException)
                    {
                        threw = true;
                    }
                }

                break;
            }

            Assert.True(threw);
            return;
        }
    }

    [Fact]
    public void Board_RangeOverflow_ThrowsInsteadOfWrapping()
    {
        using var board = Board.Create(ManaSchema(), 1024);
        var value = new int[64];
        var words = new ulong[1];
        var threwChunk = false;
        var threwEnabled = false;
        fixed (int* v = value)
        fixed (ulong* w = words)
        {
            try
            {
                board.RunIntrinsicsChunk(v, 7, Clamp.Constant(0, 100), int.MaxValue - 10, 20, null);
            }
            catch (ArgumentOutOfRangeException)
            {
                threwChunk = true;
            }

            try
            {
                board.RunEnabledChunk(v, 7, Clamp.Constant(0, 100), w, 1, int.MaxValue - 63, null);
            }
            catch (ArgumentOutOfRangeException)
            {
                threwEnabled = true;
            }
        }

        Assert.True(threwChunk);
        Assert.True(threwEnabled);
    }
}
