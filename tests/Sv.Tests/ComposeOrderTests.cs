using System;
using System.IO;
using Sv;
using Sv.Compose;
using Xunit;

namespace Sv.Tests;

// Atom 1.6 (roadmap issues/0058, issue 0067): builder declaration order is execution
// order and id order (law L10, F15); the world is one mutable instance passed by
// reference — struct fields persist across frames (F14); tape playback rejects
// truncation before reading inputs (F19).

public unsafe class ComposeOrderTests
{
    struct FirstEvent
    {
#pragma warning disable CS0649
        public int Slot;
#pragma warning restore CS0649
    }

    struct SecondEvent
    {
#pragma warning disable CS0649
        public int Slot;
#pragma warning restore CS0649
    }

    struct OrderWorld : IFrameWorld
    {
        public static string Trail = "";

        public int Counter;

        public void Reconcile(Game game)
        {
            Trail += "1";
        }

        public void DispatchEvent(Game game, ushort typeId, void* payload)
        {
        }

        public void RunIntrinsics(Game game)
        {
            Trail += "2";
            Counter++;
        }

        public void RunConditions(Game game)
        {
            Trail += "7";
        }
    }

    struct FirstTimeline : ITimelineStep
    {
        public static bool Loop => true;

        public static void Apply(Game game, float[] effect)
        {
            OrderWorld.Trail += ReferenceEquals(game.Effect(0), effect) ? "3" : "?";
        }

        public static void Transform(Game game, float[] effect)
        {
        }

        public static void Advance(Game game)
        {
        }
    }

    struct SecondTimeline : ITimelineStep
    {
        public static bool Loop => true;

        public static void Apply(Game game, float[] effect)
        {
            OrderWorld.Trail += ReferenceEquals(game.Effect(1), effect) ? "4" : "?";
        }

        public static void Transform(Game game, float[] effect)
        {
        }

        public static void Advance(Game game)
        {
        }
    }

    struct FirstKernel : IKernelStep
    {
        public static void Run<THost>(ref THost world, Game game, float dt) where THost : struct, IFrameWorld
        {
            OrderWorld.Trail += "5";
        }
    }

    struct SecondKernel : IKernelStep
    {
        public static void Run<THost>(ref THost world, Game game, float dt) where THost : struct, IFrameWorld
        {
            OrderWorld.Trail += "6";
        }
    }

    static GameHost<EventLink<SecondEvent, EventLink<FirstEvent, Chain>>, TimelineLink<SecondTimeline, TimelineLink<FirstTimeline, Chain>>, KernelLink<SecondKernel, KernelLink<FirstKernel, Chain>>, OrderWorld> Build()
    {
        var schema = SchemaBuilder.Create().Stat("power", 1f).Intrinsic("hp").Build();
        return Game.Create(schema, 64)
            .Event<FirstEvent>()
            .Event<SecondEvent>()
            .Timeline<FirstTimeline>()
            .Timeline<SecondTimeline>()
            .Kernel<FirstKernel>()
            .Kernel<SecondKernel>()
            .Build(new OrderWorld(), InputState.Create(1, 1, 8));
    }

    [Fact]
    public void DeclarationOrderIsExecutionOrderAndIdOrder()
    {
        using var host = Build();
        Assert.True(host.Game.Channel<FirstEvent>().Id < host.Game.Channel<SecondEvent>().Id);
        OrderWorld.Trail = "";
        host.Frame.Run(0.016f);
        Assert.Equal("1234567", OrderWorld.Trail);
    }

    [Fact]
    public void WorldFieldsPersistAcrossFrames()
    {
        using var host = Build();
        host.Frame.Run(0.016f);
        host.Frame.Run(0.016f);
        Assert.Equal(2, host.World.Counter);
    }

    [Fact]
    public void PlaybackRejectsEveryTruncationCleanly()
    {
        var schema = SchemaBuilder.Create().Stat("power", 1f).Intrinsic("hp").Build();
        using var host = Game.Create(schema, 64).Build(new OrderWorld(), InputState.Create(2, 1, 8));
        var axes = stackalloc uint[2];
        var buttons = stackalloc ulong[1];
        var path = Path.Combine(Path.GetTempPath(), $"sv-trunc-{Guid.NewGuid():N}.tape");
        try
        {
            host.Record(path);
            for (var frame = 0; frame < 3; frame++)
            {
                host.Frame.Tick(0.016666668f, axes, buttons);
            }

            host.EndRecord();
            var bytes = File.ReadAllBytes(path);
            var rejected = 0;
            for (var length = 0; length < bytes.Length; length++)
            {
                var truncated = new byte[length];
                Array.Copy(bytes, truncated, length);
                File.WriteAllBytes(path, truncated);
                using var replay = Game.Create(schema, 64).Build(new OrderWorld(), InputState.Create(2, 1, 8));
                try
                {
                    replay.Playback(path);
                }
                catch (InvalidDataException)
                {
                    rejected++;
                }
                catch (InvalidOperationException)
                {
                }
            }

            Assert.True(rejected > 0, "no truncation was rejected — the bounds check never fired");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
