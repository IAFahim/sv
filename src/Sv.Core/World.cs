using System;

namespace Sv
{
    public sealed unsafe class World : IDisposable
    {
        public const int DefaultFramesPerEpoch = 2;

        readonly LanePool pool;
        readonly Journal journal;
        readonly int chunkCapacity;
        readonly int framesPerEpoch;
        WorldView view;
        LaneWriter main;
        uint zeroLane;
        int templateCapacity;
        int* cursor;
        int unassignedCursor;
        int chunkCount;
        int patternCount;
        int patternCapacity;
        ulong* patternKeys;
        uint* patternLanes;
        long frame;

        World(ReadOnlySpan<LaneClass> classes, int channels, int entityCapacity, StoragePolicy policy, int ringEpochs, int framesPerEpoch)
        {
            WorldLayout.Validate(classes, channels);
            if (entityCapacity < 1 || entityCapacity > Slot.Capacity - 1)
            {
                throw new ArgumentOutOfRangeException(nameof(entityCapacity), "sv: a world holds 1 to 2^22 - 1 entities");
            }

            if (framesPerEpoch < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(framesPerEpoch), "sv: an epoch holds at least one frame");
            }

            this.framesPerEpoch = framesPerEpoch;
            chunkCapacity = (entityCapacity + Slot.Rows - 1) >> Slot.RowBits;
            pool = new LanePool();
            journal = new Journal(ringEpochs);

            var valueLanes = classes.Length;
            var layout = default(WorldLayout);
            layout.Classes = (LaneClass*)Memory.Alloc((nuint)(valueLanes + 1));
            for (var lane = 0; lane < valueLanes; lane++)
            {
                layout.Classes[lane] = classes[lane];
            }

            layout.ValueLanes = valueLanes;
            layout.MetaLane = valueLanes;
            layout.DeathLane = valueLanes + 1;
            layout.LanesPerChunk = valueLanes + WorldLayout.MetaLanes;
            layout.Channels = channels;

            view.Layout = layout;
            view.LanesPerChunk = layout.LanesPerChunk;
            view.Policy = policy;
            view.Segments = pool.Segments;
            view.Epoch = journal.Current;

            var tableEntries = (long)chunkCapacity * layout.LanesPerChunk;
            view.LaneOf = (uint*)Memory.Alloc((nuint)tableEntries, sizeof(uint));
            templateCapacity = 4;
            view.TemplateLanes = (uint*)Memory.Alloc((nuint)(templateCapacity * Math.Max(1, valueLanes)), sizeof(uint));
            cursor = (int*)Memory.AllocZeroed((nuint)templateCapacity, sizeof(int));
            patternCapacity = 16;
            patternKeys = (ulong*)Memory.Alloc((nuint)patternCapacity, sizeof(ulong));
            patternLanes = (uint*)Memory.Alloc((nuint)patternCapacity, sizeof(uint));

            zeroLane = pool.Take();
            Memory.Clear(Lanes.At(view, zeroLane), (nuint)Lanes.Bytes);
            *Lanes.EpochOf(view, zeroLane) = 0u;
            patternKeys[0] = 0ul;
            patternLanes[0] = zeroLane;
            patternCount = 1;

            if (policy == StoragePolicy.Dense)
            {
                var perSegment = Lanes.UsableLanes / layout.LanesPerChunk;
                var chunk = 0;
                while (chunk < chunkCapacity)
                {
                    var here = Math.Min(perSegment, chunkCapacity - chunk);
                    var segment = pool.AddSegment(here * layout.LanesPerChunk);
                    var first = ((uint)segment << Lanes.SegmentShift) + (uint)Lanes.HeaderLanes;
                    for (var local = 0; local < here; local++, chunk++)
                    {
                        var row = view.LaneOf + (long)chunk * layout.LanesPerChunk;
                        var block = first + (uint)(local * layout.LanesPerChunk);
                        for (var lane = 0; lane < layout.LanesPerChunk; lane++)
                        {
                            row[lane] = block + (uint)lane;
                        }
                    }
                }
            }
            else
            {
                for (var entry = 0L; entry < tableEntries; entry++)
                {
                    view.LaneOf[entry] = zeroLane;
                }
            }

            main = pool.CreateWriter(2 * layout.LanesPerChunk + 4);
        }

        public static World Create(
            ReadOnlySpan<LaneClass> classes,
            int channels,
            int entityCapacity,
            StoragePolicy policy = StoragePolicy.Shared,
            int ringEpochs = Journal.DefaultRing,
            int framesPerEpoch = DefaultFramesPerEpoch) =>
            new World(classes, channels, entityCapacity, policy, ringEpochs, framesPerEpoch);

        public WorldView View => view;

        public WorldLayout Layout => view.Layout;

        public StoragePolicy Policy => view.Policy;

