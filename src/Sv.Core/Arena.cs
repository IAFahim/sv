using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sv
{
    public enum ArenaPolicy : byte
    {
        Grow = 0,
        HardCap = 1,
    }

    public enum LayerVerdict : byte
    {
        Fits = 0,
        Rerun = 1,
        Discarded = 2,
    }

    public readonly struct RawMark
    {
        public readonly int Blocks;
        public readonly long Records;
        public readonly int Interval;

        public RawMark(int blocks, long records, int interval)
        {
            Blocks = blocks;
            Records = records;
            Interval = interval;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ArenaCore
    {
        public const int BlockShift = 8;
        public const int BlockRecords = 1 << BlockShift;
        public const int SegmentShift = 9;
        public const int SegmentBlocks = 1 << SegmentShift;
        public const int SegmentRecords = SegmentBlocks * BlockRecords;

        public Effect** Segments;
        public int* Fill;
        public byte* Ordered;
        public int SegmentCount;
        public int SegmentLimit;
        public int Blocks;
        public int Taken;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Effect* Block(ArenaCore* core, int block) =>
            core->Segments[block >> SegmentShift] + ((block & (SegmentBlocks - 1)) << BlockShift);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int TakenBlocks(ArenaCore* core) => core->Taken < core->Blocks ? core->Taken : core->Blocks;
    }

    [StructLayout(LayoutKind.Sequential, Size = 128)]
    public unsafe struct EffectWriter
    {
        public ArenaCore* Core;
        public Effect* Block;
        public ulong LastKey;
        public long Sent;
        public long Missed;
        public int BlockIndex;
        public int Fill;
        public int LastAmount;
        public int Ordered;
        public int Exhausted;

        public static EffectWriter Create(ArenaCore* core)
        {
            var writer = default(EffectWriter);
            writer.Core = core;
            writer.BlockIndex = -1;
            writer.Fill = ArenaCore.BlockRecords;
            return writer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Send(ulong key, int amount, int hits)
        {
            if ((amount == 0) | (hits <= 0))
            {
                return false;
            }

            Sent++;
            if (Fill == ArenaCore.BlockRecords && !Open())
            {
                return false;
            }

            var ordered = key > LastKey | (key == LastKey & amount >= LastAmount);
            Ordered &= ordered ? 1 : 0;
            Block[Fill] = new Effect(key, amount, hits);
            Fill++;
            LastKey = key;
            LastAmount = amount;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Send(int target, int source, int kind, int channel, int amount, int hits) =>
            Send(EffectKey.Pack(target, source, (byte)kind, (byte)channel, Origin.Direct, 0), amount, hits);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Send(int target, int source, int kind, int channel, Origin origin, int amount, int hits) =>
            Send(EffectKey.Pack(target, source, (byte)kind, (byte)channel, origin, 0), amount, hits);

        [MethodImpl(MethodImplOptions.NoInlining)]
        bool Open()
        {
            Close();
            if (Exhausted != 0)
            {
                Missed++;
                return false;
            }

            var index = Interlocked.Increment(ref Core->Taken) - 1;
            if (index >= Core->Blocks)
            {
                Exhausted = 1;
                Missed++;
                return false;
            }

            BlockIndex = index;
            Block = ArenaCore.Block(Core, index);
            Fill = 0;
            Ordered = 1;
            LastKey = 0;
            LastAmount = int.MinValue;
            return true;
        }

        void Close()
        {
            if (BlockIndex >= 0)
            {
                Core->Fill[BlockIndex] = Fill;
                Core->Ordered[BlockIndex] = (byte)Ordered;
            }

            BlockIndex = -1;
            Block = null;
            Fill = ArenaCore.BlockRecords;
        }

        public void Flush() => Close();

        public void Forget()
        {
            BlockIndex = -1;
            Block = null;
            Fill = ArenaCore.BlockRecords;
            Exhausted = 0;
            Sent = 0;
            Missed = 0;
        }
    }

    public sealed unsafe class RawArena : IDisposable
    {
        readonly NativeArena arena = new NativeArena();

        ArenaCore* core;

        EffectWriter* writers;

        long carried;

        int interval;

        public RawArena(int capacity, int writerCount, ArenaPolicy policy, int segmentLimit = 4096)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), $"sv: raw arena of {capacity} records is empty");
            }

            if (writerCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(writerCount), $"sv: raw arena needs at least one writer, got {writerCount}");
            }

            if (segmentLimit < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(segmentLimit), $"sv: segment limit {segmentLimit} is below one");
            }

            Writers = writerCount;
            Policy = policy;
            core = arena.AllocZeroed<ArenaCore>(1);
            core->SegmentLimit = segmentLimit;
            core->Segments = (Effect**)arena.AllocZeroed<IntPtr>(segmentLimit);
            writers = arena.AllocAlignedZeroed<EffectWriter>(writerCount);
            for (var index = 0; index < writerCount; index++)
            {
                writers[index] = EffectWriter.Create(core);
            }

            var blocks = (int)(((long)capacity + ArenaCore.BlockRecords - 1) >> ArenaCore.BlockShift) + writerCount;
            if (!Grow(blocks))
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), $"sv: raw arena of {capacity} records exceeds {segmentLimit} segments");
            }

            interval = 1;
        }

        public int Writers { get; }

        public ArenaPolicy Policy { get; }

        public int Grows { get; private set; }

        public long DroppedLayers { get; private set; }

        public long DroppedRecords { get; private set; }

        public ArenaCore* Core => core;

        public int Blocks => core->Blocks;

        public long Capacity => (long)core->Blocks << ArenaCore.BlockShift;

        public EffectWriter* Writer(int index)
        {
            if ((uint)index >= (uint)Writers)
            {
                throw new ArgumentOutOfRangeException(nameof(index), $"sv: writer {index} of {Writers}");
            }

            return writers + index;
        }

        public long Sent
        {
            get
            {
                var sent = carried;
                for (var index = 0; index < Writers; index++)
                {
                    sent += writers[index].Sent;
                }

                return sent;
            }
        }

        public long Missed
        {
            get
            {
                var missed = 0L;
                for (var index = 0; index < Writers; index++)
                {
                    missed += writers[index].Missed;
                }

                return missed;
            }
        }

        public void Flush()
        {
            for (var index = 0; index < Writers; index++)
            {
                writers[index].Flush();
            }
        }

        public RawMark Mark()
        {
            Flush();
            interval++;
            return new RawMark(ArenaCore.TakenBlocks(core), Sent, interval);
        }

        public LayerVerdict EndLayer(in RawMark mark)
        {
            Flush();
            var records = Sent;
            if (!Overflows(records, interval, core->Blocks) && Missed == 0)
            {
                interval++;
                return LayerVerdict.Fits;
            }

            var layerRecords = records - mark.Records;
            Release(mark);
            if (Policy == ArenaPolicy.Grow)
            {
                var needed = BlocksFor(records, mark.Interval);
                var doubled = (long)core->Blocks * 2;
                var target = needed + (needed >> 1) > doubled ? needed + (needed >> 1) : doubled;
                if (target <= int.MaxValue && Grow((int)target))
                {
                    Grows++;
                    return LayerVerdict.Rerun;
                }
            }

            DroppedLayers++;
            DroppedRecords += layerRecords;
            return LayerVerdict.Discarded;
        }

        public void Release(in RawMark mark)
        {
            for (var index = 0; index < Writers; index++)
            {
                writers[index].Forget();
            }

            core->Taken = mark.Blocks;
            carried = mark.Records;
            interval = mark.Interval - 1;
        }

        public void Reset()
        {
            for (var index = 0; index < Writers; index++)
            {
                writers[index].Forget();
            }

            core->Taken = 0;
            carried = 0;
            interval = 1;
        }

        bool Overflows(long records, int intervals, int blocks) => BlocksFor(records, intervals) > blocks;

        long BlocksFor(long records, int intervals) =>
            ((records + ArenaCore.BlockRecords - 1) >> ArenaCore.BlockShift) + (long)intervals * Writers;

        bool Grow(int blocks)
        {
            var segments = (blocks + ArenaCore.SegmentBlocks - 1) >> ArenaCore.SegmentShift;
            if (segments > core->SegmentLimit)
            {
                return false;
            }

            if (segments <= core->SegmentCount)
            {
                return true;
            }

            var oldBlocks = core->Blocks;
            var newBlocks = segments << ArenaCore.SegmentShift;
            for (var segment = core->SegmentCount; segment < segments; segment++)
            {
                core->Segments[segment] = arena.AllocAlignedZeroed<Effect>(ArenaCore.SegmentRecords);
            }

            core->Fill = arena.Realloc(core->Fill, oldBlocks, newBlocks);
            core->Ordered = arena.Realloc(core->Ordered, oldBlocks, newBlocks);
            Memory.Clear(core->Fill + oldBlocks, (nuint)(newBlocks - oldBlocks) * sizeof(int));
            Memory.Clear(core->Ordered + oldBlocks, (nuint)(newBlocks - oldBlocks));
            core->SegmentCount = segments;
            core->Blocks = newBlocks;
            return true;
        }

        public void Dispose()
        {
            if (core == null)
            {
                return;
            }

            for (var segment = 0; segment < core->SegmentCount; segment++)
            {
                arena.FreeAligned(core->Segments[segment], ArenaCore.SegmentRecords);
            }

            arena.Free(core->Fill, core->Blocks);
            arena.Free(core->Ordered, core->Blocks);
            arena.Free((IntPtr*)core->Segments, core->SegmentLimit);
            arena.FreeAligned(writers, Writers);
            arena.Free(core, 1);
            core = null;
            writers = null;
            arena.Dispose();
        }
    }

    public enum OverflowLevel : byte
    {
        None = 0,
        MergedKeys = 1,
        MergedSources = 2,
        MergedKinds = 3,
        Truncated = 4,
    }

    public sealed unsafe class CanonicalArena : IDisposable
    {
        readonly NativeArena arena = new NativeArena();

        Effect* records;

        Effect* scratch;

        int capacity;

        int* start;

        int* @base;

        int* merged;

        int* active;

        int* counts;

        int* totals;

        long* dead;

        long* faults;

        public CanonicalArena(int chunks, int parts, int limit, ArenaPolicy policy)
        {
            if (chunks < 1 || chunks > EffectKey.ChunkLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(chunks), $"sv: {chunks} chunks is outside 1..{EffectKey.ChunkLimit}");
            }

            if (parts < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(parts), $"sv: seal needs at least one part, got {parts}");
            }

            if (limit < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(limit), $"sv: canonical limit {limit} is below one");
            }

            Chunks = chunks;
            Parts = parts;
            Limit = limit;
            Policy = policy;
            start = arena.AllocZeroed<int>(chunks + 1);
            @base = arena.AllocZeroed<int>(chunks);
            merged = arena.AllocZeroed<int>(chunks);
            active = arena.AllocZeroed<int>(chunks);
            counts = arena.AllocAlignedZeroed<int>(parts * chunks);
            totals = arena.AllocZeroed<int>(2);
            dead = arena.AllocAlignedZeroed<long>(parts);
            faults = arena.AllocAlignedZeroed<long>(parts);
        }

        public int Chunks { get; }

        public int Parts { get; }

        public int Limit { get; }

        public ArenaPolicy Policy { get; }

        public int Capacity => capacity;

        public int Count { get; private set; }

        public int ActiveCount { get; private set; }

        public long DroppedDead { get; private set; }

        public long RouteFaults { get; private set; }

        public long Missed { get; private set; }

        public OverflowLevel Overflow { get; private set; }

        public long DroppedOverflow { get; private set; }

        public int Grows { get; private set; }

        public Effect* Records => records;

        public int* Start => start;

        public int* Active => active;

        public Effect* Segment(int chunk, out int count)
        {
            if ((uint)chunk >= (uint)Chunks)
            {
                count = 0;
                return records;
            }

            count = start[chunk + 1] - start[chunk];
            return records + start[chunk];
        }

        public SealView Begin(RawArena raw, in RouteTable route)
        {
            if (route.Chunks > Chunks)
            {
                throw new ArgumentOutOfRangeException(nameof(route), $"sv: route covers {route.Chunks} chunks, canonical arena {Chunks}");
            }

            if (route.Live == null)
            {
                throw new ArgumentNullException(nameof(route), "sv: route table has no live words");
            }

            raw.Flush();
            if (raw.Capacity > int.MaxValue)
            {
                throw new InvalidOperationException($"sv: raw arena of {raw.Capacity} records exceeds the canonical index range");
            }

            var needed = (int)raw.Capacity;
            if (needed > capacity)
            {
                arena.FreeAligned(records, capacity);
                arena.FreeAligned(scratch, capacity);
                records = arena.AllocAlignedZeroed<Effect>(needed);
                scratch = arena.AllocAlignedZeroed<Effect>(needed);
                capacity = needed;
                Grows++;
            }

            return View(raw.Core, route, ArenaCore.TakenBlocks(raw.Core));
        }

        public void End(in SealView view, RawArena raw)
        {
            Count = totals[Seal.TotalCount];
            ActiveCount = totals[Seal.TotalActive];
            var deadRecords = 0L;
            var faultRecords = 0L;
            for (var part = 0; part < Parts; part++)
            {
                deadRecords += dead[part];
                faultRecords += faults[part];
            }

            DroppedDead = deadRecords;
            RouteFaults = faultRecords;
            Missed = raw.Missed;
            Overflow = OverflowLevel.None;
            DroppedOverflow = 0;
            if (Policy == ArenaPolicy.HardCap && Count > Limit)
            {
                Degrade(view);
            }

#if SV_CHECKED
            var fault = Seal.Check(records, Count);
            if (fault != null)
            {
                throw new InvalidOperationException(fault);
            }
#endif
            raw.Reset();
        }

        public void Run<TForward>(RawArena raw, in RouteTable route, ref TForward forward)
            where TForward : struct, IForward
        {
            var view = Begin(raw, route);
            for (var part = 0; part < Parts; part++)
            {
                Seal.Count(view, ref forward, part);
            }

            Seal.Plan(view);
            for (var part = 0; part < Parts; part++)
            {
                Seal.Scatter(view, part);
            }

            Seal.Order(view, 0, Chunks);
            Seal.Finish(view);
            Seal.Compact(view, view.Scratch, view.Records, 0, totals[Seal.TotalActive]);
            End(view, raw);
        }

        void Degrade(in SealView sealedView)
        {
            var view = sealedView;
            for (var level = 1; level <= 3 && Count > Limit; level++)
            {
                view.Records = records;
                view.Scratch = scratch;
                Seal.DegradeAll(view, level);
                Seal.Finish(view);
                Seal.Compact(view, records, scratch, 0, totals[Seal.TotalActive]);
                var swap = records;
                records = scratch;
                scratch = swap;
                Count = totals[Seal.TotalCount];
                ActiveCount = totals[Seal.TotalActive];
                Overflow = (OverflowLevel)level;
            }

            if (Count > Limit)
            {
                view.Records = records;
                view.Scratch = scratch;
                DroppedOverflow = Seal.Truncate(view, Limit);
                Seal.Finish(view);
                Count = totals[Seal.TotalCount];
                ActiveCount = totals[Seal.TotalActive];
                Overflow = OverflowLevel.Truncated;
            }
        }

        SealView View(ArenaCore* raw, in RouteTable route, int blocks)
        {
            var view = default(SealView);
            view.Raw = raw;
            view.Records = records;
            view.Scratch = scratch;
            view.Start = start;
            view.Base = @base;
            view.Merged = merged;
            view.Active = active;
            view.Counts = counts;
            view.Totals = totals;
            view.Dead = dead;
            view.Faults = faults;
            view.Route = route;
            view.Chunks = Chunks;
            view.Parts = Parts;
            view.Blocks = blocks;
            return view;
        }

        public void Dispose()
        {
            if (start == null)
            {
                return;
            }

            arena.FreeAligned(records, capacity);
            arena.FreeAligned(scratch, capacity);
            arena.Free(start, Chunks + 1);
            arena.Free(@base, Chunks);
            arena.Free(merged, Chunks);
            arena.Free(active, Chunks);
            arena.FreeAligned(counts, Parts * Chunks);
            arena.Free(totals, 2);
            arena.FreeAligned(dead, Parts);
            arena.FreeAligned(faults, Parts);
            records = null;
            scratch = null;
            start = null;
            arena.Dispose();
        }
    }
}
