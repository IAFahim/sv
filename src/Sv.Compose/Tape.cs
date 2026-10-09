using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Sv.Compose;

/// <summary>
/// Determinism tape (format v3). Each recorded frame is written as
/// dt bits, input counts, the captured axes and button words, the 64-bit frame digest, and the
/// raw digest body. The body is the dt bits, axes bits, button words, per stat the live rows
/// only (ascending slot order, walked via the live bitmap; fully live words copy whole), then
/// the live bitmap words — a sparse board pays for entities, not capacity; dead rows are
/// outside the determinism contract. The digest is XXH64 (seed 0) over the body. Recording and per-frame digesting are
/// managed-allocation-free; only the file stream and its fixed buffers are allocated, at
/// <c>Record</c> time.
/// </summary>
public static unsafe class Tape
{
    public const uint Version = 3;

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
            var column = board.OutputPtr(board.Schema.Stat(board.Schema.Stats[stat].Name));
            for (var word = 0; word < words; word++)
            {
                var bits = board.LiveWord(word);
                if (bits == ulong.MaxValue)
                {
                    Buffer.MemoryCopy(column + (word << 6), cursor, 64 * sizeof(float), 64 * sizeof(float));
                    cursor += 64 * sizeof(float);
                    continue;
                }

                while (bits != 0)
                {
                    var slot = (word << 6) + BitOperations.TrailingZeroCount(bits);
                    bits &= bits - 1;
                    *(float*)cursor = column[slot];
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
}

internal sealed unsafe class TapeWriter : IDisposable
{
    readonly FileStream stream;

    readonly Game game;

    byte* body;

    int bodyCapacity;

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
        var length = Tape.BodyLength(game);
        if (length > bodyCapacity)
        {
            bodyCapacity = Math.Max(length, bodyCapacity * 2);
            body = body == null
                ? (byte*)NativeMemory.Alloc((nuint)bodyCapacity, 16)
                : (byte*)NativeMemory.Realloc(body, (nuint)bodyCapacity);
        }

        var digest = Tape.WriteBody(game, dt, body);
        var inputBytes = game.Input.Axes * sizeof(uint) + game.Input.ButtonWords * sizeof(ulong);
        var dtBits = BitConverter.SingleToUInt32Bits(dt);
        var axesCount = game.Input.Axes;
        var wordCount = game.Input.ButtonWords;
        stream.Write(new ReadOnlySpan<byte>(&dtBits, sizeof(uint)));
        stream.Write(new ReadOnlySpan<byte>(&axesCount, sizeof(int)));
        stream.Write(new ReadOnlySpan<byte>(&wordCount, sizeof(int)));
        stream.Write(new ReadOnlySpan<byte>(body + sizeof(uint), inputBytes));
        stream.Write(new ReadOnlySpan<byte>(&digest, sizeof(ulong)));
        stream.Write(new ReadOnlySpan<byte>(&length, sizeof(int)));
        stream.Write(new ReadOnlySpan<byte>(body, length));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        stream.Dispose();
        if (body != null)
        {
            NativeMemory.Free(body);
        }

        disposed = true;
    }
}