        public int ChunkCapacity => chunkCapacity;

        public int ChunkCount => chunkCount;

        public int TemplateCount => view.TemplateCount;

        public int FramesPerEpoch => framesPerEpoch;

        public int RingEpochs => journal.Ring;

        public long Frame => frame;

        public uint Epoch => journal.Current;

        public uint OldestEpoch => journal.Oldest;

        public Journal Journal => journal;

        public LanePool Pool => pool;

        public long TableBytes => (long)chunkCapacity * view.LanesPerChunk * sizeof(uint);

        public int AddTemplate(ReadOnlySpan<uint> defaults)
        {
            var valueLanes = view.Layout.ValueLanes;
            if (defaults.Length != valueLanes)
            {
                throw new ArgumentException("sv: a template gives one default per pack lane", nameof(defaults));
            }

            if (view.TemplateCount == templateCapacity)
            {
                var grown = templateCapacity * 2;
                view.TemplateLanes = (uint*)Memory.Realloc(view.TemplateLanes, (nuint)(grown * Math.Max(1, valueLanes)) * sizeof(uint));
                cursor = (int*)Memory.Realloc(cursor, (nuint)grown * sizeof(int));
                templateCapacity = grown;
            }

            var template = view.TemplateCount;
            var row = view.TemplateLanes + (long)template * valueLanes;
            for (var lane = 0; lane < valueLanes; lane++)
            {
                var laneClass = view.Layout.Classes[lane];
                row[lane] = laneClass == LaneClass.Retired ? zeroLane : PatternLane(laneClass == LaneClass.Event, defaults[lane]);
            }

            cursor[template] = 0;
            view.TemplateCount = template + 1;
            return template;
        }

        uint PatternLane(bool levels, uint bits)
        {
            var key = (levels ? 1ul << 32 : 0ul) | bits;
            var empty = bits == 0u;
            for (var at = 0; at < patternCount; at++)
            {
                if (patternKeys[at] == key || (empty && patternKeys[at] == 0ul))
                {
                    return patternLanes[at];
                }
            }

            var lane = pool.Take();
            var bytes = Lanes.At(view, lane);
            if (levels)
            {
                for (var word = 0; word < Lanes.Words; word++)
                {
                    ((ulong*)bytes)[word] = ((bits >> word) & 1u) != 0u ? ulong.MaxValue : 0ul;
                }
            }
            else
            {
                LaneMath.Fill((int*)bytes, (int)bits);
            }

            *Lanes.EpochOf(view, lane) = 0u;
            if (patternCount == patternCapacity)
            {
                patternCapacity *= 2;
                patternKeys = (ulong*)Memory.Realloc(patternKeys, (nuint)patternCapacity * sizeof(ulong));
                patternLanes = (uint*)Memory.Realloc(patternLanes, (nuint)patternCapacity * sizeof(uint));
            }

            patternKeys[patternCount] = key;
            patternLanes[patternCount] = lane;
            patternCount++;
            return lane;
        }

        public int TemplateOf(int chunk) =>
            (uint)chunk < (uint)chunkCapacity ? ChunkMeta.TemplateOf(Lanes.WordsOf(view, chunk, view.Layout.MetaLane)[ChunkMeta.Template]) : -1;

        public ulong LiveMask(int chunk) =>
            (uint)chunk < (uint)chunkCapacity ? Lanes.WordsOf(view, chunk, view.Layout.MetaLane)[ChunkMeta.Live] : 0ul;

        public ulong DyingMask(int chunk) =>
            (uint)chunk < (uint)chunkCapacity ? Lanes.WordsOf(view, chunk, view.Layout.MetaLane)[ChunkMeta.Dying] : 0ul;

        public SlotState StateOf(int slot)
        {
            var chunk = Slot.Chunk(slot);
            if ((uint)chunk >= (uint)chunkCapacity)
            {
                return SlotState.Free;
            }

            var meta = Lanes.WordsOf(view, chunk, view.Layout.MetaLane);
            var bit = Slot.Bit(slot);
            return (meta[ChunkMeta.Live] & bit) != 0ul ? SlotState.Live : (meta[ChunkMeta.Dying] & bit) != 0ul ? SlotState.Dying : SlotState.Free;
        }

        public uint DeathEpochOf(int slot) =>
            StateOf(slot) == SlotState.Dying ? (uint)Lanes.Ints(view, Slot.Chunk(slot), view.Layout.DeathLane)[Slot.Row(slot)] : 0u;

