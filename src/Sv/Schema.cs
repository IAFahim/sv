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

public readonly record struct StatSource(StatId Stat, float Coefficient);

public sealed record StatDef(string Name, float Base)
{
    public IReadOnlyList<StatSource> Sources { get; init; } = Array.Empty<StatSource>();
}

public sealed record IntrinsicDef(string Name, int? Min, int? Max);

public sealed unsafe class Schema
{
    public IReadOnlyList<StatDef> Stats { get; }

    public IReadOnlyList<IntrinsicDef> Intrinsics { get; }

    private readonly Dictionary<string, StatId> statIds;

    private readonly Dictionary<string, IntrinsicId> intrinsicIds;

    public ulong ContentHash { get; }

    internal Schema(StatDef[] stats, IntrinsicDef[] intrinsics)
    {
        Stats = stats;
        Intrinsics = intrinsics;
        ContentHash = HashContent(stats, intrinsics);
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

    static ulong HashContent(StatDef[] stats, IntrinsicDef[] intrinsics)
    {
        var bytes = new List<byte>(64 * (stats.Length + intrinsics.Length));
        void AddU32(uint value)
        {
            bytes.Add((byte)value);
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)(value >> 16));
            bytes.Add((byte)(value >> 24));
        }

        void AddI32(int value) => AddU32(unchecked((uint)value));

        void AddName(string name)
        {
            var utf8 = System.Text.Encoding.UTF8.GetBytes(name);
            AddI32(utf8.Length);
            bytes.AddRange(utf8);
        }

        foreach (var def in stats)
        {
            bytes.Add(1);
            AddName(def.Name);
            AddU32(BitConverter.SingleToUInt32Bits(def.Base));
            AddI32(def.Sources.Count);
            foreach (var source in def.Sources)
            {
                AddI32(source.Stat.Index);
                AddU32(BitConverter.SingleToUInt32Bits(source.Coefficient));
            }
        }

        foreach (var def in intrinsics)
        {
            bytes.Add(2);
            AddName(def.Name);
            bytes.Add(def.Min.HasValue ? (byte)1 : (byte)0);
            if (def.Min.HasValue)
            {
                AddI32(def.Min.Value);
            }

            bytes.Add(def.Max.HasValue ? (byte)1 : (byte)0);
            if (def.Max.HasValue)
            {
                AddI32(def.Max.Value);
            }
        }

        var flat = bytes.ToArray();
        fixed (byte* pin = flat)
        {
            return XxHash64.Hash(pin, flat.Length);
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

    /// <summary>
    /// Declares a stat derived from earlier-declared stats: each source contributes one synthetic
    /// Added entry, coefficient * source value (one IEEE multiply), folded before every real
    /// modifier in declaration order. Sources must already be declared, so the graph is acyclic.
    /// </summary>
    public SchemaBuilder Derived(string name, float @base, params (string Source, float Coefficient)[] sources)
    {
        Stat(name, @base);
        if (sources == null || sources.Length == 0)
        {
            stats.RemoveAt(stats.Count - 1);
            throw new ArgumentException($"sv: derived stat '{name}' needs at least one source");
        }

        var resolved = new StatSource[sources.Length];
        for (var i = 0; i < sources.Length; i++)
        {
            var index = stats.FindIndex(0, stats.Count - 1, def => def.Name == sources[i].Source);
            if (index < 0)
            {
                stats.RemoveAt(stats.Count - 1);
                throw new ArgumentException($"sv: derived stat '{name}' source '{sources[i].Source}' must be declared before it");
            }

            if (!float.IsFinite(sources[i].Coefficient))
            {
                stats.RemoveAt(stats.Count - 1);
                throw new ArgumentException($"sv: derived stat '{name}' coefficient for '{sources[i].Source}' must be finite");
            }

            resolved[i] = new StatSource(new StatId(index), sources[i].Coefficient);
        }

        stats[stats.Count - 1] = stats[stats.Count - 1] with { Sources = resolved };
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
