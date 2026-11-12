using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Sv.NorthStar2;
using Tl;
using Tl.Gen.Tlb;
using Tl.Unity;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs.LowLevel.Unsafe;

namespace Sv.Unity.TlTests
{
    public struct NsHp : IComponentData { public int Value; }
    public struct NsMaxHp : IComponentData { public float Value; }
    public struct NsArmor : IComponentData { public float Value; }
    public struct NsLow : IComponentData, IEnableableComponent { }
    public struct NsTarget : IComponentData { public int Value; }

    public unsafe class SvNorthStar2AdapterTests
    {
        public static readonly ulong[] Twin1000 = { 0x939FBDEE2E7DEE50ul, 0xE5CE5FA31824A22Eul, 0x75035C43BBDC14AFul, 0xE9C2A3B5AB8D2EC5ul, 0xE9C2A3B5AB8D2EC5ul, 0xE9C2A3B5AB8D2EC5ul };
        public static readonly ulong[] TwinMillion = { 0xC02336ECA6D817FAul, 0x139FA001187F8423ul, 0x08D2B1F0B05E6B55ul, 0x2E7A61280D20C9F4ul, 0x2E7A61280D20C9F4ul, 0x2E7A61280D20C9F4ul };
        const int Ticks = 6;
        const ushort Brawl = 1;
        const ushort Guard = 2;
        const int ColumnHp = 1;
        const int ColumnDelta = 2;
        const int ColumnMaxHp = 3;
        const int ColumnArmor = 4;
        const int ColumnLow = 5;
        const int ColumnInbox = 6;
        const int ColumnOutgoing = 8;

        enum Kind : byte { Start, Add, Remove, Send, Local }

        struct Command
        {
            public Kind Kind;
            public int Slot;
            public int Amount;
            public int Source;
            public ushort Stat;
            public ModifierKind Modifier;
            public float Value;
            public long ExpiresAt;

            public static Command Start(int slot, ushort timeline) => new Command { Kind = Kind.Start, Slot = slot, Amount = timeline };

            public static Command Add(int slot, ushort stat, ModifierKind kind, float value, int source, long expiresAt = long.MaxValue) => new Command { Kind = Kind.Add, Slot = slot, Stat = stat, Modifier = kind, Value = value, Source = source, ExpiresAt = expiresAt };

            public static Command Remove(int slot, int source) => new Command { Kind = Kind.Remove, Slot = slot, Source = source };

            public static Command Send(int slot, int amount) => new Command { Kind = Kind.Send, Slot = slot, Amount = amount };

            public static Command Local(int slot, int amount) => new Command { Kind = Kind.Local, Slot = slot, Amount = amount };
        }

        public struct LowHp
        {
            public int Slot;
            public int Entered;
        }

        struct LowHpMaker : IEventMaker<LowHp>
        {
            public LowHp Make(int row, int value) => new LowHp { Slot = row, Entered = value };
        }

        static List<Command>[] Journal(int rows)
        {
            var t0 = new List<Command>();
            for (var r = 0; r < rows; r++)
            {
                if (r % 3 == 0)
                {
                    t0.Add(Command.Start(r, Brawl));
                }
                else if (r % 3 == 1)
                {
                    t0.Add(Command.Start(r, Guard));
                }
            }

            t0.Add(Command.Add(5, 0, ModifierKind.Added, 50f, 7));
            t0.Add(Command.Add(6, 0, ModifierKind.Added, -60f, 8, 2));
            t0.Add(Command.Send(17, -40));
            t0.Add(Command.Send(17, 5));
            t0.Add(Command.Send(900, -100));
            t0.Add(Command.Local(2, -3));
            t0.Add(Command.Local(2, -4));
            var t1 = new List<Command> { Command.Remove(5, 7), Command.Send(3, -1_000_000), Command.Send(3, 999), Command.Local(8, -1), Command.Add(10, 1, ModifierKind.Multi, 0.5f, 9) };
            var t2 = new List<Command> { Command.Send(999, 3), Command.Local(11, -50), Command.Add(12, 0, ModifierKind.Added, -70f, 11), Command.Send(0, 0) };
            var all = new List<Command>[Ticks];
            all[0] = t0;
            all[1] = t1;
            all[2] = t2;
            for (var t = 3; t < Ticks; t++)
            {
                all[t] = new List<Command>();
            }

            return all;
        }

