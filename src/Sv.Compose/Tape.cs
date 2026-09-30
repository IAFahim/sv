using System;
using System.IO;
using System.Runtime.InteropServices;
using Sv;

namespace Sv.Compose;

/// <summary>
/// Determinism tape. Each recorded frame is written as
/// dt bits, input counts, the captured axes and button words, the frame digest, and the raw
/// digest body. The digest is FNV-1a over the dt bits followed by the body: axes bits, button
/// words, every sv stat column in schema order (full capacity, dead slots included), then the
/// live bitmap words. Recording and per-frame digesting are managed-allocation-free; only the
/// file stream and its fixed buffers are allocated, at <c>Record</c> time.
/// </summary>
public static unsafe class Tape
{
    public const uint Version = 1;

    internal static int BodyLength(Game game)
    {
        var liveWords = (game.Board.Capacity + 63) >> 6;
        return sizeof(uint)
            + game.Input.Axes * sizeof(uint)
            + game.Input.ButtonWords * sizeof(ulong)
            + game.Board.Schema.Stats.Count * game.Board.Capacity * sizeof(float)
            + liveWords * sizeof(ulong);
    }

    internal static uint WriteBody(Game game, float dt, byte* body)
    {
        var cursor = body;
        var hash = 2166136261u;
        var dtBits = BitConverter.SingleToUInt32Bits(dt);
        *(uint*)cursor = dtBits;
        hash = Mix(hash, cursor, sizeof(uint));
        cursor += sizeof(uint);

        for (var axis = 0; axis < game.Input.Axes; axis++)
        {
            *(uint*)cursor = game.Input.AxisBits(axis);
            hash = Mix(hash, cursor, sizeof(uint));
            cursor += sizeof(uint);
        }

        for (var word = 0; word < game.Input.ButtonWords; word++)
        {
            *(ulong*)cursor = game.Input.ButtonBits(word);
            hash = Mix(hash, cursor, sizeof(ulong));
            cursor += sizeof(ulong);
        }

        var schema = game.Board.Schema;
        for (var stat = 0; stat < schema.Stats.Count; stat++)
        {
            var column = game.Board.OutputPtr(schema.Stat(schema.Stats[stat].Name));
            var bytes = game.Board.Capacity * sizeof(float);
            Buffer.MemoryCopy(column, cursor, bytes, bytes);
            hash = Mix(hash, cursor, bytes);
            cursor += bytes;
        }

        for (var word = 0; word < game.Board.LiveWordCount; word++)
        {
            *(ulong*)cursor = game.Board.LiveWord(word);
            hash = Mix(hash, cursor, sizeof(ulong));
            cursor += sizeof(ulong);
        }

        return hash;
    }

    static uint Mix(uint hash, byte* bytes, int count)
    {
        for (var i = 0; i < count; i++)
        {
            hash = (hash ^ bytes[i]) * 16777619u;
        }

        return hash;
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
        stream.Write(new ReadOnlySpan<byte>(&digest, sizeof(uint)));
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
