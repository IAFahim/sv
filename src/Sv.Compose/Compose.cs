using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Tl;

namespace Sv.Compose;

public interface IEventChain
{
    static abstract void Register(Game game);
}

public interface ITimelineChain
{
    static abstract int Count { get; }

    static abstract void Run(Game game, int index);
}

public interface IKernelChain
{
    static abstract void Run<THost>(ref THost world, Game game, float dt) where THost : struct, IFrameWorld;
}

public readonly struct Chain : IEventChain, ITimelineChain, IKernelChain
{
    static void IEventChain.Register(Game game)
    {
    }

    static int ITimelineChain.Count => 0;

    static void ITimelineChain.Run(Game game, int index)
    {
    }

    static void IKernelChain.Run<THost>(ref THost world, Game game, float dt)
    {
    }
}

public readonly struct EventLink<T, TNext> : IEventChain where T : unmanaged where TNext : struct, IEventChain
{
    static void IEventChain.Register(Game game)
    {
        TNext.Register(game);
        game.AddChannel<T>();
    }
}

public readonly struct TimelineLink<TStep, TNext> : ITimelineChain where TStep : ITimelineStep where TNext : struct, ITimelineChain
{
    static int ITimelineChain.Count => 1 + TNext.Count;

    static void ITimelineChain.Run(Game game, int index)
    {
        TNext.Run(game, index);
        var self = index + TNext.Count;
        var effect = game.Effect(self);
        Array.Clear(effect, 0, effect.Length);
        TStep.Apply(game, effect);
        TStep.Transform(game, effect);
        TStep.Advance(game);
    }
}

public readonly struct KernelLink<TStep, TNext> : IKernelChain where TStep : IKernelStep where TNext : struct, IKernelChain
{
    static void IKernelChain.Run<THost>(ref THost world, Game game, float dt)
    {
        TNext.Run(ref world, game, dt);
        TStep.Run(ref world, game, dt);
    }
}

/// <summary>
/// Host world: implements the frame duties the composition layer refuses to own — event payload
/// dispatch, the intrinsics pass (frame-frozen bounds), and the condition run-walk emits
/// (including emit-from-applied followed by zeroing <see cref="Game.Applied"/>). Dispatched as
/// constrained calls on a struct: direct, devirtualized, no boxing.
/// </summary>
public unsafe interface IFrameWorld
{
    void Reconcile(Game game);

    void DispatchEvent(Game game, ushort typeId, void* payload);

    void RunIntrinsics(Game game);

    void RunConditions(Game game);
}

/// <summary>
/// One timeline registered on the game. The tl calls live in <see cref="TimelineStep{TTrack,TClip}"/>,
/// so the tl version seam (Apply/Advance naming) is absorbed here and nowhere else.
/// </summary>
public interface ITimelineStep
{
    static abstract bool Loop { get; }

    static abstract void Apply(Game game, float[] effect);

    static abstract void Transform(Game game, float[] effect);

    static abstract void Advance(Game game);
}

public interface IKernelStep
{
    static abstract void Run<THost>(ref THost world, Game game, float dt) where THost : struct, IFrameWorld;
}

public static class TimelineStep<TTrack, TClip> where TClip : unmanaged where TTrack : unmanaged, IBlend<TClip>
{
    public static void Apply(Game game, bool loop, float[] effect)
    {
        Timeline<TTrack, TClip>.Apply(game.TimelineIds, game.TimelineClocks, loop, effect);
    }

    public static void Advance(Game game, bool loop)
    {
        Timeline<TTrack, TClip>.Advance(game.TimelineIds, game.TimelineClocks, loop);
    }
}

public sealed unsafe class Game : IDisposable
{
    public Board Board { get; }

    public InputState Input { get; }

    public EventStreams Streams { get; }

    public int Capacity => Board.Capacity;

    public ushort[] TimelineIds { get; }

    public ushort[] TimelineClocks { get; }

    public int* Applied { get; }

    readonly float[][] effects;

    readonly Dictionary<Type, object> channels;

    readonly long gameShape;

    bool disposed;

    const uint GameMagic = 0x47414d45;

