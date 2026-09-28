using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sv;
using Tl;

namespace Sv.NorthStar;

public struct Landed
{
    public int Slot;
}

public struct ManaGained
{
    public int Slot;

    public int Amount;
}

public static class NorthStarEvents
{
    [UnmanagedCallersOnly]
    public static Landed MakeLanded(int slot, int value) => new() { Slot = slot };

    [UnmanagedCallersOnly]
    public static ManaGained MakeMana(int slot, int value) => new() { Slot = slot, Amount = value };
}

public sealed class FrameRecord
{
    public uint[] PositionBits = Array.Empty<uint>();

    public uint[] VelocityBits = Array.Empty<uint>();

    public uint[] YBits = Array.Empty<uint>();

    public uint[] MoveSpeedBits = Array.Empty<uint>();

    public uint[] JumpPowerBits = Array.Empty<uint>();

    public uint[] MaxManaBits = Array.Empty<uint>();

    public int[] Mana = Array.Empty<int>();

    public int[] Applied = Array.Empty<int>();

    public byte[] Landed = Array.Empty<byte>();

    public List<string> Events = new();
}

public sealed unsafe class NorthStarHarness : IDisposable
{
    public const int Entities = 4;

    public const int RegenButton = 0;

    public const float Gravity = -9.8f;

    public const float Restitution = 0.5f;

    public readonly float Dt = 0.016666668f;

    public Board Board = null!;

    StatId moveSpeed;

    StatId jumpPower;

    StatId maxMana;

    float* moveSpeedColumn;

    float* jumpPowerColumn;

    float* maxManaColumn;

    int* mana;

    int* applied;

    byte* landedCol;

    public InputState Input = null!;

    EventStreams streams = null!;

    Channel<Landed> landedChannel = null!;

    Channel<ManaGained> manaChannel = null!;

    Clamp manaClamp;

    public float[] Position = new float[Entities];

    public float[] Velocity = new float[Entities];

    public float[] Y = new float[Entities];

    public ushort[] Ids = new ushort[Entities];

    public ushort[] TickClock = new ushort[Entities];

    public List<string> EventLog = new();

    public int[] LastApplied = new int[Entities];

    readonly bool ownsInput;

    public NorthStarHarness()
    {
        ownsInput = true;
        Initialize();
    }

    public NorthStarHarness(InputState sharedInput)
    {
        ownsInput = false;
        Initialize();
        Input.Dispose();
        Input = sharedInput;
    }

