using System;
using System.Runtime.InteropServices;
using Sv;
using Sv.Compose;
using static Program;

internal unsafe struct FrameWorld : IFrameWorld
{
    public int* Mana;

    public byte* Landed;

    public long* Counter;

    public StatId Ceiling;

    public void Reconcile(Game game)
    {
    }

    public void DispatchEvent(Game game, ushort typeId, void* payload)
    {
        *Counter += ((ManaEvent*)payload)->Amount;
    }

    public void RunIntrinsics(Game game)
    {
        if (game.Input.Pressed(0))
        {
            game.Board.RunIntrinsicsChunk(Mana, 25, Clamp.StatMax(game.Board, Ceiling), 0, Rows, game.Applied);
        }
    }

    public void RunConditions(Game game)
    {
        var channel = game.Channel<ManaEvent>();
        game.Streams.EmitFromBools(channel, Landed, 0, Rows, &Makers.MakeMana);
        game.Streams.EmitFromNonZero(channel, game.Applied, 0, Rows, &Makers.MakeMana);
        for (var i = 0; i < Rows; i++)
        {
            game.Applied[i] = 0;
        }
    }
}

internal sealed unsafe class FrameRunBench : BenchBase
{
    GameHost<EventLink<ManaEvent, Chain>, Chain, Chain, FrameWorld> host = null!;

    int* mana;

    byte* landed;

    long* counter;

    readonly bool tape;

    FrameRunBench(bool tape)
    {
        this.tape = tape;
    }

    internal static FrameRunBench New(bool tape, string path)
    {
        var bench = new FrameRunBench(tape);
        var schema = SchemaBuilder.Create()
            .Stat("power", 10f)
            .Stat("ceiling", 900_000f)
            .Intrinsic("mana")
            .Build();
        bench.mana = (int*)NativeMemory.AllocZeroed((nuint)Rows * (nuint)sizeof(int), (nuint)sizeof(int));
        bench.landed = (byte*)NativeMemory.AllocZeroed((nuint)Rows, 1);
        bench.counter = (long*)NativeMemory.AllocZeroed(sizeof(long), sizeof(long));
        var world = new FrameWorld
        {
            Mana = bench.mana,
            Landed = bench.landed,
            Counter = bench.counter,
            Ceiling = schema.Stat("ceiling"),
        };
        bench.host = Game.Create(schema, Rows)
            .Event<ManaEvent>()
            .Build(world, InputState.Create(1, 1, 16));
        for (var i = 0; i < Rows; i++)
        {
            bench.host.Game.Board.Acquire();
        }

        bench.host.Game.Board.Recalculate();
        if (tape)
        {
            bench.host.Record(path);
        }

        return bench;
    }

    public override string Verify()
    {
        for (var i = 0; i < Rows; i++)
        {
            mana[i] = 40;
        }

        *counter = 0;
        var axes = stackalloc uint[1];
        var buttons = stackalloc ulong[1];
        buttons[0] = 1;
        host.Game.Input.Capture(axes, buttons);
        host.Frame.Run(0.016666668f);
        buttons[0] = 0;
        host.Game.Input.Capture(axes, buttons);
        host.Frame.Run(0.016666668f);
        if (mana[0] != 65 || *counter != 25L * Rows)
        {
            return $"MISMATCH frame: mana {mana[0]} != 65, drained {*counter} != {25L * Rows}";
        }

        var power = host.Game.Board.OutputPtr(host.Game.Board.Schema.Stat("power"));
        if (BitConverter.SingleToUInt32Bits(power[0]) != BitConverter.SingleToUInt32Bits(10f))
        {
            return $"MISMATCH broadcast: {power[0]} != 10";
        }

        return "ok";
    }

    public override long Frame(int iterations)
    {
        var axes = stackalloc uint[1];
        var buttons = stackalloc ulong[1];
        long checksum = 0;
        for (var it = 0; it < iterations; it++)
        {
            buttons[0] = it % 2 == 0 ? 1ul : 0ul;
            host.Game.Input.Capture(axes, buttons);
            host.Frame.Run(0.016666668f);
            checksum += *counter + mana[it % Rows];
        }

        return checksum;
    }

    public override void Dispose()
    {
        if (tape)
        {
            host.EndRecord();
        }

        host.Dispose();
        NativeMemory.Free(mana);
        NativeMemory.Free(landed);
        NativeMemory.Free(counter);
    }
}

internal sealed unsafe class TapeRecordBench : BenchBase
{
    GameHost<EventLink<ManaEvent, Chain>, Chain, Chain, FrameWorld> host = null!;

    int* mana;

    byte* landed;

    long* counter;

    internal static TapeRecordBench New(string path)
    {
        var bench = new TapeRecordBench();
        var schema = SchemaBuilder.Create()
            .Stat("power", 10f)
            .Stat("ceiling", 900_000f)
            .Intrinsic("mana")
            .Build();
        bench.mana = (int*)NativeMemory.AllocZeroed((nuint)Rows * (nuint)sizeof(int), (nuint)sizeof(int));
        bench.landed = (byte*)NativeMemory.AllocZeroed((nuint)Rows, 1);
        bench.counter = (long*)NativeMemory.AllocZeroed(sizeof(long), sizeof(long));
        var world = new FrameWorld
        {
            Mana = bench.mana,
            Landed = bench.landed,
            Counter = bench.counter,
            Ceiling = schema.Stat("ceiling"),
        };
        bench.host = Game.Create(schema, Rows)
            .Event<ManaEvent>()
            .Build(world, InputState.Create(1, 1, 16));
        for (var i = 0; i < Rows; i++)
        {
            bench.host.Game.Board.Acquire();
        }

        bench.host.Game.Board.Recalculate();
        bench.host.Record(path);
        return bench;
    }

    public override string Verify()
    {
        return "ok";
    }

    public override long Frame(int iterations)
    {
        long checksum = 0;
        for (var it = 0; it < iterations; it++)
        {
            host.CaptureFrame(0.016666668f);
            checksum += it;
        }

        return checksum;
    }

    public override void Dispose()
    {
        host.EndRecord();
        host.Dispose();
        NativeMemory.Free(mana);
        NativeMemory.Free(landed);
        NativeMemory.Free(counter);
    }
}