        static byte[] Bytes<T>(T value) where T : unmanaged
        {
            var bytes = new byte[sizeof(T)];
            fixed (byte* b = bytes)
            {
                *(T*)b = value;
            }

            return bytes;
        }

        static TypedTrack Track<T>(uint start, uint end, T clip) where T : unmanaged
        {
            var track = new TypedTrack { Type = typeof(TlNoSettings<T>) };
            track.Clips.Add(new TypedClip { Type = typeof(T), Start = start, End = end, Data = Bytes(clip) });
            return track;
        }

        public static byte[] Library(List<TypedTimeline> timelines)
        {
            var manifest = LibraryBake.Manifest(timelines.Select(t => t.Name).ToList(), new List<string>(), 60);
            var kinds = LibraryBake.Kinds(new[] { typeof(HitClip).Assembly });
            var parts = new List<LibraryPart>();
            foreach (var timeline in timelines)
            {
                var tlb = LibraryBake.BakeTyped(timeline, manifest);
                parts.Add(new LibraryPart { Name = timeline.Name, Tlb = tlb, Kinds = LibraryBake.KindPayload(timeline.Name, tlb, kinds), Next = (ushort)LibraryAssembler.NextOf(tlb) });
            }

            return LibraryAssembler.Assemble(parts, 60, LibrarySettings.Default);
        }

        static byte[] NorthStar2Library()
        {
            var brawl = new TypedTimeline { Name = "brawl", Duration = 3 };
            brawl.Tracks.Add(Track(0u, 1u, new HitClip { Amount = 20 }));
            brawl.Tracks.Add(Track(0u, 1u, new HealClip { Amount = 10 }));
            brawl.Tracks.Add(Track(1u, 3u, new PoisonClip { Amount = 1 }));
            brawl.Tracks.Add(Track(1u, 2u, new StrikeClip { Amount = 7 }));
            var guard = new TypedTimeline { Name = "guard", Duration = 3 };
            guard.Tracks.Add(Track(0u, 2u, new GuardClip { Armor = 5 }));
            guard.Tracks.Add(Track(0u, 3u, new PoisonClip { Amount = 2 }));
            return Library(new List<TypedTimeline> { brawl, guard });
        }

        public sealed class Rig : IDisposable
        {
            public readonly int Rows;
            public readonly TlLibrary Tl;
            public readonly World World;
            public readonly EntityManager Manager;
            public readonly ComponentSystemGroup TlGroup;
            public readonly Schema Schema;
            public readonly Board Board;
            public readonly SvHost Host;
            public readonly StatId MaxHp;
            public readonly StatId Armor;
            public readonly NativeArray<Entity> Entities;
            public readonly SystemHandle Begin;
            public readonly SystemHandle End;

            public Rig(byte[] library, int rows, Func<int, int> hpOf, Func<int, int> targetOf)
            {
                Rows = rows;
                Tl = TlLibrary.FromBytes(library);
                World = new World("SvNorthStar2Adapter");
                Manager = World.EntityManager;
                Tl.Publish(Manager);
                TlGroup = (ComponentSystemGroup)typeof(TlSceneRun).GetMethod("AddSystems", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { World });
                using (var stepQuery = Manager.CreateEntityQuery(typeof(TimelineStep)))
                {
                    stepQuery.SetSingleton(new TimelineStep { Forward = true });
                }

                Schema = SchemaBuilder.Create().Stat("max_hp", 100f).Stat("armor", 10f).Intrinsic("hp").Build();
                MaxHp = Schema.Stat("max_hp");
                Armor = Schema.Stat("armor");
                Board = Board.Create(Schema, rows);
                var links = new SvLinkRegistry();
                links.Stat<NsMaxHp>(MaxHp);
                links.Stat<NsArmor>(Armor);
                var hp = links.Intrinsic<NsHp, HpDelta>(Schema.Intrinsic("hp"), SvBound.Of(0), SvBound.OfStat<NsMaxHp>(), true);
                links.Condition<NsLow>(hp, 3, 10, 4, 10, EventDerive.DefaultCapacity);
                links.Send<Outgoing, NsTarget>(hp);
                links.Link<HpDelta>(1, LinkMode.Delta);
                links.Link<NsMaxHp>(1, LinkMode.Read);
                Host = SvHost.Install(World, Board, links, digestEvery: 1);
                Begin = World.GetExistingSystem<SvBeginSystem>();
                End = World.GetExistingSystem<SvEndSystem>();
                Entities = Spawn(rows);
                for (var r = 0; r < rows; r++)
                {
                    Manager.SetComponentData(Entities[r], new NsHp { Value = hpOf(r) });
                    Manager.SetComponentData(Entities[r], new NsTarget { Value = targetOf(r) });
                    var slot = Host.Bind(Entities[r]);
                    if (slot != r)
                    {
                        throw new InvalidOperationException($"slot {slot} for row {r}");
                    }
                }
            }

