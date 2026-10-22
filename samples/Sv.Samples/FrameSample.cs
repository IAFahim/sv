using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kernels.Testing;
using Sv;
using Sv.Compose;
using Tl;

public readonly record struct JumpClip(float Velocity);

public readonly record struct JumpTrack(float Scale) : IBlend<JumpClip>
{
    public void Blend(in JumpClip first, in JumpClip second, float factor, out JumpClip result)
    {
        result = new JumpClip(first.Velocity + (second.Velocity - first.Velocity) * factor);
    }
}

public readonly struct MoveY : ITrack<JumpTrack, JumpClip>
{
    public static void OnActive(in Frame<JumpTrack, JumpClip> frame, ref float y)
    {
        y += frame.Direction * frame.Clip.Velocity * frame.Track.Scale;
    }
}

// 100,000 entities on the frozen frame (clause 7) through ONE call: Frame.Run.
// Drain, intrinsics, recalc, timeline, kernel, conditions, swap — then the tape
// replays the whole run bit-identically on a fresh host.

internal static unsafe class FrameSample
{
    const int Rows = 100_000;

    const float Dt = 0.016666668f;

    public struct Landed
    {
        public int Slot;
    }

    public struct ManaGained
    {
        public int Slot;

        public int Amount;
    }

    [UnmanagedCallersOnly]
    static Landed MakeLanded(int slot, int value) => new() { Slot = slot };

    [UnmanagedCallersOnly]
    static ManaGained MakeMana(int slot, int value) => new() { Slot = slot, Amount = value };

    public unsafe struct World : IFrameWorld
    {
        public float[] Position;

        public float[] Velocity;

        public int* Mana;

        public byte* LandedCol;

        public long* LandedCount;

        public long* ManaCount;

        public StatId MoveSpeed;

        public StatId MaxMana;

        public void Reconcile(Game game)
        {
        }

        public void DispatchEvent(Game game, ushort typeId, void* payload)
        {
            if (typeId == game.Channel<Landed>().Id)
            {
                (*LandedCount)++;
                game.Board.AddModifier((*(Landed*)payload).Slot, MoveSpeed, new Modifier(ModifierKind.Multi, 1.5f));
                return;
            }

            (*ManaCount)++;
        }

        public void RunIntrinsics(Game game)
        {
            if (game.Input.Pressed(0))
            {
                game.Board.RunIntrinsicsChunk(Mana, 25, Clamp.StatMax(game.Board, MaxMana), 0, Rows, game.Applied);
            }
        }

        public void RunConditions(Game game)
        {
            game.Streams.EmitFromBools(game.Channel<Landed>(), LandedCol, 0, Rows, &MakeLanded);
            game.Streams.EmitFromNonZero(game.Channel<ManaGained>(), game.Applied, 0, Rows, &MakeMana);
            for (var i = 0; i < Rows; i++)
            {
                game.Applied[i] = 0;
            }
        }
    }

    public struct Jump : ITimelineStep
    {
        static StatId jumpPower;

        static bool cached;

        public static bool Loop => true;

        public static void Apply(Game game, float[] effect)
        {
            TimelineStep<JumpTrack, JumpClip>.Apply(game, Loop, effect);
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
            TimelineStep<JumpTrack, JumpClip>.Advance(game, Loop);
        }
    }

    public struct Fall : IKernelStep
    {
        public static void Run<THost>(ref THost world, Game game, float dt) where THost : struct, IFrameWorld
        {
            ref var host = ref Unsafe.As<THost, World>(ref world);
            var gravityDt = StrictFloat.Multiply(-9.8f, dt);
            for (var i = 0; i < Rows; i++)
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
        }
    }

