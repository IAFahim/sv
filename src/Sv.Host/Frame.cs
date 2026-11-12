using System;
using System.Runtime.InteropServices;
using Sv.Host.Net;

namespace Sv.Host
{
    public sealed class HostOptions
    {
        public int EntityCapacity = 1 << 16;
        public StoragePolicy Policy = StoragePolicy.Shared;
        public int RingEpochs = Journal.DefaultRing;
        public int FramesPerEpoch = World.DefaultFramesPerEpoch;
        public int Workers = 1;
        public int Parts = 64;
        public int RawCapacity = 1 << 16;
        public int CanonicalLimit = int.MaxValue;
        public ArenaPolicy Arena = ArenaPolicy.Grow;
        public int ExternalWriters = 1;
        public int EventCapacity = 16384;
        public int DigestEvery = 1;
        public LatePolicy Late = LatePolicy.Clamp;
        public Width? Width;
        public double SpinMicroseconds = Sv.Host.Workers.DefaultSpinMicroseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct HostEvent
    {
        public readonly int Event;
        public readonly int Slot;
        public readonly bool Entered;

        public HostEvent(int raised, int slot, bool entered)
        {
            Event = raised;
            Slot = slot;
            Entered = entered;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct RecordBuffer
    {
        public byte* Pointer;
        public long Bytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct FrameContext
    {
        public int Capacity;
        public RecordBuffer* Buffers;
        public WorldView World;
        public SealView Seal;
        public Effect* Records;
        public int* Start;
        public int* Active;
        public int ActiveCount;
        public int Parts;
        public ulong* Recheck;
        public ulong* StatRows;
        public ulong* Touched;
        public ulong* EdgeAny;
        public ulong* Edges;
        public int* Scratch;
        public int ScratchInts;
        public int EventCount;
        public LaneWriter* LaneWriters;
        public EffectWriter* NextWriters;
        public ulong* StatPairs;
        public int* PairFrom;
        public int* PairTo;
        public ulong* DigestParts;
        public int ChunkCount;
        public Width Width;
    }

    public static unsafe class Phases<TKernels>
        where TKernels : struct, IPackKernels
    {
        public const int DigestStride = 16;

        public const int RecordScratchBytes = 32;

        public static void SealCount(void* state, int part)
        {
            var context = (FrameContext*)state;
            var forward = default(PackForward<TKernels>);
            forward.World = context->World;
            Sv.Seal.Count(context->Seal, ref forward, part);
        }

        public static void SealScatter(void* state, int part) => Sv.Seal.Scatter(((FrameContext*)state)->Seal, part);

        public static void SealOrder(void* state, int part)
        {
            var context = (FrameContext*)state;
            WorkRange.Of(context->Seal.Chunks, part, context->Parts, out var from, out var to);
            Sv.Seal.Order(context->Seal, from, to);
        }

        public static void SealCompact(void* state, int part)
        {
            var context = (FrameContext*)state;
            var active = context->Seal.Totals[Sv.Seal.TotalActive];
            WorkRange.Of(active, part, context->Parts, out var from, out var to);
            Sv.Seal.Compact(context->Seal, context->Seal.Scratch, context->Seal.Records, from, to);
        }

        public static void Decide(void* state, int part)
        {
            var context = (FrameContext*)state;
            WorkRange.Of(context->ActiveCount, part, context->Parts, out var from, out var to);
            var kernels = default(TKernels);
            var bytes = (nuint)context->ScratchInts * sizeof(int);
            ref var buffer = ref context->Buffers[part];
            for (var index = from; index < to; index++)
            {
                var chunk = context->Active[index];
                var scratch = context->Scratch + (long)index * context->ScratchInts;
                Memory.Clear(scratch, bytes);
                var first = context->Start[chunk];
                var count = context->Start[chunk + 1] - first;
                if (count == 0)
                {
                    context->Touched[index] = 0ul;
                    continue;
                }

                var need = (long)count * RecordScratchBytes;
                if (need > buffer.Bytes)
                {
                    buffer.Pointer = (byte*)Memory.Realloc(buffer.Pointer, (nuint)need);
                    buffer.Bytes = need;
                }

                context->Touched[index] = kernels.Decide(context->World, chunk, context->Capacity, context->Records + first, count, scratch, buffer.Pointer, ref context->NextWriters[part]);
            }
        }

        public static void Commit(void* state, int part)
        {
            var context = (FrameContext*)state;
            WorkRange.Of(context->ActiveCount, part, context->Parts, out var from, out var to);
            var kernels = default(TKernels);
            var floor = stackalloc int[Lanes.Rows];
            var ceiling = stackalloc int[Lanes.Rows];
            ref var writer = ref context->LaneWriters[part];
            var deltaLanes = kernels.DeltaLanes;
            for (var index = from; index < to; index++)
            {
                var chunk = context->Active[index];
                var scratch = context->Scratch + (long)index * context->ScratchInts;
                var touched = context->Touched[index];
                var statRows = context->StatRows[index];
                var hasRecords = context->Start[chunk + 1] != context->Start[chunk];
                var changed = 0ul;
                for (var lane = 0; lane < deltaLanes; lane++)
                {
                    var delta = scratch + (lane << Slot.RowBits);
                    var rows = hasRecords ? LaneScan.NonZero(context->Width, delta) : 0ul;
                    if (statRows != 0ul)
                    {
                        var floorStat = kernels.FloorStat(lane);
                        var ceilingStat = kernels.CeilingStat(lane);
                        var pairFrom = context->PairFrom[chunk];
                        var pairTo = context->PairTo[chunk];
                        rows |= floorStat >= 0 ? Collect.RowsOfLane(context->StatPairs, pairFrom, pairTo, floorStat) : 0ul;
                        rows |= ceilingStat >= 0 ? Collect.RowsOfLane(context->StatPairs, pairFrom, pairTo, ceilingStat) : 0ul;
                    }

                    if (rows == 0ul)
                    {
                        continue;
                    }

                    var value = (int*)Lanes.Writable(context->World, ref writer, chunk, kernels.DeltaTarget(lane));
                    kernels.Bounds(context->World, chunk, lane, floor, ceiling);
                    changed |= CommitFold.Lane(context->Width, value, delta, floor, ceiling, rows);
                }

                var live = Lanes.WordsOf(context->World, chunk, context->World.Layout.MetaLane)[ChunkMeta.Live];
                var recheck = (context->Recheck[index] | statRows | changed | touched) & live;
                var edges = context->Edges + (long)index * context->EventCount;
                context->EdgeAny[index] = recheck == 0ul || context->EventCount == 0
                    ? 0ul
                    : kernels.Events(context->World, ref writer, chunk, recheck, scratch, edges);
            }
        }

        public static void EdgeEmits(void* state, int part)
        {
            var context = (FrameContext*)state;
            WorkRange.Of(context->ActiveCount, part, context->Parts, out var from, out var to);
            var kernels = default(TKernels);
            for (var index = from; index < to; index++)
            {
                if (context->EdgeAny[index] != 0ul)
                {
                    kernels.EdgeEmits(context->World, context->Active[index], context->Capacity, context->Edges + (long)index * context->EventCount, context->Scratch + (long)index * context->ScratchInts, ref context->NextWriters[part]);
                }
            }
        }

        public static void Digest(void* state, int part)
        {
            var context = (FrameContext*)state;
            WorkRange.Of(context->ChunkCount, part, context->Parts, out var from, out var to);
            var sum = 0ul;
            unchecked
            {
                for (var chunk = from; chunk < to; chunk++)
                {
                    sum += WorldDigest.OfChunk(context->World, chunk);
                }
            }

            context->DigestParts[part * DigestStride] = sum;
        }
    }

    public sealed unsafe class FrameLoop<TKernels> : IDisposable
        where TKernels : struct, IPackKernels
    {
        readonly HostOptions options;
        readonly World world;
        readonly StatMachine stats;
        readonly RawArena raw;
        readonly CanonicalArena canonical;
        readonly InputLog log;
        readonly EventLog published;
        readonly Workers workers;
        readonly int parts;
        readonly int chunkCapacity;
        readonly int chunkWords;
        readonly int eventCount;
        readonly int scratchInts;
        readonly int lanesWritten;
        FrameContext* context;
        LaneWriter* laneWriters;
        ulong* live;
        ulong* forwarding;
        ulong* pendingBits;
        ulong* pendingRows;
        ulong* activeBits;
        ulong* statRowsByChunk;
        int* pairFrom;
        int* pairTo;
        int activeCapacity;
        StatOp* ops;
        int opCount;
        int opCapacity;
        LoggedRecord* replayRecords;
        int replayCapacity;
        HostEvent* events;
        int eventsCount;
        int eventsCapacity;
        ulong* eventKeys;
        int* denseCounts;
        EventChange* changes;
        int changeCount;
        int changeCapacity;
        Effect** carryRecords;
        int* carryCount;
        int* carryCapacity;
        uint* carryEpoch;
        ulong** carryPending;
        int* carryPendingCount;
        int* carryPendingCapacity;
        long earliestLate;
        bool stepping;
        bool replaying;

        public FrameLoop(HostPack pack, HostOptions? hostOptions = null)
        {
            options = hostOptions ?? new HostOptions();
            var kernels = default(TKernels);
            if (kernels.CompiledAgainst != kernels.PackHash || kernels.PackHash != pack.PackHash)
            {
                throw new InvalidOperationException("sv: the compiled kernels were built against another pack");
            }

            if (options.Parts < 1 || options.ExternalWriters < 1 || options.DigestEvery < 0 || options.EventCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(hostOptions), "sv: parts and external writers are at least one");
            }

            world = World.Create(pack.Classes, pack.Channels, options.EntityCapacity, options.Policy, options.RingEpochs, options.FramesPerEpoch);
            stats = new StatMachine(world, pack.Stats, ReadOnlySpan<StatSource>.Empty, ReadOnlySpan<int>.Empty, options.RingEpochs);
            AddTemplates(pack);
            parts = options.Parts;
            chunkCapacity = world.ChunkCapacity;
            chunkWords = ChunkSet.Words(chunkCapacity);
            eventCount = kernels.EventCount;
            scratchInts = Math.Max(1, kernels.ScratchLanes) << Slot.RowBits;
            lanesWritten = kernels.LanesWrittenPerChunk;
            raw = new RawArena(options.RawCapacity, options.ExternalWriters + parts, options.Arena);
            canonical = new CanonicalArena(chunkCapacity, parts, Math.Max(1, options.CanonicalLimit), options.Arena == ArenaPolicy.HardCap ? ArenaPolicy.HardCap : ArenaPolicy.Grow);
            log = new InputLog(InputLog.SpanOf(world), chunkCapacity, sizeof(StatOp));
            published = new EventLog(InputLog.SpanOf(world));
            workers = new Workers(Math.Max(1, options.Workers), options.SpinMicroseconds);
            Allocate();
            stats.MarkEpoch(world.Epoch);
            earliestLate = long.MaxValue;
        }

        public World World => world;

        public StatMachine Stats => stats;

        public InputLog Log => log;

        public RawArena Raw => raw;

        public CanonicalArena Canonical => canonical;

        public long Frame => world.Frame;

        public ulong LastDigest { get; private set; }

        public long ReplayFaults { get; private set; }

        public HostEvent* Events => events;

        public int EventCount => eventsCount;

        public int* DenseCounts => denseCounts;

        public EventChange* Changes => changes;

        public int ChangeCount => changeCount;

        public void ClearChanges() => changeCount = 0;

        void AddTemplates(HostPack pack)
        {
            var lanes = pack.Classes.Length;
            var values = new int[lanes];
            var bits = new uint[lanes];
            for (var template = 0; template < pack.Templates; template++)
            {
                Array.Copy(pack.TemplateValues[template], values, lanes);
                for (var s = 0; s < pack.Stats.Length; s++)
                {
                    stats.SetTemplateBase(template, pack.Stats[s].Lane, values[pack.Stats[s].Lane]);
                }

                stats.FoldTemplate(template, values);
                for (var lane = 0; lane < lanes; lane++)
                {
                    bits[lane] = (uint)values[lane];
                }

                if (world.AddTemplate(bits) != template)
                {
                    throw new InvalidOperationException("sv: templates are added in pack order");
                }
            }
        }

        void Allocate()
        {
            context = (FrameContext*)Memory.AllocZeroed(1, (nuint)sizeof(FrameContext));
            laneWriters = (LaneWriter*)Memory.AllocZeroed((nuint)parts, (nuint)sizeof(LaneWriter));
            for (var part = 0; part < parts; part++)
            {
                laneWriters[part] = world.CreateWriter(lanesWritten * WorkRange.MinimumItems + 4);
            }

            live = (ulong*)Memory.AllocZeroed((nuint)chunkCapacity, sizeof(ulong));
            forwarding = (ulong*)Memory.AllocZeroed((nuint)chunkCapacity, sizeof(ulong));
            pendingRows = (ulong*)Memory.AllocZeroed((nuint)chunkCapacity, sizeof(ulong));
            statRowsByChunk = (ulong*)Memory.AllocZeroed((nuint)chunkCapacity, sizeof(ulong));
            pairFrom = (int*)Memory.AllocZeroed((nuint)chunkCapacity, sizeof(int));
            pairTo = (int*)Memory.AllocZeroed((nuint)chunkCapacity, sizeof(int));
            pendingBits = (ulong*)Memory.AllocZeroed((nuint)chunkWords, sizeof(ulong));
            activeBits = (ulong*)Memory.AllocZeroed((nuint)chunkWords, sizeof(ulong));
            denseCounts = (int*)Memory.AllocZeroed((nuint)Math.Max(1, eventCount), sizeof(int));
            context->DigestParts = (ulong*)Memory.AllocAligned((nuint)parts * Phases<TKernels>.DigestStride * sizeof(ulong), 128);
            context->Parts = parts;
            context->Capacity = options.EntityCapacity;
            context->Buffers = (RecordBuffer*)Memory.AllocZeroed((nuint)parts, (nuint)sizeof(RecordBuffer));
            context->EventCount = eventCount;
            context->ScratchInts = scratchInts;
            context->LaneWriters = laneWriters;
            context->NextWriters = raw.Writer(options.ExternalWriters);
            context->Width = options.Width ?? Widths.FromEnvironment();
            var ring = world.RingEpochs;
            carryRecords = (Effect**)Memory.AllocZeroed((nuint)ring, (nuint)sizeof(Effect*));
            carryCount = (int*)Memory.AllocZeroed((nuint)ring, sizeof(int));
            carryCapacity = (int*)Memory.AllocZeroed((nuint)ring, sizeof(int));
            carryEpoch = (uint*)Memory.AllocZeroed((nuint)ring, sizeof(uint));
            carryPending = (ulong**)Memory.AllocZeroed((nuint)ring, (nuint)sizeof(ulong*));
            carryPendingCount = (int*)Memory.AllocZeroed((nuint)ring, sizeof(int));
            carryPendingCapacity = (int*)Memory.AllocZeroed((nuint)ring, sizeof(int));
            carryEpoch[world.Epoch % (uint)ring] = world.Epoch;
            EnsureActive(Math.Min(chunkCapacity, 256));
        }

        void EnsureActive(int count)
        {
            if (count <= activeCapacity)
            {
                return;
            }

            var grown = Math.Min(chunkCapacity, Math.Max(count, activeCapacity * 2));
            context->Active = (int*)Memory.Realloc(context->Active, (nuint)grown * sizeof(int));
            context->Recheck = (ulong*)Memory.Realloc(context->Recheck, (nuint)grown * sizeof(ulong));
            context->StatRows = (ulong*)Memory.Realloc(context->StatRows, (nuint)grown * sizeof(ulong));
            context->Touched = (ulong*)Memory.Realloc(context->Touched, (nuint)grown * sizeof(ulong));
            context->EdgeAny = (ulong*)Memory.Realloc(context->EdgeAny, (nuint)grown * sizeof(ulong));
            context->Edges = (ulong*)Memory.Realloc(context->Edges, (nuint)grown * (nuint)Math.Max(1, eventCount) * sizeof(ulong));
            context->Scratch = (int*)Memory.Realloc(context->Scratch, (nuint)grown * (nuint)scratchInts * sizeof(int));
            activeCapacity = grown;
        }

        void Structural()
        {
            if (stepping)
            {
                throw new InvalidOperationException("sv: spawn, kill and rewind happen between frames, after every writer is absorbed");
            }
        }

        public int Spawn(int template)
        {
            Structural();
            var slot = world.Acquire(template);
            if (!replaying)
            {
                log.Spawn(world.Frame, template, slot);
            }

            Arrive(slot);
            return slot;
        }

        void Arrive(int slot)
        {
            var chunk = Slot.Chunk(slot);
            RouteChunk(chunk);
            pendingRows[chunk] |= Slot.Bit(slot);
            ChunkSet.Mark(pendingBits, chunk);
        }

        public bool Kill(int slot)
        {
            Structural();
            if (!world.Release(slot))
            {
                return false;
            }

            stats.Kill(slot);
            if (!replaying)
            {
                log.Kill(world.Frame, slot);
            }

            RouteChunk(Slot.Chunk(slot));
            return true;
        }

        void RouteChunk(int chunk)
        {
            var mask = world.LiveMask(chunk);
            live[chunk] = mask;
            var template = world.TemplateOf(chunk);
            forwarding[chunk] = template >= 0 && default(TKernels).Forwards(template) ? mask : 0ul;
        }

        void RouteAll()
        {
            for (var chunk = 0; chunk < chunkCapacity; chunk++)
            {
                if (chunk < world.ChunkCount)
                {
                    RouteChunk(chunk);
                }
                else
                {
                    live[chunk] = 0ul;
                    forwarding[chunk] = 0ul;
                }
            }
        }

        public EffectWriter* Sender(int index)
        {
            if ((uint)index >= (uint)options.ExternalWriters)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "sv: external writers are 0 to ExternalWriters - 1");
            }

            return raw.Writer(index);
        }

        public bool Send(int target, int source, int kind, int channel, int amount, int hits = 1, Origin origin = Origin.Direct) =>
            raw.Writer(0)->Send(EffectKey.Pack(target, source, (byte)kind, (byte)channel, origin, 0), amount, hits);

        public void Modify(in StatOp op)
        {
            if (opCount == opCapacity)
            {
                opCapacity = Math.Max(64, opCapacity * 2);
                ops = (StatOp*)Memory.Realloc(ops, (nuint)opCapacity * (nuint)sizeof(StatOp));
            }

            ops[opCount] = op;
            opCount++;
        }

        public int Read(int slot, int lane) => world.Read(slot, lane);

        public bool Level(int slot, int raised) => world.Level(slot, raised);

        public void Step()
        {
            stepping = true;
            var frame = world.Frame;
            var phase = context;
            phase->World = world.View;
            SealPhase();
            if (!replaying)
            {
                log.Record(frame, (LoggedRecord*)canonical.Records, canonical.Count);
            }

            StatsPhase(frame);
            phase->World = world.View;
            var active = CollectPhase();
            DecidePhase(active);
            CommitPhase(active);
            EdgePhase(active);
            Publish(frame, active);
            Remember(active);
            if (options.DigestEvery > 0 && frame % options.DigestEvery == 0L)
            {
                LastDigest = DigestPhase();
            }

            var epoch = world.Epoch;
            world.EndFrame();
            if (world.Epoch != epoch)
            {
                stats.MarkEpoch(world.Epoch);
                Carry(world.Epoch);
            }

            stepping = false;
        }

        void SealPhase()
        {
            var route = new RouteTable(live, forwarding, chunkCapacity);
            context->Seal = canonical.Begin(raw, route);
            workers.Run(&Phases<TKernels>.SealCount, context, parts);
            Seal.Plan(context->Seal);
            workers.Run(&Phases<TKernels>.SealScatter, context, parts);
            workers.Run(&Phases<TKernels>.SealOrder, context, WorkRange.PartsFor(chunkCapacity, parts));
            Seal.Finish(context->Seal);
            workers.Run(&Phases<TKernels>.SealCompact, context, WorkRange.PartsFor(context->Seal.Totals[Seal.TotalActive], parts));
            canonical.End(context->Seal, raw);
            context->Records = canonical.Records;
            context->Start = canonical.Start;
        }

        void StatsPhase(long frame)
        {
            stats.Step(frame, new Span<StatOp>(ops, opCount));
            if (!replaying && opCount > 0)
            {
                log.AppendStatOps(frame, (byte*)ops, opCount);
            }

            opCount = 0;
        }

        int CollectPhase()
        {
            var statChunks = stats.ChangedChunks;
            var statChunkCount = stats.ChangedChunkCount;
            for (var at = 0; at < statChunkCount; at++)
            {
                statRowsByChunk[statChunks[at]] = stats.ChangedRows[at];
            }

            Collect.StatPairRanges(stats.ChangedPairs, stats.ChangedPairCount, pairFrom, pairTo);
            EnsureActive(Math.Min(chunkCapacity, canonical.ActiveCount + statChunkCount + CountBits(pendingBits, chunkWords)));
            var recheckByChunk = pendingRows;
            var activeCount = Collect.Touched(activeBits, chunkWords, canonical.Active, canonical.ActiveCount, statChunks, statChunkCount, pendingBits, context->Active);
            for (var index = 0; index < activeCount; index++)
            {
                var chunk = context->Active[index];
                context->Recheck[index] = recheckByChunk[chunk];
                context->StatRows[index] = statRowsByChunk[chunk];
                recheckByChunk[chunk] = 0ul;
            }

            context->ActiveCount = activeCount;
            context->StatPairs = stats.ChangedPairs;
            context->PairFrom = pairFrom;
            context->PairTo = pairTo;
            return activeCount;
        }

        static int CountBits(ulong* bits, int words)
        {
            var count = 0;
            for (var word = 0; word < words; word++)
            {
                count += Bits.PopCount(bits[word]);
            }

            return count;
        }

        void DecidePhase(int active)
        {
            var used = WorkRange.PartsFor(active, parts);
            while (true)
            {
                var mark = raw.Mark();
                workers.Run(&Phases<TKernels>.Decide, context, used);
                if (raw.EndLayer(mark) != LayerVerdict.Rerun)
                {
                    return;
                }
            }
        }

        void CommitPhase(int active)
        {
            var used = WorkRange.PartsFor(active, parts);
            for (var part = 0; part < used; part++)
            {
                WorkRange.Of(active, part, parts, out var from, out var to);
                world.Reserve(ref laneWriters[part], (to - from) * lanesWritten + 1);
            }

            workers.Run(&Phases<TKernels>.Commit, context, used);
            for (var part = 0; part < used; part++)
            {
                world.Absorb(ref laneWriters[part]);
            }
        }

        void EdgePhase(int active)
        {
            if (eventCount == 0)
            {
                return;
            }

            var used = WorkRange.PartsFor(active, parts);
            while (true)
            {
                var mark = raw.Mark();
                workers.Run(&Phases<TKernels>.EdgeEmits, context, used);
                if (raw.EndLayer(mark) != LayerVerdict.Rerun)
                {
                    return;
                }
            }
        }

        void Publish(long frame, int active)
        {
            eventsCount = 0;
            var view = world.View;
            var capacity = options.EventCapacity;
            for (var ordinal = 0; ordinal < eventCount; ordinal++)
            {
                var raised = default(TKernels).EventId(ordinal);
                var count = 0;
                for (var index = 0; index < active; index++)
                {
                    if (context->EdgeAny[index] == 0ul)
                    {
                        continue;
                    }

                    var chunk = context->Active[index];
                    var edges = context->Edges[(long)index * eventCount + ordinal];
                    while (edges != 0ul)
                    {
                        var row = Bits.Tzcnt(edges);
                        edges &= edges - 1ul;
                        var slot = Slot.Of(chunk, row);
                        if (count < capacity)
                        {
                            AddEvent(new HostEvent(raised, slot, Lanes.Level(view, slot, raised)));
                        }

                        count++;
                    }
                }

                denseCounts[ordinal] = count > capacity ? count - capacity : 0;
            }

            for (var at = 0; at < eventsCount; at++)
            {
                eventKeys[at] = new OutwardEvent(events[at].Slot, events[at].Event, events[at].Entered).Key;
            }

            EventOrder.Sort(eventKeys, eventsCount);
            if (replaying)
            {
                var before = published.Events(frame, out var beforeCount);
                Difference(frame, before, beforeCount, eventKeys, eventsCount);
            }

            published.Record(frame, eventKeys, eventsCount);
        }

        void AddEvent(in HostEvent raised)
        {
            if (eventsCount == eventsCapacity)
            {
                eventsCapacity = Math.Max(64, eventsCapacity * 2);
                events = (HostEvent*)Memory.Realloc(events, (nuint)eventsCapacity * (nuint)sizeof(HostEvent));
                eventKeys = (ulong*)Memory.Realloc(eventKeys, (nuint)eventsCapacity * sizeof(ulong));
            }

            events[eventsCount] = raised;
            eventsCount++;
        }

        void Difference(long frame, ulong* before, int beforeCount, ulong* after, int afterCount)
        {
            var first = 0;
            var second = 0;
            while (first < beforeCount || second < afterCount)
            {
                if (second == afterCount || (first < beforeCount && before[first] < after[second]))
                {
                    Change(new EventChange(frame, before[first], false));
                    first++;
                }
                else if (first == beforeCount || after[second] < before[first])
                {
                    Change(new EventChange(frame, after[second], true));
                    second++;
                }
                else
                {
                    first++;
                    second++;
                }
            }
        }

        void Change(in EventChange change)
        {
            if (changeCount == changeCapacity)
            {
                changeCapacity = Math.Max(64, changeCapacity * 2);
                changes = (EventChange*)Memory.Realloc(changes, (nuint)changeCapacity * (nuint)sizeof(EventChange));
            }

            changes[changeCount] = change;
            changeCount++;
        }

        void Remember(int active)
        {
            for (var index = 0; index < active; index++)
            {
                var chunk = context->Active[index];
                statRowsByChunk[chunk] = 0ul;
                pairFrom[chunk] = 0;
                pairTo[chunk] = 0;
                var touched = context->Touched[index];
                if (touched != 0ul)
                {
                    pendingRows[chunk] |= touched;
                    ChunkSet.Mark(pendingBits, chunk);
                }
            }
        }

        ulong DigestPhase()
        {
            context->World = world.View;
            context->ChunkCount = world.ChunkCount;
            var used = WorkRange.PartsFor(world.ChunkCount, parts);
            workers.Run(&Phases<TKernels>.Digest, context, used);
            var sum = 0ul;
            unchecked
            {
                for (var part = 0; part < used; part++)
                {
                    sum += context->DigestParts[part * Phases<TKernels>.DigestStride];
                }
            }

            return sum;
        }

        public ulong Digest() => world.Digest();

        void Carry(uint epoch)
        {
            var at = (int)(epoch % (uint)world.RingEpochs);
            carryEpoch[at] = epoch;
            raw.Flush();
            var core = raw.Core;
            var blocks = ArenaCore.TakenBlocks(core);
            var count = 0;
            for (var block = 0; block < blocks; block++)
            {
                count += core->Fill[block];
            }

            if (count > carryCapacity[at])
            {
                carryRecords[at] = (Effect*)Memory.Realloc(carryRecords[at], (nuint)count * (nuint)sizeof(Effect));
                carryCapacity[at] = count;
            }

            var written = 0;
            for (var block = 0; block < blocks; block++)
            {
                var fill = core->Fill[block];
                Memory.Copy(ArenaCore.Block(core, block), carryRecords[at] + written, (nuint)fill * (nuint)sizeof(Effect));
                written += fill;
            }

            carryCount[at] = written;
            var pending = CountBits(pendingBits, chunkWords);
            if (pending * 2 > carryPendingCapacity[at])
            {
                carryPending[at] = (ulong*)Memory.Realloc(carryPending[at], (nuint)(pending * 2) * sizeof(ulong));
                carryPendingCapacity[at] = pending * 2;
            }

            var held = 0;
            for (var word = 0; word < chunkWords; word++)
            {
                var set = pendingBits[word];
                while (set != 0ul)
                {
                    var chunk = (word << 6) + Bits.Tzcnt(set);
                    set &= set - 1ul;
                    carryPending[at][held] = (ulong)chunk;
                    carryPending[at][held + 1] = pendingRows[chunk];
                    held += 2;
                }
            }

            carryPendingCount[at] = held;
        }

        public bool Rewind(uint epoch)
        {
            Structural();
            if (!world.Rewind(epoch))
            {
                return false;
            }

            stats.Rewind(epoch);
            raw.Reset();
            opCount = 0;
            for (var word = 0; word < chunkWords; word++)
            {
                var set = pendingBits[word];
                pendingBits[word] = 0ul;
                while (set != 0ul)
                {
                    pendingRows[(word << 6) + Bits.Tzcnt(set)] = 0ul;
                    set &= set - 1ul;
                }
            }

            var at = (int)(epoch % (uint)world.RingEpochs);
            if (carryEpoch[at] == epoch)
            {
                var writer = raw.Writer(0);
                for (var record = 0; record < carryCount[at]; record++)
                {
                    var effect = carryRecords[at][record];
                    writer->Send(effect.Key, effect.Amount, effect.Hits);
                }

                for (var held = 0; held < carryPendingCount[at]; held += 2)
                {
                    var chunk = (int)carryPending[at][held];
                    pendingRows[chunk] = carryPending[at][held + 1];
                    ChunkSet.Mark(pendingBits, chunk);
                }
            }

            RouteAll();
            return true;
        }

        public long Late(long frame, LoggedRecord* lateCanonical, int count)
        {
            var oldest = world.Journal.BeginOf(world.OldestEpoch);
            if (frame < oldest)
            {
                if (options.Late == LatePolicy.Reject)
                {
                    return -1L;
                }

                frame = oldest;
            }

            if (frame >= world.Frame)
            {
                return -1L;
            }

            log.Insert(frame, lateCanonical, count);
            earliestLate = Math.Min(earliestLate, frame);
            return frame;
        }

        public bool Resolve()
        {
            Structural();
            if (earliestLate >= world.Frame)
            {
                earliestLate = long.MaxValue;
                return false;
            }

            var now = world.Frame;
            log.Commands(now, out var waitingCommands);
            var externalSent = 0L;
            for (var index = 0; index < options.ExternalWriters; index++)
            {
                externalSent += raw.Writer(index)->Sent;
            }

            if (waitingCommands > 0 || opCount > 0 || externalSent > 0)
            {
                throw new InvalidOperationException("sv: resolve late inputs right after Step, before this frame's spawns, kills, stat ops and sends");
            }

            var epoch = world.EpochAtOrBefore(earliestLate);
            earliestLate = long.MaxValue;
            if (epoch == 0u || !Rewind(epoch))
            {
                return false;
            }

            replaying = true;
            try
            {
                while (world.Frame < now)
                {
                    Inputs(world.Frame);
                    Step();
                }
            }
            finally
            {
                replaying = false;
            }

            return true;
        }

        void Inputs(long frame)
        {
            var commands = log.Commands(frame, out var commandCount);
            for (var at = 0; at < commandCount; at++)
            {
                var command = commands[at];
                if (command.Kind == CommandKind.Spawn)
                {
                    ReplayFaults += Spawn(command.Template) == command.Slot ? 0L : 1L;
                }
                else
                {
                    ReplayFaults += Kill(command.Slot) ? 0L : 1L;
                }
            }

            var statOps = (StatOp*)log.StatOps(frame, out var statOpCount);
            for (var at = 0; at < statOpCount; at++)
            {
                Modify(statOps[at]);
            }

            var count = log.RecordCount(frame);
            if (count > replayCapacity)
            {
                replayCapacity = Math.Max(count, replayCapacity * 2);
                replayRecords = (LoggedRecord*)Memory.Realloc(replayRecords, (nuint)replayCapacity * (nuint)sizeof(LoggedRecord));
            }

            var copied = log.CopyRecords(frame, replayRecords, replayCapacity);
            var writer = raw.Writer(0);
            for (var at = 0; at < copied; at++)
            {
                writer->Send(replayRecords[at].Key, replayRecords[at].Amount, replayRecords[at].Hits);
            }
        }

        public void Dispose()
        {
            if (context == null)
            {
                return;
            }

            workers.Dispose();
            for (var part = 0; part < parts; part++)
            {
                world.DisposeWriter(ref laneWriters[part]);
            }

            for (var at = 0; at < world.RingEpochs; at++)
            {
                Memory.Free(carryRecords[at]);
                Memory.Free(carryPending[at]);
            }

            Memory.Free(carryRecords);
            Memory.Free(carryCount);
            Memory.Free(carryCapacity);
            Memory.Free(carryEpoch);
            Memory.Free(carryPending);
            Memory.Free(carryPendingCount);
            Memory.Free(carryPendingCapacity);
            Memory.Free(context->Active);
            Memory.Free(context->Recheck);
            Memory.Free(context->StatRows);
            Memory.Free(context->Touched);
            Memory.Free(context->EdgeAny);
            Memory.Free(context->Edges);
            Memory.Free(context->Scratch);
            for (var part = 0; part < parts; part++)
            {
                Memory.Free(context->Buffers[part].Pointer);
            }

            Memory.Free(context->Buffers);
            Memory.FreeAligned(context->DigestParts);
            Memory.Free(context);
            Memory.Free(laneWriters);
            Memory.Free(live);
            Memory.Free(forwarding);
            Memory.Free(pendingRows);
            Memory.Free(statRowsByChunk);
            Memory.Free(pairFrom);
            Memory.Free(pairTo);
            Memory.Free(pendingBits);
            Memory.Free(activeBits);
            Memory.Free(denseCounts);
            Memory.Free(ops);
            Memory.Free(replayRecords);
            Memory.Free(events);
            Memory.Free(eventKeys);
            Memory.Free(changes);
            context = null;
            published.Dispose();
            log.Dispose();
            canonical.Dispose();
            raw.Dispose();
            stats.Dispose();
            world.Dispose();
        }
    }
}
