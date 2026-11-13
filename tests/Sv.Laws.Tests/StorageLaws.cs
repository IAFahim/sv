namespace Sv.Laws.Tests;

public sealed unsafe class StorageLaws
{
    public static IEnumerable<object[]> PoliciesAndSeeds() =>
        from policy in new[] { StoragePolicy.Shared, StoragePolicy.Dense }
        from seed in new[] { 1ul, 2ul, 3ul, 17ul }
        select new object[] { policy, seed };

    [Theory]
    [MemberData(nameof(PoliciesAndSeeds))]
    public void AddressFunctionReadsBackEveryWriteAtItsSlotAndValue(StoragePolicy policy, ulong seed)
    {
        var gen = new Gen(seed);
        using var world = TestWorlds.Create(policy);
        var model = new Dictionary<(int Slot, int Lane), int>();
        var live = new List<int>();
        for (var op = 0; op < 400; op++)
        {
            if (live.Count == 0 || gen.Chance(10))
            {
                var template = gen.Below(2);
                var slot = world.Acquire(template);
                live.Add(slot);
                var defaults = template == 0 ? TestWorlds.HeroDefaults : TestWorlds.CrateDefaults;
                foreach (var lane in TestWorlds.NumberLanes)
                {
                    model[(slot, lane)] = (int)defaults[lane];
                }
            }
            else
            {
                var slot = live[gen.Below(live.Count)];
                var lane = TestWorlds.NumberLanes[gen.Below(TestWorlds.NumberLanes.Length)];
                var value = TestWorlds.ValueFor(gen);
                world.Write(slot, lane, value);
                model[(slot, lane)] = value;
            }

            if (gen.Chance(5))
            {
                world.EndFrame();
            }
        }

        var view = world.View;
        foreach (var ((slot, lane), value) in model)
        {
            Assert.True(value == world.Read(slot, lane), $"{gen} {policy}: slot {slot} lane {lane}");
            Assert.Equal(value, Lanes.Ints(view, Slot.Chunk(slot), lane)[Slot.Row(slot)]);
            Assert.Equal(view.LaneOf[Slot.Chunk(slot) * view.LanesPerChunk + lane], Lanes.LaneAt(view, Slot.Chunk(slot), lane));
        }
    }

    [Theory]
    [MemberData(nameof(PoliciesAndSeeds))]
    public void SourceChunkTableReadsEqualWorldReadsForEverySlotAndLane(StoragePolicy policy, ulong seed)
    {
        var gen = new Gen(seed);
        using var world = TestWorlds.Create(policy);
        var live = new List<int>();
        var table = (SourceChunk*)Memory.AllocZeroed((nuint)world.ChunkCapacity, (nuint)sizeof(SourceChunk));
        try
        {
            for (var round = 0; round < 6; round++)
            {
                TestWorlds.Mutate(world, gen, live, 120);
                world.EndFrame();
                var direct = world.View;
                var tabled = world.View;
                SourceChunks.Build(tabled, table, 0, world.ChunkCount);
                tabled.Sources = table;
                var capacity = world.ChunkCapacity * Slot.Rows;
                for (var slot = -2; slot < capacity + 2; slot++)
                {
                    Assert.True(SourceChunks.Present(direct, capacity, slot) == SourceChunks.Present(tabled, capacity, slot), $"{gen} {policy}: presence of slot {slot}");
                    for (var lane = 0; lane < world.Layout.ValueLanes; lane++)
                    {
                        Assert.True(SourceChunks.Read(direct, capacity, slot, lane, -77) == SourceChunks.Read(tabled, capacity, slot, lane, -77), $"{gen} {policy}: slot {slot} lane {lane}");
                    }
                }
            }
        }
        finally
        {
            Memory.Free(table);
        }
    }

