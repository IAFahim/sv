using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Sv;
using Xunit;

public unsafe class ChangedMaskTests
{
    [Theory]
    [InlineData(12)]
    [InlineData(345)]
    public void ChangedWords_AreExactlyTheRowsWhoseBitsChanged(int seed)
    {
        const int n = 900;
        var rng = new Random(seed);
        using var board = Board.Create(SchemaBuilder.Create().Stat("a", 3f).Stat("b", 8f).Build(), n);
        var stats = new[] { board.Schema.Stat("a"), board.Schema.Stat("b") };
        var cap = board.Capacity;
        var words = board.LiveWordCount;
        var external = (float*)NativeMemory.AllocZeroed((nuint)cap * 2 * sizeof(float));
        var before = new float[2][] { new float[cap], new float[cap] };
        var live = new List<int>();
        var handles = new Dictionary<int, List<(StatId, ModifierHandle)>>();
        try
        {
            for (var frame = 0; frame < 100; frame++)
            {
                for (var op = 0; op < 50; op++)
                {
                    var roll = rng.Next(10);
                    if ((roll < 2 || live.Count == 0) && live.Count < n)
                    {
                        var slot = board.Acquire();
                        live.Add(slot);
                        handles[slot] = new();
                    }
                    else if (roll < 3)
                    {
                        var at = rng.Next(live.Count);
                        board.Release(live[at]);
                        handles.Remove(live[at]);
                        live.RemoveAt(at);
                    }
                    else if (roll < 7)
                    {
                        var slot = live[rng.Next(live.Count)];
                        var stat = stats[rng.Next(2)];
                        handles[slot].Add((stat, board.AddModifier(slot, stat, new Modifier((ModifierKind)rng.Next(3), rng.Next(-2, 3) * 0.5f), 0, frame + rng.Next(1, 6))));
                    }
                    else
                    {
                        var slot = live[rng.Next(live.Count)];
                        board.RemoveSource(slot, 0);
                        handles[slot].Clear();
                    }
                }

                if (frame == 40)
                {
                    var map = new int[cap];
                    Array.Fill(map, -1);
                    var next = 0;
                    var renamed = new Dictionary<int, List<(StatId, ModifierHandle)>>();
                    foreach (var slot in live.OrderByDescending(x => x))
                    {
                        map[slot] = next;
                        renamed[next++] = handles[slot];
                    }

                    board.RenumberSlots(map, live.Count);
                    live = Enumerable.Range(0, live.Count).ToList();
                    handles = renamed;
                }

                if (frame == 70)
                {
                    board.BindOutput(stats[1], external, 2);
                }

                board.ExpireModifiers(frame);
                for (var s = 0; s < 2; s++)
                {
                    for (var row = 0; row < cap; row++)
                    {
                        before[s][row] = Read(board, stats[s], external, frame >= 70 && s == 1, row);
                    }
                }

                board.Recalculate();
                for (var s = 0; s < 2; s++)
                {
                    var mask = board.ChangedWords(stats[s]);
                    for (var row = 0; row < cap; row++)
                    {
                        var after = Read(board, stats[s], external, frame >= 70 && s == 1, row);
                        var expected = BitConverter.SingleToUInt32Bits(before[s][row]) != BitConverter.SingleToUInt32Bits(after);
                        var actual = (mask[row >> 6] & (1ul << (row & 63))) != 0;
                        Assert.True(expected == actual, $"frame {frame} stat {s} row {row}: expected changed={expected}");
                    }

                    for (var w = cap >> 6; w < words; w++)
                    {
                        Assert.Equal(0ul, mask[w]);
                    }
                }
            }
        }
        finally
        {
            NativeMemory.Free(external);
        }
    }

    static float Read(Board board, StatId stat, float* external, bool strided, int row)
    {
        return strided ? external[row * 2] : board.Output(stat)[row];
    }

    [Fact]
    public void ChangedWords_FeedTheRunWalk()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("cap", 10f).Intrinsic("hp").Build(), 128);
        var cap = board.Schema.Stat("cap");
        for (var i = 0; i < 128; i++)
        {
            board.Acquire();
        }

        board.Recalculate();
        board.AddModifier(3, cap, new Modifier(ModifierKind.Added, 5f));
        board.AddModifier(100, cap, new Modifier(ModifierKind.Added, -5f));
        board.Recalculate();
        var hp = stackalloc int[128];
        board.RunEnabledChunk(hp, 1, Clamp.StatMax(board, cap), board.ChangedWords(cap), board.LiveWordCount, 0);
        for (var i = 0; i < 128; i++)
        {
            Assert.Equal(i == 3 || i == 100 ? 1 : 0, hp[i]);
        }
    }
}
