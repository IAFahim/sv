using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs.LowLevel.Unsafe;

namespace Sv.Unity.Tests
{
    public struct TestOut : IComponentData { public int Value; }
    public struct TestTarget : IComponentData { public int Value; }

    public sealed class SvSmallWorld : IDisposable
    {
        public readonly World World;
        public readonly Board Board;
        public readonly Schema Schema;
        public readonly StatId MaxHp;
        public readonly EntityArchetype Archetype;
        public SvHost Host;

        public SvSmallWorld(int capacity)
        {
            Schema = SchemaBuilder.Create().Stat("max_hp", 100f).Intrinsic("hp").Build();
            MaxHp = Schema.Stat("max_hp");
            World = new World("sv-small");
            World.GetOrCreateSystemManaged<SimulationSystemGroup>();
            Board = Board.Create(Schema, capacity);
            Archetype = World.EntityManager.CreateArchetype(typeof(SvSlot), typeof(SvSlotCleanup), typeof(TestHp), typeof(TestHpDelta), typeof(TestMaxHp), typeof(TestLow), typeof(TestOut), typeof(TestTarget));
        }

        public SvLinkRegistry Links(bool send)
        {
            var links = new SvLinkRegistry();
            links.Stat<TestMaxHp>(MaxHp);
            var hp = links.Intrinsic<TestHp, TestHpDelta>(Schema.Intrinsic("hp"), SvBound.Of(0), SvBound.OfStat<TestMaxHp>(), true);
            links.Condition<TestLow>(hp, 30, 100, 40, 100);
            links.Link<TestHpDelta>(1, LinkMode.Delta);
            links.Link<TestHpDelta>(2, LinkMode.Delta, LinkScope.Remote);
            if (send)
            {
                links.Send<TestOut, TestTarget>(hp);
            }

            return links;
        }

        public SvHost Install(bool send = false, int writeCapacity = 1 << 16, int opCapacity = 1 << 16, int digestEvery = 8)
        {
            Host = SvHost.Install(World, Board, Links(send), writeCapacity, opCapacity, digestEvery);
            return Host;
        }

        public Entity Row(int hp)
        {
            var manager = World.EntityManager;
            var e = manager.CreateEntity(Archetype);
            manager.SetComponentData(e, new TestHp { Value = hp });
            manager.SetComponentEnabled<TestLow>(e, false);
            return e;
        }

        public void Frame() => World.GetExistingSystemManaged<SimulationSystemGroup>().Update();

        public bool Live(int slot) => (Board.LiveWord(slot >> 6) & (1ul << (slot & 63))) != 0;

        public int Hp(Entity e) => World.EntityManager.GetComponentData<TestHp>(e).Value;

        public void Dispose()
        {
            Host?.Dispose();
            World.Dispose();
            Board.Dispose();
        }
    }

    public class SvHostHardeningTests
    {
        [Test]
        public void RowsThatExistBeforeInstallAreAdoptedAndScattered()
        {
            using var t = new SvSmallWorld(64);
            var a = t.Board.Acquire();
            var b = t.Board.Acquire();
            var ops = new[] { StatOp.Add(b, t.MaxHp, ModifierKind.Added, -40f, 3) };
            t.Board.Apply(ops);
            t.Board.Recalculate();
            var ea = t.Row(80);
            var eb = t.Row(80);
            var stray = t.Row(80);
            var manager = t.World.EntityManager;
            manager.SetComponentData(ea, new SvSlot { Slot = a });
            manager.SetComponentData(eb, new SvSlot { Slot = b });
            manager.SetComponentData(stray, new SvSlot { Slot = 40 });
            t.Install();
            t.Frame();
            Assert.AreEqual(100f, manager.GetComponentData<TestMaxHp>(ea).Value, "a pre-Install row gets its stats scattered");
            Assert.AreEqual(60f, manager.GetComponentData<TestMaxHp>(eb).Value);
            Assert.AreEqual(60, t.Hp(eb), "and the fold clamps to the scattered bound");
            Assert.AreEqual(ea, t.Host.EntityOf(a));
            Assert.AreEqual(eb, t.Host.EntityOf(b));
            Assert.AreEqual(Entity.Null, t.Host.EntityOf(40), "an SvSlot that is not live on the board is not adopted");
            Assert.AreEqual(b, manager.GetComponentData<SvSlotCleanup>(eb).Slot);
        }

        [Test]
        public void TwoEntitiesClaimingOneLiveSlotFailAtInstall()
        {
            using var t = new SvSmallWorld(64);
            var a = t.Board.Acquire();
            t.World.EntityManager.SetComponentData(t.Row(50), new SvSlot { Slot = a });
            t.World.EntityManager.SetComponentData(t.Row(50), new SvSlot { Slot = a });
            Assert.Throws<InvalidOperationException>(() => t.Install());
        }

