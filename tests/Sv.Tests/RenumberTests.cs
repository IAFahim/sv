using System;
using Sv;
using Xunit;

public unsafe class RenumberTests
{
    static Schema Schema() =>
        SchemaBuilder.Create()
            .Stat("move_speed", 6f)
            .Stat("max_mana", 100f)
            .Intrinsic("mana", 0, 1000)
            .Build();

    [Fact]
    public void RenumberSlots_PreservesStatFolds_BitExact()
    {
        using var board = Board.Create(Schema(), 128);
        var speed = board.Schema.Stat("move_speed");
        var mana = board.Schema.Stat("max_mana");
        var rng = new Random(20260930);
        for (var i = 0; i < 64; i++)
        {
            Assert.Equal(i, board.Acquire());
        }

        for (var i = 0; i < 64; i++)
        {
            board.AddModifier(i, speed, new Modifier(ModifierKind.Added, rng.Next(-20, 20)));
            board.AddModifier(i, speed, new Modifier(ModifierKind.Multi, rng.Next(-3, 4)));
            board.AddModifier(i, mana, new Modifier(ModifierKind.Additive, rng.Next(1, 5)));
        }

        board.Recalculate();
        var expectedSpeed = new uint[128];
        var expectedMana = new uint[128];
        for (var slot = 0; slot < 64; slot++)
        {
            expectedSpeed[slot] = BitConverter.SingleToUInt32Bits(board.Stat(slot, speed));
            expectedMana[slot] = BitConverter.SingleToUInt32Bits(board.Stat(slot, mana));
        }

        for (var slot = 3; slot < 64; slot += 4)
        {
            board.Release(slot);
        }

        Assert.Equal(48, board.LiveCount);
        var map = new int[board.Capacity];
        for (var i = 0; i < map.Length; i++)
        {
            map[i] = -1;
        }

        var next = 0;
        for (var slot = 0; slot < 64; slot++)
        {
            if (slot % 4 == 3)
            {
                continue;
            }

            map[slot] = next++;
        }

        board.RenumberSlots(map, board.LiveCount);
        board.Recalculate();

        for (var slot = 0; slot < 64; slot++)
        {
            if (map[slot] < 0)
            {
                continue;
            }

            var target = map[slot];
            Assert.Equal(expectedSpeed[slot], BitConverter.SingleToUInt32Bits(board.Stat(target, speed)));
            Assert.Equal(expectedMana[slot], BitConverter.SingleToUInt32Bits(board.Stat(target, mana)));
            Assert.Equal(expectedSpeed[slot], BitConverter.SingleToUInt32Bits(board.OutputPtr(speed)[target]));
            Assert.Equal(expectedMana[slot], BitConverter.SingleToUInt32Bits(board.OutputPtr(mana)[target]));
        }
    }

    [Fact]
    public void RenumberSlots_KeepsAcquireReturningLowestFree()
    {
        using var board = Board.Create(Schema(), 64);
        for (var i = 0; i < 48; i++)
        {
            Assert.Equal(i, board.Acquire());
        }

        for (var slot = 0; slot < 48; slot += 2)
        {
            board.Release(slot);
        }

        var map = new int[board.Capacity];
        for (var i = 0; i < map.Length; i++)
        {
            map[i] = -1;
        }

        var next = 0;
        for (var slot = 1; slot < 48; slot += 2)
        {
            map[slot] = next++;
        }

        board.RenumberSlots(map, board.LiveCount);
        var acquired = new int[board.LiveCount];
        for (var i = 0; i < acquired.Length; i++)
        {
            acquired[i] = board.Acquire();
        }

        Array.Sort(acquired);
        for (var i = 0; i < acquired.Length; i++)
        {
            Assert.Equal(24 + i, acquired[i]);
        }
    }

    [Fact]
    public void RenumberSlots_RejectsBadMaps()
    {
        using var board = Board.Create(Schema(), 64);
        board.Acquire();
        board.Acquire();
        var map = new int[board.Capacity];
        Assert.Throws<InvalidOperationException>(() => board.RenumberSlots(map, 3));
        map.AsSpan().Fill(-1);
        Assert.Throws<ArgumentException>(() => board.RenumberSlots(map, 2));
        map[0] = 0;
        map[1] = 0;
        Assert.Throws<ArgumentException>(() => board.RenumberSlots(map, 2));
        map[1] = 63;
        board.RenumberSlots(map, 2);
        Assert.Equal(2, board.LiveCount);
        Assert.Equal(1, board.Acquire());
    }

    [Fact]
    public void ColumnsPermute_MatchesScalarOracle()
    {
        var column = stackalloc float[16];
        var oracle = new float[16];
        var rng = new Random(20260931);
        for (var i = 0; i < 16; i++)
        {
            column[i] = rng.NextSingle() * 100f;
            oracle[i] = column[i];
        }

        var map = new int[16] { 2, -1, 0, 5, 1, -1, 3, 9, 4, 7, 6, -1, 8, 11, 10, 12 };
        var expected = new float[13];
        for (var old = 0; old < 16; old++)
        {
            if (map[old] >= 0)
            {
                expected[map[old]] = oracle[old];
            }
        }

        Columns.Permute(column, map, 13);
        for (var i = 0; i < 13; i++)
        {
            Assert.Equal(BitConverter.SingleToUInt32Bits(expected[i]), BitConverter.SingleToUInt32Bits(column[i]));
        }
    }

    [Fact]
    public void ColumnsPermute_OutOfRangeTarget_Throws()
    {
        var column = stackalloc int[8];
        for (var i = 0; i < 8; i++)
        {
            column[i] = i;
        }

        var map = new int[8] { 3, -1, -1, -1, -1, -1, -1, -1 };
        Assert.Throws<ArgumentOutOfRangeException>(() => Columns.Permute(column, map, 2));
        Assert.Equal(0, column[0]);
    }
}
