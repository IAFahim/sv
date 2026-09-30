using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Frent;
using Sv;
using Sv.Frent;

// 100,000 Frent entities churn, fragment, and heal. Slot renumbering is a board verb:
// Reconcile() rewrites SvSlot in place and hands back the permutation; host columns
// follow with Columns.Permute; the next fused pass is contiguous again.

internal static unsafe class ReconcileSample
{
    const int Rows = 100_000;

    struct Health
    {
        public int Value;
    }

    public static void Run()
    {
        Console.WriteLine($"[reconcile] {Rows:N0} Frent entities — fragment, run fused windows, heal");
        var schema = SchemaBuilder.Create().Stat("max_mana", 100f).Intrinsic("mana").Build();
        using var world = new World();
        using var population = new Population(world, schema, Rows);
        var entities = new Entity[Rows];
        var original = new int[Rows];
        var column = new float[Rows];
        var applied = (int*)NativeMemory.AllocZeroed((nuint)Rows * (nuint)sizeof(int), (nuint)sizeof(int));
        var clamp = Clamp.Constant(0, 1_000_000);

        var clock = Stopwatch.StartNew();
        for (var i = 0; i < Rows; i++)
        {
            entities[i] = world.Create(new Health { Value = 100_000 - i });
            population.Bind(entities[i]);
            population.TrySlot(entities[i], out original[i]);
            column[original[i]] = i;
        }

        clock.Stop();
        Console.WriteLine($"  create + bind {Rows:N0} entities in {clock.Elapsed.TotalMilliseconds:0.0} ms");

        foreach (var window in population.Chunks<Health>())
        {
            window.Apply(population.Board, 0, clamp, new Span<int>(applied, Rows));
        }

        Console.WriteLine($"  fused pass while dense: {population.FastPathChunks} contiguous chunk(s) ran zero-copy, {population.RemapChunks} remapped");

        clock.Restart();
        for (var i = 2; i < Rows; i += 3)
        {
            population.Unbind(entities[i]);
            entities[i].Delete();
        }

        clock.Stop();
        var live = population.Board.LiveCount;
        Console.WriteLine($"  delete every 3rd: {Rows:N0} -> {live:N0} live in {clock.Elapsed.TotalMilliseconds:0.0} ms — slots are now full of holes");

        clock.Restart();
        foreach (var window in population.Chunks<Health>())
        {
            window.Apply(population.Board, -7, clamp, new Span<int>(applied, Rows));
        }

        clock.Stop();
        Console.WriteLine($"  fused pass over the fragments: {clock.Elapsed.TotalMilliseconds:0.0} ms —" +
                          $" {population.FastPathChunks} contiguous, {population.RemapChunks} needed the Pull/Push remap");

        clock.Restart();
        var permutation = population.Reconcile();
        clock.Stop();
        Console.WriteLine($"  Reconcile(): slots renumbered dense in {clock.Elapsed.TotalMilliseconds:0.0} ms");

        var contiguous = 0;
        var chunks = 0;
        foreach (var window in population.Chunks<Health>())
        {
            chunks++;
            if (window.IsContiguous)
            {
                contiguous++;
            }
        }

        Console.WriteLine($"  next chunk walk: {contiguous}/{chunks} chunks slot-contiguous — healed");

        fixed (float* columnPtr = column)
        {
            Columns.Permute(columnPtr, permutation, live);
        }

        var moved = -1;
        for (var i = 0; i < Rows && moved < 0; i++)
        {
            if (i >= 2 && (i - 2) % 3 == 0)
            {
                continue;
            }

            population.TrySlot(entities[i], out var now);
            if (now != original[i])
            {
                moved = i;
            }
        }

        population.TrySlot(entities[moved], out var movedSlot);
        Console.WriteLine($"  entity #{moved}: slot {original[moved]} -> {movedSlot}; Health {entities[moved].Get<Health>().Value:N0} moved with its SvSlot," +
                          $" the host column value {column[movedSlot]:0} followed with Columns.Permute");

        clock.Restart();
        for (var i = 0; i < Rows / 3; i++)
        {
            population.Bind(world.Create(new Health { Value = i }));
        }

        clock.Stop();
        Console.WriteLine($"  rebind {Rows / 3:N0} fresh entities in {clock.Elapsed.TotalMilliseconds:0.0} ms — free list reused, live {population.Board.LiveCount:N0}");
        NativeMemory.Free(applied);
    }
}
