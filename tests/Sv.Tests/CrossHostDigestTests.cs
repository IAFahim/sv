using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sv;
using Xunit;
using Xunit.Abstractions;

public unsafe class CrossHostDigestTests
{
    const ulong Expected = 0x0781B61BE6F53640ul;

    readonly ITestOutputHelper output;

    public CrossHostDigestTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    struct Xorshift
    {
        ulong state;

        public Xorshift(ulong seed)
        {
            state = seed;
        }

        public ulong Next()
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            return state;
        }

        public int Below(int n) => (int)(Next() % (ulong)n);
    }

    public struct Gained
    {
        public int Slot;

        public int Amount;
    }

    [UnmanagedCallersOnly]
    static Gained MakeGained(int slot, int value) => new() { Slot = slot, Amount = value };

    static ulong Mix(ulong hash, ulong value)
    {
        unchecked
        {
            hash ^= value;
            hash *= 0x100000001B3ul;
            return hash ^ (hash >> 29);
        }
    }

    internal static ulong RunScenario()
    {
        const int n = 3000;
        const int frames = 150;
        var rng = new Xorshift(0x9E3779B97F4A7C15ul);
        using var board = Board.Create(SchemaBuilder.Create()
            .Stat("speed", 6f)
            .Stat("max_mana", 100f)
            .Stat("floor", -5f)
            .Intrinsic("mana")
            .Build(), n);
        var speed = board.Schema.Stat("speed");
        var maxMana = board.Schema.Stat("max_mana");
        var floor = board.Schema.Stat("floor");
        var stats = new[] { speed, maxMana, floor };
        var cap = board.Capacity;
        var mana = (int*)NativeMemory.AllocZeroed((nuint)cap * sizeof(int));
        var delta = (int*)NativeMemory.AllocZeroed((nuint)cap * sizeof(int));
        var applied = (int*)NativeMemory.AllocZeroed((nuint)cap * sizeof(int));
        var words = (ulong*)NativeMemory.AllocZeroed((nuint)(cap / 64) * sizeof(ulong));
        using var streams = EventStreams.Create(2, 256);
        var gained = streams.CreateChannel<Gained>(1);
        var live = new List<int>();
        var handles = new Dictionary<int, List<(StatId, ModifierHandle)>>();
        var kinds = new[] { ModifierKind.Added, ModifierKind.Additive, ModifierKind.Multi };
        var values = new[] { 0.1f, 0.25f, -0.3f, 1.7f, 3.3333333f, -2.5f, 1e-3f, 0.7f };
        ulong hash = 0xCBF29CE484222325ul;
        try
        {
            for (var frame = 0; frame < frames; frame++)
            {
                for (var op = 0; op < 60; op++)
                {
                    var roll = rng.Below(10);
                    if ((roll < 3 || live.Count == 0) && live.Count < n)
                    {
                        var slot = board.Acquire();
                        live.Add(slot);
                        handles[slot] = new List<(StatId, ModifierHandle)>();
                        mana[slot] = rng.Below(80);
                    }
                    else if (roll < 4)
                    {
                        var at = rng.Below(live.Count);
                        board.Release(live[at]);
                        handles.Remove(live[at]);
                        live.RemoveAt(at);
                    }
                    else if (roll < 8)
                    {
                        var slot = live[rng.Below(live.Count)];
                        var stat = stats[rng.Below(3)];
                        var modifier = new Modifier(kinds[rng.Below(3)], values[rng.Below(values.Length)]);
                        handles[slot].Add((stat, board.AddModifier(slot, stat, modifier)));
                    }
                    else
                    {
                        var slot = live[rng.Below(live.Count)];
                        var list = handles[slot];
                        if (list.Count > 0)
                        {
                            var at = rng.Below(list.Count);
                            board.RemoveModifier(slot, list[at].Item1, list[at].Item2);
                            list.RemoveAt(at);
                        }
                    }
                }

                board.Recalculate();
                for (var i = 0; i < cap; i++)
                {
                    delta[i] = rng.Below(41) - 20;
                }

                board.RunIntrinsicsChunk(mana, delta, Clamp.Stat(board, floor, maxMana), 0, cap, applied);
                for (var w = 0; w < board.LiveWordCount; w++)
                {
                    streams.EmitFromNonZero(gained, applied, board.LiveWord(w), w << 6, &MakeGained);
                }
                for (var w = 0; w < cap / 64; w++)
                {
                    words[w] = rng.Next() & rng.Next();
                }

                board.RunEnabledChunk(mana, 3, Clamp.StatMax(board, maxMana), words, cap / 64, 0, applied);
                for (var w = 0; w < board.LiveWordCount; w++)
                {
                    streams.EmitFromNonZero(gained, applied, board.LiveWord(w), w << 6, &MakeGained);
                }
                streams.Swap();
                while (streams.TryReadNext(out var type, out var payload, out _))
                {
                    var evt = *(Gained*)payload;
                    hash = Mix(hash, ((ulong)type << 48) ^ ((ulong)(uint)evt.Slot << 16) ^ (uint)evt.Amount);
                }

                for (var w = 0; w < board.LiveWordCount; w++)
                {
                    var bits = board.LiveWord(w);
                    hash = Mix(hash, bits);
                    while (bits != 0)
                    {
                        var slot = (w << 6) + System.Numerics.BitOperations.TrailingZeroCount(bits);
                        bits &= bits - 1;
                        foreach (var stat in stats)
                        {
                            hash = Mix(hash, BitConverter.SingleToUInt32Bits(board.Output(stat)[slot]));
                        }

                        hash = Mix(hash, (uint)mana[slot]);
                    }
                }
            }
        }
        finally
        {
            NativeMemory.Free(mana);
            NativeMemory.Free(delta);
            NativeMemory.Free(applied);
            NativeMemory.Free(words);
        }

        return hash;
    }

    [Fact]
    public void Scenario_DigestIsIdenticalOnEveryHostAndIsa()
    {
        var digest = RunScenario();
        output.WriteLine($"cross-host digest {digest:X16} on {RuntimeInformation.OSArchitecture} {RuntimeInformation.OSDescription} avx2={System.Runtime.Intrinsics.X86.Avx2.IsSupported} v256={System.Runtime.Intrinsics.Vector256.IsHardwareAccelerated} v128={System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated}");
        Assert.Equal(digest, RunScenario());
        Assert.Equal(Expected.ToString("X16"), digest.ToString("X16"));
    }
}