    static GameHost<EventLink<ManaGained, EventLink<Landed, Chain>>, TimelineLink<Jump, Chain>, KernelLink<Fall, Chain>, World> Build()
    {
        var schema = SchemaBuilder.Create()
            .Stat("move_speed", 6f)
            .Stat("jump_power", 1f)
            .Stat("max_mana", 100f)
            .Intrinsic("mana")
            .Build();
        var mana = (int*)NativeMemory.AllocZeroed((nuint)Rows * (nuint)sizeof(int), (nuint)sizeof(int));
        var landed = (byte*)NativeMemory.AllocZeroed((nuint)Rows, 1);
        var world = new World
        {
            Position = new float[Rows],
            Velocity = new float[Rows],
            Mana = mana,
            LandedCol = landed,
            LandedCount = (long*)NativeMemory.AllocZeroed(sizeof(long), sizeof(long)),
            ManaCount = (long*)NativeMemory.AllocZeroed(sizeof(long), sizeof(long)),
            MoveSpeed = schema.Stat("move_speed"),
            MaxMana = schema.Stat("max_mana"),
        };
        var host = Game.Create(schema, Rows)
            .Event<Landed>()
            .Event<ManaGained>()
            .Timeline<Jump>()
            .Kernel<Fall>()
            .Build(world, InputState.Create(2, 1, 8));
        var jumpId = TimelineAsset.Load(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "jump.tlb")));
        for (var i = 0; i < Rows; i++)
        {
            host.Game.Board.Acquire();
            host.Game.TimelineIds[i] = jumpId;
            world.Position[i] = 0.01f;
            world.Velocity[i] = -130f;
            mana[i] = 40;
        }

        host.Game.Board.Recalculate();
        return host;
    }

    static void Tick(GameHost<EventLink<ManaGained, EventLink<Landed, Chain>>, TimelineLink<Jump, Chain>, KernelLink<Fall, Chain>, World> host, uint[] axes, ulong[] words, ulong buttons)
    {
        words[0] = buttons;
        fixed (uint* ax = axes)
        fixed (ulong* bt = words)
        {
            host.Frame.Tick(Dt, ax, bt);
        }
    }

    public static void Run()
    {
        Console.WriteLine($"[frame] {Rows:N0} entities, one call per frame");
        var axes = new uint[] { BitConverter.SingleToUInt32Bits(Dt), 0x3F800000u };
        var words = new ulong[] { 1 };
        var idle = new ulong[] { 0 };
        var path = Path.Combine(Path.GetTempPath(), "sv-frame-sample.tape");
        try
        {
            using var live = Build();
            live.Record(path);
            for (var t = 0; t < 3; t++)
            {
                fixed (uint* ax = axes)
                fixed (ulong* bt = words)
                {
                    live.Game.Input.Capture(ax, bt);
                    live.Frame.Run(Dt);
                }
            }

            live.EndRecord();
            Console.WriteLine($"  three taped frames: {*live.World.LandedCount:N0} landed + {*live.World.ManaCount:N0} mana events drained," +
                              $" mana 40 -> {live.World.Mana[0]}, move_speed 6 -> {live.Game.Board.Output(live.World.MoveSpeed)[0]} (landed Multi 1.5)," +
                              $" jump arc {live.Game.Effect(0)[0]:0.0} x jump_power {live.Game.Board.Output(live.Game.Board.Schema.Stat("jump_power"))[0]:0.0}");
            Console.WriteLine($"  tape: {new FileInfo(path).Length / 1024.0:0.0} KiB — inputs + digest + body per frame");

            var (steady, bytes) = Program.Measure(200, () => Tick(live, axes, idle, 0));
            Console.WriteLine($"  steady frame:                        {steady:0.0} us  ({Rows / steady:0} M entities/s, {bytes} B)");

            var sink = OperatingSystem.IsLinux() ? "/dev/null" : Path.Combine(Path.GetTempPath(), "sv-frame-sink.tape");
            live.Record(sink);
            var (taped, tapedBytes) = Program.Measure(200, () => Tick(live, axes, idle, 0));
            live.EndRecord();
            Console.WriteLine($"  with the tape digest running:        {taped:0.0} us  (+{taped - steady:0.0} us for whole-run determinism, {tapedBytes} B)");

            using var replay = Build();
            var frames = replay.Playback(path);
            Console.WriteLine($"  playback on a fresh host: {frames}/3 frames bit-identical — mana {replay.World.Mana[0]}, velocity {replay.World.Velocity[0]:0.0}," +
                              $" events {(*replay.World.LandedCount + *replay.World.ManaCount):N0}");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
