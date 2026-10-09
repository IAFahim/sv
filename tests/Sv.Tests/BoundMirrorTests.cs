using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Sv;
using Xunit;

public unsafe class BoundMirrorTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(99)]
    public void MirrorClamp_MatchesStatClamp_ThroughChurnRenumberAndRebind(int seed)
    {
        const int n = 700;
        var rng = new Random(seed);
        using var board = Board.Create(SchemaBuilder.Create().Stat("floor", -10f).Stat("ceiling", 50f).Intrinsic("hp").Build(), n);
        var floor = board.Schema.Stat("floor");
        var ceiling = board.Schema.Stat("ceiling");
        var cap = board.Capacity;
        var external = (float*)NativeMemory.AllocZeroed((nuint)cap * 2 * sizeof(float));
        var a = (int*)NativeMemory.AllocZeroed((nuint)cap * sizeof(int));
        var b = (int*)NativeMemory.AllocZeroed((nuint)cap * sizeof(int));
        var appliedA = (int*)NativeMemory.AllocZeroed((nuint)cap * sizeof(int));
        var appliedB = (int*)NativeMemory.AllocZeroed((nuint)cap * sizeof(int));
        var delta = (int*)NativeMemory.AllocZeroed((nuint)cap * sizeof(int));
        var words = (ulong*)NativeMemory.AllocZeroed((nuint)(cap / 64) * sizeof(ulong));
        var floorInts = (int*)NativeMemory.AllocZeroed((nuint)cap * sizeof(int));
        var ceilingInts = (int*)NativeMemory.AllocZeroed((nuint)cap * sizeof(int));
        var live = new List<int>();
        var values = new[] { 0.5f, -0.75f, 3e9f, -4e9f, 1e30f, 2.25f, -1f, 0.1f };
        try
        {
            for (var i = 0; i < n / 2; i++)
            {
                live.Add(board.Acquire());
            }

            board.Recalculate();
            board.MirrorBounds(floor);
            board.MirrorBounds(ceiling);
            for (var frame = 0; frame < 80; frame++)
            {
                for (var op = 0; op < 30; op++)
                {
                    var roll = rng.Next(6);
                    if (roll == 0 && live.Count < n)
                    {
                        live.Add(board.Acquire());
                    }
                    else if (roll == 1 && live.Count > 1)
                    {
                        var at = rng.Next(live.Count);
                        board.Release(live[at]);
                        live.RemoveAt(at);
                    }
                    else
                    {
                        var slot = live[rng.Next(live.Count)];
                        board.AddModifier(slot, rng.Next(2) == 0 ? floor : ceiling, new Modifier((ModifierKind)rng.Next(3), values[rng.Next(values.Length)]));
                    }
                }

                if (frame == 30)
                {
                    var map = new int[cap];
                    Array.Fill(map, -1);
                    var next = 0;
                    foreach (var slot in live.OrderByDescending(x => x))
                    {
                        map[slot] = next++;
                    }

                    board.RenumberSlots(map, live.Count);
                    live = Enumerable.Range(0, live.Count).ToList();
                }

                if (frame == 50)
                {
                    board.BindOutput(floor, external, 2);
                    board.BindOutput(ceiling, external + 1, 2);
                }

                board.Recalculate();
                for (var i = 0; i < cap; i++)
                {
                    a[i] = b[i] = rng.Next(-100, 100);
                    delta[i] = rng.Next(-1000, 1000);
                }

                for (var w = 0; w < cap / 64; w++)
                {
                    words[w] = (ulong)rng.NextInt64();
                }

                var reference = frame < 50 ? Clamp.Stat(board, floor, ceiling) : Clamp.Columns(floorInts, ceilingInts);
                var referenceMax = frame < 50 ? Clamp.StatMax(board, ceiling) : Clamp.Columns(null, ceilingInts);
                if (frame >= 50)
                {
                    foreach (var slot in live)
                    {
                        floorInts[slot] = Clamp.BoundOf(external[slot * 2]);
                        ceilingInts[slot] = Clamp.BoundOf(external[slot * 2 + 1]);
                    }
                }

                board.RunIntrinsicsChunk(a, delta, reference, 0, cap, appliedA);
                board.RunIntrinsicsChunk(b, delta, Clamp.StatMirror(board, floor, ceiling), 0, cap, appliedB);
                board.RunEnabledChunk(a, 7, referenceMax, words, cap / 64, 0, appliedA);
                board.RunEnabledChunk(b, 7, Clamp.StatMirrorMax(board, ceiling), words, cap / 64, 0, appliedB);
                foreach (var slot in live)
                {
                    Assert.True(a[slot] == b[slot], $"frame {frame} slot {slot}: stat {a[slot]} mirror {b[slot]}");
                    Assert.True(appliedA[slot] == appliedB[slot], $"frame {frame} slot {slot} applied");
                }
            }
        }
        finally
        {
            NativeMemory.Free(external);
            NativeMemory.Free(a);
            NativeMemory.Free(b);
            NativeMemory.Free(appliedA);
            NativeMemory.Free(appliedB);
            NativeMemory.Free(delta);
            NativeMemory.Free(words);
            NativeMemory.Free(floorInts);
            NativeMemory.Free(ceilingInts);
        }
    }

    [Fact]
    public void Mirror_FillsFromCurrentColumn_AndIsStable()
    {
        using var board = Board.Create(SchemaBuilder.Create().Stat("cap", 7.9f).Build(), 64);
        var cap = board.Schema.Stat("cap");
        var slot = board.Acquire();
        board.AddModifier(slot, cap, new Modifier(ModifierKind.Multi, 1e30f));
        board.Recalculate();
        var mirror = board.MirrorBounds(cap);
        Assert.Equal(int.MaxValue, mirror[slot]);
        Assert.True(mirror == board.MirrorBounds(cap));
        var other = board.Acquire();
        board.Recalculate();
        Assert.Equal(7, mirror[other]);
    }
}
