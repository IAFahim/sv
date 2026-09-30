using System;
using NUnit.Framework;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Sv;
using Sv.Unity;

namespace Sv.Unity.Tests.Editor
{
    public sealed unsafe class SvUnityEditorTests
    {
        struct Health : IComponentData
        {
            public int Value;
        }

        static Schema HealthSchema() =>
            SchemaBuilder.Create()
                .Stat("max_mana", 100f)
                .Intrinsic("mana")
                .Build();

        [Test]
        public void DenseChunk_WritesThroughToChunkStorage_MatchesOracle()
        {
            using var world = new World("sv-editor");
            var manager = world.EntityManager;
            using var population = new UnityPopulation(HealthSchema(), 256);
            var archetype = manager.CreateArchetype(typeof(Health), typeof(SvSlot));
            var entities = manager.CreateEntity(archetype, 96, Allocator.Temp);
            for (var i = 0; i < entities.Length; i++)
            {
                manager.SetComponentData(entities[i], new Health { Value = 500 - i });
                population.Bind(manager, entities[i]);
            }

            var slotHandle = manager.GetComponentTypeHandle<SvSlot>();
            var healthHandle = manager.GetComponentTypeHandle<Health>();
            var chunks = manager.GetAllChunks(Allocator.Temp);
            var sv = new SvChunk(population.Board, slotHandle);
            var clamp = Clamp.Constant(0, 400);
            var applied = new NativeArray<int>(128, Allocator.Temp);
            for (var c = 0; c < chunks.Length; c++)
            {
                var health = healthHandle;
                sv.RunIntrinsics(ref health, chunks[c], false, default, -3, clamp, applied.AsSpan());
            }

            for (var i = 0; i < entities.Length; i++)
            {
                Assert.AreEqual(Math.Clamp(500 - i - 3, 0, 400), manager.GetComponentData<Health>(entities[i]).Value, $"entity {i}");
            }

            applied.Dispose();
            chunks.Dispose();
            entities.Dispose();
        }

        [Test]
        public void EnabledMaskChunk_BitIdenticalToDensePath()
        {
            using var world = new World("sv-editor");
            var manager = world.EntityManager;
            using var population = new UnityPopulation(HealthSchema(), 256);
            var archetype = manager.CreateArchetype(typeof(Health), typeof(SvSlot));
            var entities = manager.CreateEntity(archetype, 128, Allocator.Temp);
            var dense = new int[128];
            for (var i = 0; i < entities.Length; i++)
            {
                dense[i] = 200 + i;
            }

            var slotHandle = manager.GetComponentTypeHandle<SvSlot>();
            var healthHandle = manager.GetComponentTypeHandle<Health>();
            var chunks = manager.GetAllChunks(Allocator.Temp);
            var sv = new SvChunk(population.Board, slotHandle);
            var clamp = Clamp.Constant(0, 1000);

            for (var pass = 0; pass < 2; pass++)
            {
                for (var i = 0; i < entities.Length; i++)
                {
                    manager.SetComponentData(entities[i], new Health { Value = dense[i] });
                }

                var health = healthHandle;
                if (pass == 0)
                {
                    sv.RunIntrinsics(ref health, chunks[0], false, default, 7, clamp, Span<int>.Empty);
                }
                else
                {
                    var mask = new v128(0xFFFFFFFFFFFFFFFFul, 0xFFFFFFFFFFFFFFFFul);
                    sv.RunIntrinsics(ref health, chunks[0], true, mask, 7, clamp, Span<int>.Empty);
                }

                for (var i = 0; i < entities.Length; i++)
                {
                    Assert.AreEqual(Math.Clamp(dense[i] + 7, 0, 1000), manager.GetComponentData<Health>(entities[i]).Value, $"entity {i} pass {pass}");
                }
            }

            chunks.Dispose();
            entities.Dispose();
        }
    }
}