    Game(Schema schema, int capacity, int timelineCount, InputState input)
    {
        Board = Board.Create(schema, capacity);
        var shapeBytes = stackalloc byte[16];
        *(long*)shapeBytes = Board.StateShape;
        *(int*)(shapeBytes + 8) = timelineCount;
        *(int*)(shapeBytes + 12) = Board.Capacity;
        gameShape = (long)XxHash64.Hash(shapeBytes, 16);
        Input = input;
        Streams = EventStreams.Create();
        TimelineIds = new ushort[Board.Capacity];
        TimelineClocks = new ushort[Board.Capacity];
        Applied = (int*)NativeMemory.AllocZeroed((nuint)Board.Capacity * sizeof(int));
        effects = new float[timelineCount][];
        for (var i = 0; i < timelineCount; i++)
        {
            effects[i] = new float[Board.Capacity];
        }

        channels = new Dictionary<Type, object>();
    }

    public static GameBuilder<Chain, Chain, Chain> Create(Schema schema, int capacity)
    {
        return new GameBuilder<Chain, Chain, Chain>(schema, capacity);
    }

    public float[] Effect(int index) => effects[index];

    public Channel<T> Channel<T>() where T : unmanaged
    {
        return (Channel<T>)channels[typeof(T)];
    }

    /// <summary>
    /// Applies a reconcile permutation to every slot-indexed column the game owns: timeline ids,
    /// clocks, the applied column and every timeline effect column. Dead slots read zero
    /// afterwards. Pass the permutation returned by <c>Population.Reconcile</c> alongside
    /// <see cref="Board.RenumberSlots"/>; the host remains responsible for its own columns via
    /// <see cref="Sv.Columns.Permute{T}(T*, ReadOnlySpan{int}, int)"/>.
    /// </summary>
    public unsafe void Renumber(ReadOnlySpan<int> oldToNew)
    {
        if (oldToNew.Length != Capacity)
        {
            throw new ArgumentException($"sv.compose: renumber map must cover capacity ({Capacity} entries)");
        }

        fixed (ushort* ids = TimelineIds)
        fixed (ushort* clocks = TimelineClocks)
        {
            Columns.Permute(ids, oldToNew, TimelineIds.Length);
            Columns.Permute(clocks, oldToNew, TimelineClocks.Length);
        }

        Columns.Permute(Applied, oldToNew, Capacity);
        foreach (var effect in effects)
        {
            fixed (float* column = effect)
            {
                Columns.Permute(column, oldToNew, effect.Length);
            }
        }
    }

    internal static Game Build(Schema schema, int capacity, int timelineCount, InputState input)
    {
        return new Game(schema, capacity, timelineCount, input);
    }

    internal void AddChannel<T>() where T : unmanaged
    {
        channels[typeof(T)] = Streams.CreateChannel<T>();
    }

    /// <summary>
    /// Saves the whole composed frame state — board, input, event streams, timeline ids and clocks,
    /// the applied column and every tl effect column — into one reusable buffer. Loading it and
    /// re-running the same inputs reproduces the same frames bit-identically (rollback). The host
    /// world's own columns are the host's to save.
    /// </summary>
    public void SaveState(SvState state)
    {
        state.BeginWrite();
        state.Header(GameMagic, gameShape);
        Board.WriteState(state);
        Input.WriteState(state);
        Streams.WriteState(state);
        fixed (ushort* ids = TimelineIds)
        fixed (ushort* clocks = TimelineClocks)
        {
            state.Write(ids, (long)TimelineIds.Length * sizeof(ushort));
            state.Write(clocks, (long)TimelineClocks.Length * sizeof(ushort));
        }

        state.Write(Applied, (long)Capacity * sizeof(int));
        foreach (var effect in effects)
        {
            fixed (float* column = effect)
            {
                state.Write(column, (long)effect.Length * sizeof(float));
            }
        }
    }

    public void LoadState(SvState state)
    {
        state.BeginRead();
        state.Expect(GameMagic, gameShape, "game");
        Board.ReadState(state);
        Input.ReadState(state);
        Streams.ReadState(state);
        fixed (ushort* ids = TimelineIds)
        fixed (ushort* clocks = TimelineClocks)
        {
            state.Read(ids, (long)TimelineIds.Length * sizeof(ushort));
            state.Read(clocks, (long)TimelineClocks.Length * sizeof(ushort));
        }

        state.Read(Applied, (long)Capacity * sizeof(int));
        foreach (var effect in effects)
        {
            fixed (float* column = effect)
            {
                state.Read(column, (long)effect.Length * sizeof(float));
            }
        }

        state.EndRead();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Board.Dispose();
        Streams.Dispose();
        NativeMemory.Free(Applied);
        disposed = true;
    }
}