        [Test]
        public void UnbindRemovesSvSlotStopsTheFoldAndCancelsASameFrameBind()
        {
            using var t = new SvSmallWorld(64);
            t.Install();
            var kept = t.Row(80);
            var dropped = t.Row(80);
            var keptSlot = t.Host.Bind(kept);
            var droppedSlot = t.Host.Bind(dropped);
            t.Frame();
            var manager = t.World.EntityManager;
            var late = t.Row(80);
            var lateSlot = t.Host.Bind(late);
            Assert.IsTrue(t.Host.IsPending(lateSlot));
            t.Host.Unbind(lateSlot);
            t.Host.Unbind(droppedSlot);
            Assert.IsFalse(t.Host.IsPending(lateSlot));
            Assert.IsFalse(t.Live(lateSlot));
            Assert.IsFalse(t.Live(droppedSlot));
            t.Frame();
            Assert.IsFalse(manager.HasComponent<SvSlot>(late), "a Bind cancelled in the same frame leaves no SvSlot");
            Assert.IsFalse(manager.HasComponent<SvSlot>(dropped));
            Assert.IsFalse(manager.HasComponent<SvSlotCleanup>(dropped));
            Assert.AreEqual(Entity.Null, t.Host.EntityOf(droppedSlot));
            manager.SetComponentData(dropped, new TestHpDelta { Value = -30 });
            manager.SetComponentData(kept, new TestHpDelta { Value = -30 });
            t.Frame();
            Assert.AreEqual(80, t.Hp(dropped), "an unbound row is not folded");
            Assert.AreEqual(50, t.Hp(kept));
            var again = t.Host.Bind(dropped);
            t.Frame();
            Assert.AreEqual(again, manager.GetComponentData<SvSlot>(dropped).Slot);
            Assert.AreEqual(dropped, t.Host.EntityOf(again));
            Assert.AreEqual(keptSlot, manager.GetComponentData<SvSlot>(kept).Slot);
        }

        [Test]
        public void DestroyedEntitiesFreeTheirSlotsWithoutUnbind()
        {
            using var t = new SvSmallWorld(64);
            t.Install();
            var rows = new Entity[10];
            var slots = new int[10];
            for (var i = 0; i < rows.Length; i++)
            {
                rows[i] = t.Row(70);
                slots[i] = t.Host.Bind(rows[i]);
            }

            t.Frame();
            var manager = t.World.EntityManager;
            var live = t.Board.LiveCount;
            manager.DestroyEntity(rows[3]);
            manager.DestroyEntity(rows[7]);
            var doomed = t.Row(70);
            var doomedSlot = t.Host.Bind(doomed);
            manager.DestroyEntity(doomed);
            t.Frame();
            Assert.AreEqual(live - 2, t.Board.LiveCount);
            Assert.IsFalse(t.Live(slots[3]));
            Assert.IsFalse(t.Live(slots[7]));
            Assert.IsFalse(t.Live(doomedSlot), "a row destroyed before its first frame frees its slot too");
            Assert.AreEqual(Entity.Null, t.Host.EntityOf(slots[3]));
            using var husks = manager.CreateEntityQuery(new EntityQueryBuilder(Allocator.Temp).WithAll<SvSlotCleanup>().WithAbsent<SvSlot>());
            Assert.IsTrue(husks.IsEmptyIgnoreFilter, "the cleanup husks are gone");
            for (var round = 0; round < 50; round++)
            {
                var e = t.Row(70);
                t.Host.Bind(e);
                t.Frame();
                manager.DestroyEntity(e);
                t.Frame();
            }

            Assert.AreEqual(live - 2, t.Board.LiveCount, "spawn and destroy churn does not leak slots");
        }

        [Test]
        public void StatOpsAndStatWritesAboveTheCapacityAreChunkedNotThrown()
        {
            using var t = new SvSmallWorld(512);
            t.Install(false, 8, 4);
            var rows = new Entity[300];
            var slots = new int[300];
            for (var i = 0; i < rows.Length; i++)
            {
                rows[i] = t.Row(90);
                slots[i] = t.Host.Bind(rows[i]);
            }

            t.Frame();
            var manager = t.World.EntityManager;
            for (var i = 0; i < rows.Length; i++)
            {
                Assert.AreEqual(100f, manager.GetComponentData<TestMaxHp>(rows[i]).Value, "300 first writes through an 8-write scatter buffer");
            }

            for (var i = 0; i < rows.Length; i++)
            {
                t.Host.Enqueue(StatOp.Add(slots[i], t.MaxHp, ModifierKind.Added, -(i % 50), 10 + i));
            }

            Assert.GreaterOrEqual(t.Host.OpCapacity, 300, "the StatOp queue grows");
            t.Frame();
            for (var i = 0; i < rows.Length; i++)
            {
                Assert.AreEqual(t.Board.Stat(slots[i], t.MaxHp), manager.GetComponentData<TestMaxHp>(rows[i]).Value);
                Assert.AreEqual(Math.Min(90, 100 - i % 50), t.Hp(rows[i]));
            }
        }

