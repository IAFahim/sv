using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using Sv.Columns.Canon;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sv.Columns.Entities.Tests
{
    public sealed class ColumnsEntitiesTests
    {
        static string Arena() => File.ReadAllText(Path.GetFullPath("Packages/com.sv.columns/Tests/Packs~/arena.json"));

        static string Hex(ulong value) => value.ToString("X16");

        static void AssertBurst(ColumnsBurstHost host)
        {
            Debug.Log("SVCOLUMNS entities burst enabled " + BurstCompiler.IsEnabled + ", bucket probe " + (host.BucketRanBurst ? 1 : 0) + ", resolve probe " + (host.ResolveRanBurst ? 1 : 0) + ", threads " + host.Records.ThreadSets + ", parts " + host.World.ResolveParts);
            if (BurstCompiler.IsEnabled)
            {
                Assert.IsTrue(host.BucketRanBurst, "the bucket job ran managed");
                Assert.IsTrue(host.ResolveRanBurst, "the resolve job ran managed");
            }
        }

        [TestCase(FrameKernel.Rows, 1, 1)]
        [TestCase(FrameKernel.Rows, 3, 7)]
        [TestCase(FrameKernel.Rows, 2, 0)]
        [TestCase(FrameKernel.Scalar, 1, 1)]
        [TestCase(FrameKernel.Scalar, 2, 5)]
        public void TheCanonRunOnTheBurstHostHasTheDotNetDigest(FrameKernel kernel, int writers, int parts)
        {
            using var pack = PackJson.Read(Arena()).Compile();
            using var host = new ColumnsBurstHost(pack, CanonRun.Capacity, writers, parts, kernel);
            Assert.AreEqual(Hex(CanonRun.Pinned), Hex(CanonRun.Run(host.World, host.Step)));
            AssertBurst(host);
        }

        [TestCase(FrameKernel.Rows, 1)]
        [TestCase(FrameKernel.Rows, 0)]
        [TestCase(FrameKernel.Scalar, 3)]
        public void TheCanonRunSentFromABurstJobHasTheDotNetDigest(FrameKernel kernel, int parts)
        {
            using var pack = PackJson.Read(Arena()).Compile();
            using var host = new ColumnsBurstHost(pack, CanonRun.Capacity, 1, parts, kernel);
            using var traffic = new ColumnsTraffic(4000);
            Assert.AreEqual(Hex(CanonRun.Pinned), Hex(BurstCanon(host, traffic)));
            Assert.AreEqual(BurstCompiler.IsEnabled, traffic.RanBurst);
            AssertBurst(host);
        }

        static ulong BurstCanon(ColumnsBurstHost host, ColumnsTraffic traffic)
        {
            var world = host.World;
            var pack = world.Pack;
            var slots = new int[600];
            var count = 0;
            for (var t = 0; t < pack.TemplateNames.Count; t++)
            {
                for (var i = 0; i < 60; i++)
                {
                    slots[count++] = world.Spawn(t);
                }
            }

            var channels = pack.ChannelNames.Count;
            var state = 0x2545F491u;
            var hash = 0xCBF29CE484222325ul;
            for (var frame = 0; frame < CanonRun.Frames; frame++)
            {
                if (frame == 12)
                {
                    world.Set(slots[7], pack.Lane("ImmuneMask"), 0b101);
                    world.Set(slots[70], pack.Lane("KindMask"), 0b10);
                }

                if (frame == 24)
                {
                    world.Despawn(slots[3]);
                    slots[3] = world.Spawn(pack.Template("phoenix"));
                }

                for (var r = 0; r < 4000; r++)
                {
                    state ^= state << 13;
                    state ^= state >> 17;
                    state ^= state << 5;
                    var target = slots[(int)((state >> 7) % (uint)count)];
                    var channel = (int)((state >> 11) % (uint)channels);
                    var amount = (int)((state >> 15) % 41u) - 12;
                    var hits = 1 + (int)((state >> 23) & 3u);
                    if ((state & 7u) == 0)
                    {
                        traffic.SendFrom(slots[(int)((state >> 9) % (uint)count)], target, channel, amount, hits);
                    }
                    else
                    {
                        traffic.Send(target, channel, amount, hits);
                    }
                }

                host.Schedule(traffic.Schedule(host.Sender));
                host.Complete();
                hash = (hash ^ world.Digest()) * 0x100000001B3ul;
            }

            return hash;
        }

        [Test]
        public void TheBurstHostMatchesTheClassicStepUnderRandomTraffic()
        {
            using var pack = PackJson.Read(Arena()).Compile();
            var lanes = new List<int>();
            for (var l = 0; l < pack.LaneNames.Count; l++)
            {
                if (pack.LaneNames[l][0] != '@')
                {
                    lanes.Add(l);
                }
            }

            using var traffic = new ColumnsTraffic(1200);
            for (var seed = 1; seed <= 6; seed++)
            {
                var dice = new System.Random(seed);
                var writers = 1 + seed % 3;
                var parts = 1 + seed % 5;
                using var host = new ColumnsBurstHost(pack, 2048, writers, parts, seed % 2 == 0 ? FrameKernel.Rows : FrameKernel.Scalar);
                using var classic = new ColumnWorld(pack, 2048, writers, parts);
                var live = new List<int>();
                for (var i = dice.Next(50, 400); i > 0; i--)
                {
                    var template = dice.Next(pack.TemplateNames.Count);
                    var slot = classic.Spawn(template);
                    Assert.AreEqual(slot, host.World.Spawn(template));
                    live.Add(slot);
                }

                for (var frame = 0; frame < 30; frame++)
                {
                    for (var i = dice.Next(0, 1200); i > 0; i--)
                    {
                        var target = dice.Next(4) == 0 ? dice.Next(-3, 2100) : live[dice.Next(live.Count)];
                        var channel = dice.Next(4) == 0 ? dice.Next(-1, 18) : dice.Next(pack.ChannelNames.Count);
                        var amount = dice.Next(6) == 0 ? (long)dice.Next(-1, 2) << 40 : dice.Next(-30, 51);
                        var hits = dice.Next(1, 4);
                        var source = live[dice.Next(live.Count)];
                        var road = dice.Next(10);
                        if (road == 0)
                        {
                            var raw = ((ulong)(uint)dice.Next(0, 1 << 22) << Record.TargetShift) | (uint)dice.Next(0, 1 << 20);
                            classic.Writer(0).SendRecord(raw);
                            traffic.SendRecord(raw);
                        }
                        else if (road < 3)
                        {
                            var writer = dice.Next(writers);
                            classic.Writer(writer).SendFrom(source, target, channel, amount, hits);
                            host.World.Writer(writer).SendFrom(source, target, channel, amount, hits);
                        }
                        else if (road < 5)
                        {
                            classic.Writer(0).SendFrom(source, target, channel, amount, hits);
                            traffic.SendFrom(source, target, channel, amount, hits);
                        }
                        else
                        {
                            classic.Writer(0).Send(target, channel, amount, hits);
                            traffic.Send(target, channel, amount, hits);
                        }
                    }

                    if (dice.Next(3) == 0)
                    {
                        var slot = live[dice.Next(live.Count)];
                        var lane = lanes[dice.Next(lanes.Count)];
                        var value = dice.Next(10) == 0 ? dice.Next(int.MinValue / 2, int.MaxValue / 2) : dice.Next(-10, 90);
                        classic.Set(slot, lane, value);
                        host.World.Set(slot, lane, value);
                    }

                    if (dice.Next(20) == 0 && live.Count > 1)
                    {
                        var gone = live[dice.Next(live.Count)];
                        live.Remove(gone);
                        classic.Despawn(gone);
                        host.World.Despawn(gone);
                    }

                    if (dice.Next(20) == 0)
                    {
                        var template = dice.Next(pack.TemplateNames.Count);
                        var slot = classic.Spawn(template);
                        Assert.AreEqual(slot, host.World.Spawn(template));
                        live.Add(slot);
                    }

                    classic.Step();
                    host.Schedule(traffic.Schedule(host.Sender));
                    host.Complete();
                    Assert.AreEqual(Hex(classic.Digest()), Hex(host.World.Digest()), "seed " + seed + " frame " + frame);
                    Assert.AreEqual(classic.Dropped, host.World.Dropped, "seed " + seed + " frame " + frame);
                }

                AssertBurst(host);
            }
        }

        [Test]
        public void TheCanonLateInputRunOnTheBurstHostIsPinned()
        {
            using var pack = PackJson.Read(Arena()).Compile();
            var shapes = new[] { (FrameKernel.Rows, 1, 1), (FrameKernel.Rows, 3, 7), (FrameKernel.Scalar, 2, 5) };
            foreach (var (kernel, writers, parts) in shapes)
            {
                using var host = new ColumnsBurstHost(pack, CanonRun.Capacity, writers, parts, kernel, history: CanonRun.LateHistory);
                var digest = CanonRun.RunWithLateInput(host.World, host.Step, host.Resimulate);
                Assert.AreEqual(Hex(CanonRun.PinnedLate), Hex(digest), kernel + " writers " + writers + " parts " + parts);
                AssertBurst(host);
            }
        }

        static List<int> Alive(ColumnWorld world)
        {
            var slots = new List<int>();
            for (var slot = 0; slot < world.Capacity; slot++)
            {
                if (world.IsAlive(slot))
                {
                    slots.Add(slot);
                }
            }

            return slots;
        }

        static void SameInputs(ColumnWorld classic, ColumnsBurstHost host, string where)
        {
            for (var f = classic.OldestFrame; f < classic.Frame; f++)
            {
                var expected = classic.Inputs(f);
                var actual = host.Inputs(f);
                Assert.AreEqual(expected.ActionCount, actual.ActionCount, where + " actions of frame " + f);
                Assert.AreEqual(expected.RecordCount, actual.RecordCount, where + " records of frame " + f);
                var left = new ulong[expected.RecordCount];
                var right = new ulong[actual.RecordCount];
                for (var i = 0; i < left.Length; i++)
                {
                    left[i] = expected.Record(i);
                    right[i] = actual.Record(i);
                }

                Array.Sort(left);
                Array.Sort(right);
                CollectionAssert.AreEqual(left, right, where + " record set of frame " + f);
            }
        }

        [Test]
        public void TheBurstHostWithHistoryMatchesTheClassicWorldThroughLateInputsAndRewinds()
        {
            using var pack = PackJson.Read(Arena()).Compile();
            using var traffic = new ColumnsTraffic(1200);
            for (var seed = 1; seed <= 6; seed++)
            {
                var dice = new System.Random(100 + seed);
                var writers = 1 + seed % 3;
                var parts = 1 + seed % 5;
                var structure = seed % 2 == 1;
                using var host = new ColumnsBurstHost(pack, 2048, writers, parts, seed % 3 == 0 ? FrameKernel.Scalar : FrameKernel.Rows, history: 8);
                using var classic = new ColumnWorld(pack, 2048, writers, parts, history: 8);
                for (var i = dice.Next(50, 400); i > 0; i--)
                {
                    var template = dice.Next(pack.TemplateNames.Count);
                    Assert.AreEqual(classic.Spawn(template), host.World.Spawn(template));
                }

                for (var frame = 0; frame < 40; frame++)
                {
                    var live = Alive(classic);
                    var where = "seed " + seed + " frame " + frame;
                    if (frame > 3 && dice.Next(4) == 0)
                    {
                        var at = Math.Max(classic.OldestFrame, classic.Frame - 1 - dice.Next(6));
                        if (dice.Next(5) == 0)
                        {
                            classic.Rewind(at);
                            host.Rewind(at);
                            Assert.AreEqual(Hex(classic.Digest()), Hex(host.World.Digest()), where + " rewind to " + at);
                            continue;
                        }

                        for (var i = dice.Next(1, 4); i > 0; i--)
                        {
                            var target = live[dice.Next(live.Count)];
                            var road = dice.Next(structure ? 5 : 3);
                            if (road == 0)
                            {
                                var raw = ((ulong)(uint)target << Record.TargetShift) | (uint)dice.Next(0, 1 << 20);
                                classic.Inputs(at).AddRecord(raw);
                                host.Inputs(at).AddRecord(raw);
                            }
                            else if (road == 1)
                            {
                                var send = ColumnAction.Send(target, dice.Next(pack.ChannelNames.Count), dice.Next(-20, 60), dice.Next(1, 3));
                                classic.Inputs(at).AddAction(send);
                                host.Inputs(at).AddAction(send);
                            }
                            else if (road == 2)
                            {
                                var send = ColumnAction.SendFrom(live[dice.Next(live.Count)], target, dice.Next(pack.ChannelNames.Count), dice.Next(-20, 60), 1);
                                classic.Inputs(at).InsertAction(0, send);
                                host.Inputs(at).InsertAction(0, send);
                            }
                            else if (road == 3)
                            {
                                var spawn = ColumnAction.Spawn(dice.Next(pack.TemplateNames.Count));
                                classic.Inputs(at).AddAction(spawn);
                                host.Inputs(at).AddAction(spawn);
                            }
                            else
                            {
                                var gone = ColumnAction.Despawn(target);
                                classic.Inputs(at).AddAction(gone);
                                host.Inputs(at).AddAction(gone);
                            }
                        }

                        classic.Resimulate();
                        host.Resimulate();
                        Assert.AreEqual(Hex(classic.Digest()), Hex(host.World.Digest()), where + " resimulated from " + at);
                        Assert.AreEqual(classic.Dropped, host.World.Dropped, where + " resimulated from " + at);
                        SameInputs(classic, host, where);
                        live = Alive(classic);
                    }

                    if (dice.Next(4) == 0)
                    {
                        var template = dice.Next(pack.TemplateNames.Count);
                        Assert.AreEqual(classic.Spawn(template), host.World.Spawn(template));
                    }

                    if (dice.Next(6) == 0 && live.Count > 1)
                    {
                        var gone = live[dice.Next(live.Count)];
                        classic.Despawn(gone);
                        host.World.Despawn(gone);
                    }

                    live = Alive(classic);
                    for (var i = dice.Next(0, 1200); i > 0; i--)
                    {
                        var target = dice.Next(5) == 0 ? dice.Next(-3, 2100) : live[dice.Next(live.Count)];
                        var channel = dice.Next(5) == 0 ? dice.Next(-1, 18) : dice.Next(pack.ChannelNames.Count);
                        var amount = dice.Next(-30, 51);
                        var source = dice.Next(8) == 0 ? dice.Next(-2, 2100) : live[dice.Next(live.Count)];
                        var road = dice.Next(10);
                        if (road == 0)
                        {
                            var raw = ((ulong)(uint)dice.Next(0, 1 << 22) << Record.TargetShift) | (uint)dice.Next(0, 1 << 20);
                            classic.Writer(0).SendRecord(raw);
                            traffic.SendRecord(raw);
                        }
                        else if (road < 3)
                        {
                            var writer = dice.Next(writers);
                            classic.Writer(writer).SendFrom(source, target, channel, amount, 1);
                            host.World.Writer(writer).SendFrom(source, target, channel, amount, 1);
                        }
                        else if (road < 5)
                        {
                            classic.Writer(0).SendFrom(source, target, channel, amount, 2);
                            traffic.SendFrom(source, target, channel, amount, 2);
                        }
                        else
                        {
                            classic.Writer(0).Send(target, channel, amount, 1);
                            traffic.Send(target, channel, amount, 1);
                        }
                    }

                    classic.Step();
                    host.Schedule(traffic.Schedule(host.Sender));
                    host.Complete();
                    Assert.AreEqual(Hex(classic.Digest()), Hex(host.World.Digest()), where);
                    Assert.AreEqual(classic.Dropped, host.World.Dropped, where);
                }

                SameInputs(classic, host, "seed " + seed);
                AssertBurst(host);
            }
        }

        [Test]
        public void WarmBurstFramesWithHistoryAndAWarmResimulateAllocateNothing()
        {
            using var pack = PackJson.Read(Arena()).Compile();
            using var host = new ColumnsBurstHost(pack, 4096, 2, 0, history: 16);
            using var traffic = new ColumnsTraffic(4096);
            var fire = pack.Channel("Fire");
            var slots = new int[3000];
            for (var i = 0; i < slots.Length; i++)
            {
                slots[i] = host.World.Spawn(i % pack.TemplateNames.Count);
            }

            void Frames(int count)
            {
                for (var f = 0; f < count; f++)
                {
                    for (var i = 0; i < slots.Length; i++)
                    {
                        traffic.Send(slots[i], fire, 1, 1);
                    }

                    host.World.Writer(f & 1).Send(slots[f % slots.Length], fire, 2);
                    host.Schedule(traffic.Schedule(host.Sender));
                    host.Complete();
                    if ((f & 7) == 7)
                    {
                        host.Inputs(host.World.Frame - 5).AddRecord(((ulong)(uint)slots[f] << Record.TargetShift) | (uint)fire);
                        host.Resimulate();
                    }
                }
            }

            Frames(64);
            var windows = new long[4];
            var start = GC.GetAllocatedBytesForCurrentThread();
            windows[0] = GC.GetAllocatedBytesForCurrentThread() - start;
            for (var w = 1; w < windows.Length; w++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                Frames(32);
                windows[w] = GC.GetAllocatedBytesForCurrentThread() - before;
            }

            Debug.Log("SVCOLUMNS entities warm allocation per 32 burst frames with history and 4 resimulates, after an empty window of " + windows[0] + " B: " + string.Join(", ", Array.ConvertAll(windows, w => w.ToString()), 1, windows.Length - 1) + " B");
            Assert.AreEqual(0L, windows[windows.Length - 1] - windows[0]);
            AssertBurst(host);
        }

        [Test]
        public void WarmBurstFramesAllocateNothing()
        {
            using var pack = PackJson.Read(Arena()).Compile();
            using var host = new ColumnsBurstHost(pack, 4096, 2, 0);
            using var traffic = new ColumnsTraffic(4096);
            var fire = pack.Channel("Fire");
            var slots = new int[3000];
            for (var i = 0; i < slots.Length; i++)
            {
                slots[i] = host.World.Spawn(i % pack.TemplateNames.Count);
            }

            void Frames(int count)
            {
                for (var f = 0; f < count; f++)
                {
                    for (var i = 0; i < slots.Length; i++)
                    {
                        traffic.Send(slots[i], fire, 1, 1);
                    }

                    host.World.Writer(f & 1).Send(slots[f % slots.Length], fire, 2);
                    host.Schedule(traffic.Schedule(host.Sender));
                    host.Complete();
                }
            }

            Frames(64);
            var windows = new long[4];
            var start = GC.GetAllocatedBytesForCurrentThread();
            windows[0] = GC.GetAllocatedBytesForCurrentThread() - start;
            for (var w = 1; w < windows.Length; w++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                Frames(32);
                windows[w] = GC.GetAllocatedBytesForCurrentThread() - before;
            }

            Debug.Log("SVCOLUMNS entities warm allocation per 32 burst frames, after an empty window of " + windows[0] + " B: " + string.Join(", ", Array.ConvertAll(windows, w => w.ToString()), 1, windows.Length - 1) + " B");
            Assert.AreEqual(0L, windows[windows.Length - 1] - windows[0]);
        }

        [Test]
        public void TheHostRefusesWorkAfterDispose()
        {
            var pack = PackJson.Read(Arena()).Compile();
            var host = new ColumnsBurstHost(pack, 256, 1, 1, FrameKernel.Rows, true);
            host.Schedule();
            Assert.IsTrue(host.Running);
            host.Dispose();
            host.Dispose();
            Assert.Throws<ObjectDisposedException>(() => host.Schedule());
            Assert.Throws<ObjectDisposedException>(() => new ColumnWorld(pack, 64).Dispose());
        }

        sealed class Mirror : IDisposable
        {
            public readonly CompiledPack Pack;
            public readonly ColumnWorld World;

            public Mirror(string json, int capacity, int parts, int history = 0)
            {
                Pack = PackJson.Read(json).Compile();
                World = new ColumnWorld(Pack, capacity, 1, parts, history: history);
            }

            public void Dispose()
            {
                World.Dispose();
                Pack.Dispose();
            }
        }

        static Entity HostEntity(EntityManager manager)
        {
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<ColumnsHostHandle>());
            return query.GetSingletonEntity();
        }

        [Test]
        public void TheStepSystemSpawnsStepsAndDespawnsEntitiesLikeTheClassicWorld()
        {
            var json = Arena();
            var blob = ColumnsPacks.Create(json);
            using var mirror = new Mirror(json, 1024, 3);
            var world = new World("sv columns test");
            try
            {
                var manager = world.EntityManager;
                var system = world.CreateSystem<ColumnsStepSystem>();
                var config = manager.CreateEntity(typeof(ColumnsWorldConfig));
                manager.SetComponentData(config, new ColumnsWorldConfig { Pack = blob, Capacity = 1024, Writers = 1, ResolveParts = 3, Kernel = FrameKernel.Rows });
                var pack = mirror.Pack;
                var health = pack.Lane("Health");
                var shield = pack.Lane("Shield");
                var entities = new List<Entity>();
                for (var i = 0; i < 200; i++)
                {
                    var entity = manager.CreateEntity(typeof(ColumnsTemplate));
                    manager.SetComponentData(entity, new ColumnsTemplate { Value = i % pack.TemplateNames.Count });
                    if (i % 7 == 0)
                    {
                        var values = manager.AddBuffer<ColumnsLaneValue>(entity);
                        values.Add(new ColumnsLaneValue { Lane = health, Value = 40 + i });
                        values.Add(new ColumnsLaneValue { Lane = shield, Value = i });
                    }

                    entities.Add(entity);
                }

                var slots = new Dictionary<Entity, int>();
                var spawnOrder = new List<Entity>(entities);
                spawnOrder.Sort();
                foreach (var entity in spawnOrder)
                {
                    var index = entities.IndexOf(entity);
                    var slot = mirror.World.Spawn(index % pack.TemplateNames.Count);
                    if (index % 7 == 0)
                    {
                        mirror.World.Set(slot, health, 40 + index);
                        mirror.World.Set(slot, shield, index);
                    }

                    slots[entity] = slot;
                }

                system.Update(world.Unmanaged);
                mirror.World.Step();
                var host = ColumnsStepSystem.HostOf(manager, HostEntity(manager));
                host.Complete();
                Assert.AreEqual(Hex(mirror.World.Digest()), Hex(host.World.Digest()));
                foreach (var entity in entities)
                {
                    Assert.AreEqual(slots[entity], manager.GetComponentData<ColumnsSlot>(entity).Value);
                }

                var channels = pack.ChannelNames.Count;
                for (var frame = 0; frame < 20; frame++)
                {
                    var sender = manager.GetComponentData<ColumnsState>(HostEntity(manager)).Sender;
                    for (var i = 0; i < 300; i++)
                    {
                        var target = slots[entities[(i * 31 + frame) % entities.Count]];
                        var channel = (i + frame) % channels;
                        var amount = (i * 13 + frame) % 41 - 12;
                        sender.Send(target, channel, amount);
                        mirror.World.Writer(0).Send(target, channel, amount);
                    }

                    if (frame == 5)
                    {
                        var gone = new List<int>();
                        for (var i = 3; i < entities.Count; i += 9)
                        {
                            gone.Add(slots[entities[i]]);
                            manager.DestroyEntity(entities[i]);
                        }

                        gone.Sort();
                        foreach (var slot in gone)
                        {
                            mirror.World.Despawn(slot);
                        }

                        for (var i = entities.Count - 1; i >= 3; i--)
                        {
                            if ((i - 3) % 9 == 0)
                            {
                                entities.RemoveAt(i);
                            }
                        }
                    }

                    if (frame == 9)
                    {
                        var dropped = entities[10];
                        manager.RemoveComponent<ColumnsTemplate>(dropped);
                        mirror.World.Despawn(slots[dropped]);
                        entities.Remove(dropped);
                        system.Update(world.Unmanaged);
                        mirror.World.Step();
                        host.Complete();
                        Assert.IsFalse(manager.HasComponent<ColumnsSlot>(dropped));
                        Assert.IsFalse(manager.HasComponent<ColumnsSlotCleanup>(dropped));
                        Assert.AreEqual(Hex(mirror.World.Digest()), Hex(host.World.Digest()));
                        continue;
                    }

                    if (frame == 12)
                    {
                        var born = manager.CreateEntity(typeof(ColumnsTemplate));
                        manager.SetComponentData(born, new ColumnsTemplate { Value = pack.Template("phoenix") });
                        slots[born] = mirror.World.Spawn(pack.Template("phoenix"));
                        entities.Add(born);
                    }

                    system.Update(world.Unmanaged);
                    mirror.World.Step();
                    host.Complete();
                    Assert.AreEqual(Hex(mirror.World.Digest()), Hex(host.World.Digest()), "frame " + frame);
                    Assert.AreEqual(mirror.World.Dropped, host.World.Dropped);
                }

                var view = manager.GetComponentData<ColumnsState>(HostEntity(manager)).View;
                foreach (var entity in entities)
                {
                    var slot = manager.GetComponentData<ColumnsSlot>(entity).Value;
                    Assert.AreEqual(slots[entity], slot);
                    Assert.AreEqual(mirror.World.Get(slot, health), view.Get(slot, health));
                }

                AssertBurst(host);
            }
            finally
            {
                world.Dispose();
                blob.Dispose();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static WeakReference HostIn(World world)
        {
            var manager = world.EntityManager;
            return new WeakReference(ColumnsStepSystem.HostOf(manager, HostEntity(manager)));
        }

        [Test]
        public void TheStepSystemKeepsHistoryAndResimulatesLateInputsLikeTheClassicWorld()
        {
            var json = Arena();
            var blob = ColumnsPacks.Create(json);
            using var mirror = new Mirror(json, 1024, 3, 8);
            var world = new World("sv columns history test");
            try
            {
                var manager = world.EntityManager;
                var system = world.CreateSystem<ColumnsStepSystem>();
                var config = manager.CreateEntity(typeof(ColumnsWorldConfig));
                manager.SetComponentData(config, new ColumnsWorldConfig { Pack = blob, Capacity = 1024, Writers = 1, ResolveParts = 3, Kernel = FrameKernel.Rows, History = 8 });
                var pack = mirror.Pack;
                var entities = new List<Entity>();
                for (var i = 0; i < 150; i++)
                {
                    var entity = manager.CreateEntity(typeof(ColumnsTemplate));
                    manager.SetComponentData(entity, new ColumnsTemplate { Value = i % pack.TemplateNames.Count });
                    entities.Add(entity);
                }

                var spawnOrder = new List<Entity>(entities);
                spawnOrder.Sort();
                var slots = new Dictionary<Entity, int>();
                foreach (var entity in spawnOrder)
                {
                    slots[entity] = mirror.World.Spawn(entities.IndexOf(entity) % pack.TemplateNames.Count);
                }

                system.Update(world.Unmanaged);
                mirror.World.Step();
                var host = ColumnsStepSystem.HostOf(manager, HostEntity(manager));
                host.Complete();
                Assert.AreEqual(Hex(mirror.World.Digest()), Hex(host.World.Digest()));
                var channels = pack.ChannelNames.Count;
                var fire = pack.Channel("Fire");
                for (var frame = 0; frame < 24; frame++)
                {
                    var sender = manager.GetComponentData<ColumnsState>(HostEntity(manager)).Sender;
                    for (var i = 0; i < 200; i++)
                    {
                        var target = slots[entities[(i * 17 + frame) % entities.Count]];
                        var channel = (i + frame) % channels;
                        var amount = (i * 7 + frame) % 37 - 10;
                        sender.Send(target, channel, amount);
                        mirror.World.Writer(0).Send(target, channel, amount);
                    }

                    system.Update(world.Unmanaged);
                    mirror.World.Step();
                    host.Complete();
                    Assert.AreEqual(Hex(mirror.World.Digest()), Hex(host.World.Digest()), "frame " + frame);
                    if (frame % 6 == 5)
                    {
                        var at = host.World.Frame - 4;
                        var target = slots[entities[frame]];
                        var late = ((ulong)(uint)target << Record.TargetShift) | (uint)frame;
                        mirror.World.Inputs(at).AddRecord(late);
                        host.Inputs(at).AddRecord(late);
                        var send = ColumnAction.Send(slots[entities[frame + 1]], fire, 30, 2);
                        mirror.World.Inputs(at).AddAction(send);
                        host.Inputs(at).AddAction(send);
                        mirror.World.Resimulate();
                        host.Resimulate();
                        Assert.AreEqual(mirror.World.Frame, host.World.Frame);
                        Assert.AreEqual(Hex(mirror.World.Digest()), Hex(host.World.Digest()), "resimulated at frame " + frame);
                        Assert.AreEqual(mirror.World.Dropped, host.World.Dropped);
                    }
                }

                system.Update(world.Unmanaged);
                host.Complete();
                var state = manager.GetComponentData<ColumnsState>(HostEntity(manager));
                Assert.AreEqual(host.World.Frame - 1, state.Frame);
                AssertBurst(host);
            }
            finally
            {
                world.Dispose();
                blob.Dispose();
            }
        }

        [Test]
        public void DisposingTheWorldFreesTheHost()
        {
            var blob = ColumnsPacks.Create(Arena());
            var world = new World("sv columns dispose");
            WeakReference host;
            try
            {
                var system = world.CreateSystem<ColumnsStepSystem>();
                var config = world.EntityManager.CreateEntity(typeof(ColumnsWorldConfig));
                world.EntityManager.SetComponentData(config, new ColumnsWorldConfig { Pack = blob, Capacity = 512, Writers = 1 });
                system.Update(world.Unmanaged);
                system.Update(world.Unmanaged);
                host = HostIn(world);
            }
            finally
            {
                world.Dispose();
                blob.Dispose();
            }

            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            Assert.IsFalse(host.IsAlive);
        }

        static void Bake(World world, BlobAssetStore store, params GameObject[] roots)
        {
            var hybrid = typeof(Baker<>).Assembly;
            var utility = hybrid.GetType("Unity.Entities.BakingUtility", true)!;
            MethodInfo? bake = null;
            foreach (var method in utility.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var parameters = method.GetParameters();
                if (method.Name == "BakeGameObjects" && parameters.Length == 3 && parameters[1].ParameterType == typeof(GameObject[]))
                {
                    bake = method;
                }
            }

            Assert.IsNotNull(bake, "BakingUtility.BakeGameObjects(World, GameObject[], BakingSettings) is gone");
            var settingsType = bake!.GetParameters()[2].ParameterType;
            object? settings = null;
            foreach (var constructor in settingsType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var parameters = constructor.GetParameters();
                if (parameters.Length == 2 && parameters[0].ParameterType.IsEnum && parameters[1].ParameterType == typeof(BlobAssetStore))
                {
                    settings = constructor.Invoke(new object[] { Enum.Parse(parameters[0].ParameterType, "AssignName"), store });
                }
            }

            Assert.IsNotNull(settings, "BakingSettings(BakingFlags, BlobAssetStore) is gone");
            bake.Invoke(null, new object[] { world, roots, settings! });
        }

        [Test]
        public void BakedAuthoringStepsInTheSystem()
        {
            var json = Arena();
            var pack = new TextAsset(json);
            var columns = new GameObject("columns world");
            var unit = new GameObject("unit");
            var store = new BlobAssetStore(128);
            var world = new World("sv columns bake");
            try
            {
                var authoring = columns.AddComponent<ColumnsWorldAuthoring>();
                authoring.Pack = pack;
                authoring.Capacity = 256;
                authoring.ResolveParts = 2;
                var entity = unit.AddComponent<ColumnsEntityAuthoring>();
                entity.World = authoring;
                entity.Template = "burning";
                entity.Lanes = new[] { new ColumnsEntityAuthoring.LaneOverride { Lane = "Health", Value = 60 } };
                Bake(world, store, columns, unit);
                var manager = world.EntityManager;
                using var configs = manager.CreateEntityQuery(ComponentType.ReadOnly<ColumnsWorldConfig>());
                var config = configs.GetSingleton<ColumnsWorldConfig>();
                Assert.AreEqual(256, config.Capacity);
                Assert.AreEqual(json, ColumnsPacks.JsonOf(ref config.Pack.Value));
                using var compiled = PackJson.Read(json).Compile();
                using var units = manager.CreateEntityQuery(ComponentType.ReadOnly<ColumnsTemplate>());
                var baked = units.GetSingletonEntity();
                Assert.AreEqual(compiled.Template("burning"), manager.GetComponentData<ColumnsTemplate>(baked).Value);
                var lanes = manager.GetBuffer<ColumnsLaneValue>(baked);
                Assert.AreEqual(1, lanes.Length);
                Assert.AreEqual(compiled.Lane("Health"), lanes[0].Lane);
                var system = world.CreateSystem<ColumnsStepSystem>();
                for (var f = 0; f < 3; f++)
                {
                    system.Update(world.Unmanaged);
                }

                var host = ColumnsStepSystem.HostOf(manager, HostEntity(manager));
                host.Complete();
                var slot = manager.GetComponentData<ColumnsSlot>(baked).Value;
                Assert.IsTrue(host.View.IsAlive(slot));
                Assert.Less(host.View.Get(slot, compiled.Lane("Health")), 60);
            }
            finally
            {
                world.Dispose();
                store.Dispose();
                UnityEngine.Object.DestroyImmediate(columns);
                UnityEngine.Object.DestroyImmediate(unit);
                UnityEngine.Object.DestroyImmediate(pack);
            }
        }

        [UnityTest]
        public IEnumerator InPlayModeTheDefaultWorldStepsTheColumns()
        {
            yield return new EnterPlayMode();
            var world = World.DefaultGameObjectInjectionWorld;
            Assert.IsNotNull(world);
            var manager = world.EntityManager;
            var blob = ColumnsPacks.Create(Arena());
            using var compiled = PackJson.Read(Arena()).Compile();
            var config = manager.CreateEntity(typeof(ColumnsWorldConfig));
            manager.SetComponentData(config, new ColumnsWorldConfig { Pack = blob, Capacity = 512, Writers = 1, ResolveParts = (JobsUtility.JobWorkerCount + 1) * 2 });
            var unit = manager.CreateEntity(typeof(ColumnsTemplate));
            manager.SetComponentData(unit, new ColumnsTemplate { Value = compiled.Template("burning") });
            for (var f = 0; f < 6; f++)
            {
                yield return null;
            }

            var states = manager.CreateEntityQuery(ComponentType.ReadOnly<ColumnsState>());
            states.CompleteDependency();
            var state = states.GetSingleton<ColumnsState>();
            states.Dispose();
            Assert.Greater(state.Frame, 0);
            var slot = manager.GetComponentData<ColumnsSlot>(unit).Value;
            Assert.Less(state.View.Get(slot, compiled.Lane("Health")), 100);
            if (BurstCompiler.IsEnabled)
            {
                Assert.IsTrue(state.BurstBucket && state.BurstResolve);
            }

            yield return new ExitPlayMode();
            blob.Dispose();
        }
    }
}