public sealed class GameBuilder<TEvents, TTimelines, TKernels>
    where TEvents : struct, IEventChain
    where TTimelines : struct, ITimelineChain
    where TKernels : struct, IKernelChain
{
    readonly Schema schema;

    readonly int capacity;

    internal GameBuilder(Schema schema, int capacity)
    {
        this.schema = schema;
        this.capacity = capacity;
    }

    public GameBuilder<EventLink<T, TEvents>, TTimelines, TKernels> Event<T>() where T : unmanaged
    {
        return new GameBuilder<EventLink<T, TEvents>, TTimelines, TKernels>(schema, capacity);
    }

    public GameBuilder<TEvents, TimelineLink<TStep, TTimelines>, TKernels> Timeline<TStep>() where TStep : ITimelineStep
    {
        return new GameBuilder<TEvents, TimelineLink<TStep, TTimelines>, TKernels>(schema, capacity);
    }

    public GameBuilder<TEvents, TTimelines, KernelLink<TStep, TKernels>> Kernel<TStep>() where TStep : IKernelStep
    {
        return new GameBuilder<TEvents, TTimelines, KernelLink<TStep, TKernels>>(schema, capacity);
    }

    public GameHost<TEvents, TTimelines, TKernels, THost> Build<THost>(THost world, InputState input)
        where THost : struct, IFrameWorld
    {
        var game = Game.Build(schema, capacity, TTimelines.Count, input);
        TEvents.Register(game);
        return new GameHost<TEvents, TTimelines, TKernels, THost>(game, world);
    }
}

