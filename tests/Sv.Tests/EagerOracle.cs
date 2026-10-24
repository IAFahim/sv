using System;
using System.Collections.Generic;

namespace Sv.Tests;

// The frozen eager oracle (roadmap issues/0058, atom 0.3) — an independent, managed
// model of today's frame semantics, written from the contract and the receipts, not
// by calling production: the clause-3 canonical fold with derived synthetic entries
// and set-change propagation, and the atom-50 saturating intrinsics step with the
// owner-decided total clamp (decision 0044: crossed bounds are ceiling-wins,
// bus relay 0052) and the saturating applied difference. Phase 4's drift monoid
// must stay bit-equal to FrameStep (law L7); every phase-2 memory atom must keep
// the program tests green (law L1). This file is the reference — it changes only
// alongside a bus-backed contract bump (e.g. this atom-90 clause-10/18 update),
// never to chase production.

public static class EagerOracle
{
    public const int SyntheticIdBase = int.MinValue;

    public static int BoundOf(float value)
    {
        if (float.IsNaN(value))
        {
            return 0;
        }

        if (value >= 2147483648.0f)
        {
            return int.MaxValue;
        }

        if (value < -2147483648.0f)
        {
            return int.MinValue;
        }

        return unchecked((int)value);
    }

    public static int ClampTotal(long value, int lo, int hi)
    {
        if (lo > hi)
        {
            lo = hi;
        }

        if (value < lo)
        {
            return lo;
        }

        if (value > hi)
        {
            return hi;
        }

        return (int)value;
    }

    public static int SaturatingSubtract(int after, int before)
    {
        var diff = after - before;
        return ((after ^ before) & (after ^ diff)) < 0 ? (after < 0 ? int.MinValue : int.MaxValue) : diff;
    }

    /// <summary>
    /// One eager intrinsics pass over [offset, offset + count): enabled rows take
    /// clamp(floor-first)(saturate(before + delta)), disabled rows are untouched with
    /// applied 0. A null min/max column skips that bound, mirroring Clamp.Columns.
    /// </summary>
    public static void FrameStep(int[] value, int[]? deltas, int uniformDelta, int[]? minColumn, int[]? maxColumn, ulong[]? words, int offset, int count, int[]? applied)
    {
        for (var i = 0; i < count; i++)
        {
            var slot = offset + i;
            var enabled = words == null || (words[(slot - offset) >> 6] & (1ul << (slot & 63))) != 0;
            if (!enabled)
            {
                if (applied != null)
                {
                    applied[slot] = 0;
                }

                continue;
            }

            var before = value[slot];
            var sum = (long)before + (deltas != null ? deltas[slot] : uniformDelta);
            var after = ClampTotal(sum, minColumn?[slot] ?? int.MinValue, maxColumn?[slot] ?? int.MaxValue);
            value[slot] = after;
            if (applied != null)
            {
                applied[slot] = SaturatingSubtract(after, before);
            }
        }
    }

    public static float Fold(float @base, IReadOnlyList<(byte Kind, float Value, long Id)> canonical)
    {
        var value = @base;
        for (var i = 0; i < canonical.Count; i++)
        {
            if (canonical[i].Kind == 0)
            {
                value = value + canonical[i].Value;
            }
        }

        var additive = 1f;
        for (var i = 0; i < canonical.Count; i++)
        {
            if (canonical[i].Kind == 1)
            {
                additive = additive + canonical[i].Value;
            }
        }

        value = value * additive;
        for (var i = 0; i < canonical.Count; i++)
        {
            if (canonical[i].Kind == 2)
            {
                value = value * (1f + canonical[i].Value);
            }
        }

        return value;
    }

    public static float CanonicalNaN(float value)
    {
        return float.IsNaN(value) ? BitConverter.UInt32BitsToSingle(0x7FC00000u) : value;
    }
}

/// <summary>
/// Mirrors the board's stat semantics op for op: insertion-id modifiers, derived
/// synthetic entries (int.MinValue + k, ahead of every real id), canonical (kind, id)
/// order, set-change propagation to dependents, source values read from the last
/// resolution (base while a source pair has never resolved), CanonicalNaN at
/// broadcast, zeroed rows for dead and never-resolved pairs.
/// </summary>
public sealed class EagerModel
{
    public struct Entry
    {
        public long Id;

        public byte Kind;

        public float Value;

        public int Source;

        public long Expiry;
    }

    readonly int statCount;

    readonly float[] bases;

    readonly (int Stat, float Coefficient)[][] sources;

    readonly int[][] dependents;

    List<Entry>[] content = Array.Empty<List<Entry>>();

    bool[] resolved = Array.Empty<bool>();

    bool[] dirty = Array.Empty<bool>();

    bool[] live = Array.Empty<bool>();

    float[] lastValue = Array.Empty<float>();

    string?[] prevKey = Array.Empty<string>();

    public long NextId = 1;

    public EagerModel(float[] bases, (int stat, float coefficient)[][] sources)
    {
        statCount = bases.Length;
        this.bases = bases;
        this.sources = sources;
        dependents = new int[statCount][];
        for (var stat = 0; stat < statCount; stat++)
        {
            var list = new HashSet<int>();
            for (var candidate = 0; candidate < statCount; candidate++)
            {
                foreach (var (source, _) in sources[candidate])
                {
                    if (source == stat)
                    {
                        list.Add(candidate);
                    }
                }
            }

            dependents[stat] = [.. list];
        }
    }