    [Fact]
    public void DenseTableIsTheIdentityAndEveryChunkIsOneContiguousBlock()
    {
        using var world = TestWorlds.Create(StoragePolicy.Dense);
        var view = world.View;
        var before = new uint[world.ChunkCapacity * view.LanesPerChunk];
        for (var chunk = 0; chunk < world.ChunkCapacity; chunk++)
        {
            var first = Lanes.LaneAt(view, chunk, 0);
            for (var lane = 0; lane < view.LanesPerChunk; lane++)
            {
                Assert.Equal(first + (uint)lane, Lanes.LaneAt(view, chunk, lane));
                Assert.True(Lanes.BytesOf(view, chunk, 0) + lane * Lanes.Bytes == Lanes.BytesOf(view, chunk, lane));
                before[chunk * view.LanesPerChunk + lane] = Lanes.LaneAt(view, chunk, lane);
            }
        }

        var gen = new Gen(5);
        TestWorlds.Mutate(world, gen, new List<int>(), 300);
        world.EndFrame();
        world.EndFrame();
        TestWorlds.Mutate(world, gen, TestWorlds.LiveSlots(world), 300);
        view = world.View;
        for (var index = 0; index < before.Length; index++)
        {
            Assert.Equal(before[index], view.LaneOf[index]);
        }
    }

    [Fact]
    public void SharedPolicyPointsAFreshChunkAtTheTemplateLanes()
    {
        using var world = TestWorlds.Create(StoragePolicy.Shared);
        var slot = world.Acquire(0);
        var view = world.View;
        var defaults = Lanes.TemplateRow(view, 0);
        for (var lane = 0; lane < TestWorlds.Classes.Length; lane++)
        {
            Assert.Equal(defaults[lane], Lanes.LaneAt(view, Slot.Chunk(slot), lane));
        }
    }

    [Fact]
    public void SharedPolicyFirstWriteCopiesOnlyTheWrittenLaneAndLeavesTheTemplateLaneUntouched()
    {
        using var world = TestWorlds.Create(StoragePolicy.Shared);
        var first = world.Acquire(0);
        var other = world.Acquire(0);
        var view = world.View;
        var templateLane = Lanes.TemplateRow(view, 0)[1];
        Assert.True(world.Write(first, 1, 99));
        view = world.View;
        Assert.NotEqual(templateLane, Lanes.LaneAt(view, Slot.Chunk(first), 1));
        Assert.Equal(Lanes.TemplateRow(view, 0)[2], Lanes.LaneAt(view, Slot.Chunk(first), 2));
        Assert.Equal(99, world.Read(first, 1));
        Assert.Equal(0, world.Read(other, 1));
        var template = (int*)Lanes.At(view, templateLane);
        for (var row = 0; row < Lanes.Rows; row++)
        {
            Assert.Equal(0, template[row]);
        }
    }

    [Fact]
    public void TemplatesWithEqualDefaultsShareOnePatternLane()
    {
        using var world = TestWorlds.Create(StoragePolicy.Shared);
        var view = world.View;
        var hero = Lanes.TemplateRow(view, 0);
        var crate = Lanes.TemplateRow(view, 1);
        Assert.Equal(hero[0], crate[0]);
        Assert.Equal(hero[1], crate[2]);
        Assert.Equal(hero[4], crate[4]);
        Assert.NotEqual(hero[2], crate[2]);
        Assert.NotEqual(hero[3], crate[3]);
    }

    [Theory]
    [InlineData(StoragePolicy.Shared)]
    [InlineData(StoragePolicy.Dense)]
    public void FirstWriteToALaneInAnEpochJournalsOnceAndLaterWritesArePlain(StoragePolicy policy)
    {
        using var world = TestWorlds.Create(policy);
        var slot = world.Acquire(0);
        world.EndFrame();
        world.EndFrame();
        var epoch = world.Epoch;
        Assert.Equal(0, world.Journal.Count(epoch));
        world.Write(slot, 1, 1);
        Assert.Equal(1, world.Journal.Count(epoch));
        world.Write(slot, 1, 2);
        world.Write(slot, 1, 3);
        Assert.Equal(1, world.Journal.Count(epoch));
        world.EndFrame();
        world.EndFrame();
        Assert.Equal(epoch + 1, world.Epoch);
        world.Write(slot, 1, 4);
        Assert.Equal(1, world.Journal.Count(world.Epoch));
    }

