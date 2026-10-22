using System;
using System.Collections.Generic;
using Sv;
using Xunit;

public unsafe class SparseChangeRowsTests
{
    [Theory]
    [InlineData(12)]
    [InlineData(345)]
    public void SparseIndices_MatchTheDenseMask_EveryFrame(int seed)
    {
        const int n = 300;
        var rng = new Random(seed);
        using var board = Board.Create(SchemaBuilder.Create().Stat("a", 3f).Stat("b", 8f).Build(), n);
        var stats = new[] { board.Schema.Stat("a"), board.Schema.Stat("b") };
        var words = board.LiveWordCount;
        var live = new List<int>();
        var handles = new Dictionary<int, List<(StatId Stat, ModifierHandle Handle)>>();
        for (var frame = 0; frame < 60; frame++)
        {
            for (var op = 0; op < 25; op++)
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

            board.ExpireModifiers(frame);
            board.Recalculate();
            for (var s = 0; s < 2; s++)
            {
                var mask = board.ChangedWords(stats[s]);
                var indices = board.ChangedWordIndices(stats[s]);
                var count = board.ChangedWordCount(stats[s]);
                Assert.InRange(count, 0, words);

                var seen = new HashSet<int>();
                ulong union = 0;
                for (var i = 0; i < count; i++)
                {
                    Assert.True(seen.Add(indices[i]), $"stat {s}: word {indices[i]} listed twice");
                    Assert.NotEqual(0ul, mask[indices[i]]);
                    union |= mask[indices[i]];
                }

                ulong dense = 0;
                for (var w = 0; w < words; w++)
                {
                    dense |= mask[w];
                }

                Assert.Equal(dense, union);
                for (var w = 0; w < words; w++)
                {
                    var listed = seen.Contains(w);
                    Assert.Equal(listed, mask[w] != 0);
                }
            }
        }
    }

    [Fact]
    public void SparseView_SurvivesSaveLoad_AndSaveIsByteStable()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("a", 3f).Stat("b", 8f).Build(), 128);
        var stats = new[] { board.Schema.Stat("a"), board.Schema.Stat("b") };
        for (var i = 0; i < 24; i++)
        {
            board.Acquire();
        }

        board.AddModifier(3, stats[0], new Modifier(ModifierKind.Multi, 1.5f));
        board.AddModifier(9, stats[1], new Modifier(ModifierKind.Added, 2f));
        board.Recalculate();
        var counts = new int[2];
        var indices = new int[2][];
        var bits = new ulong[2][];
        var words = board.LiveWordCount;
        for (var s = 0; s < 2; s++)
        {
            counts[s] = board.ChangedWordCount(stats[s]);
            indices[s] = new int[counts[s]];
            bits[s] = new ulong[words];
            var source = board.ChangedWordIndices(stats[s]);
            for (var i = 0; i < counts[s]; i++)
            {
                indices[s][i] = source[i];
            }

            var mask = board.ChangedWords(stats[s]);
            for (var w = 0; w < words; w++)
            {
                bits[s][w] = mask[w];
            }
        }

        Assert.True(counts[0] > 0 && counts[1] > 0, "both stats must have changed rows");

        using var saved = new SvState();
        board.SaveState(saved);
        var before = saved.Bytes.ToArray();
        board.LoadState(saved);
        for (var s = 0; s < 2; s++)
        {
            Assert.Equal(counts[s], board.ChangedWordCount(stats[s]));
            var source = board.ChangedWordIndices(stats[s]);
            for (var i = 0; i < counts[s]; i++)
            {
                Assert.Equal(indices[s][i], source[i]);
            }

            var mask = board.ChangedWords(stats[s]);
            for (var w = 0; w < words; w++)
            {
                Assert.Equal(bits[s][w], mask[w]);
            }
        }

        using var resaved = new SvState();
        board.SaveState(resaved);
        Assert.True(before.AsSpan().SequenceEqual(resaved.Bytes), "save after load must be byte-identical");
    }

    [Fact]
    public void UntouchedStat_HasZeroChangedWords()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("a", 3f).Stat("b", 0f).Build(), 64);
        var a = board.Schema.Stat("a");
        var b = board.Schema.Stat("b");
        var slot = board.Acquire();
        Assert.Equal(0, board.ChangedWordCount(a));
        board.AddModifier(slot, a, new Modifier(ModifierKind.Added, 1f));
        board.Recalculate();
        Assert.True(board.ChangedWordCount(a) >= 1);
        Assert.Equal(0, board.ChangedWordCount(b));
    }
}