            NativeArray<Entity> Spawn(int rows)
            {
                var registry = KindRegistry.Discover();
                var states = new List<Type> { typeof(HpDelta), typeof(Outgoing), typeof(ArmorPulse) };
                var types = new List<ComponentType> { typeof(global::Tl.Unity.Playback), typeof(TimelineRequest), typeof(TimelinePlaying), typeof(TimelineHorizon) };
                types.AddRange(states.Select(t => ComponentType.ReadWrite(t)));
                types.AddRange(new ComponentType[] { typeof(SvSlot), typeof(SvSlotCleanup), typeof(NsHp), typeof(NsMaxHp), typeof(NsArmor), typeof(NsLow), typeof(NsTarget) });
                var actives = new List<ComponentType>();
                for (var k = 0; k < registry.Count; k++)
                {
                    var kind = registry[k];
                    if (kind.Columns.Length == 0 || !kind.Columns.All(states.Contains))
                    {
                        continue;
                    }

                    actives.Add(ComponentType.ReadWrite(kind.Active));
                    foreach (var type in new[] { kind.Entry, kind.Active, kind.Enabled })
                    {
                        if (!types.Contains(ComponentType.ReadWrite(type)))
                        {
                            types.Add(ComponentType.ReadWrite(type));
                        }
                    }
                }

                if (actives.Count != 3)
                {
                    throw new InvalidOperationException("Damage, Strike and ArmorEdge bind to the three [TlState] columns");
                }

                var template = Manager.CreateEntity(types.ToArray());
                Manager.SetComponentEnabled<TimelinePlaying>(template, false);
                Manager.SetComponentEnabled<NsLow>(template, false);
                foreach (var active in actives)
                {
                    Manager.SetComponentEnabled(template, active, false);
                }

                var entities = new NativeArray<Entity>(rows, Allocator.Persistent);
                Manager.Instantiate(template, entities);
                Manager.DestroyEntity(template);
                return entities;
            }

            public void Frame()
            {
                Begin.Update(World.Unmanaged);
                TlGroup.Update();
                End.Update(World.Unmanaged);
                Manager.CompleteAllTrackedJobs();
            }

            public void Dispose()
            {
                Host.Dispose();
                Entities.Dispose();
                World.Dispose();
                Board.Dispose();
                Tl.Dispose();
            }
        }

        sealed class Mirror : IDisposable
        {
            readonly Rig rig;
            readonly int rows;
            readonly int words;
            readonly ushort[] timelineOf;
            readonly int[] hp;
            readonly int[] delta;
            readonly int[] outgoing;
            readonly int[] pulse;
            readonly float[] maxHp;
            readonly float[] armor;
            readonly ulong* edges;
            readonly ulong* low;
            readonly EventStreams streams;
            readonly Channel<LowHp> lowHp;
            readonly Channel<DenseEvent> dense;
            readonly EventDerive derive;
            readonly List<StatOp> next = new List<StatOp>();
            readonly EntityQuery query;
            long tick;

            public Mirror(Rig rig)
            {
                this.rig = rig;
                rows = rig.Rows;
                words = (rows + 63) >> 6;
                timelineOf = new ushort[rows];
                hp = new int[rows];
                delta = new int[rows];
                outgoing = new int[rows];
                pulse = new int[rows];
                maxHp = new float[rows];
                armor = new float[rows];
                edges = (ulong*)UnsafeUtility.Malloc(words * 8L, 64, Allocator.Persistent);
                low = (ulong*)UnsafeUtility.Malloc(words * 8L, 64, Allocator.Persistent);
                streams = EventStreams.Create(1, 1 << 16);
                lowHp = streams.CreateChannel<LowHp>();
                dense = streams.CreateChannel<DenseEvent>();
                derive = new EventDerive(lowHp.Id, EventDerive.DefaultCapacity);
                query = rig.Manager.CreateEntityQuery(typeof(SvSlot), typeof(NsHp), typeof(HpDelta), typeof(Outgoing), typeof(ArmorPulse), typeof(NsMaxHp), typeof(NsArmor));
            }

