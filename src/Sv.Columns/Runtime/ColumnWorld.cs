using System;
using System.Collections.Generic;

namespace Sv.Columns
{
    public sealed unsafe class ColumnWorld : IDisposable
    {
        public const int Rows = Law.Rows;
        readonly CompiledPack pack;
        readonly PackTables* tables;
        readonly int laneCount;
        readonly int chunkInts;
        readonly int classLongs;
        readonly int chunkCount;
        readonly RecordWriter[] writers;
        readonly List<int>[] openChunks;
        readonly int parts;
        int* lanes;
        int* chunkTemplates;
        ulong* alive;
        SlotGate* gates;
        long** blocks;
        int* stamps;
        long* dropped;
        byte* scratch;
        int scratchBytes;
        int frame;
        int nextChunk;

        public ColumnWorld(CompiledPack pack, int capacity, int writerCount = 1, int resolveParts = 1, KernelChoice kernels = KernelChoice.Best)
        {
            if (capacity < 1 || capacity > Record.SlotLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "capacity is 1 to " + Record.SlotLimit + " entities");
            }

            this.pack = pack;
            tables = pack.Tables;
            laneCount = tables->LaneCount;
            chunkInts = laneCount * Rows;
            classLongs = tables->ClassCount * Rows;
            chunkCount = (capacity + Rows - 1) / Rows;
            Capacity = chunkCount * Rows;
            parts = Math.Max(1, resolveParts);
            Vectorized = kernels != KernelChoice.Scalar && Kernels.Supported;
            lanes = Native.Zeroed<int>((long)chunkCount * chunkInts);
            chunkTemplates = Native.Zeroed<int>(chunkCount);
            for (var c = 0; c < chunkCount; c++)
            {
                chunkTemplates[c] = -1;
            }

            alive = Native.Zeroed<ulong>(chunkCount);
            gates = Native.Zeroed<SlotGate>(Capacity);
            writers = new RecordWriter[Math.Max(1, writerCount)];
            for (var w = 0; w < writers.Length; w++)
            {
                writers[w] = new RecordWriter(this, 1024);
            }

            blocks = (long**)Native.Zeroed((long)writers.Length * chunkCount * sizeof(long*));
            stamps = Native.Zeroed<int>((long)writers.Length * chunkCount);
            dropped = Native.Zeroed<long>(writers.Length * 8L);
            scratchBytes = Scratch.BytesFor(tables);
            scratch = (byte*)Native.Zeroed((long)parts * scratchBytes);
            openChunks = new List<int>[tables->TemplateCount];
            for (var t = 0; t < openChunks.Length; t++)
            {
                openChunks[t] = new List<int>();
            }

            NoSourceFlags = tables->NoSourceFlags;
        }

        public CompiledPack Pack => pack;

        public int Capacity { get; }

        public int Chunks => chunkCount;

        public int Writers => writers.Length;

        public int ResolveParts => parts;

        public bool Vectorized { get; }

        public int Frame => frame;

        public int Count { get; private set; }

        internal uint NoSourceFlags { get; }

        public RecordWriter Writer(int index) => writers[index];

        public long Dropped
        {
            get
            {
                var sum = 0L;
                for (var w = 0; w < writers.Length; w++)
                {
                    sum += dropped[w * 8];
                }

                return sum;
            }
        }

        internal SlotGate GateOf(int slot) => (uint)slot < (uint)Capacity ? gates[slot] : default;

        int* ChunkLanes(int chunk) => lanes + (long)chunk * chunkInts;

        public int Spawn(int template)
        {
            if ((uint)template >= (uint)tables->TemplateCount)
            {
                throw new ArgumentOutOfRangeException(nameof(template));
            }

            var open = openChunks[template];
            while (open.Count > 0 && alive[open[open.Count - 1]] == ulong.MaxValue)
            {
                open.RemoveAt(open.Count - 1);
            }

            int chunk;
            if (open.Count > 0)
            {
                chunk = open[open.Count - 1];
            }
            else
            {
                while (nextChunk < chunkCount && chunkTemplates[nextChunk] >= 0)
                {
                    nextChunk++;
                }

                if (nextChunk == chunkCount)
                {
                    chunk = FindEmptyChunk();
                    if (chunk < 0)
                    {
                        throw new InvalidOperationException("the world is full: no chunk is free for template '" + pack.TemplateNames[template] + "'");
                    }
                }
                else
                {
                    chunk = nextChunk;
                }

                chunkTemplates[chunk] = template;
                open.Add(chunk);
            }

            var row = TrailingZeros(~alive[chunk]);
            alive[chunk] |= 1ul << row;
            var defaults = tables->Templates[template].Defaults;
            var chunkLanes = ChunkLanes(chunk);
            for (var l = 0; l < laneCount; l++)
            {
                chunkLanes[l * Rows + row] = defaults[l];
            }

            var slot = chunk * Rows + row;
            gates[slot] = Law.GateOf(tables, chunkLanes, row);
            Count++;
            return slot;
        }

