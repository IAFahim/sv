using System;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Sv;
using Sv.Unity;
using Xunit;

public struct Health : IComponentData
{
    public int Value;
}

public struct Stamina : IComponentData
{
    public int Value;
}

public sealed unsafe class SvUnityFacadeTests
{
    static Schema Schema() =>
        SchemaBuilder.Create()
            .Stat("max_mana", 100f)
            .Intrinsic("mana")
            .Build();

    [Fact]
    public void BindUnbind_SlotLifecycle_StaysDense()
    {
        using var world = new Unity.Entities.World("sv");
        var manager = world.EntityManager;
        using var population = new UnityPopulation(Schema(), 256);
        var archetype = manager.CreateArchetype(typeof(Health));
        var entities = manager.CreateEntity(archetype, 40, Allocator.Temp);
        for (var i = 0; i < entities.Length; i++)
        {
            Assert.Equal(i, population.Bind(manager, entities[i]));
        }

        Assert.Equal(40, population.Board.LiveCount);
        for (var i = 1; i < 40; i += 2)
        {
            population.Unbind(manager, entities[i]);
        }

        Assert.Equal(20, population.Board.LiveCount);
        var lookup = manager.GetComponentLookup<SvSlot>();
        Assert.False(population.TrySlot(lookup, entities[1], out _));
        Assert.True(population.TrySlot(lookup, entities[0], out var slot0));
        Assert.Equal(0, slot0);
        Assert.Equal(1, population.Bind(manager, entities[1]));
        entities.Dispose();
    }

    [Fact]
    public void DenseChunk_WritesThroughToStorage_MatchesOracle()
    {
        using var world = new Unity.Entities.World("sv");
        var manager = world.EntityManager;
        using var population = new UnityPopulation(Schema(), 256);
        var archetype = manager.CreateArchetype(typeof(Health));
        var entities = manager.CreateEntity(archetype, 96, Allocator.Temp);
        for (var i = 0; i < entities.Length; i++)
        {
            manager.SetComponentData(entities[i], new Health { Value = 500 - i });
            population.Bind(manager, entities[i]);
        }

        population.Reconcile(manager);
        var sv = new SvChunk(population.Board, manager.GetComponentTypeHandle<SvSlot>(), manager.GetComponentTypeHandle<SvRange>());
        var healthHandle = manager.GetComponentTypeHandle<Health>();
        var chunks = manager.GetAllChunks();
        var applied = new Span<int>(new int[128]);
        for (var c = 0; c < chunks.Length; c++)
        {
            var health = healthHandle;
            sv.RunIntrinsics(ref health, chunks[c], false, default, -3, Clamp.Constant(0, 400), applied);
        }

        for (var i = 0; i < entities.Length; i++)
        {
            Assert.Equal(Math.Clamp(500 - i - 3, 0, 400), manager.GetComponentData<Health>(entities[i]).Value);
        }

        Assert.Equal(Math.Clamp(500 - 3, 0, 400) - 500, applied[0]);
        Assert.Equal(Math.Clamp(500 - 95 - 3, 0, 400) - (500 - 95), applied[95]);
        chunks.Dispose();
        entities.Dispose();
    }

