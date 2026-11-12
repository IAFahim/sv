namespace Sv
{
    public static unsafe class WorldDigest
    {
        public const int LivenessColumn = 0xFFFF;

        public static ulong OfChunk(in WorldView world, int chunk)
        {
            var meta = Lanes.WordsOf(world, chunk, world.Layout.MetaLane);
            var live = meta[ChunkMeta.Live];
            var template = ChunkMeta.TemplateOf(meta[ChunkMeta.Template]);
            if (live == 0ul || (uint)template >= (uint)world.TemplateCount)
            {
                return 0ul;
            }

            var first = chunk << Slot.RowBits;
            var sum = 0ul;
            unchecked
            {
                var rows = live;
                while (rows != 0ul)
                {
                    var row = Bits.Tzcnt(rows);
                    rows &= rows - 1ul;
                    sum += CellDigest.Key(LivenessColumn, first + row, (uint)template);
                }

                var defaults = Lanes.TemplateRow(world, template);
                for (var lane = 0; lane < world.Layout.ValueLanes; lane++)
                {
                    var current = Lanes.LaneAt(world, chunk, lane);
                    var fallback = defaults[lane];
                    var laneClass = world.Layout.Classes[lane];
                    if (current == fallback || laneClass == LaneClass.Retired)
                    {
                        continue;
                    }

                    var cells = Lanes.At(world, current);
                    var expected = Lanes.At(world, fallback);
                    if (laneClass == LaneClass.Event)
                    {
                        var differ = LaneMath.DifferWords((ulong*)cells, (ulong*)expected) & live;
                        while (differ != 0ul)
                        {
                            var row = Bits.Tzcnt(differ);
                            differ &= differ - 1ul;
                            sum += CellDigest.Key(lane, first + row, LaneMath.RowLevels((ulong*)cells, row));
                        }
                    }
                    else
                    {
                        var differ = Mask.Differ((int*)cells, (int*)expected) & live;
                        while (differ != 0ul)
                        {
                            var row = Bits.Tzcnt(differ);
                            differ &= differ - 1ul;
                            sum += CellDigest.Key(lane, first + row, ((uint*)cells)[row]);
                        }
                    }
                }
            }

            return sum;
        }

        public static ulong Of(in WorldView world, int chunkCount)
        {
            var sum = 0ul;
            unchecked
            {
                for (var chunk = 0; chunk < chunkCount; chunk++)
                {
                    sum += OfChunk(world, chunk);
                }
            }

            return sum;
        }
    }
}
