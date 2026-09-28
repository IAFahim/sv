using System;
using System.Runtime.InteropServices;
using Sv;
using Xunit;

public unsafe class NoAllocationTests
{
    struct ManaEvent
    {
        public int Slot;

        public int Amount;
    }

    [UnmanagedCallersOnly]
    static ManaEvent MakeMana(int slot, int value) => new() { Slot = slot, Amount = value };

    [Fact]
    public void BoardVerbs_AllocateZeroManagedBytes()
    {
        var schema = SchemaBuilder.Create()
            .Stat("speed", 6f)
            .Intrinsic("mana")
            .Build();
        using var board = Board.Create(schema, 128);
        var speed = schema.Stat("speed");
        var column = stackalloc float[128];
        board.BindOutput(speed, column);
        for (var i = 0; i < 127; i++)
        {
            board.Acquire();
        }

        var value = stackalloc int[128];
        var delta = stackalloc int[128];
        var applied = stackalloc int[128];
        var clamp = Clamp.Constant(0, 1000);
        for (var i = 0; i < 128; i++)
        {
            value[i] = 500;
            delta[i] = 3;
        }

        board.AddModifier(0, speed, new Modifier(ModifierKind.Multi, 1.5f));
        board.RunIntrinsicsChunk(value, 7, clamp, 0, 128, applied);
        board.RunIntrinsicsChunk(value, delta, clamp, 0, 128, applied);
        board.RunEnabledChunk(value, 7, clamp, 0x5555_5555_5555_5555ul, 0, applied);
        board.Recalculate();
        var released = board.Acquire();
        board.Release(released);

        var before = GC.GetAllocatedBytesForCurrentThread();
        long sink = 0;
        for (var it = 0; it < 2000; it++)
        {
            board.RunIntrinsicsChunk(value, 7, clamp, 0, 128, applied);
            board.RunIntrinsicsChunk(value, delta, clamp, 0, 128, applied);
            board.RunEnabledChunk(value, 7, clamp, 0x5555_5555_5555_5555ul, 0, applied);
            var handle = board.AddModifier(it % 127, speed, new Modifier(ModifierKind.Multi, 1.5f));
            board.RemoveModifier(it % 127, speed, handle);
            board.Recalculate();
            var slot = board.Acquire();
            board.Release(slot);
            sink += value[it % 128] + applied[it % 128];
        }

        var deltaBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(deltaBytes == 0, $"board verbs allocated {deltaBytes} managed bytes (sink {sink})");
    }

    [Fact]
    public void EventVerbs_AllocateZeroManagedBytes()
    {
        using var streams = EventStreams.Create();
        var channel = streams.CreateChannel<ManaEvent>();
        var cond = stackalloc byte[256];
        var values = stackalloc int[256];
        for (var i = 0; i < 256; i++)
        {
            cond[i] = (byte)(i % 3 == 0 ? 1 : 0);
            values[i] = i % 5 == 0 ? i : 0;
        }

        streams.EmitFromBools(channel, cond, 0, 256, &MakeMana);
        streams.EmitFromNonZero(channel, values, 0, 256, &MakeMana);
        streams.Swap();
        while (streams.TryReadNext(out _, out _, out _))
        {
        }

        channel.Write(new ManaEvent { Slot = 1, Amount = 2 });

        var before = GC.GetAllocatedBytesForCurrentThread();
        long sink = 0;
        for (var it = 0; it < 2000; it++)
        {
            channel.Write(new ManaEvent { Slot = it % 256, Amount = 1 });
            streams.EmitFromBools(channel, cond, 0, 256, &MakeMana);
            streams.EmitFromNonZero(channel, values, 0, 256, &MakeMana);
            streams.EmitFromMask(channel, 0x00FF_FFFF_FFFF_FFFFul, 0, &MakeMana);
            streams.Swap();
            while (streams.TryReadNext(out _, out var payload, out _))
            {
                sink += ((ManaEvent*)payload)->Amount;
            }
        }

        var deltaBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(deltaBytes == 0, $"event verbs allocated {deltaBytes} managed bytes (sink {sink})");
    }

    [Fact]
    public void InputVerbs_AllocateZeroManagedBytes()
    {
        using var input = InputState.Create(4, 2, 32);
        var axes = stackalloc uint[4];
        var buttons = stackalloc ulong[2];
        for (var i = 0; i < 16; i++)
        {
            buttons[0] = (ulong)i;
            input.Capture(axes, buttons);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        long sink = 0;
        for (var it = 0; it < 2000; it++)
        {
            axes[0] = (uint)it;
            buttons[0] = (ulong)it;
            buttons[1] = ~(ulong)it;
            input.Capture(axes, buttons);
            sink += (input.Down(0) ? 1 : 0) + (input.Pressed(1) ? 1 : 0) + (input.Released(64) ? 1 : 0) + input.AxisBits(3);
        }

        input.BeginReplay(input.OldestRingTick);
        while (input.ReplayStep())
        {
            sink += input.AxisBits(0);
        }

        var deltaBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(deltaBytes == 0, $"input verbs allocated {deltaBytes} managed bytes (sink {sink})");
    }
}
