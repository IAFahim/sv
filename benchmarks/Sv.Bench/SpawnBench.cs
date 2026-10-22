using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Sv;
using static Program;

// spawn1m — the roadmap 2.7 receipt (issues/0074, Roadmap.md 4.4): spawning 2^20 entities
// from one resolved template must stay under 10 ms — n block-id writes, materialized
// column and mirror fills, one block refcount add, no pair resolving.

internal static unsafe class Spawn1m
{
    const int Count = 1 << 20;

    internal static int Run(string scenario, int ms)
    {
        if (scenario != "all" && scenario != "spawn1m")
        {
            return 0;
        }

        var schema = SchemaBuilder.Create()
            .Stat("max_hp", 100f)
            .Stat("max_mana", 100f)
            .Stat("max_stamina", 100f)
            .Stat("max_shield", 50f)
            .Stat("armor", 10f)
            .Stat("move_speed", 6f)
            .Stat("strength", 10f)
            .Stat("crit", 5f)
            .Stat("regen_hp", 2f)
            .Stat("regen_mana", 3f)
            .Stat("bonus_armor", 0f)
            .Stat("haste", 0f)
            .Derived("atk", 1f, ("strength", 2f))
            .Derived("defense", 1f, ("armor", 1.5f))
            .Derived("power", 1f, ("strength", 0.5f))
            .Derived("crit_cap", 75f, ("crit", 1f))
            .Intrinsic("hp")
            .Intrinsic("mana")
            .Intrinsic("stamina")
            .Intrinsic("shield")
            .Build();
        using var board = Board.Create(schema, Count + 64);
        var armor = schema.Stat("armor");
        var atk = schema.Stat("atk");
        var maxHp = schema.Stat("max_hp");
        var bound = Allocator.New<float>(board.Capacity);
        board.BindOutput(armor, bound);
        var mirror = board.MirrorBounds(maxHp);
        var template = board.Acquire();
        board.AddModifier(template, armor, new Modifier(ModifierKind.Multi, 1.25f));
        board.Recalculate();
        var slots = Allocator.New<int>(Count);

        var samples = new double[9];
        var verify = "ok";
        var allocBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var rep = 0; rep < samples.Length; rep++)
        {
            var start = Stopwatch.GetTimestamp();
            board.Spawn(template, Count, slots);
            var ticks = Stopwatch.GetTimestamp() - start;
            samples[rep] = ticks / (double)Stopwatch.Frequency * 1000.0;
            if (board.LiveCount != Count + 1)
            {
                verify = $"FAIL live {board.LiveCount} != {Count + 1}";
            }

            if (Math.Abs(bound[slots[Count / 2]] - 22.5f) > 0.0001f || mirror[slots[Count / 2]] != Clamp.BoundOf(100f))
            {
                verify = $"FAIL spawned row {bound[slots[Count / 2]]}/{mirror[slots[Count / 2]]} != template";
            }

            for (var i = 0; i < Count; i++)
            {
                board.Release(slots[i]);
            }
        }

        var allocDelta = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        Array.Sort(samples);
        var p50 = samples[samples.Length / 2];
        if (allocDelta != 0)
        {
            verify = $"FAIL allocated {allocDelta} managed bytes";
        }

        if (p50 > 10.0)
        {
            verify = $"FAIL p50 {p50:0.000} ms > 10 ms budget";
        }

        NativeMemory.Free(bound);
        NativeMemory.Free(slots);
        if (Markdown)
        {
            Console.WriteLine($"| spawn1m | {p50:0.000} ms | {Count:N0} spawned from one template | {(verify.StartsWith("ok", StringComparison.Ordinal) ? "0 B" : "FAIL")} |");
            return verify.StartsWith("ok", StringComparison.Ordinal) ? 0 : 1;
        }

        Console.WriteLine(verify.StartsWith("ok", StringComparison.Ordinal)
            ? $"spawn1m                 {p50,9:0.000} ms/spawn  ({Count:N0} entities, 16 stats, 1 bound column + 1 mirror)  alloc=0B"
            : $"spawn1m                 FAIL: {verify}");
        return verify.StartsWith("ok", StringComparison.Ordinal) ? 0 : 1;
    }
}
