using System;
using System.IO;
using System.Runtime.InteropServices;
using Sv;
using Xunit;

public unsafe class StateRollbackTests
{
    struct Xorshift
    {
        public ulong State;

        public ulong Next()
        {
            State ^= State << 13;
            State ^= State >> 7;
            State ^= State << 17;
            return State;
        }

        public int Below(int n) => (int)(Next() % (ulong)n);
    }

    static Schema Schema() => SchemaBuilder.Create()
        .Stat("str", 10f)
        .Stat("cap", 50f)
        .Derived("atk", 1f, ("str", 2f))
        .Intrinsic("hp")
        .Build();

    static void Mix(ref ulong hash, ulong value)
    {
        unchecked
        {
            hash = (hash ^ value) * 0x100000001B3ul;
            hash ^= hash >> 31;
        }
    }

    static int NthLive(Board board, int n)
    {
        for (var w = 0; w < board.LiveWordCount; w++)
        {
            var bits = board.LiveWord(w);
            while (bits != 0)
            {
                if (n-- == 0)
                {
                    return (w << 6) + System.Numerics.BitOperations.TrailingZeroCount(bits);
                }

                bits &= bits - 1;
            }
        }

        throw new InvalidOperationException();
    }

    static ulong Frame(Board board, ref Xorshift rng, long tick)
    {
        var stats = new[] { board.Schema.Stat("str"), board.Schema.Stat("cap"), board.Schema.Stat("atk") };
        for (var op = 0; op < 40; op++)
        {
            var roll = rng.Below(10);
            if ((roll < 2 || board.LiveCount == 0) && board.LiveCount < board.Capacity)
            {
                board.Acquire();
            }
            else if (roll < 3)
            {
                board.Release(NthLive(board, rng.Below(board.LiveCount)));
            }
            else if (roll < 8)
            {
                var slot = NthLive(board, rng.Below(board.LiveCount));
                var expires = rng.Below(3) == 0 ? long.MaxValue : tick + rng.Below(8);
                board.AddModifier(slot, stats[rng.Below(3)], new Modifier((ModifierKind)rng.Below(3), (rng.Below(9) - 4) * 0.375f), rng.Below(3), expires);
            }
            else
            {
                board.RemoveSource(NthLive(board, rng.Below(board.LiveCount)), rng.Below(3));
            }
        }

        board.ExpireModifiers(tick);
        board.Recalculate();
        ulong hash = 1469598103934665603ul;
        var mirror = board.MirrorBounds(stats[1]);
        for (var w = 0; w < board.LiveWordCount; w++)
        {
            Mix(ref hash, board.LiveWord(w));
            foreach (var stat in stats)
            {
                Mix(ref hash, board.ChangedWords(stat)[w]);
            }
        }

        for (var slot = 0; slot < board.Capacity; slot++)
        {
            foreach (var stat in stats)
            {
                Mix(ref hash, BitConverter.SingleToUInt32Bits(board.Output(stat)[slot]));
            }

            Mix(ref hash, (uint)mirror[slot]);
        }

        Mix(ref hash, (ulong)board.PendingExpiries);
        Mix(ref hash, (ulong)board.SetCount);
        Mix(ref hash, (ulong)board.LiveCount);
        return hash;
    }