            public ulong Step(List<Command> journal)
            {
                var manager = rig.Manager;
                var host = rig.Host;
                foreach (var op in next)
                {
                    host.Enqueue(op);
                }

                next.Clear();
                var inbox = host.InboxWriter(0);
                foreach (var command in journal)
                {
                    switch (command.Kind)
                    {
                        case Kind.Start:
                            manager.GetBuffer<TimelineRequest>(rig.Entities[command.Slot]).Add(new TimelineRequest { Slot = -1, Timeline = (ushort)command.Amount, At = 0, Op = RequestOp.Start });
                            timelineOf[command.Slot] = (ushort)command.Amount;
                            break;
                        case Kind.Add:
                            host.Enqueue(StatOp.Add(command.Slot, rig.Schema.Stat(rig.Schema.Stats[command.Stat].Name), command.Modifier, command.Value, command.Source, command.ExpiresAt));
                            break;
                        case Kind.Remove:
                            host.Enqueue(StatOp.RemoveSource(command.Slot, command.Source));
                            break;
                        case Kind.Send:
                            inbox.Send(command.Slot, command.Amount);
                            break;
                        case Kind.Local:
                            var d = manager.GetComponentData<HpDelta>(rig.Entities[command.Slot]);
                            d.Value = unchecked(d.Value + command.Amount);
                            manager.SetComponentData(rig.Entities[command.Slot], d);
                            break;
                    }
                }

                rig.Frame();
                Read();
                var e = host.EdgeWords(0);
                var c = host.ConditionWords(0);
                long flips = 0;
                for (var w = 0; w < words; w++)
                {
                    edges[w] = e[w];
                    low[w] = c[w];
                    flips += Bits.PopCount(e[w]);
                }

                Assert.AreEqual(flips, host.Channel(0).Count, "the adapter channel counts the same edges");
                derive.Begin();
                derive.AddCount(flips);
                var maker = new LowHpMaker();
                derive.Derive(streams, lowHp, dense, tick, edges, low, words, ref maker);
                for (var row = 0; row < rows; row++)
                {
                    var value = pulse[row];
                    if (value == 0)
                    {
                        continue;
                    }

                    var source = 0x40000000 | (timelineOf[row] << 8);
                    next.Add(value > 0 ? StatOp.Add(row, rig.Armor, ModifierKind.Added, value, source) : StatOp.RemoveSource(row, source));
                    pulse[row] = 0;
                    manager.SetComponentData(rig.Entities[row], new ArmorPulse { Value = 0 });
                }

                streams.Swap();
                var digest = Digest();
                tick++;
                return digest;
            }

            void Read()
            {
                using var slots = query.ToComponentDataArray<SvSlot>(Allocator.Temp);
                using var hps = query.ToComponentDataArray<NsHp>(Allocator.Temp);
                using var deltas = query.ToComponentDataArray<HpDelta>(Allocator.Temp);
                using var outs = query.ToComponentDataArray<Outgoing>(Allocator.Temp);
                using var pulses = query.ToComponentDataArray<ArmorPulse>(Allocator.Temp);
                using var maxes = query.ToComponentDataArray<NsMaxHp>(Allocator.Temp);
                using var armors = query.ToComponentDataArray<NsArmor>(Allocator.Temp);
                Assert.AreEqual(rows, slots.Length);
                for (var i = 0; i < slots.Length; i++)
                {
                    var s = slots[i].Slot;
                    hp[s] = hps[i].Value;
                    delta[s] = deltas[i].Value;
                    outgoing[s] = outs[i].Value;
                    pulse[s] = pulses[i].Value;
                    maxHp[s] = maxes[i].Value;
                    armor[s] = armors[i].Value;
                }
            }