public sealed class GameHost<TEvents, TTimelines, TKernels, THost> : IDisposable
    where TEvents : struct, IEventChain
    where TTimelines : struct, ITimelineChain
    where TKernels : struct, IKernelChain
    where THost : struct, IFrameWorld
{
    public Game Game { get; }

    THost world;

    public ref THost World => ref world;

    public FrameRunner Frame { get; }

    TapeWriter? tape;

    internal GameHost(Game game, THost world)
    {
        Game = game;
        this.world = world;
        Frame = new FrameRunner(this);
    }

    public void Dispose()
    {
        tape?.Dispose();
        tape = null;
        Game.Dispose();
    }

    /// <summary>
    /// Begins taping: every subsequent <see cref="FrameRunner.Run"/> appends one frame
    /// (inputs, digest, body) to <paramref name="path"/>. Managed-allocation-free per frame;
    /// flush on <see cref="EndRecord"/> or Dispose.
    /// </summary>
    public void Record(string path)
    {
        if (tape != null)
        {
            throw new InvalidOperationException("sv.compose: a tape is already recording");
        }

        tape = new TapeWriter(Game, path);
    }

    public void EndRecord()
    {
        if (tape == null)
        {
            throw new InvalidOperationException("sv.compose: no tape is recording");
        }

        tape.Dispose();
        tape = null;
    }

    /// <summary>
    /// Re-drives this host from a tape: for each recorded frame, re-captures the recorded
    /// inputs, runs the frame, and compares the recomputed digest. Bit-identity is asserted
    /// per frame; under SV_CHECKED the full body is also compared byte-for-byte.
    /// </summary>
    public unsafe int Playback(string path)
    {
        if (tape != null)
        {
            throw new InvalidOperationException("sv.compose: cannot play back while a tape is recording");
        }

        var bytes = File.ReadAllBytes(path);
        var game = Game;
        var axes = new uint[game.Input.Axes];
        var buttons = new ulong[game.Input.ButtonWords];
        var capacity = Math.Max(64, Tape.BodyLength(game));
        var body = (byte*)NativeMemory.Alloc((nuint)capacity);
        fixed (uint* axesPtr = axes)
        fixed (ulong* buttonsPtr = buttons)
        {
            try
            {
                fixed (byte* file = bytes)
                {
                    var cursor = file;
                    var end = file + bytes.Length;
                    if (end - cursor < 8 || cursor[0] != (byte)'S' || cursor[1] != (byte)'V' || cursor[2] != (byte)'T' || cursor[3] != (byte)'P')
                    {
                        throw new InvalidDataException("sv.compose: not a tape file");
                    }

                    cursor += 4;
                    var version = *(uint*)cursor;
                    cursor += 4;
                    if (version != Tape.Version)
                    {
                        throw new InvalidDataException($"sv.compose: tape version {version} unsupported (expected {Tape.Version})");
                    }

                    var frame = 0;
                    while (cursor < end)
                    {
                        if (end - cursor < 16)
                        {
                            throw new InvalidDataException($"sv.compose: tape truncated in frame {frame} header");
                        }

                        var dt = BitConverter.UInt32BitsToSingle(*(uint*)cursor);
                        cursor += sizeof(uint);
                        var axesCount = *(int*)cursor;
                        cursor += sizeof(int);
                        var wordCount = *(int*)cursor;
                        cursor += sizeof(int);
                        if (axesCount < 0 || wordCount < 0 || axesCount != game.Input.Axes || wordCount != game.Input.ButtonWords)
                        {
                            throw new InvalidDataException($"sv.compose: tape frame {frame} input shape ({axesCount} axes, {wordCount} words) does not match this host");
                        }

                        if (end - cursor < (long)axesCount * sizeof(uint) + (long)wordCount * sizeof(ulong))
                        {
                            throw new InvalidDataException($"sv.compose: tape truncated in frame {frame} inputs");
                        }

                        for (var axis = 0; axis < axesCount; axis++)
                        {
                            axes[axis] = *(uint*)cursor;
                            cursor += sizeof(uint);
                        }

                        for (var word = 0; word < wordCount; word++)
                        {
                            buttons[word] = *(ulong*)cursor;
                            cursor += sizeof(ulong);
                        }

                        if (end - cursor < sizeof(ulong) + sizeof(int))
                        {
                            throw new InvalidDataException($"sv.compose: tape truncated in frame {frame} digest");
                        }

                        var recorded = *(ulong*)cursor;
                        cursor += sizeof(ulong);
                        var bodyLength = *(int*)cursor;
                        cursor += sizeof(int);
                        if (bodyLength < 0 || end - cursor < bodyLength)
                        {
                            throw new InvalidDataException($"sv.compose: tape frame {frame} body corrupt (length {bodyLength})");
                        }

                        var recordedBody = cursor;
                        cursor += bodyLength;
                        game.Input.Capture(axesPtr, buttonsPtr);
                        Frame.Run(dt);
                        var needed = Tape.BodyLength(game);
                        if (needed > capacity)
                        {
                            capacity = needed * 2;
                            body = (byte*)NativeMemory.Realloc(body, (nuint)capacity);
                        }

                        var digest = Tape.WriteBody(game, dt, body);
                        if (digest != recorded)
                        {
                            throw new InvalidOperationException($"sv.compose: tape digest mismatch at frame {frame} (recorded {recorded:X16}, replayed {digest:X16})");
                        }

                        VerifyBody(frame, body, recordedBody, bodyLength);
                        frame++;
                    }

                    return frame;
                }
            }
            finally
            {
                NativeMemory.Free(body);
            }
        }
    }

    [System.Diagnostics.Conditional("SV_CHECKED")]
    static unsafe void VerifyBody(int frame, byte* replayed, byte* recorded, int length)
    {
        for (var i = 0; i < length; i++)
        {
            if (replayed[i] != recorded[i])
            {
                throw new InvalidOperationException($"sv.compose: tape body mismatch at frame {frame} byte {i} (recorded {recorded[i]:X2}, replayed {replayed[i]:X2})");
            }
        }
    }

    internal void CaptureFrame(float dt)
    {
        tape?.CaptureFrame(dt);
    }

    public sealed class FrameRunner
    {
        readonly GameHost<TEvents, TTimelines, TKernels, THost> owner;

        internal FrameRunner(GameHost<TEvents, TTimelines, TKernels, THost> owner)
        {
            this.owner = owner;
        }

        public unsafe void Run(float dt)
        {
            var game = owner.Game;
            ref var world = ref owner.world;
            while (game.Streams.TryReadNext(out var typeId, out var payload, out _))
            {
                world.DispatchEvent(game, typeId, payload);
            }

            world.Reconcile(game);
            world.RunIntrinsics(game);
            game.Board.Recalculate();
            TTimelines.Run(game, 0);
            TKernels.Run(ref world, game, dt);
            world.RunConditions(game);
            game.Streams.Swap();
            owner.CaptureFrame(dt);
        }

        public unsafe void Tick(float dt, uint* axes, ulong* buttons)
        {
            owner.Game.Input.Capture(axes, buttons);
            Run(dt);
        }
    }
}
