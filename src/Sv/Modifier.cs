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
