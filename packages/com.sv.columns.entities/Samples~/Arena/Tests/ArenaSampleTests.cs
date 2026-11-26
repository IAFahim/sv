using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Entities;

namespace Sv.Columns.Entities.Samples.Arena.Tests
{
    public sealed class ArenaSampleTests
    {
        [Test]
        public void BurstAttacksAndReadsMatchTheClassicWorld()
        {
            var json = File.ReadAllText(Path.GetFullPath("Packages/com.sv.columns/Tests/Packs~/arena.json"));
            var blob = ColumnsPacks.Create(json);
            using var pack = PackJson.Read(json).Compile();
            using var mirror = new ColumnWorld(pack, 256);
            var world = new World("sv columns arena sample");
            try
            {
                var manager = world.EntityManager;
                var attack = world.CreateSystem<ArenaAttackSystem>();
                var step = world.CreateSystem<ColumnsStepSystem>();
                var read = world.CreateSystem<ArenaHealthSystem>();
                var config = manager.CreateEntity(typeof(ColumnsWorldConfig));
                manager.SetComponentData(config, new ColumnsWorldConfig { Pack = blob, Capacity = 256, Writers = 1, ResolveParts = 2 });
                var fire = pack.Channel("Fire");
                var health = pack.Lane("Health");
                var victims = new Entity[8];
                var attackers = new Entity[8];
                for (var i = 0; i < victims.Length; i++)
                {
                    victims[i] = manager.CreateEntity(typeof(ColumnsTemplate), typeof(ArenaHealth));
                    manager.SetComponentData(victims[i], new ColumnsTemplate { Value = pack.Template("dummy") });
                    manager.SetComponentData(victims[i], new ArenaHealth { Lane = health });
                }

                for (var i = 0; i < attackers.Length; i++)
                {
                    attackers[i] = manager.CreateEntity(typeof(ColumnsTemplate), typeof(ArenaAttack));
                    manager.SetComponentData(attackers[i], new ColumnsTemplate { Value = pack.Template("dummy") });
                    manager.SetComponentData(attackers[i], new ArenaAttack { Target = victims[i], Channel = fire, Amount = 1 + i });
                }

                var victimSlots = new int[victims.Length];
                var attackerSlots = new int[attackers.Length];
                var spawnOrder = new List<Entity>(victims);
                spawnOrder.AddRange(attackers);
                spawnOrder.Sort();
                foreach (var entity in spawnOrder)
                {
                    var slot = mirror.Spawn(pack.Template("dummy"));
                    var victim = Array.IndexOf(victims, entity);
                    if (victim >= 0)
                    {
                        victimSlots[victim] = slot;
                    }
                    else
                    {
                        attackerSlots[Array.IndexOf(attackers, entity)] = slot;
                    }
                }

                for (var frame = 0; frame < 12; frame++)
                {
                    if (frame > 0)
                    {
                        for (var i = 0; i < attackers.Length; i++)
                        {
                            mirror.Writer(0).SendFrom(attackerSlots[i], victimSlots[i], fire, 1 + i);
                        }
                    }

                    attack.Update(world.Unmanaged);
                    step.Update(world.Unmanaged);
                    read.Update(world.Unmanaged);
                    mirror.Step();
                    manager.CompleteAllTrackedJobs();
                    for (var i = 0; i < victims.Length; i++)
                    {
                        Assert.AreEqual(victimSlots[i], manager.GetComponentData<ColumnsSlot>(victims[i]).Value);
                        Assert.AreEqual(mirror.Get(victimSlots[i], health), manager.GetComponentData<ArenaHealth>(victims[i]).Value, "frame " + frame + " victim " + i);
                    }
                }

                Assert.Less(manager.GetComponentData<ArenaHealth>(victims[7]).Value, 100);
            }
            finally
            {
                world.Dispose();
                blob.Dispose();
            }
        }
    }
}