        int FindEmptyChunk()
        {
            for (var c = 0; c < chunkCount; c++)
            {
                if (alive[c] == 0)
                {
                    if (chunkTemplates[c] >= 0)
                    {
                        openChunks[chunkTemplates[c]].Remove(c);
                    }

                    return c;
                }
            }

            return -1;
        }

        static int TrailingZeros(ulong value)
        {
            var count = 0;
            while ((value & 1ul) == 0)
            {
                value >>= 1;
                count++;
            }

            return count;
        }

        public void Despawn(int slot)
        {
            CheckAlive(slot);
            var chunk = slot / Rows;
            var row = slot % Rows;
            var wasFull = alive[chunk] == ulong.MaxValue;
            alive[chunk] &= ~(1ul << row);
            var chunkLanes = ChunkLanes(chunk);
            for (var l = 0; l < laneCount; l++)
            {
                chunkLanes[l * Rows + row] = 0;
            }

            gates[slot] = default;
            Count--;
            if (wasFull)
            {
                openChunks[chunkTemplates[chunk]].Add(chunk);
            }
        }

        public bool IsAlive(int slot) => (uint)slot < (uint)Capacity && ((alive[slot / Rows] >> (slot % Rows)) & 1ul) != 0;

        void CheckAlive(int slot)
        {
            if (!IsAlive(slot))
            {
                throw new ArgumentException("slot " + slot + " is not a live entity", nameof(slot));
            }
        }

        public int TemplateOf(int slot)
        {
            CheckAlive(slot);
            return chunkTemplates[slot / Rows];
        }

        public int Get(int slot, int lane)
        {
            CheckAlive(slot);
            if ((uint)lane >= (uint)laneCount)
            {
                throw new ArgumentOutOfRangeException(nameof(lane));
            }

            return ChunkLanes(slot / Rows)[lane * Rows + slot % Rows];
        }

        public void Set(int slot, int lane, int value)
        {
            CheckAlive(slot);
            if ((uint)lane >= (uint)(laneCount - 2))
            {
                throw new ArgumentOutOfRangeException(nameof(lane), "only pack lanes can be set");
            }

            var chunkLanes = ChunkLanes(slot / Rows);
            chunkLanes[lane * Rows + slot % Rows] = value;
            gates[slot] = Law.GateOf(tables, chunkLanes, slot % Rows);
        }

        public bool Event(int slot, int eventIndex) => ((uint)Get(slot, tables->EventsLane) >> eventIndex & 1u) != 0;

        public bool Entered(int slot, int eventIndex) => ((uint)Get(slot, tables->EnteredLane) >> eventIndex & 1u) != 0;

        public ulong ChunkAlive(int chunk) => alive[chunk];

        public ulong ChunkEntered(int chunk, int eventIndex)
        {
            var entered = ChunkLanes(chunk) + tables->EnteredLane * Rows;
            var mask = 0ul;
            for (var row = 0; row < Rows; row++)
            {
                mask |= (ulong)(((uint)entered[row] >> eventIndex) & 1u) << row;
            }

            return mask & alive[chunk];
        }

        public void Step()
        {
            BeginFrame();
            for (var w = 0; w < writers.Length; w++)
            {
                Reduce(w);
            }

            for (var p = 0; p < parts; p++)
            {
                Resolve(p);
            }

            EndFrame();
        }

        public void BeginFrame()
        {
            var total = 0L;
            for (var w = 0; w < writers.Length; w++)
            {
                total += writers[w].Count;
            }

            if (total > int.MaxValue)
            {
                throw new InvalidOperationException("a frame holds at most " + int.MaxValue + " records");
            }

            frame++;
        }

        public void Reduce(int writer)
        {
            var source = writers[writer];
            var records = source.Records;
            var count = source.Count;
            var writerBlocks = blocks + (long)writer * chunkCount;
            var writerStamps = stamps + (long)writer * chunkCount;
            var lost = 0L;
            var lastChunk = -1;
            long* block = null;
            for (var i = 0; i < count; i++)
            {
                var record = records[i];
                var target = Record.Target(record);
                var chunk = target / Rows;
                if (target >= Capacity || ((alive[chunk] >> (target % Rows)) & 1ul) == 0 || Record.Channel(record) >= tables->ChannelCount || Record.Kind(record) >= tables->KindCount)
                {
                    lost++;
                    continue;
                }

                if (chunk != lastChunk)
                {
                    block = BlockFor(writerBlocks, writerStamps, chunk);
                    lastChunk = chunk;
                }

                Law.ReduceRecord(tables, gates, record, block);
            }

            dropped[writer * 8] += lost;
            source.Clear();
        }

