using System;
using System.Diagnostics;
using Sv;

internal static unsafe class DayToDay
{
    static long opTicks;
    static long opCount;

    static void Op(Action act)
    {
        var sw = Stopwatch.GetTimestamp();
        act();
        opTicks += Stopwatch.GetTimestamp() - sw;
        opCount++;
    }

    static double TimeIt(int warmup, int iterations, Action act)
    {
        for (var i = 0; i < warmup; i++)
        {
            act();
        }

        opTicks = 0;
        opCount = 0;
        var sw = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++)
        {
            act();
        }

        var total = Stopwatch.GetTimestamp() - sw;
        return total / (double)iterations / Stopwatch.Frequency * 1_000_000_000.0;
    }

    static void Row(string name, int iterations, Action act, string unit = "ns/op")
    {
        var perOp = TimeIt(Math.Min(64, iterations), iterations, act);
        Console.WriteLine($"  {name,-34} {perOp,12:0.0} {unit}  (n={iterations})");
    }

    public static int Run(string scenario, int ms)
    {
        if (scenario != "all" && scenario != "day-to-day")
        {
            return 0;
        }

        Console.WriteLine("day-to-day  (1024 live slots, 5 stats, warm loops, i9-14900K Release)");
        var schema = SchemaBuilder.Create()
            .Stat("power", 1f)
            .Stat("crit", 5f)
            .Derived("damage", 0f, ("power", 0.5f))
            .Curve("curved", 0f, "power", new[] { 0f, 10f, 18f, 24f, 28f, 30f }, 0f, 25f)
            .Intrinsic("mana")
            .Build();
        using var board = Board.Create(schema, 4096);
        var power = board.Schema.Stat("power");
        var crit = board.Schema.Stat("crit");
        var damage = board.Schema.Stat("damage");
        var curved = board.Schema.Stat("curved");
        var column = stackalloc float[4096];
        board.BindOutput(damage, column);
        var slots = new int[1024];
        for (var i = 0; i < 1024; i++)
        {
            slots[i] = board.Acquire();
        }

        using var drift = DriftStore.Create(4096);
        drift.AttachBoard(board);
        for (var i = 0; i < 1024; i++)
        {
            drift.Capture(i, 100, 0, ClampShift.Step(-2, 0, 100));
        }

        var handle = board.AddModifier(slots[0], power, new Modifier(ModifierKind.Multi, 0.25f));
        board.Recalculate();
        var folded = board.Stat(slots[0], damage);
        if (folded != 0.625f)
        {
            Console.WriteLine($"  verify failed: damage {folded}");
            return 1;
        }

        var tick = 0L;
        var dueArray = new long[64];
        var stateA = new SvState();
        var stateB = new SvState();
        var mapping = new int[4096];
        var ops = new DriftOp[8];
        var rule = ClampShift.Step(-2, 0, 100);

        Row("Acquire + Release (pair)", 200_000, () =>
        {
            var s = board.Acquire();
            board.Release(s);
        });

        Row("AddModifier (no expiry)", 100_000, () =>
        {
            var h = board.AddModifier(slots[1], crit, new Modifier(ModifierKind.Added, 1f));
            board.RemoveModifier(slots[1], crit, h);
        });

        Row("AddModifier (wheel expiry)", 50_000, () =>
        {
            tick++;
            var h = board.AddModifier(slots[2], crit, new Modifier(ModifierKind.Added, 1f), 0, tick + 600);
            board.RemoveModifier(slots[2], crit, h);
        });

        Row("Stat (folded set read)", 1_000_000, () =>
        {
            folded = board.Stat(slots[0], damage);
        });

        Row("Stat (curve source, folded)", 1_000_000, () =>
        {
            folded = board.Stat(slots[0], curved);
        });

        Row("Stat (re-resolve after churn)", 50_000, () =>
        {
            var h = board.AddModifier(slots[3], power, new Modifier(ModifierKind.Added, 3f));
            folded = board.Stat(slots[3], damage);
            board.RemoveModifier(slots[3], power, h);
        });

        var handles = new ModifierHandle[8];
        Row("8xAdd + Recalculate + 8xRemove", 20_000, () =>
        {
            for (var k = 0; k < 8; k++)
            {
                handles[k] = board.AddModifier(slots[4 + k], power, new Modifier(ModifierKind.Added, k));
            }

            board.Recalculate();
            for (var k = 0; k < 8; k++)
            {
                board.RemoveModifier(slots[4 + k], power, handles[k]);
            }
        });

        Row("Recalculate (clean)", 100_000, () => board.Recalculate());

        Row("ExpireModifiers (nothing due)", 100_000, () =>
        {
            tick++;
            board.ExpireModifiers(tick);
        });

        Row("crossing due + TakeDueCrossings", 50_000, () =>
        {
            tick++;
            board.ScheduleCrossing(tick + 8, tick);
            board.ExpireModifiers(tick);
            board.TakeDueCrossings(dueArray);
        });

        Row("Digest (Zobrist)", 20_000, () =>
        {
            var d = board.Digest;
            if (d == 0)
            {
                throw new InvalidOperationException();
            }
        });

        Row("BindOutput (rebind)", 20_000, () => board.BindOutput(damage, column));

        Row("BindSourceAnchor + Clear (pair)", 50_000, () =>
        {
            board.BindSourceAnchor(damage, 0, slots[5]);
            board.ClearSourceAnchor(damage, 0);
        });

        Row("SaveState + LoadState (pair)", 200, () =>
        {
            board.SaveState(stateA);
            board.LoadState(stateA);
        });

        Row("undo window + frame + Rollback (cycle)", 10_000, () =>
        {
            board.BeginUndoWindow(1);
            board.BeginUndoFrame();
            board.AddModifier(slots[6], crit, new Modifier(ModifierKind.Added, 1f));
            board.Rollback(1);
            board.EndUndoWindow();
        });

        Row("RenumberSlots (256 moved)", 200, () =>
        {
            for (var i = 0; i < 4096; i++)
            {
                mapping[i] = i;
            }

            for (var k = 0; k < 128; k++)
            {
                var a = slots[2 * k];
                var b = slots[2 * k + 1];
                mapping[a] = b;
                mapping[b] = a;
            }

            board.RenumberSlots(mapping, 1024);
            for (var k = 0; k < 128; k++)
            {
                var a = slots[2 * k];
                var b = slots[2 * k + 1];
                mapping[a] = b;
                mapping[b] = a;
            }

            board.RenumberSlots(mapping, 1024);
        });

        Row("drift Capture (segment write)", 50_000, () => drift.Capture(slots[7], 50, tick, rule));

        Row("drift Read (lazy eval)", 200_000, () =>
        {
            var v = drift.Read(slots[7], 10);
            if (v < 0)
            {
                throw new InvalidOperationException();
            }
        });

        for (var k = 0; k < 8; k++)
        {
            ops[k] = new DriftOp(slots[8 + k], ClampShift.Step(-1, 0, 100));
        }

        Row("drift ApplyBatch (8 ops)", 20_000, () =>
        {
            tick++;
            drift.ApplyBatch(tick, ops);
        });

        var intrinsicColumn = stackalloc int[4096];
        var applied = stackalloc int[4096];
        Row("RunIntrinsicsChunk (256 rows)", 20_000, () =>
        {
            board.RunIntrinsicsChunk(intrinsicColumn, 3, Clamp.Constant(0, 1000), slots[0], 256, applied);
        });

        Row("InvalidateBindings", 2_000, () => board.InvalidateBindings());

        if (folded == float.MinValue)
        {
            Console.WriteLine("  dead output");
            return 1;
        }

        stateA.Dispose();
        stateB.Dispose();
        return 0;
    }
}