            ulong Digest()
            {
                ulong state = 0;
                unchecked
                {
                    for (var slot = 0; slot < rows; slot++)
                    {
                        var cell = rig.Host.InboxCell(0, slot);
                        state += CellDigest.Key(ColumnHp, slot, (uint)hp[slot]);
                        state += CellDigest.Key(ColumnDelta, slot, (uint)delta[slot]);
                        var m = maxHp[slot];
                        var a = armor[slot];
                        state += CellDigest.Key(ColumnMaxHp, slot, *(uint*)&m);
                        state += CellDigest.Key(ColumnArmor, slot, *(uint*)&a);
                        state += CellDigest.Key(ColumnLow, slot, (uint)((low[slot >> 6] >> (slot & 63)) & 1ul));
                        state += CellDigest.Key(ColumnInbox, slot, (uint)cell);
                        state += CellDigest.Key(ColumnInbox + 1, slot, (uint)(cell >> 32));
                        state += CellDigest.Key(ColumnOutgoing, slot, (uint)outgoing[slot]);
                    }

                    var events = 0xCBF29CE484222325ul;
                    while (streams.TryReadNext(out var type, out var payload, out var size))
                    {
                        events = (events ^ type) * 0x100000001B3ul;
                        for (var i = 0; i < size; i += 4)
                        {
                            events = (events ^ *(uint*)((byte*)payload + i)) * 0x100000001B3ul;
                        }
                    }

                    var pending = (ulong)next.Count;
                    foreach (var op in next)
                    {
                        pending = (pending ^ (ulong)op.Slot ^ ((ulong)op.ValueBits << 32) ^ ((ulong)op.Op << 62)) * 0x100000001B3ul;
                    }

                    return Mix(Mix(Mix(state, events), rig.Board.Digest), pending);
                }
            }

            static ulong Mix(ulong a, ulong b)
            {
                unchecked
                {
                    var h = a ^ (b * 0x9E3779B97F4A7C15ul);
                    h ^= h >> 31;
                    h *= 0xBF58476D1CE4E5B9ul;
                    return h ^ (h >> 29);
                }
            }

            public void Dispose()
            {
                query.Dispose();
                derive.Dispose();
                streams.Dispose();
                UnsafeUtility.Free(edges, Allocator.Persistent);
                UnsafeUtility.Free(low, Allocator.Persistent);
            }
        }

        [TestCase(1000, 1)]
        [TestCase(1000, 11)]
        [TestCase(1 << 20, 11)]
        [TestCase(1 << 20, 1)]
        public void NorthStar2ThroughTheAdapterWithTlKindJobsAndTheSendPassEqualsTheDotNetTwin(int rows, int workers)
        {
            var saved = JobsUtility.JobWorkerCount;
            JobsUtility.JobWorkerCount = workers;
            try
            {
                SvProbe.ManagedRuns = 0;
                var journal = Journal(rows);
                using var rig = new Rig(NorthStar2Library(), rows, r => 95 - (r % 7) * 10, r => (int)(((long)r * 7 + 13) % rows));
                using var mirror = new Mirror(rig);
                var digests = new ulong[Ticks];
                var start = System.Diagnostics.Stopwatch.StartNew();
                for (var t = 0; t < Ticks; t++)
                {
                    digests[t] = mirror.Step(journal[t]);
                }

                using var tickQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<TimelineKindTick>().WithOptions(EntityQueryOptions.IncludeSystems).Build(rig.Manager);
                var kindManaged = tickQuery.GetSingleton<TimelineKindTick>().ManagedRuns;
                UnityEngine.Debug.Log($"svns2adapter rows={rows} workers={workers} jobWorkers={JobsUtility.JobWorkerCount} digests={string.Join(" ", digests.Select(d => d.ToString("X16")))} hostDigest={rig.Host.Digest:X16} probeManagedRuns={SvProbe.ManagedRuns} tlKindTickManagedRuns={kindManaged} ms={start.Elapsed.TotalMilliseconds:F0}");
                CollectionAssert.AreEqual(rows == 1000 ? Twin1000 : TwinMillion, digests);
                Assert.AreEqual(0, kindManaged, "the tl kind tick job ran managed");
                Assert.AreEqual(0, SvProbe.ManagedRuns, "an sv job ran managed");
            }
            finally
            {
                JobsUtility.JobWorkerCount = saved;
            }
        }
    }
}
