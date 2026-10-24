using System;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Sv;
using Sv.Unity;
using Xunit;

public sealed unsafe class SvUnityV2Tests
{
    static Schema ManaSchema() =>
        SchemaBuilder.Create()
            .Stat("max_mana", 100f)
            .Intrinsic("mana")
            .Build();

    static Schema PowerSchema() =>
        SchemaBuilder.Create()
            .Stat("power", 10f)
            .Intrinsic("mana")
            .Build();

    [Fact]
    public void SvRange_ReconcileWritesPerChunkRanges_TwoArchetypes()
    {
        using var world = new Unity.Entities.World("sv");
        var manager = world.EntityManager;
        using var population = new UnityPopulation(ManaSchema(), 256);
        var healthArchetype = manager.CreateArchetype(typeof(Health));
        var staminaArchetype = manager.CreateArchetype(typeof(Stamina));
        var manaArchetype = manager.CreateArchetype(typeof(SvMana));
        var first = manager.CreateEntity(healthArchetype, 70, Allocator.Temp);
        var second = manager.CreateEntity(staminaArchetype, 60, Allocator.Temp);
        var third = manager.CreateEntity(manaArchetype, 10, Allocator.Temp);
        for (var i = 0; i < first.Length; i++)
        {
            population.Bind(manager, first[i]);
        }

        for (var i = 0; i < second.Length; i++)
        {
            population.Bind(manager, second[i]);
        }

        population.Reconcile(manager);
        var chunks = manager.GetAllChunks();
        Assert.Equal(3, chunks.Length);
        var rangeHandle = manager.GetComponentTypeHandle<SvRange>();
        var slotHandle = manager.GetComponentTypeHandle<SvSlot>();
        var healthRange = chunks[0].GetChunkComponentData(ref rangeHandle);
        Assert.Equal(0, healthRange.Start);
        Assert.Equal(70, healthRange.Count);
        var staminaRange = chunks[1].GetChunkComponentData(ref rangeHandle);
        Assert.Equal(70, staminaRange.Start);
        Assert.Equal(60, staminaRange.Count);
        var manaRange = chunks[2].GetChunkComponentData(ref rangeHandle);
        Assert.Equal(0, manaRange.Count);

        var healthSlots = chunks[0].GetComponentDataPtrRW(ref slotHandle);
        Assert.Equal(0, healthSlots[0].Slot);
        Assert.Equal(69, healthSlots[69].Slot);
        var staminaSlots = chunks[1].GetComponentDataPtrRW(ref slotHandle);
        Assert.Equal(70, staminaSlots[0].Slot);
        Assert.Equal(129, staminaSlots[59].Slot);

        chunks.Dispose();
        first.Dispose();
        second.Dispose();
        third.Dispose();
    }

    [Fact]
    public void SvRange_StaleAfterDelete_ThrowsUntilReconciled_RunsThroughRange()
    {
        using var world = new Unity.Entities.World("sv");
        var manager = world.EntityManager;
        using var population = new UnityPopulation(ManaSchema(), 256);
        var archetype = manager.CreateArchetype(typeof(Health));
        var entities = manager.CreateEntity(archetype, 70, Allocator.Temp);
        for (var i = 0; i < entities.Length; i++)
        {
            manager.SetComponentData(entities[i], new Health { Value = 500 - i });
            population.Bind(manager, entities[i]);
        }

        population.Reconcile(manager);
        var sv = new SvChunk(population.Board, manager.GetComponentTypeHandle<SvSlot>(), manager.GetComponentTypeHandle<SvRange>());
        var healthHandle = manager.GetComponentTypeHandle<Health>();
        var chunks = manager.GetAllChunks();
        var health = healthHandle;
        sv.RunIntrinsics(ref health, chunks[0], false, default, 1, Clamp.Constant(0, 1000), Span<int>.Empty);
        chunks.Dispose();

        population.Unbind(manager, entities[5]);
        chunks = manager.GetAllChunks();
        var rangeHandle = manager.GetComponentTypeHandle<SvRange>();
        var stale = chunks[0].GetChunkComponentData(ref rangeHandle);
        Assert.Equal(0, stale.Start);
        Assert.Equal(70, stale.Count);
        health = healthHandle;
        Assert.Throws<ArgumentException>(() => sv.RunIntrinsics(ref health, chunks[0], false, default, -2, Clamp.Constant(0, 1000), Span<int>.Empty));
        Assert.Throws<ArgumentException>(() => sv.RunIntrinsics(ref health, chunks[0], true, new v128(0x0F0F0F0F0F0F0F0Ful, 0x0Ful), -2, Clamp.Constant(0, 1000), Span<int>.Empty));

        population.Reconcile(manager);
        var reconciled = chunks[0].GetChunkComponentData(ref rangeHandle);
        Assert.Equal(0, reconciled.Start);
        Assert.Equal(69, reconciled.Count);
        health = healthHandle;
        sv.RunIntrinsics(ref health, chunks[0], false, default, -2, Clamp.Constant(0, 1000), Span<int>.Empty);
        health = healthHandle;
        sv.RunIntrinsics(ref health, chunks[0], true, new v128(0x0F0F0F0F0F0F0F0Ful, 0x0Ful), 3, Clamp.Constant(0, 1000), Span<int>.Empty);

        for (var i = 0; i < entities.Length; i++)
        {
            if (i == 5)
            {
                continue;
            }

            var row = i == 69 ? 5 : i;
            var expected = Math.Clamp(Math.Clamp(500 - i + 1, 0, 1000) - 2, 0, 1000);
            if ((row % 8) < 4)
            {
                expected = Math.Clamp(expected + 3, 0, 1000);
            }

            Assert.Equal(expected, manager.GetComponentData<Health>(entities[i]).Value);
        }

        chunks.Dispose();
        entities.Dispose();
    }

