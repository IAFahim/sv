using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Sv.Unity.Tests
{
    public unsafe class SvAdapterTests
    {
        static int Law(int before, int delta, int floor, int ceiling) => IntrinsicLaw.Step(before, delta, floor, ceiling);

        [Test]
        public void FoldEqualsTheScalarLawWithInboxSendsAndConsumerClears()
        {
            using var t = new SvTestWorld(300, 512);
            t.Frame();
            var random = new System.Random(0x5F01);
            var hp = new int[300];
            var mana = new int[300];
            for (var i = 0; i < 300; i++)
            {
                hp[i] = t.HpOf(i);
                mana[i] = t.ManaOf(i);
            }

            var inbox = t.Host.InboxWriter(0);
            for (var frame = 0; frame < 20; frame++)
            {
                var hpDelta = new long[300];
                var manaDelta = new int[300];
                for (var i = 0; i < 300; i++)
                {
                    if (random.Next(3) == 0)
                    {
                        var d = random.Next(-40, 41);
                        t.AddHpDelta(i, d);
                        hpDelta[i] += d;
                    }

                    if (random.Next(2) == 0)
                    {
                        var d = random.Next(-30, 31);
                        t.AddManaDelta(i, d);
                        manaDelta[i] += d;
                    }
                }

                for (var s = 0; s < 40; s++)
                {
                    var target = random.Next(300);
                    var amount = random.Next(-20, 21);
                    inbox.Send(t.Slots[target], amount);
                    hpDelta[target] += amount;
                }

                t.Frame();
                for (var i = 0; i < 300; i++)
                {
                    hp[i] = Law(hp[i], IntrinsicLaw.Saturate(hpDelta[i]), 0, 100);
                    mana[i] = Law(mana[i], manaDelta[i], 0, SvTestWorld.ManaCeiling);
                    Assert.AreEqual(hp[i], t.HpOf(i), $"hp row {i} frame {frame}");
                    Assert.AreEqual(mana[i], t.ManaOf(i), $"mana row {i} frame {frame}");
                    Assert.AreEqual(0, t.World.EntityManager.GetComponentData<TestHpDelta>(t.Entities[i]).Value, "the fold clears the delta it consumes");
                }
            }
        }

        [Test]
        public void TheChangeFilterSkipsUntouchedChunksAndTheFoldsOwnWritesDoNotRetrigger()
        {
            using var t = new SvTestWorld(200, 256);
            t.Frame();
            t.Frame();
            var manager = t.World.EntityManager;
            var chunk = manager.GetChunk(t.Entities[0]);
            var handle = manager.GetComponentTypeHandle<TestHpDelta>(true);
            var raw = (TestHpDelta*)chunk.GetComponentDataPtrRO(ref handle);
            var before = t.HpOf(0);
            var row = -1;
            var entityHandle = manager.GetEntityTypeHandle();
            var entities = chunk.GetNativeArray(entityHandle);
            for (var r = 0; r < chunk.Count; r++)
            {
                if (entities[r] == t.Entities[0]) row = r;
            }

            raw[row].Value = -7;
            t.Frame();
            Assert.AreEqual(before, t.HpOf(0), "a delta written without a change version is not folded: the chunk was skipped");
            t.Host.Filter = false;
            t.Frame();
            Assert.AreEqual(before - 7, t.HpOf(0), "with the filter off the same chunk is folded");
        }

        [Test]
        public void ScatterWritesStatBitsAndTheSameFrameFoldClampsToTheNewBound()
        {
            using var t = new SvTestWorld(64, 128);
            t.Frame();
            t.Host.Enqueue(StatOp.Add(t.Slots[3], t.MaxHp, ModifierKind.Added, -40f, 7));
            t.Frame();
            Assert.AreEqual(t.Board.Stat(t.Slots[3], t.MaxHp), t.MaxHpOf(3));
            Assert.AreEqual(60f, t.MaxHpOf(3));
            Assert.AreEqual(Math.Min(50 + 3, 60), t.HpOf(3));
            t.AddHpDelta(3, 30);
            t.Frame();
            Assert.AreEqual(60, t.HpOf(3), "the clamp reads the scattered stat");
            t.Host.Enqueue(StatOp.RemoveSource(t.Slots[3], 7));
            t.Frame();
            Assert.AreEqual(100f, t.MaxHpOf(3));
            Assert.AreEqual(100f, t.MaxHpOf(4));
        }

        [Test]
        public void EdgeBitsBecomeSortedEventsAndDriveTheGate()
        {
            using var t = new SvTestWorld(100, 128);
            t.Frame();
            var hits = new[] { 71, 5, 40, 12 };
            foreach (var i in hits)
            {
                t.AddHpDelta(i, -1000);
            }

            t.Frame();
            var channel = t.Host.Channel(0);
            Assert.AreEqual(4, channel.Count);
            Assert.AreEqual(0, channel.Dense);
            var events = t.Host.Events(0);
            Assert.AreEqual(4, events.Length);
            for (var e = 1; e < events.Length; e++)
            {
                Assert.Less(events[e - 1].Slot, events[e].Slot, "events are in ascending slot order");
            }

            foreach (var i in hits)
            {
                Assert.IsTrue(t.World.EntityManager.IsComponentEnabled<TestLow>(t.Entities[i]));
                Assert.IsTrue(t.Host.ConditionBit(0, t.Slots[i]));
            }

            t.AddHpDelta(5, 35);
            t.Frame();
            Assert.AreEqual(0, t.Host.Channel(0).Count, "35 is below the 40 % stay line: no edge (hysteresis)");
            t.AddHpDelta(5, 10);
            t.Frame();
            Assert.AreEqual(1, t.Host.Channel(0).Count);
            Assert.AreEqual(0, t.Host.Events(0)[0].Entered);
            Assert.IsFalse(t.World.EntityManager.IsComponentEnabled<TestLow>(t.Entities[5]));
        }

        [Test]
        public void AboveTheCapAChannelDeliversOneDenseRecordAndKeepsTheEdgeBits()
        {
            using var t = new SvTestWorld(100, 128, 8);
            t.Frame();
            for (var i = 0; i < 20; i++)
            {
                t.AddHpDelta(i, -1000);
            }

            t.Frame();
            var channel = t.Host.Channel(0);
            Assert.AreEqual(20, channel.Count);
            Assert.AreEqual(1, channel.Dense);
            Assert.AreEqual(0, t.Host.Events(0).Length, "no partial delivery");
            for (var i = 0; i < 20; i++)
            {
                Assert.IsTrue(t.Host.EdgeBit(0, t.Slots[i]), "the mask form stays readable");
            }
        }

        [Test]
        public void DigestIsChunkOrderInvariantAndEqualsTheManagedCellSum()
        {
            ulong forward;
            using (var a = new SvTestWorld(150, 256))
            {
                a.Host.DigestEvery = 1;
                a.Frame();
                a.AddHpDelta(9, -3);
                a.World.EntityManager.AddComponent<Prefab>(a.Entities[20]);
                a.World.EntityManager.RemoveComponent<Prefab>(a.Entities[20]);
                a.Frame();
                forward = a.Host.Digest;
                var cells = 0ul;
                unchecked
                {
                    for (var i = 0; i < 150; i++)
                    {
                        cells += CellDigest.Key(a.Host.Links.Intrinsics[0].Column, a.Slots[i], (uint)a.HpOf(i));
                        cells += CellDigest.Key(a.Host.Links.Intrinsics[1].Column, a.Slots[i], (uint)a.ManaOf(i));
                    }
                }

                UnityEngine.Debug.Log($"svdigest a board={a.Board.Digest:X16} cells={a.Host.CellDigestValue:X16}");
                Assert.AreEqual(cells, a.Host.CellDigestValue);
                Assert.AreEqual(unchecked(a.Board.Digest + cells), forward);
            }

            using (var b = new SvTestWorld(150, 256, 65536, true))
            {
                b.Host.DigestEvery = 1;
                b.Frame();
                b.AddHpDelta(9, -3);
                b.Frame();
                UnityEngine.Debug.Log($"svdigest b board={b.Board.Digest:X16} cells={b.Host.CellDigestValue:X16}");
                Assert.AreEqual(forward, b.Host.Digest, "reverse creation order, one archetype move less: same slot-keyed digest");
            }
        }

        [Test]
        public void TemplateSpawnAssignsSlotsWritesSvSlotAndKeepsPrefabStats()
        {
            using var t = new SvTestWorld(4, 64);
            t.Frame();
            var manager = t.World.EntityManager;
            var prefab = manager.Instantiate(t.Entities[0]);
            manager.AddComponent<Prefab>(prefab);
            manager.SetComponentData(prefab, new TestMaxHp { Value = 100f });
            using var spawned = new NativeArray<Entity>(10, Allocator.Temp);
            t.Host.Spawn(manager, prefab, t.Slots[0], spawned);
            t.Frame();
            var seen = new System.Collections.Generic.HashSet<int>();
            for (var i = 0; i < spawned.Length; i++)
            {
                var slot = manager.GetComponentData<SvSlot>(spawned[i]).Slot;
                Assert.That(slot, Is.InRange(0, 63));
                Assert.AreEqual(-1, Array.IndexOf(t.Slots, slot), "a spawned row gets a fresh slot");
                Assert.IsTrue(seen.Add(slot));
                Assert.AreEqual(spawned[i], t.Host.EntityOf(slot));
                Assert.AreEqual(100f, manager.GetComponentData<TestMaxHp>(spawned[i]).Value);
            }
        }

        [Test]
        public void LinkTableRejectsARemoteWriteWithoutAnInbox()
        {
            var schema = SchemaBuilder.Create().Intrinsic("hp").Build();
            var links = new SvLinkRegistry();
            links.Intrinsic<TestMana, TestManaDelta>(schema.Intrinsic("hp"), SvBound.Of(0), SvBound.Of(10));
            links.Link<TestManaDelta>(1, LinkMode.Delta, LinkScope.Remote);
            Assert.AreEqual(LinkFault.RemoteWithoutInbox, links.Validate());
        }

        [Test]
        public void AllocationWindowsAreReported()
        {
            using var t = new SvTestWorld(500, 512);
            t.Host.DigestEvery = 1;
            var inbox = t.Host.InboxWriter(0);
            for (var i = 0; i < 16; i++)
            {
                t.Frame();
            }

            var a = GC.GetAllocatedBytesForCurrentThread();
            for (var f = 0; f < 100; f++) t.Frame();
            var b = GC.GetAllocatedBytesForCurrentThread();
            for (var f = 0; f < 100; f++) { inbox.Send(t.Slots[f], -1); t.Frame(); }
            var c = GC.GetAllocatedBytesForCurrentThread();
            for (var f = 0; f < 100; f++) t.Host.Enqueue(StatOp.Add(t.Slots[f], t.MaxHp, ModifierKind.Added, 1f, 100 + f));
            var d = GC.GetAllocatedBytesForCurrentThread();
            t.Frame();
            var e = GC.GetAllocatedBytesForCurrentThread();
            for (var f = 0; f < 100; f++) t.Host.Enqueue(StatOp.Add(t.Slots[f], t.MaxHp, ModifierKind.Added, 1f, 300 + f));
            t.Frame();
            var g = GC.GetAllocatedBytesForCurrentThread();
            var plain = b - a;
            var sends = c - b;
            var enqueue = d - c;
            var firstOps = e - d;
            var secondOps = g - e;
            UnityEngine.Debug.Log($"svalloc plain={plain} sends={sends} enqueue={enqueue} firstOps={firstOps} secondOps={secondOps}");
        }

        [Test]
        public void WarmFramesAllocateNothingOnTheMainThreadAndEveryJobRanBurst()
        {
            using var t = new SvTestWorld(500, 512);
            t.Host.DigestEvery = 1;
            var inbox = t.Host.InboxWriter(0);
            for (var i = 0; i < 16; i++)
            {
                t.Frame();
            }

            SvProbe.ManagedRuns = 0;
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var f = 0; f < 200; f++)
            {
                inbox.Send(t.Slots[f % 500], -1);
                if ((f & 15) == 0)
                {
                    t.Host.Enqueue(StatOp.Add(t.Slots[f % 500], t.MaxHp, ModifierKind.Added, 1f, 100 + f));
                }

                t.Frame();
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
            Assert.AreEqual(0L, allocated);
            Assert.AreEqual(0, SvProbe.ManagedRuns, "a job ran managed");
        }
    }
}
