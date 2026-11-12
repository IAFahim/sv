namespace Sv.Laws.Tests;

public static unsafe class TestWorlds
{
    public static readonly LaneClass[] Classes =
    {
        LaneClass.Stat, LaneClass.Intrinsic, LaneClass.Intrinsic, LaneClass.Event, LaneClass.Retired, LaneClass.Intrinsic,
    };

    public static readonly uint[] HeroDefaults = { 1000u, 0u, 7u, 0b101u, 0u, unchecked((uint)-3) };

    public static readonly uint[] CrateDefaults = { 1000u, 50u, 0u, 0u, 0u, 0u };

    public static readonly int[] NumberLanes = { 0, 1, 2, 5 };

    public const int EventLane = 3;

    public const int Channels = 4;

    public static World Create(StoragePolicy policy, int entities = 512, int ring = Journal.DefaultRing, int framesPerEpoch = World.DefaultFramesPerEpoch)
    {
        var world = World.Create(Classes, Channels, entities, policy, ring, framesPerEpoch);
        world.AddTemplate(HeroDefaults);
        world.AddTemplate(CrateDefaults);
        return world;
    }

    public static int[] Snapshot(World world)
    {
        var view = world.View;
        var cells = new int[world.ChunkCapacity * view.LanesPerChunk * Lanes.Rows];
        var at = 0;
        for (var chunk = 0; chunk < world.ChunkCapacity; chunk++)
        {
            for (var lane = 0; lane < view.LanesPerChunk; lane++)
            {
                var source = Lanes.Ints(view, chunk, lane);
                for (var row = 0; row < Lanes.Rows; row++)
                {
                    cells[at++] = source[row];
                }
            }
        }

        return cells;
    }

    public static List<int> LiveSlots(World world)
    {
        var live = new List<int>();
        for (var slot = 0; slot < world.ChunkCapacity * Slot.Rows; slot++)
        {
            if (world.StateOf(slot) == SlotState.Live)
            {
                live.Add(slot);
            }
        }

        return live;
    }

    public static int ValueFor(Gen gen) => gen.Chance(30) ? Gen.EdgeInts[gen.Below(Gen.EdgeInts.Length)] : gen.Between(-500, 500);

    public static void Mutate(World world, Gen gen, List<int> live, int operations)
    {
        for (var op = 0; op < operations; op++)
        {
            var roll = gen.Below(100);
            if (roll < 15 || live.Count == 0)
            {
                live.Add(world.Acquire(gen.Below(world.TemplateCount)));
            }
            else if (roll < 22)
            {
                var index = gen.Below(live.Count);
                Assert.True(world.Release(live[index]), $"{gen}: release of a live slot failed");
                live.RemoveAt(index);
            }
            else if (roll < 85)
            {
                var slot = live[gen.Below(live.Count)];
                Assert.True(world.Write(slot, NumberLanes[gen.Below(NumberLanes.Length)], ValueFor(gen)), $"{gen}: write failed");
            }
            else if (roll < 95)
            {
                var slot = live[gen.Below(live.Count)];
                Assert.True(world.WriteLevel(slot, EventId.Of(EventLane, gen.Below(EventId.PerLane)), gen.Chance(50)), $"{gen}: level write failed");
            }
            else
            {
                var slot = live[gen.Below(live.Count)];
                var lane = NumberLanes[gen.Below(NumberLanes.Length)];
                var template = world.TemplateOf(Slot.Chunk(slot));
                var defaults = template == 0 ? HeroDefaults : CrateDefaults;
                world.Write(slot, lane, (int)defaults[lane]);
            }
        }
    }

    public static byte[] Image(World world, int chunk)
    {
        var buffer = new byte[ChunkImage.MaxBytes(world.Layout)];
        fixed (byte* image = buffer)
        {
            var length = world.SaveChunk(chunk, image, buffer.Length);
            return buffer.AsSpan(0, length).ToArray();
        }
    }

    public static ChunkImageFault Load(World world, int chunk, byte[] image)
    {
        fixed (byte* bytes = image)
        {
            return world.LoadChunk(chunk, bytes, image.Length);
        }
    }

    public static ulong ReferenceDigest(World world)
    {
        var view = world.View;
        var sum = 0ul;
        unchecked
        {
            for (var chunk = 0; chunk < world.ChunkCount; chunk++)
            {
                var template = world.TemplateOf(chunk);
                if (template < 0)
                {
                    continue;
                }

                var defaults = template == 0 ? HeroDefaults : CrateDefaults;
                for (var row = 0; row < Slot.Rows; row++)
                {
                    var slot = Slot.Of(chunk, row);
                    if (world.StateOf(slot) != SlotState.Live)
                    {
                        continue;
                    }

                    sum += CellDigest.Key(WorldDigest.LivenessColumn, slot, (uint)template);
                    for (var lane = 0; lane < Classes.Length; lane++)
                    {
                        if (Classes[lane] == LaneClass.Retired)
                        {
                            continue;
                        }

                        if (Classes[lane] == LaneClass.Event)
                        {
                            var levels = LaneMath.RowLevels(Lanes.WordsOf(view, chunk, lane), row);
                            if (levels != defaults[lane])
                            {
                                sum += CellDigest.Key(lane, slot, levels);
                            }
                        }
                        else
                        {
                            var bits = (uint)world.Read(slot, lane);
                            if (bits != defaults[lane])
                            {
                                sum += CellDigest.Key(lane, slot, bits);
                            }
                        }
                    }
                }
            }
        }

        return sum;
    }
}
