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

public readonly struct StatId : IEquatable<StatId>
{
    public int Index { get; }

    internal StatId(int index)
    {
        Index = index;
    }

    public bool Equals(StatId other) => Index == other.Index;

    public override bool Equals(object? obj) => obj is StatId other && Equals(other);

    public override int GetHashCode() => Index;

    public static bool operator ==(StatId left, StatId right) => left.Equals(right);

    public static bool operator !=(StatId left, StatId right) => !left.Equals(right);

    public override string ToString() => $"Stat({Index})";
}

public sealed record StatDef(string Name, float Base);

public sealed record IntrinsicDef(string Name, int? Min, int? Max);

public sealed class Schema
{
    public IReadOnlyList<StatDef> Stats { get; }

    public IReadOnlyList<IntrinsicDef> Intrinsics { get; }

    private readonly Dictionary<string, StatId> statIds;

    private readonly Dictionary<string, IntrinsicId> intrinsicIds;

    internal Schema(StatDef[] stats, IntrinsicDef[] intrinsics)
    {
        Stats = stats;
        Intrinsics = intrinsics;
        statIds = new Dictionary<string, StatId>(stats.Length, StringComparer.Ordinal);
        for (var i = 0; i < stats.Length; i++)
        {
            statIds.Add(stats[i].Name, new StatId(i));
        }

        intrinsicIds = new Dictionary<string, IntrinsicId>(intrinsics.Length, StringComparer.Ordinal);
        for (var i = 0; i < intrinsics.Length; i++)
        {
            intrinsicIds.Add(intrinsics[i].Name, new IntrinsicId(i));
        }
    }

    public StatId Stat(string name)
    {
        if (!statIds.TryGetValue(name, out var id))
        {
            throw new KeyNotFoundException($"sv: unknown stat '{name}'");
        }

        return id;
    }

    public bool TryStat(string name, out StatId id) => statIds.TryGetValue(name, out id);

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
    private readonly List<StatDef> stats = new();

    private readonly List<IntrinsicDef> intrinsics = new();

    public static SchemaBuilder Create() => new();

    public SchemaBuilder Stat(string name, float @base)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("sv: stat name must be non-empty", nameof(name));
        }

        if (!float.IsFinite(@base))
        {
            throw new ArgumentException($"sv: stat '{name}' base must be finite");
        }

        stats.Add(new StatDef(name, @base));
        return this;
    }

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
        if (stats.Count == 0 && intrinsics.Count == 0)
        {
            throw new InvalidOperationException("sv: schema declares no stats and no intrinsics");
        }

        var statNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var def in stats)
        {
            if (!statNames.Add(def.Name))
            {
                throw new InvalidOperationException($"sv: duplicate stat '{def.Name}'");
            }
        }

        var intrinsicNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var def in intrinsics)
        {
            if (!intrinsicNames.Add(def.Name))
            {
                throw new InvalidOperationException($"sv: duplicate intrinsic '{def.Name}'");
            }
        }

        return new Schema(stats.ToArray(), intrinsics.ToArray());
    }
}
