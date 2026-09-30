using Kernels.Testing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sv;
using Sv.Compose;
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

public unsafe struct NorthStarWorld : IFrameWorld
{
    public float[] Position;

    public float[] Velocity;

    public int* Mana;

    public byte* LandedCol;

    public int[] LastApplied;

    public List<string> Log;

    public StatId MoveSpeed;

    public StatId MaxMana;

#if KERNELS_PRESENT
    public Simulation.Position[] KernelPositions;

    public Simulation.Velocity[] KernelVelocities;
#endif

    public void Reconcile(Game game)
    {
    }

    public void DispatchEvent(Game game, ushort typeId, void* payload)
    {
        if (typeId == game.Channel<Landed>().Id)
        {
            var evt = *(Landed*)payload;
            Log.Add($"landed:{evt.Slot}");
            game.Board.AddModifier(evt.Slot, MoveSpeed, new Modifier(ModifierKind.Multi, 1.5f));
            return;
        }

        var mana = *(ManaGained*)payload;
        Log.Add($"mana:{mana.Slot}:{mana.Amount}");
    }

    public void RunIntrinsics(Game game)
    {
        if (game.Input.Pressed(NorthStarHarness.RegenButton))
        {
            game.Board.RunIntrinsicsChunk(Mana, 25, Clamp.StatMax(game.Board, MaxMana), 0, NorthStarHarness.Entities, game.Applied);
        }
    }

    public void RunConditions(Game game)
    {
        game.Streams.EmitFromBools(game.Channel<Landed>(), LandedCol, 0, NorthStarHarness.Entities, &NorthStarEvents.MakeLanded);
        game.Streams.EmitFromNonZero(game.Channel<ManaGained>(), game.Applied, 0, NorthStarHarness.Entities, &NorthStarEvents.MakeMana);
        for (var i = 0; i < NorthStarHarness.Entities; i++)
        {
            LastApplied[i] = game.Applied[i];
            game.Applied[i] = 0;
        }
    }
}

public unsafe struct NorthStarJump : ITimelineStep
{
    static StatId jumpPower;

    static bool cached;

    public static bool Loop => true;

    public static void Apply(Game game, float[] effect)
    {
        TimelineStep<global::NorthStar.JumpTrack, global::NorthStar.JumpClip>.Apply(game, Loop, effect);
    }

    public static void Transform(Game game, float[] effect)
    {
        if (!cached)
        {
            jumpPower = game.Board.Schema.Stat("jump_power");
            cached = true;
        }

        var power = game.Board.OutputPtr(jumpPower);
        for (var i = 0; i < effect.Length; i++)
        {
            effect[i] = StrictFloat.Multiply(effect[i], power[i]);
        }
    }

    public static void Advance(Game game)
    {
        TimelineStep<global::NorthStar.JumpTrack, global::NorthStar.JumpClip>.Advance(game, Loop);
    }
}

public unsafe struct NorthStarFall : IKernelStep
{
    public static void Run<THost>(in THost world, Game game, float dt) where THost : struct, IFrameWorld
    {
        ref var host = ref Unsafe.As<THost, NorthStarWorld>(ref Unsafe.AsRef(in world));
        var gravityDt = StrictFloat.Multiply(NorthStarHarness.GravityField, dt);
#if KERNELS_PRESENT
        for (var i = 0; i < NorthStarHarness.Entities; i++)
        {
            host.KernelVelocities[i].Y = host.Velocity[i];
            host.KernelPositions[i].Y = host.Position[i];
        }

        var fall = new Simulation.Fall { Dt = dt, Gravity = NorthStarHarness.GravityField };
        fall.RunChunk(host.KernelPositions, host.KernelVelocities);
        for (var i = 0; i < NorthStarHarness.Entities; i++)
        {
            var velBefore = host.Velocity[i];
            var posBefore = host.Position[i];
            var posAfter = StrictFloat.Add(posBefore, StrictFloat.Multiply(velBefore, dt));
            var velAfter = StrictFloat.Subtract(velBefore, gravityDt);
            if (posAfter < 0f)
            {
                posAfter = 0f;
                velAfter = 0f;
            }

            if (BitConverter.SingleToUInt32Bits(host.KernelPositions[i].Y) != BitConverter.SingleToUInt32Bits(posAfter) ||
                BitConverter.SingleToUInt32Bits(host.KernelVelocities[i].Y) != BitConverter.SingleToUInt32Bits(velAfter))
            {
                throw new InvalidOperationException($"kernel/scalar divergence row {i}: pos {host.KernelPositions[i].Y} vs {posAfter}, vel {host.KernelVelocities[i].Y} vs {velAfter}");
            }

            host.Position[i] = host.KernelPositions[i].Y;
            host.Velocity[i] = host.KernelVelocities[i].Y;
            host.LandedCol[i] = (byte)(posBefore != 0f && posAfter == 0f ? 1 : 0);
        }
#else
        for (var i = 0; i < NorthStarHarness.Entities; i++)
        {
            var velBefore = host.Velocity[i];
            var posBefore = host.Position[i];
            var posAfter = StrictFloat.Add(posBefore, StrictFloat.Multiply(velBefore, dt));
            var velAfter = StrictFloat.Subtract(velBefore, gravityDt);
            if (posAfter < 0f)
            {
                posAfter = 0f;
                velAfter = 0f;
            }

            host.Velocity[i] = velAfter;
            host.Position[i] = posAfter;
            host.LandedCol[i] = (byte)(posBefore != 0f && posAfter == 0f ? 1 : 0);
        }
#endif
    }
}

