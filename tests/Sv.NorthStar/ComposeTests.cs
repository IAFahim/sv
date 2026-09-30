using System;
using System.Collections.Generic;
using System.IO;
using Kernels.Testing;
using Sv;
using Sv.Compose;
using Tl;
using Xunit;

namespace Sv.NorthStar;

public unsafe struct WalkWorld : IFrameWorld
{
    public const int Entities = 4;

    public float[] Position;

    public float[] Velocity;

    public int[] Mana;

    public byte[] LandedCol;

    public int[] LastApplied;

    public List<string> Log;

    public StatId MoveSpeed;

    public StatId MaxMana;

    public WalkWorld(Schema schema)
    {
        Position = new float[Entities];
        Velocity = new float[Entities];
        Mana = new int[Entities];
        LandedCol = new byte[Entities];
        LastApplied = new int[Entities];
        Log = new List<string>();
        MoveSpeed = schema.Stat("move_speed");
        MaxMana = schema.Stat("max_mana");
    }

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
            fixed (int* mana = Mana)
            {
                game.Board.RunIntrinsicsChunk(mana, 25, Clamp.StatMax(game.Board, MaxMana), 0, Entities, game.Applied);
            }
        }
    }

    public void RunConditions(Game game)
    {
        var landed = game.Channel<Landed>();
        var manaChannel = game.Channel<ManaGained>();
        fixed (byte* landedPtr = LandedCol)
        {
            game.Streams.EmitFromBools(landed, landedPtr, 0, Entities, &NorthStarEvents.MakeLanded);
        }

        game.Streams.EmitFromNonZero(manaChannel, game.Applied, 0, Entities, &NorthStarEvents.MakeMana);
        for (var i = 0; i < Entities; i++)
        {
            LastApplied[i] = game.Applied[i];
            game.Applied[i] = 0;
        }
    }
}

public unsafe struct JumpTimeline : ITimelineStep
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

public struct FallKernel : IKernelStep
{
    public static void Run<THost>(in THost world, Game game, float dt) where THost : struct, IFrameWorld
    {
        ref var walk = ref System.Runtime.CompilerServices.Unsafe.As<THost, WalkWorld>(ref System.Runtime.CompilerServices.Unsafe.AsRef(in world));
        var gravityDt = StrictFloat.Multiply(NorthStarHarness.GravityField, dt);
        for (var i = 0; i < WalkWorld.Entities; i++)
        {
            var velBefore = walk.Velocity[i];
            var posBefore = walk.Position[i];
            var posAfter = StrictFloat.Add(posBefore, StrictFloat.Multiply(velBefore, dt));
            var velAfter = StrictFloat.Subtract(velBefore, gravityDt);
            if (posAfter < 0f)
            {
                posAfter = 0f;
                velAfter = 0f;
            }

            walk.Velocity[i] = velAfter;
            walk.Position[i] = posAfter;
            walk.LandedCol[i] = (byte)(posBefore != 0f && posAfter == 0f ? 1 : 0);
        }
    }
}

public sealed unsafe class ComposeTests
{
    const float Dt = 0.016666668f;