    [Theory]
    [MemberData(nameof(PoliciesAndSeeds))]
    public void ChunkImageRoundTripGivesTheSameImageAndTheSameCells(StoragePolicy policy, ulong seed)
    {
        var gen = new Gen(seed);
        using var source = TestWorlds.Create(policy);
        var live = new List<int>();
        for (var frame = 0; frame < 12; frame++)
        {
            TestWorlds.Mutate(source, gen, live, 40);
            source.EndFrame();
        }

        using var target = TestWorlds.Create(policy);
        TestWorlds.Mutate(target, new Gen(seed + 1000), new List<int>(), 200);
        for (var chunk = 0; chunk < source.ChunkCapacity; chunk++)
        {
            var image = TestWorlds.Image(source, chunk);
            Assert.Equal(ChunkImageFault.None, TestWorlds.Load(target, chunk, image));
            Assert.Equal(image, TestWorlds.Image(target, chunk));
            Assert.Equal(source.LiveMask(chunk), target.LiveMask(chunk));
            Assert.Equal(source.DyingMask(chunk), target.DyingMask(chunk));
            for (var row = 0; row < Slot.Rows; row++)
            {
                var slot = Slot.Of(chunk, row);
                Assert.Equal(source.StateOf(slot), target.StateOf(slot));
                Assert.Equal(source.DeathEpochOf(slot), target.DeathEpochOf(slot));
                if (source.StateOf(slot) == SlotState.Free)
                {
                    continue;
                }

                foreach (var lane in TestWorlds.NumberLanes)
                {
                    Assert.True(source.Read(slot, lane) == target.Read(slot, lane), $"{gen} {policy}: slot {slot} lane {lane}");
                }

                for (var word = 0; word < EventId.PerLane; word++)
                {
                    var raised = EventId.Of(TestWorlds.EventLane, word);
                    Assert.Equal(source.Level(slot, raised), target.Level(slot, raised));
                }
            }
        }

        Assert.Equal(source.Digest(), target.Digest());
    }

    [Theory]
    [MemberData(nameof(Seeds.Few), MemberType = typeof(Seeds))]
    public void ChunkImageAndDigestAreTheSameUnderBothPoliciesAndAnyPoolHistory(ulong seed)
    {
        var digests = new List<ulong>();
        var images = new List<byte[][]>();
        foreach (var policy in new[] { StoragePolicy.Shared, StoragePolicy.Dense })
        {
            foreach (var churn in new[] { false, true })
            {
                var gen = new Gen(seed);
                using var world = TestWorlds.Create(policy);
                var live = new List<int>();
                for (var frame = 0; frame < 10; frame++)
                {
                    TestWorlds.Mutate(world, gen, live, 30);
                    if (churn)
                    {
                        Churn(world, live);
                    }

                    world.EndFrame();
                }

                digests.Add(world.Digest());
                images.Add(Enumerable.Range(0, world.ChunkCapacity).Select(chunk => TestWorlds.Image(world, chunk)).ToArray());
            }
        }

        Assert.All(digests, digest => Assert.Equal(digests[0], digest));
        for (var variant = 1; variant < images.Count; variant++)
        {
            for (var chunk = 0; chunk < images[0].Length; chunk++)
            {
                Assert.True(images[0][chunk].SequenceEqual(images[variant][chunk]), $"seed {seed}: variant {variant} chunk {chunk} image differs");
            }
        }
    }

    static void Churn(World world, List<int> live)
    {
        foreach (var slot in live)
        {
            foreach (var lane in TestWorlds.NumberLanes)
            {
                var kept = world.Read(slot, lane);
                world.Write(slot, lane, kept ^ 0x5A5A5A5A);
                world.Write(slot, lane, kept);
            }
        }
    }