        [Test]
        public void ASecondHostInOneWorldFailsAtInstall()
        {
            using var t = new SvSmallWorld(64);
            t.Install();
            using var other = Board.Create(t.Schema, 64);
            var e = Assert.Throws<InvalidOperationException>(() => SvHost.Install(t.World, other, t.Links(false)));
            StringAssert.Contains("one SvHost per world", e.Message);
        }

        [Test]
        public void DigestEveryIsConfigurableAndDigestTickSaysWhenTheDigestWasTaken()
        {
            using var t = new SvSmallWorld(64);
            t.Install(digestEvery: 3);
            Assert.AreEqual(3, t.Host.DigestEvery);
            Assert.AreEqual(-1, t.Host.DigestTick);
            var e = t.Row(80);
            t.Host.Bind(e);
            var seen = new long[7];
            for (var f = 0; f < 7; f++)
            {
                t.World.EntityManager.SetComponentData(e, new TestHpDelta { Value = -1 });
                t.Frame();
                seen[f] = t.Host.DigestTick;
            }

            CollectionAssert.AreEqual(new long[] { 0, 0, 0, 3, 3, 3, 6 }, seen);
            var stale = t.Host.Digest;
            t.World.EntityManager.SetComponentData(e, new TestHpDelta { Value = -1 });
            t.Frame();
            Assert.AreEqual(stale, t.Host.Digest, "between digest ticks the digest is the one of DigestTick");
            t.Host.DigestEvery = 1;
            t.World.EntityManager.SetComponentData(e, new TestHpDelta { Value = -1 });
            t.Frame();
            Assert.AreEqual(t.Host.Tick - 1, t.Host.DigestTick);
            Assert.AreNotEqual(stale, t.Host.Digest);
        }

        static ulong SendScenario(int workers, out int[] finalHp)
        {
            var saved = JobsUtility.JobWorkerCount;
            JobsUtility.JobWorkerCount = workers;
            try
            {
                const int rows = 700;
                using var t = new SvSmallWorld(1024);
                t.Install(true, digestEvery: 1);
                var manager = t.World.EntityManager;
                var entities = new Entity[rows];
                var slots = new int[rows];
                var model = new int[rows];
                for (var i = 0; i < rows; i++)
                {
                    model[i] = 40 + i % 60;
                    entities[i] = t.Row(model[i]);
                    slots[i] = t.Host.Bind(entities[i]);
                }

                t.Frame();
                var random = new Random(0x5E4D);
                var pending = new long[rows];
                for (var frame = 0; frame < 30; frame++)
                {
                    var arriving = pending;
                    pending = new long[rows];
                    for (var i = 0; i < rows; i++)
                    {
                        if (random.Next(4) != 0)
                        {
                            continue;
                        }

                        var target = random.Next(rows + 8);
                        var amount = random.Next(-25, 26);
                        manager.SetComponentData(entities[i], new TestOut { Value = amount });
                        manager.SetComponentData(entities[i], new TestTarget { Value = target < rows ? slots[target] : -1 - target });
                        if (target < rows)
                        {
                            pending[target] += amount;
                        }
                    }

                    t.Frame();
                    for (var i = 0; i < rows; i++)
                    {
                        model[i] = IntrinsicLaw.Step(model[i], IntrinsicLaw.Saturate(arriving[i]), 0, 100);
                        Assert.AreEqual(model[i], t.Hp(entities[i]), $"row {i} frame {frame}");
                        Assert.AreEqual(0, manager.GetComponentData<TestOut>(entities[i]).Value, "the send pass consumes the effect column");
                    }
                }

                finalHp = model;
                return t.Host.Digest;
            }
            finally
            {
                JobsUtility.JobWorkerCount = saved;
            }
        }

        [Test]
        public void TheSendPassMovesEffectColumnsThroughTheInboxDeterministicallyAtOneAndElevenWorkers()
        {
            SvProbe.ManagedRuns = 0;
            var one = SendScenario(1, out var hpOne);
            var eleven = SendScenario(11, out var hpEleven);
            UnityEngine.Debug.Log($"svsend workers1={one:X16} workers11={eleven:X16}");
            CollectionAssert.AreEqual(hpOne, hpEleven);
            Assert.AreEqual(one, eleven);
            Assert.AreEqual(0, SvProbe.ManagedRuns, "a job ran managed");
        }

        [Test]
        public void ASendPassNeedsARemoteIntrinsic()
        {
            var schema = SchemaBuilder.Create().Intrinsic("hp").Build();
            var links = new SvLinkRegistry();
            var hp = links.Intrinsic<TestHp, TestHpDelta>(schema.Intrinsic("hp"), SvBound.Of(0), SvBound.Of(10));
            Assert.Throws<ArgumentException>(() => links.Send<TestOut, TestTarget>(hp));
        }
    }
}