    [Fact]
    public void EnabledMask_AllEnabled_IsBitIdenticalToDensePath()
    {
        var initial = new int[128];
        var rng = new Random(20261001);
        for (var i = 0; i < initial.Length; i++)
        {
            initial[i] = rng.Next(0, 900);
        }

        var results = new int[128][];
        for (var pass = 0; pass < 2; pass++)
        {
            using var world = new Unity.Entities.World("sv");
            var manager = world.EntityManager;
            using var population = new UnityPopulation(Schema(), 256);
            var archetype = manager.CreateArchetype(typeof(Health));
            var entities = manager.CreateEntity(archetype, 128, Allocator.Temp);
            for (var i = 0; i < entities.Length; i++)
            {
                manager.SetComponentData(entities[i], new Health { Value = initial[i] });
                population.Bind(manager, entities[i]);
            }

            population.Reconcile(manager);
            var sv = new SvChunk(population.Board, manager.GetComponentTypeHandle<SvSlot>(), manager.GetComponentTypeHandle<SvRange>());
            var healthHandle = manager.GetComponentTypeHandle<Health>();
            var chunks = manager.GetAllChunks();
            var health = healthHandle;
            if (pass == 0)
            {
                sv.RunIntrinsics(ref health, chunks[0], false, default, 7, Clamp.Constant(0, 1000), Span<int>.Empty);
            }
            else
            {
                sv.RunIntrinsics(ref health, chunks[0], true, new v128(ulong.MaxValue, ulong.MaxValue), 7, Clamp.Constant(0, 1000), Span<int>.Empty);
            }

            results[pass] = new int[128];
            for (var i = 0; i < entities.Length; i++)
            {
                results[pass][i] = manager.GetComponentData<Health>(entities[i]).Value;
            }

            chunks.Dispose();
            entities.Dispose();
        }

        Assert.Equal(results[0], results[1]);
    }

    [Fact]
    public void EnabledMask_64AlignedChunk_MatchesOracle()
    {
        using var world = new Unity.Entities.World("sv");
        var manager = world.EntityManager;
        using var population = new UnityPopulation(Schema(), 256);
        var archetype = manager.CreateArchetype(typeof(Health));
        var entities = manager.CreateEntity(archetype, 128, Allocator.Temp);
        for (var i = 0; i < entities.Length; i++)
        {
            manager.SetComponentData(entities[i], new Health { Value = 500 - i });
            population.Bind(manager, entities[i]);
        }

        population.Reconcile(manager);
        var sv = new SvChunk(population.Board, manager.GetComponentTypeHandle<SvSlot>(), manager.GetComponentTypeHandle<SvRange>());
        var healthHandle = manager.GetComponentTypeHandle<Health>();
        var chunks = manager.GetAllChunks();
        var applied = new Span<int>(new int[128]);
        var health = healthHandle;
        sv.RunIntrinsics(ref health, chunks[0], true, new v128(0x5555555555555555ul, 0x5555555555555555ul), 10, Clamp.Constant(0, 1000), applied);
        for (var i = 0; i < entities.Length; i++)
        {
            var expected = i % 2 == 0 ? Math.Clamp(500 - i + 10, 0, 1000) : 500 - i;
            Assert.Equal(expected, manager.GetComponentData<Health>(entities[i]).Value);
            Assert.Equal(i % 2 == 0 ? 10 : 0, applied[i]);
        }

        chunks.Dispose();
        entities.Dispose();
    }