    [Theory]
    [InlineData(StoragePolicy.Shared)]
    [InlineData(StoragePolicy.Dense)]
    public void FreeRowsReadAsDefaultsInTheChunkImage(StoragePolicy policy)
    {
        using var world = TestWorlds.Create(policy);
        var slot = world.Acquire(0);
        world.Write(slot, 1, 5);
        var clean = TestWorlds.Image(world, 0);
        var dirty = (byte[])clean.Clone();
        var freeRow = 10;
        var firstStoredLane = ChunkImage.HeaderBytes + (ChunkImage.MapWords(TestWorlds.Classes.Length) << 3);
        BitConverter.GetBytes(1234).CopyTo(dirty, firstStoredLane + freeRow * sizeof(int));
        Assert.Equal(ChunkImageFault.None, TestWorlds.Load(world, 0, dirty));
        Assert.Equal(clean, TestWorlds.Image(world, 0));
    }

    [Fact]
    public void LoadRejectsBrokenImagesWithAFaultAndLeavesTheChunkAlone()
    {
        using var world = TestWorlds.Create(StoragePolicy.Shared);
        var slot = world.Acquire(0);
        world.Write(slot, 1, 5);
        world.Release(world.Acquire(0));
        var good = TestWorlds.Image(world, 0);
        var digest = world.Digest();

        var badMagic = (byte[])good.Clone();
        badMagic[0] ^= 1;
        var tooShort = good.AsSpan(0, ChunkImage.HeaderBytes - 1).ToArray();
        var badLength = good.Concat(new byte[] { 0 }).ToArray();
        var unknownTemplate = (byte[])good.Clone();
        BitConverter.GetBytes(7).CopyTo(unknownTemplate, 8);
        var liveAndDying = (byte[])good.Clone();
        BitConverter.GetBytes(1ul).CopyTo(liveAndDying, 16);
        BitConverter.GetBytes(1ul).CopyTo(liveAndDying, 24);
        var tooManyLanes = (byte[])good.Clone();
        BitConverter.GetBytes(TestWorlds.Classes.Length + 1).CopyTo(tooManyLanes, 4);

        Assert.Equal(ChunkImageFault.BadMagic, TestWorlds.Load(world, 0, badMagic));
        Assert.Equal(ChunkImageFault.TooShort, TestWorlds.Load(world, 0, tooShort));
        Assert.Equal(ChunkImageFault.BadLength, TestWorlds.Load(world, 0, badLength));
        Assert.Equal(ChunkImageFault.UnknownTemplate, TestWorlds.Load(world, 0, unknownTemplate));
        Assert.Equal(ChunkImageFault.BadRows, TestWorlds.Load(world, 0, liveAndDying));
        Assert.Equal(ChunkImageFault.TooManyLanes, TestWorlds.Load(world, 0, tooManyLanes));
        Assert.Equal(ChunkImageFault.ChunkOutOfRange, TestWorlds.Load(world, world.ChunkCapacity, good));
        Assert.Equal(digest, world.Digest());
        Assert.Equal(good, TestWorlds.Image(world, 0));
    }

    [Theory]
    [MemberData(nameof(PoliciesAndSeeds))]
    public void DigestIsTheSumOfOneKeyPerLiveCellThatDiffersFromItsDefault(StoragePolicy policy, ulong seed)
    {
        var gen = new Gen(seed);
        using var world = TestWorlds.Create(policy);
        var live = new List<int>();
        for (var frame = 0; frame < 8; frame++)
        {
            TestWorlds.Mutate(world, gen, live, 50);
            world.EndFrame();
            Assert.True(TestWorlds.ReferenceDigest(world) == world.Digest(), $"{gen} {policy}: frame {frame}");
        }
    }

