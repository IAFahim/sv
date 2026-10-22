using System;

namespace Sv;

public enum ModifierKind : byte
{
    Added = 0,
    Additive = 1,
    Multi = 2,
}

public readonly struct Modifier
{
    public ModifierKind Kind { get; }

    public float Value { get; }

    public Modifier(ModifierKind kind, float value)
    {
        if (kind != ModifierKind.Added && kind != ModifierKind.Additive && kind != ModifierKind.Multi)
        {
            throw new ArgumentException($"sv: {kind} is not a modifier kind");
        }

        if (!float.IsFinite(value))
        {
            throw new ArgumentException("sv: modifier value must be finite");
        }

        Kind = kind;
        Value = value;
    }
}

public readonly struct ModifierHandle
{
    public int Id { get; }

    public ModifierHandle(int id)
    {
        Id = id;
    }
}