    [Fact]
    public void EnabledMask_MisalignedChunk_DirectPath_MatchesOracle()
    {
        using var world = new Unity.Entities.World("sv");
        var manager = world.EntityManager;
        using var population = new UnityPopulation(Schema(), 256);
        var firstArchetype = manager.CreateArchetype(typeof(Health));
        var secondArchetype = manager.CreateArchetype(typeof(Stamina));
        var first = manager.CreateEntity(firstArchetype, 70, Allocator.Temp);
        for (var i = 0; i < first.Length; i++)
        {
            population.Bind(manager, first[i]);
        }

        var second = manager.CreateEntity(secondArchetype, 60, Allocator.Temp);
        for (var i = 0; i < second.Length; i++)
        {
            manager.SetComponentData(second[i], new Stamina { Value = 900 - i });
            Assert.Equal(70 + i, population.Bind(manager, second[i]));
        }

        population.Reconcile(manager);
        var sv = new SvChunk(population.Board, manager.GetComponentTypeHandle<SvSlot>(), manager.GetComponentTypeHandle<SvRange>());
        var staminaHandle = manager.GetComponentTypeHandle<Stamina>();
        var chunks = manager.GetAllChunks();
        Assert.Equal(2, chunks.Length);
        var applied = new Span<int>(new int[128]);
        var stamina = staminaHandle;
        sv.RunIntrinsics(ref stamina, chunks[1], true, new v128(0x0F0F0F0F0F0F0F0Ful, 0x0F0F0F0F0F0F0F0Ful), -5, Clamp.Constant(0, 1000), applied);
        for (var i = 0; i < second.Length; i++)
        {
            var enabled = (i % 8) < 4;
            var expected = enabled ? Math.Clamp(900 - i - 5, 0, 1000) : 900 - i;
            Assert.Equal(expected, manager.GetComponentData<Stamina>(second[i]).Value);
            Assert.Equal(enabled ? -5 : 0, applied[i]);
        }

        chunks.Dispose();
        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public void EnabledMask_PartialChunk_TailBounded_MatchesOracle()
    {
        using var world = new Unity.Entities.World("sv");
        var manager = world.EntityManager;
        using var population = new UnityPopulation(Schema(), 256);
        var archetype = manager.CreateArchetype(typeof(Health));
        var entities = manager.CreateEntity(archetype, 70, Allocator.Temp);
        for (var i = 0; i < entities.Length; i++)
        {
            manager.SetComponentData(entities[i], new Health { Value = 500 });
            population.Bind(manager, entities[i]);
        }

        population.Reconcile(manager);
        var sv = new SvChunk(population.Board, manager.GetComponentTypeHandle<SvSlot>(), manager.GetComponentTypeHandle<SvRange>());
        var healthHandle = manager.GetComponentTypeHandle<Health>();
        var chunks = manager.GetAllChunks();
        var applied = new Span<int>(new int[128]);
        var health = healthHandle;
        sv.RunIntrinsics(ref health, chunks[0], true, new v128(ulong.MaxValue, 0b010101ul), 7, Clamp.Constant(0, 1000), applied);
        for (var i = 0; i < 70; i++)
        {
            var on = i < 64 || (i & 1) == 0;
            Assert.Equal(on ? 507 : 500, manager.GetComponentData<Health>(entities[i]).Value);
            Assert.Equal(on ? 7 : 0, applied[i]);
        }

        chunks.Dispose();
        entities.Dispose();
    }

    [Fact]
    public void FragmentedChunk_Throws_UntilReconcile_RestoresContiguity()
    {
        using var world = new Unity.Entities.World("sv");
        var manager = world.EntityManager;
        using var population = new UnityPopulation(Schema(), 256);
        var archetype = manager.CreateArchetype(typeof(Health));
        var entities = manager.CreateEntity(archetype, 100, Allocator.Temp);
        for (var i = 0; i < entities.Length; i++)
        {
            manager.SetComponentData(entities[i], new Health { Value = 400 - i });
            population.Bind(manager, entities[i]);
        }

        for (var i = 1; i < 100; i += 3)
        {
            population.Unbind(manager, entities[i]);
        }

        Assert.Equal(67, population.Board.LiveCount);
        var sv = new SvChunk(population.Board, manager.GetComponentTypeHandle<SvSlot>(), manager.GetComponentTypeHandle<SvRange>());
        var healthHandle = manager.GetComponentTypeHandle<Health>();
        var chunks = manager.GetAllChunks();
        var health = healthHandle;
        Assert.Throws<ArgumentException>(() => sv.RunIntrinsics(ref health, chunks[0], false, default, 1, Clamp.Constant(0, 500), Span<int>.Empty));

        using var map = population.Reconcile(manager);
        Assert.Equal(67, population.Board.LiveCount);
        chunks.Dispose();
        chunks = manager.GetAllChunks();
        health = healthHandle;
        sv.RunIntrinsics(ref health, chunks[0], false, default, -2, Clamp.Constant(0, 500), Span<int>.Empty);
        for (var i = 0; i < 100; i++)
        {
            if (i % 3 == 1)
            {
                continue;
            }

            Assert.Equal(Math.Clamp(400 - i - 2, 0, 500), manager.GetComponentData<Health>(entities[i]).Value);
            population.TrySlot(manager.GetComponentLookup<SvSlot>(), entities[i], out var slot);
            Assert.InRange(slot, 0, 66);
        }

        chunks.Dispose();
        entities.Dispose();
    }
}
