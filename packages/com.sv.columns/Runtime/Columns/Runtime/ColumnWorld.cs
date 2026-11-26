using System.Runtime.CompilerServices;
using System;
using System.Collections.Generic;

namespace Sv.Columns
{
    public sealed unsafe partial class ColumnWorld : IDisposable
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
        readonly bool[] listed;
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
        bool stepping;

        public ColumnWorld(CompiledPack pack, int capacity, int writerCount = 1, int resolveParts = 1, KernelChoice kernels = KernelChoice.Best, int history = 0)
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
            KernelPath = kernels == KernelChoice.Best && Kernels.Supported ? KernelPath.Vector256 : kernels != KernelChoice.Scalar && Simd.Supported ? KernelPath.Vector : KernelPath.Scalar;
            Vectorized = KernelPath == KernelPath.Vector256;
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
            listed = new bool[chunkCount];
            openChunks = new List<int>[tables->TemplateCount];
            for (var t = 0; t < openChunks.Length; t++)
            {
                openChunks[t] = new List<int>();
            }

            NoSourceFlags = tables->NoSourceFlags;
            OpenHistory(history);
        }

        public CompiledPack Pack => pack;

        public int Capacity { get; }

        public int Chunks => chunkCount;

        public int Writers => writers.Length;

        public int ResolveParts => parts;

        public bool Vectorized { get; }

        public KernelPath KernelPath { get; }

        public int Frame => frame;

        public int Count { get; private set; }

        internal uint NoSourceFlags { get; }

        public RecordWriter Writer(int index) => writers[index];

        public long Dropped
        {
            get
            {
                Live();
                var sum = 0L;
                for (var w = 0; w < writers.Length; w++)
                {
                    sum += dropped[w * 8];
                }

                return sum;
            }
        }

        internal SlotGate GateOf(int slot) => (uint)slot < (uint)Capacity ? gates[slot] : default;

        void Live()
        {
            if (lanes == null)
            {
                throw new ObjectDisposedException(nameof(ColumnWorld));
            }

            if (pack.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(CompiledPack), "the pack this world runs was disposed before the world");
            }
        }

        void Editable()
        {
            Live();
            if (stepping)
            {
                throw new InvalidOperationException("a frame is running: spawn, despawn and set only before BeginFrame or after EndFrame");
            }
        }

        int* ChunkLanes(int chunk) => lanes + (long)chunk * chunkInts;

        public int Spawn(int template)
        {
            Editable();
            if ((uint)template >= (uint)tables->TemplateCount)
            {
                throw new ArgumentOutOfRangeException(nameof(template));
            }

            BeforeEdit();
            var open = openChunks[template];
            while (open.Count > 0 && alive[open[open.Count - 1]] == ulong.MaxValue)
            {
                listed[open[open.Count - 1]] = false;
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
                listed[chunk] = true;
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
            Note(ColumnAction.Spawned(template, slot));
            return slot;
        }

        int FindEmptyChunk()
        {
            for (var c = 0; c < chunkCount; c++)
            {
                if (alive[c] == 0)
                {
                    if (listed[c])
                    {
                        openChunks[chunkTemplates[c]].Remove(c);
                        listed[c] = false;
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
            Editable();
            CheckAlive(slot);
            BeforeEdit();
            var chunk = slot / Rows;
            var row = slot % Rows;
            alive[chunk] &= ~(1ul << row);
            var chunkLanes = ChunkLanes(chunk);
            for (var l = 0; l < laneCount; l++)
            {
                chunkLanes[l * Rows + row] = 0;
            }

            gates[slot] = default;
            Count--;
            if (!listed[chunk])
            {
                openChunks[chunkTemplates[chunk]].Add(chunk);
                listed[chunk] = true;
            }

            Note(ColumnAction.Despawn(slot));
        }

        public bool IsAlive(int slot)
        {
            Live();
            return (uint)slot < (uint)Capacity && ((alive[slot / Rows] >> (slot % Rows)) & 1ul) != 0;
        }

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
            Editable();
            CheckAlive(slot);
            if ((uint)lane >= (uint)(laneCount - 2))
            {
                throw new ArgumentOutOfRangeException(nameof(lane), "only pack lanes can be set");
            }

            BeforeEdit();
            var chunkLanes = ChunkLanes(slot / Rows);
            chunkLanes[lane * Rows + slot % Rows] = value;
            gates[slot] = Law.GateOf(tables, chunkLanes, slot % Rows);
            Note(ColumnAction.Set(slot, lane, value));
        }

        public bool Event(int slot, int eventIndex) => ((uint)Get(slot, tables->EventsLane) >> eventIndex & 1u) != 0;

        public bool Entered(int slot, int eventIndex) => ((uint)Get(slot, tables->EnteredLane) >> eventIndex & 1u) != 0;

        public ulong ChunkAlive(int chunk)
        {
            Live();
            return (uint)chunk < (uint)chunkCount ? alive[chunk] : throw new ArgumentOutOfRangeException(nameof(chunk));
        }

        public ulong ChunkEntered(int chunk, int eventIndex)
        {
            ChunkAlive(chunk);
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
            Live();
            LoadLoggedInputs();
            var total = 0L;
            for (var w = 0; w < writers.Length; w++)
            {
                total += writers[w].Count;
            }

            if (total > int.MaxValue)
            {
                throw new InvalidOperationException("a frame holds at most " + int.MaxValue + " records");
            }

            OpenRecordLog((int)total);
            frame++;
            stepping = true;
        }

        public void Reduce(int writer)
        {
            Live();
            var source = writers[writer];
            var lost = Fold(writer, source.Records, source.Count);
            if (resimulating)
            {
                var records = LoggedRecords(writer, out var count);
                lost += Fold(writer, records, count);
            }

            var refused = source.TakeRefused();
            dropped[writer * 8] += lost + refused;
            LogRecords(writer, source.Records, source.Count, refused);
            source.Clear();
        }

        long Fold(int writer, ulong* records, int count)
        {
            var writerBlocks = blocks + (long)writer * chunkCount;
            var writerStamps = stamps + (long)writer * chunkCount;
            var lost = 0L;
            var capacity = (uint)Capacity;
            var allAdmit = tables->AllAdmit;
            var factors = tables->Factors;
            var i = 0;
            while (i < count)
            {
                var target = (int)(records[i] >> Record.TargetShift);
                if ((uint)target >= capacity)
                {
                    lost++;
                    i++;
                    continue;
                }

                var chunk = target >> 6;
                if (!Replays(chunk))
                {
                    i = CountLostInChunk(records, i, count, chunk, alive[chunk], allAdmit, ref lost);
                    continue;
                }

                var block = BlockFor(writerBlocks, writerStamps, chunk);
                long missed;
                i = Vectorized
                    ? Kernels.AddInChunk(records, i, count, chunk, block, gates + (long)chunk * Rows, alive[chunk], allAdmit, factors, tables->ScaleCount > 0, out missed)
                    : AddInChunk(records, i, count, chunk, block, gates + (long)chunk * Rows, alive[chunk], allAdmit, factors, out missed);
                lost += missed;
            }

            return lost;
        }

#if NET
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
        internal static int AddInChunk(ulong* records, int at, int count, int chunk, long* block, SlotGate* chunkGates, ulong aliveWord, ulong allAdmit, long* factors, out long missed)
        {
            var lost = 0L;
            for (; at < count; at++)
            {
                var record = records[at];
                var target = (int)(record >> Record.TargetShift);
                if ((target >> 6) != chunk)
                {
                    break;
                }

                var row = target & (Rows - 1);
                var channel = (int)(record >> Record.ChannelShift) & 15;
                var shift = (channel << 2) | ((int)(record >> Record.KindShift) & 3);
                if (((aliveWord >> row) & (allAdmit >> shift) & 1ul) == 0)
                {
                    lost++;
                    continue;
                }

                var gate = chunkGates + row;
                var admitted = -(long)((gate->Admit >> shift) & 1ul);
                var amount = (long)((record & Record.AmountMask) ^ Record.AmountSign) - (long)Record.AmountSign;
                var term = amount * factors[(channel << 8) | (int)(gate->Flags & (uint)(record >> Record.FlagsShift) & 255u)] & admitted;
                block[((channel << 1) | (int)((ulong)term >> 63)) * Rows + row] += term;
            }

            missed = lost;
            return at;
        }

        static int CountLostInChunk(ulong* records, int at, int count, int chunk, ulong aliveWord, ulong allAdmit, ref long lost)
        {
            for (; at < count; at++)
            {
                var record = records[at];
                var target = (int)(record >> Record.TargetShift);
                if ((target >> 6) != chunk)
                {
                    break;
                }

                var shift = (((int)(record >> Record.ChannelShift) & 15) << 2) | ((int)(record >> Record.KindShift) & 3);
                if (((aliveWord >> (target & (Rows - 1))) & (allAdmit >> shift) & 1ul) == 0)
                {
                    lost++;
                }
            }

            return at;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
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
            Live();
            var from = (int)((long)chunkCount * part / parts);
            var to = (int)((long)chunkCount * (part + 1) / parts);
            var area = new Scratch(scratch + (long)part * scratchBytes, tables);
            for (var chunk = from; chunk < to; chunk++)
            {
                var chunkAlive = alive[chunk];
                if (chunkAlive == 0 || !Replays(chunk))
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
                    KeepChunk(chunk, chunkLanes);
                    continue;
                }

                if (KernelPath == KernelPath.Vector)
                {
                    Simd.AddOps(tables, template, chunkLanes, chunkGates, chunkAlive, area);
                }
                else
                {
                    Law.AddOps(tables, template, chunkLanes, chunkGates, chunkAlive, area.Totals, area.Preds);
                }

                for (var row = 0; row < Rows; row++)
                {
                    if (((chunkAlive >> row) & 1ul) != 0)
                    {
                        Law.ResolveRow(tables, chunkLanes, area.Totals, area.Wide, area.Gain, row);
                        chunkGates[row] = Law.GateOf(tables, chunkLanes, row);
                    }
                }

                KeepChunk(chunk, chunkLanes);
            }
        }

        public void EndFrame()
        {
            Live();
            stepping = false;
            KeepFrame();
        }

        public ulong Digest()
        {
            Live();
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

            FreeFrames();
            foreach (var writer in writers)
            {
                writer.Dispose();
            }

            FreeHistory();

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
            blocks = null;
            stamps = null;
            dropped = null;
            scratch = null;
            gates = null;
            alive = null;
            chunkTemplates = null;
            lanes = null;
        }
    }

    public enum KernelChoice
    {
        Best = 0,
        Scalar = 1,
        Vector = 2,
    }

    public enum KernelPath
    {
        Scalar = 0,
        Vector = 1,
        Vector256 = 2,
    }

    internal unsafe readonly struct Scratch
    {
        public readonly long* Totals;
        public readonly long* Wide;
        public readonly long* Gain;
        public readonly long* Masks;
        public readonly ulong* Admit;
        public readonly ulong* OpFlags;
        public readonly long* Work0;
        public readonly long* Work1;
        public readonly bool* Preds;

        public Scratch(byte* at, PackTables* pack)
        {
            Totals = (long*)at;
            Wide = Totals + pack->ClassCount * Law.Rows;
            Gain = Wide + pack->LaneCount * Law.Rows;
            Masks = Gain + PackLimits.Channels * Law.Rows;
            Admit = (ulong*)(Masks + MaxPreds(pack) * Law.Rows);
            OpFlags = Admit + Law.Rows;
            Work0 = (long*)(OpFlags + Law.Rows);
            Work1 = Work0 + Law.Rows;
            Preds = (bool*)(Work1 + Law.Rows);
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
            ((pack->ClassCount + pack->LaneCount + PackLimits.Channels + MaxPreds(pack) + 4) * Law.Rows * 8 + MaxPreds(pack) + 63) & ~63;
    }
}