        long* BlockFor(long** writerBlocks, int* writerStamps, int chunk)
        {
            var block = writerBlocks[chunk];
            if (block == null)
            {
                block = Native.Zeroed<long>(classLongs);
                writerBlocks[chunk] = block;
            }
            else if (writerStamps[chunk] != frame)
            {
                Native.Clear(block, (long)classLongs * 8);
            }

            writerStamps[chunk] = frame;
            return block;
        }

        public void Resolve(int part)
        {
            var from = (int)((long)chunkCount * part / parts);
            var to = (int)((long)chunkCount * (part + 1) / parts);
            var area = new Scratch(scratch + (long)part * scratchBytes, tables);
            for (var chunk = from; chunk < to; chunk++)
            {
                var chunkAlive = alive[chunk];
                if (chunkAlive == 0)
                {
                    continue;
                }

                Native.Clear(area.Totals, (long)classLongs * 8);
                for (var w = 0; w < writers.Length; w++)
                {
                    if (stamps[(long)w * chunkCount + chunk] == frame)
                    {
                        var block = blocks[(long)w * chunkCount + chunk];
                        for (var i = 0; i < classLongs; i++)
                        {
                            area.Totals[i] += block[i];
                        }
                    }
                }

                var template = tables->Templates + chunkTemplates[chunk];
                var chunkLanes = ChunkLanes(chunk);
                var chunkGates = gates + (long)chunk * Rows;
                if (Vectorized)
                {
                    Kernels.ResolveChunk(tables, template, chunkLanes, chunkGates, chunkAlive, area);
                    continue;
                }

                Law.AddOps(tables, template, chunkLanes, chunkGates, chunkAlive, area.Totals, area.Preds);
                for (var row = 0; row < Rows; row++)
                {
                    if (((chunkAlive >> row) & 1ul) != 0)
                    {
                        Law.ResolveRow(tables, chunkLanes, area.Totals, area.Wide, area.Gain, row);
                        chunkGates[row] = Law.GateOf(tables, chunkLanes, row);
                    }
                }
            }
        }

        public void EndFrame()
        {
        }

        public ulong Digest()
        {
            var hash = 0xCBF29CE484222325ul;
            for (var chunk = 0; chunk < chunkCount; chunk++)
            {
                var chunkAlive = alive[chunk];
                if (chunkAlive == 0)
                {
                    continue;
                }

                var chunkLanes = ChunkLanes(chunk);
                for (var row = 0; row < Rows; row++)
                {
                    if (((chunkAlive >> row) & 1ul) == 0)
                    {
                        continue;
                    }

                    hash = Mix(hash, (ulong)(chunk * Rows + row));
                    hash = Mix(hash, (ulong)chunkTemplates[chunk]);
                    for (var l = 0; l < laneCount; l++)
                    {
                        hash = Mix(hash, (ulong)(uint)chunkLanes[l * Rows + row]);
                    }
                }
            }

            return hash;
        }

        static ulong Mix(ulong hash, ulong value)
        {
            hash ^= value;
            hash *= 0x100000001B3ul;
            hash ^= hash >> 29;
            return hash;
        }

        public void Dispose()
        {
            if (lanes == null)
            {
                return;
            }

            foreach (var writer in writers)
            {
                writer.Dispose();
            }

            for (long i = 0; i < (long)writers.Length * chunkCount; i++)
            {
                Native.Free(blocks[i]);
            }

            Native.Free(blocks);
            Native.Free(stamps);
            Native.Free(dropped);
            Native.Free(scratch);
            Native.Free(gates);
            Native.Free(alive);
            Native.Free(chunkTemplates);
            Native.Free(lanes);
            lanes = null;
        }
    }

    public enum KernelChoice
    {
        Best = 0,
        Scalar = 1,
    }

    internal unsafe readonly struct Scratch
    {
        public readonly long* Totals;
        public readonly long* Wide;
        public readonly long* Gain;
        public readonly long* Masks;
        public readonly ulong* Admit;
        public readonly ulong* OpFlags;
        public readonly bool* Preds;

        public Scratch(byte* at, PackTables* pack)
        {
            Totals = (long*)at;
            Wide = Totals + pack->ClassCount * Law.Rows;
            Gain = Wide + pack->LaneCount * Law.Rows;
            Masks = Gain + PackLimits.Channels * Law.Rows;
            Admit = (ulong*)(Masks + MaxPreds(pack) * Law.Rows);
            OpFlags = Admit + Law.Rows;
            Preds = (bool*)(OpFlags + Law.Rows);
        }

        static int MaxPreds(PackTables* pack)
        {
            var max = 0;
            for (var t = 0; t < pack->TemplateCount; t++)
            {
                max = Math.Max(max, pack->Templates[t].PredCount);
            }

            return max;
        }

        public static int BytesFor(PackTables* pack) =>
            ((pack->ClassCount + pack->LaneCount + PackLimits.Channels + MaxPreds(pack) + 2) * Law.Rows * 8 + MaxPreds(pack) + 63) & ~63;
    }
}