    static GameHost<EventLink<ManaGained, EventLink<Landed, Chain>>, TimelineLink<JumpTimeline, Chain>, KernelLink<FallKernel, Chain>, WalkWorld> BuildGame(InputState input)
    {
        var schema = SchemaBuilder.Create()
            .Stat("move_speed", 6f)
            .Stat("jump_power", 1f)
            .Stat("max_mana", 100f)
            .Intrinsic("mana")
            .Build();
        var world = new WalkWorld(schema);
        var host = Game.Create(schema, WalkWorld.Entities)
            .Event<Landed>()
            .Event<ManaGained>()
            .Timeline<JumpTimeline>()
            .Kernel<FallKernel>()
            .Build(world, input);
        var jumpId = TimelineAsset.Load(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "jump.tlb")));
        for (var i = 0; i < WalkWorld.Entities; i++)
        {
            host.Game.Board.Acquire();
            host.Game.TimelineIds[i] = jumpId;
            world.Position[i] = 0.01f;
            world.Velocity[i] = -130f;
            world.Mana[i] = 40;
        }

        host.Game.Board.Recalculate();
        return host;
    }

    static FrameRecord Record(GameHost<EventLink<ManaGained, EventLink<Landed, Chain>>, TimelineLink<JumpTimeline, Chain>, KernelLink<FallKernel, Chain>, WalkWorld> host)
    {
        var world = host.World;
        var record = new FrameRecord
        {
            PositionBits = Bits(world.Position, WalkWorld.Entities),
            VelocityBits = Bits(world.Velocity, WalkWorld.Entities),
            YBits = Bits(host.Game.Effect(0), WalkWorld.Entities),
            MoveSpeedBits = ColumnBits(host.Game, "move_speed"),
            JumpPowerBits = ColumnBits(host.Game, "jump_power"),
            MaxManaBits = ColumnBits(host.Game, "max_mana"),
            Mana = new int[WalkWorld.Entities],
            Applied = new int[WalkWorld.Entities],
            Landed = new byte[WalkWorld.Entities],
            Events = new List<string>(world.Log),
        };
        for (var i = 0; i < WalkWorld.Entities; i++)
        {
            record.Mana[i] = world.Mana[i];
            record.Applied[i] = world.LastApplied[i];
            record.Landed[i] = world.LandedCol[i];
        }

        return record;
    }

    static uint[] Bits(float[] values, int count)
    {
        var bits = new uint[count];
        for (var i = 0; i < count; i++)
        {
            bits[i] = BitConverter.SingleToUInt32Bits(values[i]);
        }

        return bits;
    }

    static uint[] ColumnBits(Game game, string stat)
    {
        var column = game.Board.OutputPtr(game.Board.Schema.Stat(stat));
        var bits = new uint[WalkWorld.Entities];
        for (var i = 0; i < WalkWorld.Entities; i++)
        {
            bits[i] = BitConverter.SingleToUInt32Bits(column[i]);
        }

        return bits;
    }

    [Fact]
    public void FrameRun_IndependentWorldPort_MatchesNorthStarHarness_BitIdentical()
    {
        var axesBits = new[] { BitConverter.SingleToUInt32Bits(Dt), 0x3F800000u };
        var buttons = new ulong[] { 1ul << NorthStarHarness.RegenButton };

        using var manual = new NorthStarHarness();
        var manualFrames = new FrameRecord[3];
        for (var t = 0; t < 3; t++)
        {
            fixed (uint* ax = axesBits)
            fixed (ulong* bt = buttons)
            {
                manual.CaptureTick(ax, bt);
            }

            manualFrames[t] = manual.Record();
        }

        using var input = InputState.Create(2, 1, 8);
        using var composed = BuildGame(input);
        var composedFrames = new FrameRecord[3];
        for (var t = 0; t < 3; t++)
        {
            fixed (uint* ax = axesBits)
            fixed (ulong* bt = buttons)
            {
                composed.Frame.Tick(Dt, ax, bt);
            }

            composedFrames[t] = Record(composed);
        }

        for (var t = 0; t < 3; t++)
        {
            Assert.Equal(manualFrames[t].PositionBits, composedFrames[t].PositionBits);
            Assert.Equal(manualFrames[t].VelocityBits, composedFrames[t].VelocityBits);
            Assert.Equal(manualFrames[t].YBits, composedFrames[t].YBits);
            Assert.Equal(manualFrames[t].MoveSpeedBits, composedFrames[t].MoveSpeedBits);
            Assert.Equal(manualFrames[t].JumpPowerBits, composedFrames[t].JumpPowerBits);
            Assert.Equal(manualFrames[t].MaxManaBits, composedFrames[t].MaxManaBits);
            Assert.Equal(manualFrames[t].Mana, composedFrames[t].Mana);
            Assert.Equal(manualFrames[t].Applied, composedFrames[t].Applied);
            Assert.Equal(manualFrames[t].Landed, composedFrames[t].Landed);
            Assert.Equal(manualFrames[t].Events, composedFrames[t].Events);
        }
    }

    [Fact]
    public void FrameRun_WarmPathAllocatesZeroManagedBytes()
    {
        using var input = InputState.Create(2, 1, 8);
        using var composed = BuildGame(input);
        var axesBits = new[] { BitConverter.SingleToUInt32Bits(Dt), 0x3F800000u };
        var buttons = new ulong[1];
        for (var warm = 0; warm < 16; warm++)
        {
            fixed (uint* ax = axesBits)
            fixed (ulong* bt = buttons)
            {
                composed.Frame.Tick(Dt, ax, bt);
            }
        }

        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        long sink = 0;
        for (var t = 0; t < 2000; t++)
        {
            fixed (uint* ax = axesBits)
            fixed (ulong* bt = buttons)
            {
                composed.Frame.Tick(Dt, ax, bt);
            }

            sink += composed.World.Mana[t % WalkWorld.Entities] + composed.World.LastApplied[t % WalkWorld.Entities];
        }

        var deltaBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(deltaBytes == 0, $"Frame.Run allocated {deltaBytes} managed bytes over 2,000 frames (sink {sink})");
    }
}