    public int Capacity { get; private set; }

    public void Grow(int capacity)
    {
        Capacity = capacity;
        content = new List<Entry>[capacity * statCount];
        resolved = new bool[capacity * statCount];
        dirty = new bool[capacity * statCount];
        live = new bool[capacity];
        lastValue = new float[capacity * statCount];
        prevKey = new string?[capacity * statCount];
    }

    public void Acquire(int slot)
    {
        live[slot] = true;
        for (var stat = 0; stat < statCount; stat++)
        {
            Reset(slot, stat);
            dirty[slot * statCount + stat] = true;
        }
    }

    public void Release(int slot)
    {
        live[slot] = false;
        for (var stat = 0; stat < statCount; stat++)
        {
            Reset(slot, stat);
        }
    }

    void Reset(int slot, int stat)
    {
        var pair = slot * statCount + stat;
        content[pair] = [];
        resolved[pair] = false;
        dirty[pair] = false;
        lastValue[pair] = 0f;
        prevKey[pair] = null;
    }

    public void AddModifier(int slot, int stat, byte kind, float value, int source, long expiry)
    {
        var pair = slot * statCount + stat;
        content[pair].Add(new Entry { Id = NextId++, Kind = kind, Value = value, Source = source, Expiry = expiry });
        resolved[pair] = true;
        dirty[pair] = true;
    }

    public void RemoveModifier(int slot, int stat, long id)
    {
        var pair = slot * statCount + stat;
        var list = content[pair];
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Id == id)
            {
                list.RemoveAt(i);
                resolved[pair] = true;
                dirty[pair] = true;
                return;
            }
        }
    }

    public int RemoveSource(int slot, int source)
    {
        var removed = 0;
        for (var stat = 0; stat < statCount; stat++)
        {
            var pair = slot * statCount + stat;
            var list = content[pair];
            var before = list.Count;
            list.RemoveAll(e => e.Source == source);
            if (list.Count != before)
            {
                removed += before - list.Count;
                resolved[pair] = true;
                dirty[pair] = true;
            }
        }

        return removed;
    }

    public int Expire(long tick)
    {
        var removed = 0;
        for (var pair = 0; pair < content.Length; pair++)
        {
            if (content[pair] == null)
            {
                continue;
            }

            var before = content[pair].Count;
            content[pair].RemoveAll(e => e.Expiry <= tick);
            if (content[pair].Count != before)
            {
                removed += before - content[pair].Count;
                resolved[pair] = true;
                dirty[pair] = true;
            }
        }

        return removed;
    }

    public void Recalculate()
    {
        var queue = new Queue<(int Slot, int Stat)>();
        var queued = new HashSet<int>();
        for (var slot = 0; slot < Capacity; slot++)
        {
            if (!live[slot])
            {
                continue;
            }

            for (var stat = 0; stat < statCount; stat++)
            {
                var pair = slot * statCount + stat;
                if (dirty[pair])
                {
                    queue.Enqueue((slot, stat));
                    queued.Add(pair);
                    dirty[pair] = false;
                }
            }
        }

        while (queue.Count > 0)
        {
            var (slot, stat) = queue.Dequeue();
            queued.Remove(slot * statCount + stat);
            if (!live[slot])
            {
                continue;
            }

            var pair = slot * statCount + stat;
            var canonical = new List<(byte Kind, float Value, long Id)>();
            for (var k = 0; k < sources[stat].Length; k++)
            {
                var (source, coefficient) = sources[stat][k];
                var sourcePair = slot * statCount + source;
                var sourceValue = resolved[sourcePair] ? lastValue[sourcePair] : bases[source];
                canonical.Add((0, coefficient * sourceValue, EagerOracle.SyntheticIdBase + k));
            }

            foreach (var entry in content[pair])
            {
                canonical.Add((entry.Kind, entry.Value, entry.Id));
            }

            canonical.Sort(static (a, b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : a.Id.CompareTo(b.Id));

            var key = ContentKey(canonical);
            var value = EagerOracle.CanonicalNaN(EagerOracle.Fold(bases[stat], canonical));
            var setChanged = prevKey[pair] != key;
            lastValue[pair] = value;
            prevKey[pair] = key;
            resolved[pair] = true;
            if (setChanged)
            {
                foreach (var dependent in dependents[stat])
                {
                    var dependentPair = slot * statCount + dependent;
                    if (!queued.Contains(dependentPair))
                    {
                        queue.Enqueue((slot, dependent));
                        queued.Add(dependentPair);
                    }
                }
            }
        }
    }

    static string ContentKey(List<(byte Kind, float Value, long Id)> canonical)
    {
        var parts = new string[canonical.Count];
        for (var i = 0; i < canonical.Count; i++)
        {
            parts[i] = $"{canonical[i].Kind}:{BitConverter.SingleToUInt32Bits(canonical[i].Value):X8}";
        }

        return string.Join(';', parts);
    }

    public float Value(int slot, int stat)
    {
        var pair = slot * statCount + stat;
        return live[slot] && resolved[pair] ? lastValue[pair] : 0f;
    }
}
