using System;

namespace Sv
{
    public enum ModifierKind : byte
    {
        Added = 0,
        Additive = 1,
        Multi = 2,
        Cap = 3,
    }

    public readonly struct Modifier
    {
        public ModifierKind Kind { get; }

        public float Value { get; }

        public Modifier(ModifierKind kind, float value)
        {
            if (kind != ModifierKind.Added && kind != ModifierKind.Additive && kind != ModifierKind.Multi && kind != ModifierKind.Cap)
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

    /// <summary>
    /// A live modifier's identity: the full 64-bit insertion id — generation in the high bits,
    /// monotone counter in the low 40. Unique for the board's lifetime across state loads and
    /// keyframe folds; sort order is insertion order (clause 3).
    /// </summary>
    public readonly struct ModifierHandle
    {
        public long Id { get; }

        public ModifierHandle(long id)
        {
            Id = id;
        }
    }
}
