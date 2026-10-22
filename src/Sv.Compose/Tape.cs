using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Sv.Compose;

/// <summary>
/// Determinism tape (format v4). Each recorded frame is written as dt bits, input counts, the
/// captured axes and button words, and the 64-bit frame digest — the Zobrist digest the board
/// maintains in O(changes) plus the frame's inputs, hashed together. No body is stored:
/// <see cref="WriteBody"/> materializes a body on demand to locate a divergence. Recording is
/// managed-allocation-free.
/// </summary>
public static unsafe class Tape
{
    public const uint Version = 4;

    internal static int BodyLength(Game game)
    {
        var liveWords = (game.Board.Capacity + 63) >> 6;
        return sizeof(uint)
            + game.Input.Axes * sizeof(uint)
            + game.Input.ButtonWords * sizeof(ulong)
            + game.Board.Schema.Stats.Count * game.Board.LiveCount * sizeof(float)
            + liveWords * sizeof(ulong);
    }

    internal static ulong WriteBody(Game game, float dt, byte* body)
    {
        var cursor = body;
        *(uint*)cursor = BitConverter.SingleToUInt32Bits(dt);
        cursor += sizeof(uint);
        for (var axis = 0; axis < game.Input.Axes; axis++)
        {
            *(uint*)cursor = game.Input.AxisBits(axis);
            cursor += sizeof(uint);
        }

        for (var word = 0; word < game.Input.ButtonWords; word++)
        {
            *(ulong*)cursor = game.Input.ButtonBits(word);
            cursor += sizeof(ulong);
        }

        var board = game.Board;
        var stats = board.Schema.Stats.Count;
        var words = board.LiveWordCount;
        for (var stat = 0; stat < stats; stat++)
        {
            var id = board.Schema.Stat(board.Schema.Stats[stat].Name);
            var external = board.OutputColumn(id);
            var column = external != null ? external : board.OutputPtr(id);
            var stride = external == null ? 1 : board.OutputStrideAt(id.Index);
            for (var word = 0; word < words; word++)
            {
                var bits = board.LiveWord(word);
                if (bits == ulong.MaxValue && stride == 1)
                {
                    Buffer.MemoryCopy(column + (word << 6), cursor, 64 * sizeof(float), 64 * sizeof(float));
                    cursor += 64 * sizeof(float);
                    continue;
                }

                while (bits != 0)
                {
                    var slot = (word << 6) + BitOperations.TrailingZeroCount(bits);
                    bits &= bits - 1;
                    *(float*)cursor = column[(nint)slot * stride];
                    cursor += sizeof(float);
                }
            }
        }

        for (var word = 0; word < words; word++)
        {
            *(ulong*)cursor = board.LiveWord(word);
            cursor += sizeof(ulong);
        }

        return XxHash64.Hash(body, (int)(cursor - body));
    }

    internal static ulong FrameDigest(Game game, float dt)
    {
        var length = sizeof(uint) + game.Input.Axes * sizeof(uint) + game.Input.ButtonWords * sizeof(ulong) + sizeof(ulong);
        var buffer = stackalloc byte[32];
        var cursor = buffer;
        *(uint*)cursor = BitConverter.SingleToUInt32Bits(dt);
        cursor += sizeof(uint);
        for (var axis = 0; axis < game.Input.Axes; axis++)
        {
            *(uint*)cursor = game.Input.AxisBits(axis);
            cursor += sizeof(uint);
        }

        for (var word = 0; word < game.Input.ButtonWords; word++)
        {
            *(ulong*)cursor = game.Input.ButtonBits(word);
            cursor += sizeof(ulong);
        }

        *(ulong*)cursor = game.Board.Digest;
        return XxHash64.Hash(buffer, length);
    }
}

internal sealed unsafe class TapeWriter : IDisposable
{
    readonly FileStream stream;

    readonly Game game;

    bool disposed;

    public TapeWriter(Game game, string path)
    {
        this.game = game;
        stream = File.Create(path);
        Span<byte> header = stackalloc byte[8];
        header[0] = (byte)'S';
        header[1] = (byte)'V';
        header[2] = (byte)'T';
        header[3] = (byte)'P';
        BitConverter.TryWriteBytes(header.Slice(4), Tape.Version);
        stream.Write(header);
    }

    public void CaptureFrame(float dt)
    {
        var inputBytes = game.Input.Axes * sizeof(uint) + game.Input.ButtonWords * sizeof(ulong);
        var digest = Tape.FrameDigest(game, dt);
        var dtBits = BitConverter.SingleToUInt32Bits(dt);
        var axesCount = game.Input.Axes;
        var wordCount = game.Input.ButtonWords;
        stream.Write(new ReadOnlySpan<byte>(&dtBits, sizeof(uint)));
        stream.Write(new ReadOnlySpan<byte>(&axesCount, sizeof(int)));
        stream.Write(new ReadOnlySpan<byte>(&wordCount, sizeof(int)));
        var axes = stackalloc uint[game.Input.Axes];
        for (var axis = 0; axis < game.Input.Axes; axis++)
        {
            axes[axis] = game.Input.AxisBits(axis);
        }

        var buttons = stackalloc ulong[game.Input.ButtonWords];
        for (var word = 0; word < game.Input.ButtonWords; word++)
        {
            buttons[word] = game.Input.ButtonBits(word);
        }

        stream.Write(new ReadOnlySpan<byte>(axes, inputBytes - game.Input.ButtonWords * sizeof(ulong)));
        stream.Write(new ReadOnlySpan<byte>(buttons, game.Input.ButtonWords * sizeof(ulong)));
        stream.Write(new ReadOnlySpan<byte>(&digest, sizeof(ulong)));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        stream.Dispose();
        disposed = true;
    }
}
