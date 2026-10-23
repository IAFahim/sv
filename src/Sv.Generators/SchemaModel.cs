using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Sv.Generators;

public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>> where T : IEquatable<T>
{
    private readonly ImmutableArray<T> items;

    public EquatableArray(ImmutableArray<T> items)
    {
        this.items = items;
    }

    public ImmutableArray<T> Items => items;

    public bool Equals(EquatableArray<T> other) => items.SequenceEqual(other.items);

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 0;
        foreach (var item in items)
        {
            hash = unchecked(hash * 31 + item.GetHashCode());
        }

        return hash;
    }
}

public sealed record LocationInfo(string FilePath, int Start, int Length, int StartLine, int StartColumn, int EndLine, int EndColumn)
{
    public Location ToLocation()
    {
        var span = new TextSpan(Start, Length);
        var lines = new LinePositionSpan(new LinePosition(StartLine, StartColumn), new LinePosition(EndLine, EndColumn));
        return Location.Create(FilePath, span, lines);
    }

    public static LocationInfo? From(Location? location)
    {
        if (location is null)
        {
            return null;
        }

        var span = location.SourceSpan;
        var lines = location.GetLineSpan();
        return new LocationInfo(
            location.SourceTree?.FilePath ?? string.Empty,
            span.Start,
            span.Length,
            lines.StartLinePosition.Line,
            lines.StartLinePosition.Character,
            lines.EndLinePosition.Line,
            lines.EndLinePosition.Character);
    }
}

public sealed record ErrorInfo(string Id, string Title, string Message, LocationInfo? Location);

public sealed record StatEntry(byte Kind, string Name, float Base, EquatableArray<(string Name, float Coefficient)> Sources);

public sealed record SchemaModel(
    string Namespace,
    string Name,
    string Accessibility,
    EquatableArray<StatEntry> Entries,
    EquatableArray<ErrorInfo> Errors,
    bool Emit);