    [Fact]
    public void BoardLoad_ReplaysBitIdentically_OnSameAndFreshBoards()
    {
        using var board = Board.Create(Schema(), 300);
        using var state = new SvState();
        var rng = new Xorshift { State = 0x2545F4914F6CDD1Dul };
        for (var tick = 0; tick < 40; tick++)
        {
            Frame(board, ref rng, tick);
        }

        board.SaveState(state);
        var savedRng = rng;
        var expected = new ulong[60];
        for (var i = 0; i < expected.Length; i++)
        {
            expected[i] = Frame(board, ref rng, 40 + i);
        }

        foreach (var fresh in new[] { false, true })
        {
            using var other = fresh ? Board.Create(Schema(), 300) : null;
            var target = other ?? board;
            if (fresh)
            {
                var noise = new Xorshift { State = 99 };
                for (var tick = 0; tick < 25; tick++)
                {
                    Frame(target, ref noise, tick);
                }
            }

            target.LoadState(state);
            rng = savedRng;
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.True(expected[i] == Frame(target, ref rng, 40 + i), $"fresh={fresh} frame {40 + i} diverged");
            }
        }
    }

    [Fact]
    public void BoardLoad_RejectsOtherShapes()
    {
        using var board = Board.Create(Schema(), 128);
        using var other = Board.Create(Schema(), 256);
        using var state = new SvState();
        board.SaveState(state);
        Assert.Throws<InvalidDataException>(() => other.LoadState(state));
        using var input = InputState.Create(1, 1, 4);
        Assert.Throws<InvalidDataException>(() => input.LoadState(state));
    }

    [Fact]
    public void BoardSaveLoad_WarmIsAllocationFree()
    {
        using var board = Board.Create(Schema(), 256);
        using var state = new SvState();
        var rng = new Xorshift { State = 7 };
        for (var tick = 0; tick < 20; tick++)
        {
            Frame(board, ref rng, tick);
        }

        board.SaveState(state);
        board.LoadState(state);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++)
        {
            board.SaveState(state);
            board.LoadState(state);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    public struct Ping
    {
        public int A;

        public long B;
    }

    [Fact]
    public void StreamsAndInput_RoundTripMidFrame()
    {
        using var streams = EventStreams.Create(2, 32);
        var c0 = streams.CreateChannel<Ping>(0);
        var c1 = streams.CreateChannel<Ping>(1);
        for (var i = 0; i < 50; i++)
        {
            c0.Write(new Ping { A = i, B = -i });
        }

        streams.Swap();
        streams.TryReadNext(out _, out _, out _);
        for (var i = 0; i < 30; i++)
        {
            c1.Write(new Ping { A = 1000 + i, B = i });
        }

        using var state = new SvState();
        streams.SaveState(state);
        using var copy = EventStreams.Create(2, 8);
        copy.CreateChannel<Ping>(0);
        copy.CreateChannel<Ping>(1);
        copy.LoadState(state);
        for (var round = 0; round < 2; round++)
        {
            while (true)
            {
                var a = streams.TryReadNext(out var ta, out var pa, out var sa);
                var b = copy.TryReadNext(out var tb, out var pb, out var sb);
                Assert.Equal(a, b);
                if (!a)
                {
                    break;
                }

                Assert.Equal(ta, tb);
                Assert.Equal(sa, sb);
                Assert.Equal(((Ping*)pa)->A, ((Ping*)pb)->A);
                Assert.Equal(((Ping*)pa)->B, ((Ping*)pb)->B);
            }

            streams.Swap();
            copy.Swap();
        }

        using var input = InputState.Create(2, 1, 8);
        var axes = stackalloc uint[2];
        var buttons = stackalloc ulong[1];
        for (var t = 0; t < 11; t++)
        {
            axes[0] = (uint)t;
            axes[1] = (uint)(t * 7);
            buttons[0] = (ulong)t * 3;
            input.Capture(axes, buttons);
        }

        input.SaveState(state);
        using var inputCopy = InputState.Create(2, 1, 8);
        inputCopy.LoadState(state);
        Assert.Equal(input.Tick, inputCopy.Tick);
        Assert.Equal(input.OldestRingTick, inputCopy.OldestRingTick);
        for (var b = 0; b < 64; b++)
        {
            Assert.Equal(input.Pressed(b), inputCopy.Pressed(b));
            Assert.Equal(input.Down(b), inputCopy.Down(b));
            Assert.Equal(input.Released(b), inputCopy.Released(b));
        }

        Assert.Equal(input.AxisBits(1), inputCopy.AxisBits(1));
    }
}
