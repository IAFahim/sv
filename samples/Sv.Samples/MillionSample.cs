using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Sv;

// One million slots, every core verb at full scale. The whole showcase is ~60 lines:
// dense slots, stat broadcast, stat-clamped intrinsics, masked run-walk, sparse events.

internal static unsafe class MillionSample
{
    const int Rows = 1_000_000;

    struct ManaEvent
    {
        public int Slot;

        public int Amount;
    }

    [UnmanagedCallersOnly]
    static ManaEvent MakeMana(int slot, int value) => new() { Slot = slot, Amount = value };

    public static void Run()
    {
        Console.WriteLine($"[million] one board, {Rows:N0} live slots");
        var schema = SchemaBuilder.Create()
            .Stat("power", 10f)
            .Stat("ceiling", 900_000f)
            .Intrinsic("mana")
            .Build();
        using var board = Board.Create(schema, Rows);
        var mana = (int*)NativeMemory.AllocZeroed((nuint)Rows * (nuint)sizeof(int), (nuint)sizeof(int));
        var applied = (int*)NativeMemory.AllocZeroed((nuint)Rows * (nuint)sizeof(int), (nuint)sizeof(int));
        for (var i = 0; i < Rows; i++)
        {
            board.Acquire();
            mana[i] = 40;
        }

        var clock = Stopwatch.StartNew();
        board.Recalculate();
        clock.Stop();
        Console.WriteLine($"  recalculate: {board.Schema.Stats.Count} stats broadcast over {Rows:N0} slots in {clock.Elapsed.TotalMilliseconds:0.0} ms" +
                          $"  ({(long)Rows * board.Schema.Stats.Count / clock.Elapsed.TotalMilliseconds / 1e3:0} M broadcasts/s)");

        var clamp = Clamp.StatMax(board, schema.Stat("ceiling"));
        var (us, bytes) = Program.Measure(20, () => board.RunIntrinsicsChunk(mana, 25, clamp, 0, Rows, applied));
        Console.WriteLine($"  intrinsics +25 under a stat clamp:   {us:0.0} us  ({Rows / us / 1000.0:0.00} M rows/s, {bytes} B)");

        (us, bytes) = Program.Measure(5, () =>
        {
            for (var offset = 0; offset < Rows; offset += 64)
            {
                board.RunEnabledChunk(mana, 7, clamp, 0x5555_5555_5555_5555ul, offset, applied);
            }
        });
        Console.WriteLine($"  enabled run-walk, 50% of slots:     {us:0.0} us  ({Rows / us / 1000.0:0.00} M rows/s, {bytes} B)");

        using var streams = EventStreams.Create(1, 1 << 22);
        var channel = streams.CreateChannel<ManaEvent>();
        var cond = (byte*)NativeMemory.AllocZeroed((nuint)Rows, 1);
        var random = new Random(41);
        var density = 0;
        for (var i = 0; i < Rows; i++)
        {
            cond[i] = (byte)(random.Next(10) == 0 ? 1 : 0);
            density += cond[i];
        }

        (us, bytes) = Program.Measure(5, () =>
        {
            streams.EmitFromBools(channel, cond, 0, Rows, &MakeMana);
            streams.Swap();
            while (streams.TryReadNext(out _, out _, out _))
            {
            }
        });
        Console.WriteLine($"  events: {density:N0} sparse events emitted and drained: {us:0.0} us  ({bytes} B)");

        mana[0] = 40;
        board.RunIntrinsicsChunk(mana, 25, clamp, 0, Rows, applied);
        Console.WriteLine($"  after one +25 tick: mana 40 -> {mana[0]}, every value inside [0, {board.Output(schema.Stat("ceiling"))[0]:0}]");
        NativeMemory.Free(mana);
        NativeMemory.Free(applied);
        NativeMemory.Free(cond);
    }
}
