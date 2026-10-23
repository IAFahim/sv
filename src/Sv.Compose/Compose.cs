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
    /// Opens an undo window of <paramref name="frames"/> completed frames across the whole
    /// composed state — board, input ring and event streams. Each <see cref="Compose.GameHost{TEvents,TTimelines,TKernels,THost}.Frame"/>
    /// run marks one frame boundary; <see cref="Rollback"/> restores any boundary still inside
    /// the window bit-exactly. Timeline columns and host columns are not covered — they are the
    /// host's to snapshot, exactly as with <see cref="SaveState(SvState)"/>.
    /// </summary>
    public void BeginUndoWindow(int frames)
    {
        Board.BeginUndoWindow(frames + 1);
        Input.BeginUndoWindow(frames + 1);
        Streams.BeginUndoWindow(frames + 1);
    }

    /// <summary>
    /// Closes the undo window; the current composed state becomes permanent.
    /// </summary>
    public void EndUndoWindow()
    {
        Board.EndUndoWindow();
        Input.EndUndoWindow();
        Streams.EndUndoWindow();
    }

    /// <summary>
    /// Completed frames currently retained by the open undo window (0 when no window is open).
    /// </summary>
    public int UndoFrames => Math.Max(0, Board.UndoFrames - 1);

    /// <summary>
    /// Rolls the composed state back <paramref name="frames"/> completed frames, bit-exactly:
    /// board writes replay backwards, the input ring and edges restore, and event stream
    /// cursors, halves and arena bytes return to their mark state, so re-driving the same
    /// inputs reproduces the same frames. Equivalent to loading a snapshot saved at that
    /// boundary, without the copy.
    /// </summary>
    public void Rollback(int frames)
    {
        if (Board.UndoFrames == 0)
        {
            throw new InvalidOperationException("sv.compose: no undo window is open");
        }

        if (frames < 1 || frames > UndoFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(frames), $"sv.compose: rollback depth must be in [1, {UndoFrames}]");
        }

        var target = Board.UndoTop - frames;
        Board.RollbackTo(target);
        Input.RollbackTo(target);
        Streams.RollbackTo(target);
        Board.BeginUndoFrame();
        Input.BeginUndoFrame();
        Streams.BeginUndoFrame();
    }

    internal void MarkUndoFrame()
    {
        if (Board.UndoFrames == 0)
        {
            return;
        }

        Board.BeginUndoFrame();
        Input.BeginUndoFrame();
        Streams.BeginUndoFrame();
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
        EndUndoWindow();
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
        var codec = new SaveCodec(state);
        VisitState(ref codec);
    }

    public void LoadState(SvState state)
    {
        state.BeginRead();
        var codec = new LoadCodec(state);
        VisitState(ref codec);
        state.EndRead();
    }

    internal void VisitState<TCodec>(ref TCodec codec) where TCodec : struct, IStateCodec
    {
        codec.Header(GameMagic, gameShape, "game");
        Board.VisitState(ref codec);
        Input.VisitState(ref codec);
        Streams.VisitState(ref codec);
        fixed (ushort* ids = TimelineIds)
        fixed (ushort* clocks = TimelineClocks)
        {
            codec.Bytes(ids, (long)TimelineIds.Length * sizeof(ushort));
            codec.Bytes(clocks, (long)TimelineClocks.Length * sizeof(ushort));
        }

        codec.Bytes(Applied, (long)Capacity * sizeof(int));
        foreach (var effect in effects)
        {
            fixed (float* column = effect)
            {
                codec.Bytes(column, (long)effect.Length * sizeof(float));
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        EndUndoWindow();
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

    /// <summary>
    /// Completed frames retained by the game's open undo window (0 when no window is open).
    /// </summary>
    public int UndoFrames => Game.UndoFrames;

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
    /// Opens an undo window of <paramref name="frames"/> completed frames over the whole
    /// composed state — board, input ring and event streams. Each subsequent frame run marks
    /// one boundary; <see cref="Rollback"/> restores any boundary still inside the window.
    /// </summary>
    public void BeginUndoWindow(int frames)
    {
        Game.BeginUndoWindow(frames);
    }

    /// <summary>
    /// Closes the undo window; the current composed state becomes permanent.
    /// </summary>
    public void EndUndoWindow()
    {
        Game.EndUndoWindow();
    }

    /// <summary>
    /// Rolls the composed game back <paramref name="frames"/> completed frames, bit-exactly:
    /// board writes replay backwards, the input ring and edges restore, and event stream
    /// cursors, halves and arena bytes return to their mark state. Re-driving the same inputs
    /// (for example with <see cref="InputState.BeginReplay"/> or the captured axes and buttons)
    /// reproduces the same frames. Timeline columns and host columns are the host's to save.
    /// </summary>
    public void Rollback(int frames)
    {
        Game.Rollback(frames);
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
        fixed (uint* axesPtr = axes)
        fixed (ulong* buttonsPtr = buttons)
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
                    if (end - cursor < 12)
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

                    if (end - cursor < (long)axesCount * sizeof(uint) + (long)wordCount * sizeof(ulong) + sizeof(ulong))
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

                    var recorded = *(ulong*)cursor;
                    cursor += sizeof(ulong);
                    game.Input.Capture(axesPtr, buttonsPtr);
                    Frame.Run(dt);
                    var digest = Tape.FrameDigest(game, dt);
                    if (digest != recorded)
                    {
                        var hint = game.Board.RecomputeDigest() != digest ? " (board incremental digest drifted from a from-scratch recompute)" : "";
                        throw new InvalidOperationException($"sv.compose: tape digest mismatch at frame {frame} (recorded {recorded:X16}, replayed {digest:X16}){hint}");
                    }

                    frame++;
                }

                return frame;
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
            game.MarkUndoFrame();
            owner.CaptureFrame(dt);
        }

        public unsafe void Tick(float dt, uint* axes, ulong* buttons)
        {
            owner.Game.Input.Capture(axes, buttons);
            Run(dt);
        }
    }
}
