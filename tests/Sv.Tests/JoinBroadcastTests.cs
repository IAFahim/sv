using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sv;
using Xunit;

public unsafe class JoinBroadcastTests
{
    static Schema Schema() =>
        SchemaBuilder.Create()
            .Stat("move_speed", 6f)
            .Stat("max_mana", 100f)
            .Build();

    [Fact]
    public void RejoiningPopulatedSet_WritesOnlyTheJoiningRow()
    {
        const int n = 4096;
        using var board = Board.Create(Schema(), n);
        var speed = board.Schema.Stat("move_speed");
        var column = (float*)NativeMemory.AllocZeroed(n * sizeof(float));
        try
        {
            board.BindOutput(speed, column);
            for (var i = 0; i < n; i++)
            {
                board.Acquire();
            }

            board.Recalculate();
            for (var i = 0; i < n; i++)
            {
                Assert.Equal(6f, column[i]);
                column[i] = -1f;
            }

            var haste = board.AddModifier(5, speed, new Modifier(ModifierKind.Added, 1f));
            board.Recalculate();
            Assert.Equal(7f, column[5]);
            board.RemoveModifier(5, speed, haste);
            board.Recalculate();
            Assert.Equal(6f, column[5]);
            for (var i = 0; i < n; i++)
            {
                if (i != 5)
                {
                    Assert.Equal(-1f, column[i]);
                }
            }
        }
        finally
        {
            NativeMemory.Free(column);
        }
    }

    [Fact]
    public void Bind_RebroadcastsEveryPopulatedSetOnNextRecalculate()
    {
        using var board = Board.Create(Schema(), 128);
        var speed = board.Schema.Stat("move_speed");
        var slots = new int[100];
        for (var i = 0; i < slots.Length; i++)
        {
            slots[i] = board.Acquire();
        }

        board.AddModifier(slots[3], speed, new Modifier(ModifierKind.Multi, 1f));
        board.Recalculate();

        var column = (float*)NativeMemory.AllocZeroed(128 * sizeof(float));
        try
        {
            board.BindOutput(speed, column);
            board.Recalculate();
            foreach (var slot in slots)
            {
                Assert.Equal(slot == slots[3] ? 12f : 6f, column[slot]);
            }
        }
        finally
        {
            NativeMemory.Free(column);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void RandomChurn_OutputMatchesPerSlotFold(int seed)
    {
        const int n = 256;
        var rng = new Random(seed);
        using var board = Board.Create(Schema(), n);
        var stats = new[] { board.Schema.Stat("move_speed"), board.Schema.Stat("max_mana") };
        var live = new List<int>();
        var handles = new Dictionary<int, List<(StatId stat, ModifierHandle handle)>>();
        var kinds = new[] { ModifierKind.Added, ModifierKind.Additive, ModifierKind.Multi };
        for (var frame = 0; frame < 200; frame++)
        {
            for (var op = 0; op < 40; op++)
            {
                var roll = rng.Next(10);
                if ((roll < 2 || live.Count == 0) && live.Count < n)
                {
                    var slot = board.Acquire();
                    live.Add(slot);
                    handles[slot] = new List<(StatId, ModifierHandle)>();
                }
                else if (roll < 3 && live.Count > 0)
                {
                    var at = rng.Next(live.Count);
                    board.Release(live[at]);
                    handles.Remove(live[at]);
                    live.RemoveAt(at);
                }
                else if (roll < 7 && live.Count > 0)
                {
                    var slot = live[rng.Next(live.Count)];
                    var stat = stats[rng.Next(stats.Length)];
                    var value = rng.Next(4) * 0.25f;
                    handles[slot].Add((stat, board.AddModifier(slot, stat, new Modifier(kinds[rng.Next(3)], value))));
                }
                else if (live.Count > 0)
                {
                    var slot = live[rng.Next(live.Count)];
                    var list = handles[slot];
                    if (list.Count > 0)
                    {
                        var at = rng.Next(list.Count);
                        board.RemoveModifier(slot, list[at].stat, list[at].handle);
                        list.RemoveAt(at);
                    }
                }
            }

            board.Recalculate();
            foreach (var slot in live)
            {
                foreach (var stat in stats)
                {
                    Assert.Equal(
                        BitConverter.SingleToUInt32Bits(board.Stat(slot, stat)),
                        BitConverter.SingleToUInt32Bits(board.Output(stat)[slot]));
                }
            }
        }
    }
}