    void Initialize()
    {
        var schema = SchemaBuilder.Create()
            .Stat("move_speed", 6f)
            .Stat("jump_power", 1f)
            .Stat("max_mana", 100f)
            .Intrinsic("mana")
            .Build();
        Board = Board.Create(schema, Entities);
        moveSpeed = schema.Stat("move_speed");
        jumpPower = schema.Stat("jump_power");
        maxMana = schema.Stat("max_mana");
        moveSpeedColumn = (float*)NativeMemory.AllocZeroed((nuint)Entities * sizeof(float), sizeof(float));
        jumpPowerColumn = (float*)NativeMemory.AllocZeroed((nuint)Entities * sizeof(float), sizeof(float));
        maxManaColumn = (float*)NativeMemory.AllocZeroed((nuint)Entities * sizeof(float), sizeof(float));
        mana = (int*)NativeMemory.AllocZeroed((nuint)Entities * sizeof(int), sizeof(int));
        applied = (int*)NativeMemory.AllocZeroed((nuint)Entities * sizeof(int), sizeof(int));
        landedCol = (byte*)NativeMemory.AllocZeroed((nuint)Entities, 1);
        Board.BindOutput(moveSpeed, moveSpeedColumn);
        Board.BindOutput(jumpPower, jumpPowerColumn);
        Board.BindOutput(maxMana, maxManaColumn);
        manaClamp = Clamp.StatMax(Board, maxMana);
        for (var i = 0; i < Entities; i++)
        {
            Board.Acquire();
        }

        Input = InputState.Create(2, 1, 8);
        streams = EventStreams.Create();
        landedChannel = streams.CreateChannel<Landed>();
        manaChannel = streams.CreateChannel<ManaGained>();

        var jumpId = TimelineAsset.Load(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "jump.tlb")));
        for (var i = 0; i < Entities; i++)
        {
            Ids[i] = jumpId;
            Position[i] = 0.01f;
            Velocity[i] = -130f;
            mana[i] = 40;
        }

        Board.Recalculate();
    }

    public void CaptureTick(uint* axesBits, ulong* buttons)
    {
        Input.Capture(axesBits, buttons);
        RunFrame();
    }

    public void ReplayTick()
    {
        AssertTrue(Input.ReplayStep(), "replay step exhausted");
        RunFrame();
    }

    void RunFrame()
    {
        while (streams.TryReadNext(out var typeId, out var payload, out _))
        {
            if (typeId == landedChannel.Id)
            {
                var evt = *(Landed*)payload;
                EventLog.Add($"landed:{evt.Slot}");
                Board.AddModifier(evt.Slot, moveSpeed, new Modifier(ModifierKind.Multi, 1.5f));
            }
            else if (typeId == manaChannel.Id)
            {
                var evt = *(ManaGained*)payload;
                EventLog.Add($"mana:{evt.Slot}:{evt.Amount}");
            }
        }

        if (Input.Pressed(RegenButton))
        {
            Board.RunIntrinsicsChunk(mana, 25, manaClamp, 0, Entities, applied);
        }

        Board.Recalculate();

        Array.Clear(Y);

        Timeline<global::NorthStar.JumpTrack, global::NorthStar.JumpClip>.Apply(Ids, TickClock, true, Y);
        for (var i = 0; i < Entities; i++)
        {
            Y[i] = StrictFloat.Mul(Y[i], jumpPowerColumn[i]);
        }

        Timeline.Advance(Ids, TickClock, true);

        FallKernel(Input.AxisF(0));

        streams.EmitFromBools(landedChannel, landedCol, 0, Entities, &NorthStarEvents.MakeLanded);
        streams.EmitFromNonZero(manaChannel, applied, 0, Entities, &NorthStarEvents.MakeMana);
        for (var i = 0; i < Entities; i++)
        {
            LastApplied[i] = applied[i];
            applied[i] = 0;
        }

        streams.Swap();
    }

    void FallKernel(float dt)
    {
        for (var i = 0; i < Entities; i++)
        {
            var gravity = StrictFloat.Mul(StrictFloat.Mul(Gravity, moveSpeedColumn[i]), dt);
            var vy = StrictFloat.Add(Velocity[i], gravity);
            var dy = StrictFloat.Add(StrictFloat.Mul(vy, dt), Y[i]);
            var pos = StrictFloat.Add(Position[i], dy);
            byte hit = 0;
            if (pos < 0f)
            {
                pos = 0f;
                vy = StrictFloat.Mul(-vy, Restitution);
                hit = 1;
            }

            Velocity[i] = vy;
            Position[i] = pos;
            landedCol[i] = hit;
        }
    }

    public FrameRecord Record()
    {
        var bits = new uint[Entities];
        var record = new FrameRecord
        {
            PositionBits = Bits(Position),
            VelocityBits = Bits(Velocity),
            YBits = Bits(Y),
            MoveSpeedBits = ColumnBits(moveSpeedColumn),
            JumpPowerBits = ColumnBits(jumpPowerColumn),
            MaxManaBits = ColumnBits(maxManaColumn),
            Mana = new int[Entities],
            Applied = new int[Entities],
            Landed = new byte[Entities],
            Events = new List<string>(EventLog),
        };
        for (var i = 0; i < Entities; i++)
        {
            record.Mana[i] = mana[i];
            record.Applied[i] = LastApplied[i];
            record.Landed[i] = landedCol[i];
        }

        return record;
    }

    static uint[] Bits(float[] values)
    {
        var bits = new uint[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            bits[i] = BitConverter.SingleToUInt32Bits(values[i]);
        }

        return bits;
    }

    uint[] ColumnBits(float* column)
    {
        var bits = new uint[Entities];
        for (var i = 0; i < Entities; i++)
        {
            bits[i] = BitConverter.SingleToUInt32Bits(column[i]);
        }

        return bits;
    }

    static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public void Dispose()
    {
        Board.Dispose();
        if (ownsInput)
        {
            Input.Dispose();
        }
        streams.Dispose();
        NativeMemory.Free(moveSpeedColumn);
        NativeMemory.Free(jumpPowerColumn);
        NativeMemory.Free(maxManaColumn);
        NativeMemory.Free(mana);
        NativeMemory.Free(applied);
        NativeMemory.Free(landedCol);
    }
}