    [Fact]
    public void SvChanged_ChunkMask_EqualsBoardChangedBits_UnchangedChunkReadsZero()
    {
        var schema = PowerSchema();
        using var board = Board.Create(schema, 256);
        var power = schema.Stat("power");
        for (var slot = 0; slot < 130; slot++)
        {
            board.Acquire();
        }

        board.Recalculate();
        board.AddModifier(3, power, new Modifier(ModifierKind.Added, 5f), 0);
        board.AddModifier(64, power, new Modifier(ModifierKind.Added, 5f), 0);
        board.AddModifier(65, power, new Modifier(ModifierKind.Added, 5f), 0);
        board.AddModifier(66, power, new Modifier(ModifierKind.Added, 5f), 0);
        board.Recalculate();

        var words = board.ChangedWords(power);
        var first = SvChanged.ChunkMask(board, power, 0, 70);
        var second = SvChanged.ChunkMask(board, power, 70, 60);
        Assert.Equal(0ul, second.ULong0);
        Assert.Equal(0ul, second.ULong1);
        Assert.Equal(1ul << 3, first.ULong0);
        Assert.Equal(0b111ul, first.ULong1);
        for (var row = 0; row < 70; row++)
        {
            var expected = (words[row >> 6] >> (row & 63)) & 1ul;
            var actual = row < 64 ? (first.ULong0 >> row) & 1ul : (first.ULong1 >> (row - 64)) & 1ul;
            Assert.Equal(expected, actual);
        }

        board.AddModifier(100, power, new Modifier(ModifierKind.Added, 5f), 0);
        board.Recalculate();
        Assert.Equal(0ul, SvChanged.ChunkMask(board, power, 0, 70).ULong0);
        Assert.Equal(0ul, SvChanged.ChunkMask(board, power, 0, 70).ULong1);
        var misaligned = SvChanged.ChunkMask(board, power, 70, 60);
        Assert.Equal(1ul << 30, misaligned.ULong0);
        Assert.Equal(0ul, misaligned.ULong1);
    }

    [Fact]
    public void SvChanged_WordCount_CoversChunkRows()
    {
        Assert.Equal(0, SvChanged.WordCount(0));
        Assert.Equal(1, SvChanged.WordCount(1));
        Assert.Equal(1, SvChanged.WordCount(63));
        Assert.Equal(1, SvChanged.WordCount(64));
        Assert.Equal(2, SvChanged.WordCount(65));
        Assert.Equal(2, SvChanged.WordCount(127));
        Assert.Equal(2, SvChanged.WordCount(128));
    }

