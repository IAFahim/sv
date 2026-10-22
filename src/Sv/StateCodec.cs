using System;
using System.Runtime.CompilerServices;

namespace Sv;

internal unsafe interface IStateCodec
{
    static abstract bool Reading { get; }

    void Header(uint magic, long shape, string what);

    void Value<T>(ref T value) where T : unmanaged;

    void Bytes(void* pointer, long bytes);
}

internal unsafe struct SaveCodec : IStateCodec
{
    private readonly SvState state;

    public SaveCodec(SvState state)
    {
        this.state = state;
    }

    public static bool Reading => false;

    public void Header(uint magic, long shape, string what)
    {
        state.Header(magic, shape);
    }

    public void Value<T>(ref T value) where T : unmanaged
    {
        state.Write(Unsafe.AsPointer(ref value), sizeof(T));
    }

    public void Bytes(void* pointer, long bytes)
    {
        state.Write(pointer, bytes);
    }
}

internal unsafe struct LoadCodec : IStateCodec
{
    private readonly SvState state;

    public LoadCodec(SvState state)
    {
        this.state = state;
    }

    public static bool Reading => true;

    public void Header(uint magic, long shape, string what)
    {
        if (state.Read<uint>() != magic || state.Read<long>() != shape)
        {
            throw new System.IO.InvalidDataException($"sv: state does not hold a {what} of this shape");
        }
    }

    public void Value<T>(ref T value) where T : unmanaged
    {
        state.Read(Unsafe.AsPointer(ref value), sizeof(T));
    }

    public void Bytes(void* pointer, long bytes)
    {
        state.Read(pointer, bytes);
    }
}

internal unsafe struct SizeCodec : IStateCodec
{
    public long Total;

    public static bool Reading => false;

    public void Header(uint magic, long shape, string what)
    {
        Total += sizeof(uint) + sizeof(long);
    }

    public void Value<T>(ref T value) where T : unmanaged
    {
        Total += sizeof(T);
    }

    public void Bytes(void* pointer, long bytes)
    {
        Total += bytes;
    }
}

internal unsafe struct HashCodec : IStateCodec
{
    private ulong hash;

    public HashCodec(ulong seed)
    {
        hash = seed;
    }

    public static bool Reading => false;

    public readonly ulong Digest => hash;

    public void Header(uint magic, long shape, string what)
    {
        Mix((byte*)&magic, sizeof(uint));
        Mix((byte*)&shape, sizeof(long));
    }

    public void Value<T>(ref T value) where T : unmanaged
    {
        Mix((byte*)Unsafe.AsPointer(ref value), sizeof(T));
    }

    public void Bytes(void* pointer, long bytes)
    {
        Mix((byte*)pointer, bytes);
    }

    void Mix(byte* bytes, long count)
    {
        unchecked
        {
            for (var i = 0; i < count; i++)
            {
                hash ^= bytes[i];
                hash *= 1099511628211ul;
            }
        }
    }
}
