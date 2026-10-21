using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Sv.Tests;

// Roadmap issues/0058, atom 0.3: production pinned bit-for-bit against the frozen
// eager oracle — the intrinsics frame step (law L7's reference) and whole random
// stat programs (law L1's reference: every live pair equals the fold recomputed
// from scratch, dead and never-resolved pairs read zero).

public unsafe class EagerOracleTests
{
    static readonly float[] ValueLattice = [-3f, -1f, -0.5f, 0f, 1e-3f, 0.5f, 1f, 1.5f, 2f, 100f, 1e30f, -1e30f];

    static readonly int[] IntLattice = [int.MinValue, int.MaxValue, -1, 0, 1, 2, 25, -25, 1000000];

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void IntrinsicsFrameStep_MatchesProduction(int seed)
    {
        var random = new Random(seed * 7919);
        var schema = SchemaBuilder.Create().Intrinsic("mana").Intrinsic("hp").Build();
        using var board = Board.Create(schema, 256);
        for (var round = 0; round < 40; round++)
        {
            var wordCount = random.Next(1, 4);
            var count = 64 * wordCount;
            var offset = 64 * random.Next(0, 256 / 64 - wordCount + 1);
            var value = SeedInts(random, 256);
            var candidate = (int[])value.Clone();
            var reference = (int[])value.Clone();
            var applied = new int[256];
            var referenceApplied = new int[256];
            int[]? deltas = random.Next(2) == 0 ? null : SeedInts(random, 256);
            var boundMode = random.Next(3);
            int constantLo = 0;
            int constantHi = 0;
            int[]? minColumn = null;
            int[]? maxColumn = null;
            if (boundMode == 0)
            {
                constantLo = IntLattice[random.Next(IntLattice.Length)];
                constantHi = IntLattice[random.Next(IntLattice.Length)];
                if (constantLo > constantHi)
                {
                    (constantLo, constantHi) = (constantHi, constantLo);
                }

                minColumn = Replicate(constantLo, 256);
                maxColumn = Replicate(constantHi, 256);
            }
            else if (boundMode == 1)
            {
                maxColumn = SeedInts(random, 256);
            }
            else
            {
                minColumn = SeedInts(random, 256);
                maxColumn = SeedInts(random, 256);
            }

            ulong[]? words = random.Next(2) == 0 ? null : SeedWords(random, wordCount);
            var delta = Pick(random);
            EagerOracle.FrameStep(reference, deltas, delta, minColumn, maxColumn, words, offset, count, referenceApplied);
            var deltasAny = deltas ?? new int[1];
            var minAny = minColumn ?? new int[1];
            var maxAny = maxColumn ?? new int[1];
            var wordsAny = words ?? new ulong[1];
            fixed (int* v = candidate)
            fixed (int* a = applied)
            fixed (int* d = deltasAny)
            fixed (int* m = minAny)
            fixed (int* x = maxAny)
            fixed (ulong* w = wordsAny)
            {
                Clamp clamp = boundMode switch
                {
                    0 => Clamp.Constant(constantLo, constantHi),
                    1 => Clamp.Columns(null, x),
                    _ => Clamp.Columns(m, x),
                };
                if (words == null && deltas == null)
                {
                    board.RunIntrinsicsChunk(v, delta, clamp, offset, count, a);
                }
                else if (words == null)
                {
                    board.RunIntrinsicsChunk(v, d, clamp, offset, count, a);
                }
                else if (deltas == null)
                {
                    board.RunEnabledChunk(v, delta, clamp, w, wordCount, offset, a);
                }
                else
                {
                    board.RunEnabledChunk(v, d, clamp, w, wordCount, offset, a);
                }
            }

            for (var i = 0; i < 256; i++)
            {
                Assert.True(candidate[i] == reference[i] && applied[i] == referenceApplied[i],
                    $"round {round} seed {seed} row {i}: value {candidate[i]} != {reference[i]} or applied {applied[i]} != {referenceApplied[i]}" +
                    $" [offset={offset} wc={wordCount} mode={boundMode} words={(words == null ? "none" : string.Join(",", words.Select(w => w.ToString("X"))))}" +
                    $" min={(minColumn == null || i >= minColumn.Length ? "none" : minColumn[i].ToString())} max={(maxColumn == null || i >= maxColumn.Length ? "none" : maxColumn[i].ToString())}" +
                    $" delta={(deltas == null ? delta.ToString() : "varying " + deltas[i])} before={value[i]}]");
            }
        }
    }

    static int Pick(Random random) => IntLattice[random.Next(IntLattice.Length)];

    static int[] Replicate(int value, int count)
    {
        var values = new int[count];
        Array.Fill(values, value);
        return values;
    }

    static int[] SeedInts(Random random, int count)
    {
        var values = new int[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = random.Next(4) == 0 ? Pick(random) : random.Next(-500_000, 500_000);
        }

        return values;
    }

