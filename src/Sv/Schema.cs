using System;
using System.Collections.Generic;

namespace Sv;

public readonly struct IntrinsicId : IEquatable<IntrinsicId>
{
    public int Index { get; }

    internal IntrinsicId(int index)
    {
        Index = index;
    }

    public bool Equals(IntrinsicId other) => Index == other.Index;

    public override bool Equals(object? obj) => obj is IntrinsicId other && Equals(other);

    public override int GetHashCode() => Index;

    public static bool operator ==(IntrinsicId left, IntrinsicId right) => left.Equals(right);

    public static bool operator !=(IntrinsicId left, IntrinsicId right) => !left.Equals(right);

    public override string ToString() => $"Intrinsic({Index})";
}

public sealed record IntrinsicDef(string Name, int? Min, int? Max);

public sealed class Schema
{
    public IReadOnlyList<IntrinsicDef> Intrinsics { get; }

    private readonly Dictionary<string, IntrinsicId> intrinsicIds;

    internal Schema(IntrinsicDef[] intrinsics)
    {
        Intrinsics = intrinsics;
        intrinsicIds = new Dictionary<string, IntrinsicId>(intrinsics.Length, StringComparer.Ordinal);
        for (var i = 0; i < intrinsics.Length; i++)
        {
            intrinsicIds.Add(intrinsics[i].Name, new IntrinsicId(i));
        }
    }

    public IntrinsicId Intrinsic(string name)
    {
        if (!intrinsicIds.TryGetValue(name, out var id))
        {
            throw new KeyNotFoundException($"sv: unknown intrinsic '{name}'");
        }

        return id;
    }

    public bool TryIntrinsic(string name, out IntrinsicId id) => intrinsicIds.TryGetValue(name, out id);
}

public sealed class SchemaBuilder
{
    private readonly List<IntrinsicDef> intrinsics = new();

    public static SchemaBuilder Create() => new();

    public SchemaBuilder Intrinsic(string name, int? min = null, int? max = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("sv: intrinsic name must be non-empty", nameof(name));
        }

        if (min.HasValue && max.HasValue && min.Value > max.Value)
        {
            throw new ArgumentException($"sv: intrinsic '{name}' has min {min.Value} > max {max.Value}");
        }

        intrinsics.Add(new IntrinsicDef(name, min, max));
        return this;
    }

    public Schema Build()
    {
        if (intrinsics.Count == 0)
        {
            throw new InvalidOperationException("sv: schema declares no intrinsics");
        }

        var distinct = new HashSet<string>(StringComparer.Ordinal);
        foreach (var def in intrinsics)
        {
            if (!distinct.Add(def.Name))
            {
                throw new InvalidOperationException($"sv: duplicate intrinsic '{def.Name}'");
            }
        }

        return new Schema(intrinsics.ToArray());
    }
}
