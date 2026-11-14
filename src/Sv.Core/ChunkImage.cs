namespace Sv
{
    public enum ChunkImageFault : byte
    {
        None = 0,
        ChunkOutOfRange = 1,
        CapacityTooSmall = 2,
        TooShort = 3,
        BadMagic = 4,
        TooManyLanes = 5,
        UnknownTemplate = 6,
        BadLength = 7,
        BadRows = 8,
    }

    public static unsafe class ChunkImage
    {
        public const uint Magic = 0x49435653u;
        public const int HeaderBytes = 32;

        public static int MapWords(int valueLanes) => (valueLanes + 63) >> 6;

        public static int MaxBytes(in WorldLayout layout) =>
            HeaderBytes + (MapWords(layout.ValueLanes) << 3) + layout.ValueLanes * Lanes.Bytes + Slot.Rows * sizeof(uint);

        public static int Save(in WorldView world, int chunk, byte* image, int capacity)
        {
            var layout = world.Layout;
            if (image == null || capacity < MaxBytes(layout))
            {
                return 0;
            }

            var meta = Lanes.WordsOf(world, chunk, layout.MetaLane);
            var template = ChunkMeta.TemplateOf(meta[ChunkMeta.Template]);
            var assigned = (uint)template < (uint)world.TemplateCount;
            var live = assigned ? meta[ChunkMeta.Live] : 0ul;
            var dying = assigned ? meta[ChunkMeta.Dying] : 0ul;
            var mapWords = MapWords(layout.ValueLanes);
            *(uint*)image = Magic;
            *(int*)(image + 4) = layout.ValueLanes;
            *(int*)(image + 8) = assigned ? template : -1;
            *(int*)(image + 12) = mapWords;
            *(ulong*)(image + 16) = live;
            *(ulong*)(image + 24) = dying;
            var map = (ulong*)(image + HeaderBytes);
            for (var word = 0; word < mapWords; word++)
            {
                map[word] = 0ul;
            }

            var at = image + HeaderBytes + (mapWords << 3);
            if (!assigned)
            {
                return (int)(at - image);
            }

            var free = ~(live | dying);
            var normal = stackalloc int[Lanes.Rows];
            var defaults = Lanes.TemplateRow(world, template);
            for (var lane = 0; lane < layout.ValueLanes; lane++)
            {
                var laneClass = layout.Classes[lane];
                var current = Lanes.LaneAt(world, chunk, lane);
                var fallback = defaults[lane];
                if (laneClass == LaneClass.Retired || current == fallback)
                {
                    continue;
                }

                var cells = Lanes.At(world, current);
                var expected = Lanes.At(world, fallback);
                bool differs;
                if (laneClass == LaneClass.Event)
                {
                    LaneMath.BlendWords(free, (ulong*)expected, (ulong*)cells, (ulong*)normal);
                    differs = LaneMath.DifferWords((ulong*)normal, (ulong*)expected) != 0ul;
                }
                else
                {
                    LaneMath.Select(free, (int*)expected, (int*)cells, normal);
                    differs = Mask.Differ(normal, (int*)expected) != 0ul;
                }

                if (differs)
                {
                    map[lane >> 6] |= 1ul << (lane & 63);
                    LaneMath.Copy(normal, at);
                    at += Lanes.Bytes;
                }
            }

            var death = (uint*)Lanes.Ints(world, chunk, layout.DeathLane);
            var rows = dying;
            while (rows != 0ul)
            {
                var row = Bits.Tzcnt(rows);
                rows &= rows - 1ul;
                *(uint*)at = death[row];
                at += sizeof(uint);
            }

            return (int)(at - image);
        }

        public static ChunkImageFault Load(in WorldView world, ref LaneWriter writer, int chunk, byte* image, int length)
        {
            var layout = world.Layout;
            if (image == null || length < HeaderBytes)
            {
                return ChunkImageFault.TooShort;
            }

            if (*(uint*)image != Magic)
            {
                return ChunkImageFault.BadMagic;
            }

            var imageLanes = *(int*)(image + 4);
            var template = *(int*)(image + 8);
            var mapWords = *(int*)(image + 12);
            var live = *(ulong*)(image + 16);
            var dying = *(ulong*)(image + 24);
            if (imageLanes < 0 || imageLanes > layout.ValueLanes || mapWords != MapWords(imageLanes))
            {
                return ChunkImageFault.TooManyLanes;
            }

            if (template < -1 || template >= world.TemplateCount)
            {
                return ChunkImageFault.UnknownTemplate;
            }

            if ((live & dying) != 0ul || ((live | dying) & Slot.Reserved(chunk)) != 0ul || (template < 0 && (live | dying) != 0ul))
            {
                return ChunkImageFault.BadRows;
            }

            if (length < HeaderBytes + (mapWords << 3))
            {
                return ChunkImageFault.TooShort;
            }

            var map = (ulong*)(image + HeaderBytes);
            var stored = 0;
            for (var word = 0; word < mapWords; word++)
            {
                stored += Bits.PopCount(map[word]);
            }

            var expectedLength = (long)HeaderBytes + (mapWords << 3) + (long)stored * Lanes.Bytes + (long)Bits.PopCount(dying) * sizeof(uint);
            if (length != expectedLength)
            {
                return ChunkImageFault.BadLength;
            }

            var meta = (ulong*)Lanes.Writable(world, ref writer, chunk, layout.MetaLane);
            if (template < 0)
            {
                meta[ChunkMeta.Template] = ChunkMeta.Unassigned;
                meta[ChunkMeta.Live] = 0ul;
                meta[ChunkMeta.Dying] = 0ul;
                return ChunkImageFault.None;
            }

            var at = image + HeaderBytes + (mapWords << 3);
            var defaults = Lanes.TemplateRow(world, template);
            for (var lane = 0; lane < layout.ValueLanes; lane++)
            {
                var present = lane < imageLanes && ((map[lane >> 6] >> (lane & 63)) & 1ul) != 0ul;
                if (present && layout.Classes[lane] != LaneClass.Retired)
                {
                    LaneMath.Copy(at, Lanes.Overwritable(world, ref writer, chunk, lane));
                }
                else
                {
                    Lanes.Share(world, ref writer, chunk, lane, defaults[lane]);
                }

                at += present ? Lanes.Bytes : 0;
            }

            meta[ChunkMeta.Template] = ChunkMeta.WordOf(template);
            meta[ChunkMeta.Live] = live;
            meta[ChunkMeta.Dying] = dying;
            var death = (uint*)Lanes.Writable(world, ref writer, chunk, layout.DeathLane);
            for (var row = 0; row < Lanes.Rows; row++)
            {
                death[row] = 0u;
            }

            var rows = dying;
            while (rows != 0ul)
            {
                var row = Bits.Tzcnt(rows);
                rows &= rows - 1ul;
                death[row] = *(uint*)at;
                at += sizeof(uint);
            }

            return ChunkImageFault.None;
        }
    }
}