    static ulong[] SeedWords(Random random, int wordCount)
    {
        var words = new ulong[wordCount];
        for (var w = 0; w < wordCount; w++)
        {
            var pick = random.Next(4);
            words[w] = pick == 0 ? ulong.MaxValue : pick == 1 ? 0 : (ulong)random.NextInt64(long.MinValue, long.MaxValue);
        }

        return words;
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public void RandomProgram_RecalculateMatchesOracle(int seed)
    {
        var random = new Random(seed);
        var schema = SchemaBuilder.Create()
            .Stat("str", 10f)
            .Stat("armor", 5f)
            .Stat("haste", 0f)
            .Stat("bonus", 0.25f)
            .Derived("atk", 1f, ("str", 2f))
            .Derived("defense", 1f, ("armor", 1.5f), ("haste", 0.25f))
            .Derived("power", 2f, ("atk", 0.125f))
            .Intrinsic("hp")
            .Build();
        var stats = new[]
        {
            schema.Stat("str"), schema.Stat("armor"), schema.Stat("haste"), schema.Stat("bonus"),
            schema.Stat("atk"), schema.Stat("defense"), schema.Stat("power"),
        };
        var bases = new float[] { 10f, 5f, 0f, 0.25f, 1f, 1f, 2f };
        var sources = new (int stat, float coefficient)[7][];
        sources[0] = [];
        sources[1] = [];
        sources[2] = [];
        sources[3] = [];
        sources[4] = [(0, 2f)];
        sources[5] = [(1, 1.5f), (2, 0.25f)];
        sources[6] = [(4, 0.125f)];

        using var board = Board.Create(schema, 64);
        var model = new EagerModel(bases, sources);
        model.Grow(board.Capacity);
        var live = new List<int>();
        var handles = new List<(int Slot, int Stat, int Id, int Source, long Expiry)>();
        long tick = 0;
        for (var i = 0; i < 32; i++)
        {
            var slot = board.Acquire();
            model.Acquire(slot);
            live.Add(slot);
        }

        for (var op = 0; op < 600; op++)
        {
            var pick = random.Next(10);
            if (pick <= 2)
            {
                var slot = live[random.Next(live.Count)];
                var stat = random.Next(7);
                var kind = random.Next(3);
                var value = ValueLattice[random.Next(ValueLattice.Length)];
                var source = random.Next(4);
                var expiry = random.Next(10) < 3 ? tick + random.Next(1, 40) : long.MaxValue;
                var handle = board.AddModifier(slot, stats[stat], new Modifier((ModifierKind)kind, value), source, expiry);
                model.AddModifier(slot, stat, (byte)kind, value, source, expiry);
                handles.Add((slot, stat, handle.Id, source, expiry));
            }
            else if (pick == 3 && handles.Count > 0)
            {
                var index = random.Next(handles.Count);
                var (slot, stat, id, _, _) = handles[index];
                board.RemoveModifier(slot, stats[stat], new ModifierHandle(id));
                model.RemoveModifier(slot, stat, id);
                handles.RemoveAt(index);
            }
            else if (pick == 4)
            {
                var slot = live[random.Next(live.Count)];
                var source = random.Next(4);
                board.RemoveSource(slot, source);
                model.RemoveSource(slot, source);
                handles.RemoveAll(h => h.Slot == slot && h.Source == source);
            }
            else if (pick == 5)
            {
                tick += random.Next(1, 6);
                board.ExpireModifiers(tick);
                model.Expire(tick);
                handles.RemoveAll(h => h.Expiry <= tick);
            }
            else if (pick == 6 && live.Count < 60)
            {
                var slot = board.Acquire();
                model.Acquire(slot);
                live.Add(slot);
            }
            else if (pick == 7 && live.Count > 4)
            {
                var index = random.Next(live.Count);
                var slot = live[index];
                board.Release(slot);
                model.Release(slot);
                live.RemoveAt(index);
                handles.RemoveAll(h => h.Slot == slot);
            }
            else
            {
                board.Recalculate();
                model.Recalculate();
                for (var stat = 0; stat < 7; stat++)
                {
                    var column = board.Output(stats[stat]);
                    for (var slot = 0; slot < board.Capacity; slot++)
                    {
                        var got = BitConverter.SingleToUInt32Bits(column[slot]);
                        var want = BitConverter.SingleToUInt32Bits(model.Value(slot, stat));
                        Assert.True(got == want,
                            $"seed {seed} op {op} tick {tick} stat {stat} slot {slot}: board {got:X8} != oracle {want:X8}");
                    }
                }
            }
        }

        board.Recalculate();
        model.Recalculate();
        Assert.True(live.Count > 4 && handles.Count > 0, "program was too passive");
    }
}