        public ulong Available(int chunk)
        {
            var meta = Lanes.WordsOf(view, chunk, view.Layout.MetaLane);
            var dying = meta[ChunkMeta.Dying];
            var held = 0ul;
            if (dying != 0ul)
            {
                var death = (uint*)Lanes.Ints(view, chunk, view.Layout.DeathLane);
                var ring = (uint)journal.Ring;
                var epoch = journal.Current;
                var rows = dying;
                while (rows != 0ul)
                {
                    var row = Bits.Tzcnt(rows);
                    rows &= rows - 1ul;
                    held |= death[row] + ring > epoch ? 1ul << row : 0ul;
                }
            }

            return ~(meta[ChunkMeta.Live] | held | Slot.Reserved(chunk));
        }

        public int Acquire(int template)
        {
            if ((uint)template >= (uint)view.TemplateCount)
            {
                throw new ArgumentOutOfRangeException(nameof(template), "sv: unknown template");
            }

            pool.Reserve(ref main, 2 * view.LanesPerChunk + 4);
            var chunk = -1;
            var free = 0ul;
            for (var at = cursor[template]; at < chunkCount; at++)
            {
                if (TemplateOf(at) == template)
                {
                    free = Available(at);
                    if (free != 0ul)
                    {
                        chunk = at;
                        break;
                    }
                }
            }

            if (chunk < 0)
            {
                chunk = Unassigned();
                Assign(chunk, template);
                free = Available(chunk);
            }

            cursor[template] = chunk;
            var row = Bits.Tzcnt(free);
            Initialize(chunk, row, template);
            var meta = (ulong*)Lanes.Writable(view, ref main, chunk, view.Layout.MetaLane);
            meta[ChunkMeta.Live] |= 1ul << row;
            meta[ChunkMeta.Dying] &= ~(1ul << row);
            journal.Absorb(ref main);
            return Slot.Of(chunk, row);
        }

        int Unassigned()
        {
            for (var at = unassignedCursor; at < chunkCount; at++)
            {
                if (TemplateOf(at) < 0)
                {
                    unassignedCursor = at + 1;
                    return at;
                }
            }

            if (chunkCount == chunkCapacity)
            {
                throw new InvalidOperationException("sv: the world is full");
            }

            var chunk = chunkCount;
            chunkCount++;
            unassignedCursor = chunkCount;
            return chunk;
        }

        void Assign(int chunk, int template)
        {
            var row = Lanes.TemplateRow(view, template);
            for (var lane = 0; lane < view.Layout.ValueLanes; lane++)
            {
                Lanes.Share(view, ref main, chunk, lane, row[lane]);
            }

            var meta = (ulong*)Lanes.Writable(view, ref main, chunk, view.Layout.MetaLane);
            meta[ChunkMeta.Template] = ChunkMeta.WordOf(template);
            meta[ChunkMeta.Live] = 0ul;
            meta[ChunkMeta.Dying] = 0ul;
        }

        void Initialize(int chunk, int row, int template)
        {
            var defaults = Lanes.TemplateRow(view, template);
            for (var lane = 0; lane < view.Layout.ValueLanes; lane++)
            {
                var current = Lanes.LaneAt(view, chunk, lane);
                var fallback = defaults[lane];
                if (current == fallback)
                {
                    continue;
                }

                var laneClass = view.Layout.Classes[lane];
                if (laneClass == LaneClass.Event)
                {
                    var wanted = LaneMath.RowLevels((ulong*)Lanes.At(view, fallback), row);
                    if (LaneMath.RowLevels((ulong*)Lanes.At(view, current), row) != wanted)
                    {
                        LaneMath.SetRowLevels((ulong*)Lanes.Writable(view, ref main, chunk, lane), row, wanted);
                    }
                }
                else if (laneClass != LaneClass.Retired)
                {
                    var wanted = ((int*)Lanes.At(view, fallback))[row];
                    if (((int*)Lanes.At(view, current))[row] != wanted)
                    {
                        ((int*)Lanes.Writable(view, ref main, chunk, lane))[row] = wanted;
                    }
                }
            }
        }

        public bool Release(int slot)
        {
            if (StateOf(slot) != SlotState.Live)
            {
                return false;
            }

            pool.Reserve(ref main, 4);
            var chunk = Slot.Chunk(slot);
            var row = Slot.Row(slot);
            var meta = (ulong*)Lanes.Writable(view, ref main, chunk, view.Layout.MetaLane);
            meta[ChunkMeta.Live] &= ~(1ul << row);
            meta[ChunkMeta.Dying] |= 1ul << row;
            ((uint*)Lanes.Writable(view, ref main, chunk, view.Layout.DeathLane))[row] = journal.Current;
            journal.Absorb(ref main);
            journal.NoteKill(journal.Current, chunk);
            return true;
        }

        public int Read(int slot, int lane) => Lanes.Read(view, slot, lane);

        public float ReadFloat(int slot, int lane) => Lanes.ReadFloat(view, slot, lane);

        public bool Level(int slot, int raised) => Lanes.Level(view, slot, raised);

