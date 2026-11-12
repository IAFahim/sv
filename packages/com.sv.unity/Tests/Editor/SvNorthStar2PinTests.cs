using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Sv.Unity.Tests
{
    public struct NsValue : IComponentData { public int Value; }
    public struct NsDelta : IComponentData { public int Value; }
    public struct NsFloor : IComponentData { public float Value; }
    public struct NsCeiling : IComponentData { public float Value; }

    public unsafe class SvNorthStar2PinTests
    {
        public const ulong BakedFold = 0x0D945D8FD131DA2Aul;
        public const ulong BakedLinks = 0xA47434070C42E4ACul;
        const int Rows = 4099;
        const int Ticks = 8;

        static ulong Next(ref ulong state)
        {
            unchecked
            {
                var z = state += 0x9E3779B97F4A7C15ul;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
                return z ^ (z >> 31);
            }
        }

        static T* Alloc<T>(int count) where T : unmanaged
        {
            var p = (T*)UnsafeUtility.Malloc(count * (long)sizeof(T), 64, Allocator.Persistent);
            UnsafeUtility.MemClear(p, count * (long)sizeof(T));
            return p;
        }

        [Test]
        public void TheFoldScenarioOnTheUnitySvCoreBuildMatchesTheDotNetPin()
        {
            var capacity = ((Rows + 63) >> 6) << 6;
            var value = Alloc<int>(capacity);
            var delta = Alloc<int>(capacity);
            var floor = Alloc<int>(capacity);
            var ceiling = Alloc<int>(capacity);
            var changed = Alloc<ulong>(capacity >> 6);
            using var inbox = new InboxBuffer(capacity);
            var state = 0x5EEDF01Dul;
            var digest = 0ul;
            for (var tick = 0; tick < Ticks; tick++)
            {
                for (var r = 0; r < Rows; r++)
                {
                    var a = Next(ref state);
                    if (tick == 0)
                    {
                        value[r] = (int)(a % 2000) - 500;
                        floor[r] = (int)((a >> 16) % 100) - 50;
                        ceiling[r] = (r % 11 == 0) ? floor[r] - 3 : floor[r] + 100 + (int)((a >> 24) % 1500);
                    }

                    delta[r] = (r % 5 == 0) ? 0 : (int)(Next(ref state) % 400) - 200;
                    if (r % 9 == 0)
                    {
                        Inbox.Send(inbox.Pointer, (int)(a % (ulong)Rows), (int)(a >> 40) % 1000);
                    }
                }

                Kernel.Fold(new Column<int>(value, capacity), Window.Range(0, Rows), new Column<int>(delta, capacity), Bounds.Rows(floor, ceiling), new Column<ulong>(changed, capacity >> 6), inbox.Column);
                digest = unchecked(digest * 0x100000001B3ul + CellDigest.Sum(1, value, 0, Rows));
                for (var w = 0; w < capacity >> 6; w++)
                {
                    digest = unchecked((digest ^ changed[w]) * 0x100000001B3ul);
                }
            }

            UnsafeUtility.Free(value, Allocator.Persistent);
            UnsafeUtility.Free(delta, Allocator.Persistent);
            UnsafeUtility.Free(floor, Allocator.Persistent);
            UnsafeUtility.Free(ceiling, Allocator.Persistent);
            UnsafeUtility.Free(changed, Allocator.Persistent);
            Assert.AreEqual(BakedFold, digest);
        }

        [Test]
        public void TheFoldScenarioThroughTheBurstFoldJobOnEntitiesMatchesTheDotNetPin()
        {
            var schema = SchemaBuilder.Create().Intrinsic("v").Build();
            using var board = Board.Create(schema, ((Rows + 63) >> 6) << 6);
            var links = new SvLinkRegistry();
            links.Intrinsic<NsValue, NsDelta>(schema.Intrinsic("v"), SvBound.OfStat<NsFloor>(), SvBound.OfStat<NsCeiling>(), true);
            var world = new World("ns2-fold");
            world.GetOrCreateSystemManaged<SimulationSystemGroup>();
            var host = SvHost.Install(world, board, links);
            var manager = world.EntityManager;
            var archetype = manager.CreateArchetype(typeof(SvSlot), typeof(NsValue), typeof(NsDelta), typeof(NsFloor), typeof(NsCeiling));
            var entities = new Entity[Rows];
            for (var r = 0; r < Rows; r++)
            {
                entities[r] = manager.CreateEntity(archetype);
                Assert.AreEqual(r, host.Bind(entities[r]));
            }

            var inbox = host.InboxWriter(0);
            var values = new int[Rows];
            var before = new int[Rows];
            var state = 0x5EEDF01Dul;
            var digest = 0ul;
            var group = world.GetExistingSystemManaged<SimulationSystemGroup>();
            SvProbe.ManagedRuns = 0;
            for (var tick = 0; tick < Ticks; tick++)
            {
                manager.CompleteAllTrackedJobs();
                for (var r = 0; r < Rows; r++)
                {
                    var a = Next(ref state);
                    if (tick == 0)
                    {
                        var v = (int)(a % 2000) - 500;
                        var f = (int)((a >> 16) % 100) - 50;
                        var c = (r % 11 == 0) ? f - 3 : f + 100 + (int)((a >> 24) % 1500);
                        manager.SetComponentData(entities[r], new NsValue { Value = v });
                        manager.SetComponentData(entities[r], new NsFloor { Value = f });
                        manager.SetComponentData(entities[r], new NsCeiling { Value = c });
                    }

                    var d = (r % 5 == 0) ? 0 : (int)(Next(ref state) % 400) - 200;
                    manager.SetComponentData(entities[r], new NsDelta { Value = d });
                    if (r % 9 == 0)
                    {
                        inbox.Send((int)(a % (ulong)Rows), (int)(a >> 40) % 1000);
                    }
                }

                for (var r = 0; r < Rows; r++)
                {
                    before[r] = manager.GetComponentData<NsValue>(entities[r]).Value;
                }

                group.Update();
                manager.CompleteAllTrackedJobs();
                for (var r = 0; r < Rows; r++)
                {
                    values[r] = manager.GetComponentData<NsValue>(entities[r]).Value;
                }

                fixed (int* p = values)
                {
                    digest = unchecked(digest * 0x100000001B3ul + CellDigest.Sum(1, p, 0, Rows));
                }

                for (var w = 0; w < (Rows + 63) >> 6; w++)
                {
                    var bits = 0ul;
                    for (var b = 0; b < 64 && (w << 6) + b < Rows; b++)
                    {
                        var r = (w << 6) + b;
                        bits |= (values[r] != before[r] ? 1ul : 0ul) << b;
                    }

                    digest = unchecked((digest ^ bits) * 0x100000001B3ul);
                }
            }

            host.Dispose();
            world.Dispose();
            Assert.AreEqual(0, SvProbe.ManagedRuns, "the fold job ran managed");
            Assert.AreEqual(BakedFold, digest);
        }

        [Test]
        public void TheNorthStar2LinkTableHashMatchesTheDotNetPin()
        {
            var columns = new[]
            {
                new ColumnDef(0x4D41585F4850, ColumnRole.StatOutput, 0, 0),
                new ColumnDef(0x41524D4F52, ColumnRole.StatOutput, 1, 0),
                new ColumnDef(0x4850, ColumnRole.Intrinsic, 0, 0),
                new ColumnDef(0x48504454, ColumnRole.Delta, 0, 2),
                new ColumnDef(0x4850494E, ColumnRole.Inbox, 0, 2),
                new ColumnDef(0x4C4F57, ColumnRole.Condition, 0, 0),
            };
            var table = new[]
            {
                new Link(1, 3, LinkMode.Delta),
                new Link(2, 3, LinkMode.Delta, LinkScope.Remote),
                new Link(2, 4, LinkMode.Delta, LinkScope.Remote),
                new Link(3, 1, LinkMode.Modifier),
                new Link(4, 5, LinkMode.Event),
                new Link(4, 0, LinkMode.Read),
                new Link(4, 2, LinkMode.Check),
            };
            Assert.AreEqual(LinkFault.None, Links.Validate(table, columns));
            Assert.AreEqual(BakedLinks, Links.Hash(table, columns));
        }
    }
}
