using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sv
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct SealView
    {
        public ArenaCore* Raw;
        public Effect* Records;
        public Effect* Scratch;
        public int* Start;
        public int* Base;
        public int* Merged;
        public int* Active;
        public int* Counts;
        public int* Totals;
        public long* Dead;
        public long* Faults;
        public RouteTable Route;
        public int Chunks;
        public int Parts;
        public int Blocks;
    }

    public static unsafe class Seal
    {
        public const int MinimumRange = 16;
        public const int SmallSort = 24;
        public const int RunLength = 16;
        public const int TotalCount = 0;
        public const int TotalActive = 1;

        public static int Merge(Effect* sorted, int count, Effect* canonical)
        {
            var written = 0;
            var at = 0;
            while (at < count)
            {
                var key = sorted[at].Key;
                var amount = sorted[at].Amount;
                var hits = 0L;
                while (at < count && sorted[at].Key == key && sorted[at].Amount == amount)
                {
                    hits += sorted[at].Hits;
                    at++;
                }

                canonical[written] = new Effect(key, amount, EffectLaw.Saturate32(hits));
                written++;
            }

            return written;
        }

        public static void Range(int items, int part, int parts, out int from, out int to)
        {
            var most = (items + MinimumRange - 1) / MinimumRange;
            var used = parts < most ? parts : most;
            if (used < 1 || part >= used)
            {
                from = items;
                to = items;
                return;
            }

            from = (int)((long)items * part / used);
            to = (int)((long)items * (part + 1) / used);
        }

        public static void Count<TForward>(in SealView view, ref TForward forward, int part)
            where TForward : struct, IForward
        {
            var counts = view.Counts + (long)part * view.Chunks;
            Memory.Clear(counts, (nuint)view.Chunks * sizeof(int));
            Range(view.Blocks, part, view.Parts, out var from, out var to);
            var dead = 0L;
            var faults = 0L;
            for (var block = from; block < to; block++)
            {
                var records = ArenaCore.Block(view.Raw, block);
                var fill = view.Raw->Fill[block];
                var kept = 0;
                var moved = 0;
                for (var at = 0; at < fill; at++)
                {
                    var record = records[at];
                    var key = record.Key;
                    var verdict = Sv.Route.Deliver(view.Route, ref forward, ref key);
                    var keep = verdict >= 0;
                    dead += verdict == Sv.Route.Dead ? 1L : 0L;
                    faults += verdict == Sv.Route.Fault ? 1L : 0L;
                    moved |= verdict == Sv.Route.Moved ? 1 : 0;
                    records[kept] = new Effect(key, record.Amount, record.Hits);
                    counts[keep ? EffectKey.Chunk(key) : 0] += keep ? 1 : 0;
                    kept += keep ? 1 : 0;
                }

                view.Raw->Fill[block] = kept;
                if (moved != 0)
                {
                    view.Raw->Ordered[block] = 0;
                }
            }

            view.Dead[part] = dead;
            view.Faults[part] = faults;
        }

        public static int Plan(in SealView view)
        {
            var running = 0;
            for (var chunk = 0; chunk < view.Chunks; chunk++)
            {
                view.Base[chunk] = running;
                var cell = view.Counts + chunk;
                for (var part = 0; part < view.Parts; part++)
                {
                    var count = *cell;
                    *cell = running;
                    running += count;
                    cell += view.Chunks;
                }

                view.Merged[chunk] = running - view.Base[chunk];
            }

            return running;
        }

        public static void Scatter(in SealView view, int part)
        {
            var cursor = view.Counts + (long)part * view.Chunks;
            Range(view.Blocks, part, view.Parts, out var from, out var to);
            for (var block = from; block < to; block++)
            {
                var records = ArenaCore.Block(view.Raw, block);
                var fill = view.Raw->Fill[block];
                if (view.Raw->Ordered[block] != 0)
                {
                    var at = 0;
                    while (at < fill)
                    {
                        var chunk = EffectKey.Chunk(records[at].Key);
                        var end = at + 1;
                        while (end < fill && EffectKey.Chunk(records[end].Key) == chunk)
                        {
                            end++;
                        }

                        var run = end - at;
                        Memory.Copy(records + at, view.Scratch + cursor[chunk], (nuint)run * (nuint)sizeof(Effect));
                        cursor[chunk] += run;
                        at = end;
                    }
                }
                else
                {
                    for (var at = 0; at < fill; at++)
                    {
                        var chunk = EffectKey.Chunk(records[at].Key);
                        view.Scratch[cursor[chunk]] = records[at];
                        cursor[chunk]++;
                    }
                }
            }
        }

        public static void Order(in SealView view, int chunkFrom, int chunkTo)
        {
            var rows = stackalloc int[EffectKey.Rows];
            for (var chunk = chunkFrom; chunk < chunkTo; chunk++)
            {
                var count = view.Merged[chunk];
                if (count == 0)
                {
                    continue;
                }

                var offset = view.Base[chunk];
                var segment = view.Scratch + offset;
                var sorted = Sort(segment, view.Records + offset, count, rows);
                view.Merged[chunk] = Merge(sorted, count, segment);
            }
        }

        public static void Finish(in SealView view)
        {
            var running = 0;
            var active = 0;
            for (var chunk = 0; chunk < view.Chunks; chunk++)
            {
                view.Start[chunk] = running;
                var count = view.Merged[chunk];
                view.Active[active] = chunk;
                active += count > 0 ? 1 : 0;
                running += count;
            }

            view.Start[view.Chunks] = running;
            view.Totals[TotalCount] = running;
            view.Totals[TotalActive] = active;
        }

        public static void Compact(in SealView view, Effect* from, Effect* to, int activeFrom, int activeTo)
        {
            for (var index = activeFrom; index < activeTo; index++)
            {
                var chunk = view.Active[index];
                Memory.Copy(from + view.Base[chunk], to + view.Start[chunk], (nuint)view.Merged[chunk] * (nuint)sizeof(Effect));
            }
        }

        public static Effect* Sort(Effect* data, Effect* temp, int count, int* rows)
        {
            if (Ascending(data, count))
            {
                return data;
            }

            if (count <= SmallSort)
            {
                Insertion(data, count);
                return data;
            }

            ByRow(data, temp, count, rows);
            var start = 0;
            for (var row = 0; row < EffectKey.Rows; row++)
            {
                var end = rows[row];
                var length = end - start;
                if (length > SmallSort)
                {
                    MergeSort(temp + start, data + start, length);
                }
                else if (length > 1)
                {
                    Insertion(temp + start, length);
                }

                start = end;
            }

            return temp;
        }

        public static bool Ascending(Effect* data, int count)
        {
            var descents = 0;
            for (var at = 1; at < count; at++)
            {
                descents |= EffectLaw.Precedes(data[at], data[at - 1]) ? 1 : 0;
            }

            return descents == 0;
        }

        public static void Insertion(Effect* data, int count)
        {
            for (var at = 1; at < count; at++)
            {
                var item = data[at];
                var hole = at;
                while (hole > 0 && EffectLaw.Precedes(item, data[hole - 1]))
                {
                    data[hole] = data[hole - 1];
                    hole--;
                }

                data[hole] = item;
            }
        }

        static void ByRow(Effect* data, Effect* temp, int count, int* rows)
        {
            Memory.Clear(rows, (nuint)EffectKey.Rows * sizeof(int));
            for (var at = 0; at < count; at++)
            {
                rows[EffectKey.Row(data[at].Key)]++;
            }

            var running = 0;
            for (var row = 0; row < EffectKey.Rows; row++)
            {
                var length = rows[row];
                rows[row] = running;
                running += length;
            }

            for (var at = 0; at < count; at++)
            {
                var row = EffectKey.Row(data[at].Key);
                temp[rows[row]] = data[at];
                rows[row]++;
            }
        }

        static void MergeSort(Effect* data, Effect* spare, int count)
        {
            for (var start = 0; start < count; start += RunLength)
            {
                var length = count - start < RunLength ? count - start : RunLength;
                Insertion(data + start, length);
            }

            var from = data;
            var to = spare;
            for (var width = RunLength; width < count; width <<= 1)
            {
                for (var low = 0; low < count; low += width << 1)
                {
                    var middle = low + width < count ? low + width : count;
                    var high = low + (width << 1) < count ? low + (width << 1) : count;
                    MergeRuns(from + low, middle - low, from + middle, high - middle, to + low);
                }

                var swap = from;
                from = to;
                to = swap;
                if (width > (int.MaxValue >> 1))
                {
                    break;
                }
            }

            if (from != data)
            {
                Memory.Copy(from, data, (nuint)count * (nuint)sizeof(Effect));
            }
        }

        static void MergeRuns(Effect* left, int leftCount, Effect* right, int rightCount, Effect* output)
        {
            var l = 0;
            var r = 0;
            var o = 0;
            while (l < leftCount && r < rightCount)
            {
                var takeRight = EffectLaw.Precedes(right[r], left[l]);
                output[o] = takeRight ? right[r] : left[l];
                r += takeRight ? 1 : 0;
                l += takeRight ? 0 : 1;
                o++;
            }

            if (l < leftCount)
            {
                Memory.Copy(left + l, output + o, (nuint)(leftCount - l) * (nuint)sizeof(Effect));
            }

            if (r < rightCount)
            {
                Memory.Copy(right + r, output + o, (nuint)(rightCount - r) * (nuint)sizeof(Effect));
            }
        }

        public static int CoalesceByKey(Effect* sorted, int count, Effect* output)
        {
            var written = 0;
            var at = 0;
            while (at < count)
            {
                var first = sorted[at];
                var total = 0L;
                var end = at;
                while (end < count && sorted[end].Key == first.Key)
                {
                    total += EffectLaw.Term(sorted[end].Amount, sorted[end].Hits);
                    end++;
                }

                if (end - at == 1)
                {
                    output[written++] = first;
                }
                else
                {
                    var merged = EffectLaw.Saturate32(total);
                    if (merged != 0)
                    {
                        output[written++] = new Effect(first.Key, merged, 1);
                    }
                }

                at = end;
            }

            return written;
        }

        public static int CoalesceByChannel(Effect* sorted, int count, Effect* output)
        {
            const ulong lineage = (3ul << EffectKey.OriginShift) | 3ul;
            var written = 0;
            var at = 0;
            while (at < count)
            {
                var first = sorted[at];
                var group = first.Key & ~lineage;
                var total = 0L;
                var origin = 0ul;
                var depth = 0ul;
                var end = at;
                while (end < count && (sorted[end].Key & ~lineage) == group)
                {
                    var key = sorted[end].Key;
                    var recordOrigin = (key >> EffectKey.OriginShift) & 3ul;
                    var recordDepth = key & 3ul;
                    origin = recordOrigin > origin ? recordOrigin : origin;
                    depth = recordDepth > depth ? recordDepth : depth;
                    total += EffectLaw.Term(sorted[end].Amount, sorted[end].Hits);
                    end++;
                }

                if (end - at == 1)
                {
                    output[written++] = first;
                }
                else
                {
                    var merged = EffectLaw.Saturate32(total);
                    if (merged != 0)
                    {
                        output[written++] = new Effect(group | (origin << EffectKey.OriginShift) | depth, merged, 1);
                    }
                }

                at = end;
            }

            return written;
        }

        public static int Degrade(Effect* segment, Effect* temp, int count, int level, int* rows)
        {
            if (level <= 1)
            {
                return CoalesceByKey(segment, count, segment);
            }

            for (var at = 0; at < count; at++)
            {
                var key = EffectKey.WithoutSource(segment[at].Key);
                key = level >= 3 ? EffectKey.WithoutKind(key) : key;
                segment[at] = new Effect(key, segment[at].Amount, segment[at].Hits);
            }

            var sorted = Sort(segment, temp, count, rows);
            return level == 2 ? CoalesceByKey(sorted, count, segment) : CoalesceByChannel(sorted, count, segment);
        }

        public static void DegradeAll(in SealView view, int level)
        {
            var rows = stackalloc int[EffectKey.Rows];
            for (var chunk = 0; chunk < view.Chunks; chunk++)
            {
                var offset = view.Start[chunk];
                var count = view.Start[chunk + 1] - offset;
                view.Base[chunk] = offset;
                view.Merged[chunk] = count == 0 ? 0 : Degrade(view.Records + offset, view.Scratch + offset, count, level, rows);
            }
        }

        public static long Truncate(in SealView view, int limit)
        {
            var kept = 0;
            var dropped = 0L;
            for (var chunk = 0; chunk < view.Chunks; chunk++)
            {
                var count = view.Merged[chunk];
                var room = limit - kept;
                var keep = count < room ? count : room;
                view.Merged[chunk] = keep;
                kept += keep;
                dropped += count - keep;
            }

            return dropped;
        }

        public static string? Check(Effect* records, int count)
        {
            for (var at = 0; at < count; at++)
            {
                var record = records[at];
                if (record.Amount == 0)
                {
                    return $"sv: canonical record {at} has amount 0";
                }

                if (record.Hits <= 0)
                {
                    return $"sv: canonical record {at} has {record.Hits} hits";
                }

                if (at > 0 && !EffectLaw.Precedes(records[at - 1], record))
                {
                    return $"sv: canonical records {at - 1} and {at} are not strictly ascending";
                }
            }

            return null;
        }
    }
}