    [Fact]
    public void V2System_FrameOrder_ReconcileIntrinsicsExpireCrossingsRecalculate()
    {
        using var world = new Unity.Entities.World("sv");
        var manager = world.EntityManager;
        using var population = new UnityPopulation(PowerSchema(), 256);
        var power = population.Board.Schema.Stat("power");
        var archetype = manager.CreateArchetype(typeof(SvMana));
        var entities = manager.CreateEntity(archetype, 96, Allocator.Temp);
        for (var i = 0; i < entities.Length; i++)
        {
            manager.SetComponentData(entities[i], new SvMana { Value = 400 + i });
            population.Bind(manager, entities[i]);
        }

        var handle = world.CreateSystem<SvIntrinsicsSystem>();
        ref var system = ref world.Unmanaged.GetUnsafeSystemRef<SvIntrinsicsSystem>(handle);
        system.Population = population;
        system.Board = population.Board;
        system.Clamp = Clamp.Constant(0, 1000);
        system.Delta = 2;
        system.DueCrossings = new NativeArray<long>(16, Allocator.Temp);
        system.StructuralChange = true;
        system.Tick = 1;
        population.Board.AddModifier(0, power, new Modifier(ModifierKind.Added, 5f), 0, 5);
        var crossingTick = ClampShift.FirstTickAtLeast(ClampShift.Step(1, 0, 1000), 400, 500, 1024);
        Assert.Equal(100L, crossingTick);
        const long crossingId = 0x123456789ABCL;
        population.Board.ScheduleCrossing(crossingTick, crossingId);

        handle.Update(world.Unmanaged);
        Assert.False(system.StructuralChange);
        for (var i = 0; i < entities.Length; i++)
        {
            Assert.Equal(402 + i, manager.GetComponentData<SvMana>(entities[i]).Value);
        }

        Assert.Equal(15f, population.Board.Stat(0, power));
        Assert.Equal(0, system.ExpiredLastFrame);
        Assert.Equal(0, system.DueCrossingCount);

        system.Tick = 5;
        handle.Update(world.Unmanaged);
        Assert.Equal(1, system.ExpiredLastFrame);
        Assert.Equal(10f, population.Board.Stat(0, power));
        Assert.Equal(0, system.DueCrossingCount);
        for (var i = 0; i < entities.Length; i++)
        {
            Assert.Equal(404 + i, manager.GetComponentData<SvMana>(entities[i]).Value);
        }

        system.Tick = 100;
        handle.Update(world.Unmanaged);
        Assert.Equal(0, system.ExpiredLastFrame);
        Assert.Equal(1, system.DueCrossingCount);
        Assert.Equal(crossingId, system.DueCrossings[0]);
        for (var i = 0; i < entities.Length; i++)
        {
            Assert.Equal(406 + i, manager.GetComponentData<SvMana>(entities[i]).Value);
        }

        entities.Dispose();
    }

    [Fact]
    public void V2System_IdenticalFrameProgramTwice_IdenticalColumnBits()
    {
        var first = RunFrameProgram(20261024);
        var second = RunFrameProgram(20261024);
        Assert.Equal(first.Values, second.Values);
        Assert.Equal(first.Changed, second.Changed);
        Assert.Equal(first.Crossings, second.Crossings);
    }

    static (int[] Values, ulong[] Changed, long[] Crossings) RunFrameProgram(int seed)
    {
        using var world = new Unity.Entities.World("sv");
        var manager = world.EntityManager;
        using var population = new UnityPopulation(PowerSchema(), 256);
        var board = population.Board;
        var power = board.Schema.Stat("power");
        var rng = new Random(seed);
        var archetype = manager.CreateArchetype(typeof(SvMana));
        var entities = manager.CreateEntity(archetype, 45, Allocator.Temp);
        var initial = new int[45];
        for (var i = 0; i < entities.Length; i++)
        {
            initial[i] = rng.Next(0, 900);
            manager.SetComponentData(entities[i], new SvMana { Value = initial[i] });
            population.Bind(manager, entities[i]);
        }

        for (var i = 0; i < 12; i++)
        {
            board.AddModifier(i * 3, power, new Modifier(ModifierKind.Added, rng.Next(-4, 5) * 0.5f), 0, rng.Next(2, 6));
        }

        board.ScheduleCrossing(3, 42);
        board.ScheduleCrossing(3, 7);

        var handle = world.CreateSystem<SvIntrinsicsSystem>();
        ref var system = ref world.Unmanaged.GetUnsafeSystemRef<SvIntrinsicsSystem>(handle);
        system.Population = population;
        system.Board = board;
        system.Clamp = Clamp.Constant(0, 1000);
        system.Delta = 3;
        system.DueCrossings = new NativeArray<long>(16, Allocator.Temp);
        system.StructuralChange = true;
        var values = new System.Collections.Generic.List<int>();
        var changed = new System.Collections.Generic.List<ulong>();
        var crossings = new System.Collections.Generic.List<long>();
        for (var tick = 1; tick <= 6; tick++)
        {
            system.Tick = tick;
            handle.Update(world.Unmanaged);
            for (var i = 0; i < entities.Length; i++)
            {
                values.Add(manager.GetComponentData<SvMana>(entities[i]).Value);
            }

            var words = board.ChangedWords(power);
            for (var w = 0; w < board.LiveWordCount; w++)
            {
                changed.Add(words[w]);
            }

            for (var i = 0; i < system.DueCrossingCount; i++)
            {
                crossings.Add(system.DueCrossings[i]);
            }
        }

        entities.Dispose();
        return (values.ToArray(), changed.ToArray(), crossings.ToArray());
    }
}