    [Fact]
    public void DigestKeysEachValueByItsSlotAndItsLane()
    {
        using var world = TestWorlds.Create(StoragePolicy.Shared);
        var first = world.Acquire(0);
        var second = world.Acquire(0);
        var empty = world.Digest();
        world.Write(first, 1, 42);
        var onFirst = world.Digest();
        world.Write(first, 1, 0);
        world.Write(second, 1, 42);
        var onSecond = world.Digest();
        world.Write(second, 1, 0);
        world.Write(first, 2, 42);
        var onOtherLane = world.Digest();
        world.Write(first, 2, 7);
        Assert.Equal(empty, world.Digest());
        Assert.Equal(unchecked(empty + CellDigest.Key(1, first, 42u)), onFirst);
        Assert.Equal(unchecked(empty + CellDigest.Key(1, second, 42u)), onSecond);
        Assert.Equal(unchecked(empty + CellDigest.Key(2, first, 42u)), onOtherLane);
        Assert.Equal(3, new HashSet<ulong> { onFirst, onSecond, onOtherLane }.Count);
    }

    [Fact]
    public void DyingAndFreeCellsAreNotHashed()
    {
        using var world = TestWorlds.Create(StoragePolicy.Shared);
        var kept = world.Acquire(0);
        var gone = world.Acquire(0);
        world.Write(kept, 1, 3);
        var withoutGone = world.Digest();
        world.Write(gone, 1, 1000);
        world.Release(gone);
        Assert.Equal(unchecked(withoutGone - CellDigest.Key(WorldDigest.LivenessColumn, gone, 0u)), world.Digest());
    }

    [Fact]
    public void ReservedSlotIsTheNoSourceKeyAndTheLastSlotOfTheSpace()
    {
        Assert.Equal((1 << 22) - 1, Slot.None);
        Assert.Equal(Slot.None, EffectKey.NoSource);
        Assert.Equal(Slot.None, Slot.Of(Slot.MaxChunks - 1, Slot.Rows - 1));
        Assert.Equal(1ul << 63, Slot.Reserved(Slot.MaxChunks - 1));
        for (var chunk = 0; chunk < Slot.MaxChunks - 1; chunk += 997)
        {
            Assert.Equal(0ul, Slot.Reserved(chunk));
        }
    }

    [Fact]
    public void WorldCapacityStopsOneBelowTheSlotSpace()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => World.Create(new[] { LaneClass.Intrinsic }, 1, Slot.Capacity));
        using var world = World.Create(new[] { LaneClass.Intrinsic }, 1, Slot.Capacity - 1);
        Assert.Equal(Slot.MaxChunks, world.ChunkCapacity);
        world.AddTemplate(new[] { 0u });
        Assert.Equal(~(1ul << 63), world.Available(Slot.MaxChunks - 1));
    }

    [Fact]
    public void AcquireNeverHandsOutTheReservedSlotWhenTheLastChunkIsOtherwiseFull()
    {
        using var world = World.Create(new[] { LaneClass.Intrinsic }, 1, Slot.Capacity - 1);
        world.AddTemplate(new[] { 0u });
        var last = Slot.MaxChunks - 1;
        var image = new byte[ChunkImage.HeaderBytes + 8];
        BitConverter.GetBytes(ChunkImage.Magic).CopyTo(image, 0);
        BitConverter.GetBytes(1).CopyTo(image, 4);
        BitConverter.GetBytes(0).CopyTo(image, 8);
        BitConverter.GetBytes(1).CopyTo(image, 12);
        BitConverter.GetBytes(~(1ul << 63)).CopyTo(image, 16);
        Assert.Equal(ChunkImageFault.None, TestWorlds.Load(world, last, image));
        var withReserved = (byte[])image.Clone();
        BitConverter.GetBytes(ulong.MaxValue).CopyTo(withReserved, 16);
        Assert.Equal(ChunkImageFault.BadRows, TestWorlds.Load(world, last, withReserved));
        Assert.Equal(0ul, world.Available(last));
        var slot = world.Acquire(0);
        Assert.NotEqual(Slot.None, slot);
        Assert.Equal(SlotState.Free, world.StateOf(Slot.None));
    }
}