public sealed unsafe class NorthStarHarness : IDisposable
{
    public const int Entities = 4;

    public const int RegenButton = 0;

    public const float Gravity = -9.8f;

    public const float GravityField = -9.8f;

    public readonly float Dt = 0.016666668f;

    GameHost<EventLink<ManaGained, EventLink<Landed, Chain>>, TimelineLink<NorthStarJump, Chain>, KernelLink<NorthStarFall, Chain>, NorthStarWorld> host = null!;

    public GameHost<EventLink<ManaGained, EventLink<Landed, Chain>>, TimelineLink<NorthStarJump, Chain>, KernelLink<NorthStarFall, Chain>, NorthStarWorld> Host => host;

    public Board Board = null!;

    StatId moveSpeed;

    StatId jumpPower;

    StatId maxMana;

    int* mana;

    byte* landedCol;

    public InputState Input = null!;

    public float[] Position = new float[Entities];

    public float[] Velocity = new float[Entities];

#if KERNELS_PRESENT
    readonly Simulation.Position[] kernelPositions = new Simulation.Position[Entities];

    readonly Simulation.Velocity[] kernelVelocities = new Simulation.Velocity[Entities];
#endif

    public float[] Y = new float[Entities];

    public ushort[] Ids = new ushort[Entities];

    public ushort[] TickClock = new ushort[Entities];

    public List<string> EventLog = new();

    public int[] LastApplied = new int[Entities];

    readonly bool ownsInput;

    public NorthStarHarness()
    {
        ownsInput = true;
        Initialize(InputState.Create(2, 1, 8));
    }

    public NorthStarHarness(InputState sharedInput)
    {
        ownsInput = false;
        Initialize(sharedInput);
    }

    void Initialize(InputState input)
    {
        var schema = SchemaBuilder.Create()
            .Stat("move_speed", 6f)
            .Stat("jump_power", 1f)
            .Stat("max_mana", 100f)
            .Intrinsic("mana")
            .Build();
        moveSpeed = schema.Stat("move_speed");
        jumpPower = schema.Stat("jump_power");
        maxMana = schema.Stat("max_mana");
        mana = (int*)NativeMemory.AllocZeroed((nuint)Entities * (nuint)sizeof(int), (nuint)sizeof(int));
        landedCol = (byte*)NativeMemory.AllocZeroed((nuint)Entities, 1);
        var world = new NorthStarWorld
        {
            Position = Position,
            Velocity = Velocity,
            Mana = mana,
            LandedCol = landedCol,
            LastApplied = LastApplied,
            Log = EventLog,
            MoveSpeed = moveSpeed,
            MaxMana = maxMana,
#if KERNELS_PRESENT
            KernelPositions = kernelPositions,
            KernelVelocities = kernelVelocities,
#endif
        };
        host = Game.Create(schema, Entities)
            .Event<Landed>()
            .Event<ManaGained>()
            .Timeline<NorthStarJump>()
            .Kernel<NorthStarFall>()
            .Build(world, input);
        Board = host.Game.Board;
        Input = input;
        Ids = host.Game.TimelineIds;
        TickClock = host.Game.TimelineClocks;
        Y = host.Game.Effect(0);

        var jumpId = TimelineAsset.Load(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "jump.tlb")));
        for (var i = 0; i < Entities; i++)
        {
            Board.Acquire();
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
        host.Frame.Run(Input.AxisF(0));
    }

    public void ReplayTick()
    {
        AssertTrue(Input.ReplayStep(), "replay step exhausted");
        host.Frame.Run(Input.AxisF(0));
    }

    public FrameRecord Record()
    {
        var record = new FrameRecord
        {
            PositionBits = Bits(Position),
            VelocityBits = Bits(Velocity),
            YBits = Bits(Y),
            MoveSpeedBits = ColumnBits(moveSpeed),
            JumpPowerBits = ColumnBits(jumpPower),
            MaxManaBits = ColumnBits(maxMana),
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
        var bits = new uint[Entities];
        for (var i = 0; i < Entities; i++)
        {
            bits[i] = BitConverter.SingleToUInt32Bits(values[i]);
        }

        return bits;
    }

    uint[] ColumnBits(StatId stat)
    {
        var column = Board.OutputPtr(stat);
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
        host.Dispose();
        NativeMemory.Free(mana);
        NativeMemory.Free(landedCol);
        if (ownsInput)
        {
            Input.Dispose();
        }
    }
}