        public bool Write(int slot, int lane, int bits)
        {
            var laneClass = view.Layout.ClassOf(lane);
            if (StateOf(slot) != SlotState.Live || (laneClass != LaneClass.Stat && laneClass != LaneClass.Intrinsic))
            {
                return false;
            }

            pool.Reserve(ref main, 1);
            ((int*)Lanes.Writable(view, ref main, Slot.Chunk(slot), lane))[Slot.Row(slot)] = bits;
            journal.Absorb(ref main);
            return true;
        }

        public bool WriteLevel(int slot, int raised, bool level)
        {
            var lane = EventId.Lane(raised);
            if (StateOf(slot) != SlotState.Live || view.Layout.ClassOf(lane) != LaneClass.Event)
            {
                return false;
            }

            pool.Reserve(ref main, 1);
            var words = (ulong*)Lanes.Writable(view, ref main, Slot.Chunk(slot), lane);
            var bit = Slot.Bit(slot);
            var word = EventId.Word(raised);
            words[word] = level ? words[word] | bit : words[word] & ~bit;
            journal.Absorb(ref main);
            return true;
        }

        public LaneWriter CreateWriter(int capacity) => pool.CreateWriter(capacity);

        public void Reserve(ref LaneWriter writer, int lanes) => pool.Reserve(ref writer, lanes);

        public void Absorb(ref LaneWriter writer) => journal.Absorb(ref writer);

        public void DisposeWriter(ref LaneWriter writer)
        {
            journal.Absorb(ref writer);
            pool.DisposeWriter(ref writer);
        }

        public void EndFrame()
        {
            frame++;
            view.Frame = frame;
            if (frame % framesPerEpoch == 0L)
            {
                Mark();
            }
        }

        void Mark()
        {
            journal.Absorb(ref main);
            var expired = journal.Expiring(out var chunks);
            for (var at = 0; at < expired; at++)
            {
                var template = TemplateOf(chunks[at]);
                if (template >= 0)
                {
                    cursor[template] = Math.Min(cursor[template], chunks[at]);
                }
            }

            journal.Advance(frame, view, pool);
            view.Epoch = journal.Current;
        }

        public uint EpochAtOrBefore(long atFrame) => journal.AtOrBefore(atFrame);

        public bool Rewind(uint epoch)
        {
            if (!journal.Holds(epoch))
            {
                return false;
            }

            journal.Absorb(ref main);
            journal.Rewind(epoch, view, pool);
            view.Epoch = journal.Current;
            frame = journal.BeginOf(epoch);
            view.Frame = frame;
            for (var template = 0; template < view.TemplateCount; template++)
            {
                cursor[template] = 0;
            }

            unassignedCursor = 0;
            return true;
        }

        public ulong Digest() => WorldDigest.Of(view, chunkCount);

        public ulong DigestOfChunk(int chunk) => (uint)chunk < (uint)chunkCount ? WorldDigest.OfChunk(view, chunk) : 0ul;

        public int SaveChunk(int chunk, byte* image, int capacity) =>
            (uint)chunk < (uint)chunkCapacity ? ChunkImage.Save(view, chunk, image, capacity) : 0;

        public ChunkImageFault LoadChunk(int chunk, byte* image, int length)
        {
            if ((uint)chunk >= (uint)chunkCapacity)
            {
                return ChunkImageFault.ChunkOutOfRange;
            }

            pool.Reserve(ref main, 2 * view.LanesPerChunk + 4);
            var fault = ChunkImage.Load(view, ref main, chunk, image, length);
            journal.Absorb(ref main);
            if (fault != ChunkImageFault.None)
            {
                return fault;
            }

            if (chunk >= chunkCount)
            {
                chunkCount = chunk + 1;
            }

            var template = TemplateOf(chunk);
            if (template < 0)
            {
                unassignedCursor = Math.Min(unassignedCursor, chunk);
                return ChunkImageFault.None;
            }

            cursor[template] = Math.Min(cursor[template], chunk);
            var dying = DyingMask(chunk);
            var death = (uint*)Lanes.Ints(view, chunk, view.Layout.DeathLane);
            while (dying != 0ul)
            {
                var row = Bits.Tzcnt(dying);
                dying &= dying - 1ul;
                journal.NoteKill(death[row], chunk);
            }

            return ChunkImageFault.None;
        }

        public void Dispose()
        {
            if (view.LaneOf == null)
            {
                return;
            }

            pool.DisposeWriter(ref main);
            Memory.Free(view.LaneOf);
            Memory.Free(view.TemplateLanes);
            Memory.Free(view.Layout.Classes);
            Memory.Free(cursor);
            Memory.Free(patternKeys);
            Memory.Free(patternLanes);
            view = default;
            cursor = null;
            patternKeys = null;
            patternLanes = null;
            journal.Dispose();
            pool.Dispose();
        }
    }
}
