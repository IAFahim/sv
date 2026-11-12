using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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
    public unsafe class SvNorthStar2UnityTests
    {
        public static readonly ulong[] Baked = { 0x939FBDEE2E7DEE50ul, 0xE5CE5FA31824A22Eul, 0x75035C43BBDC14AFul };
        const int Rows = 1000;
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

        static List<Command>[] Journal()
        {
            var t0 = new List<Command>();
            for (var r = 0; r < Rows; r++)
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
            return new[] { t0, t1, t2 };
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

        static byte[] Library()
        {
            var brawl = new TypedTimeline { Name = "brawl", Duration = 3 };
            brawl.Tracks.Add(Track(0u, 1u, new HitClip { Amount = 20 }));
            brawl.Tracks.Add(Track(0u, 1u, new HealClip { Amount = 10 }));
            brawl.Tracks.Add(Track(1u, 3u, new PoisonClip { Amount = 1 }));
            brawl.Tracks.Add(Track(1u, 2u, new StrikeClip { Amount = 7 }));
            var guard = new TypedTimeline { Name = "guard", Duration = 3 };
            guard.Tracks.Add(Track(0u, 2u, new GuardClip { Armor = 5 }));
            guard.Tracks.Add(Track(0u, 3u, new PoisonClip { Amount = 2 }));
            var timelines = new List<TypedTimeline> { brawl, guard };
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

        [TestCase(1)]
        [TestCase(11)]
        public void NorthStar2PerTickDigestsInUnityWithBurstEqualTheDotNetPins(int workers)
        {
            var saved = JobsUtility.JobWorkerCount;
            JobsUtility.JobWorkerCount = workers;
            try
            {
                var library = Library();
                UnityEngine.Debug.Log($"svns2 library bytes={library.Length} fnv={Fnv(library):X16}");
                var digests = Run(library, workers);
                UnityEngine.Debug.Log($"svns2 workers={workers} jobWorkers={JobsUtility.JobWorkerCount} digests={string.Join(" ", digests.Select(d => d.ToString("X16")))}");
                CollectionAssert.AreEqual(Baked, digests);
            }
            finally
            {
                JobsUtility.JobWorkerCount = saved;
            }
        }

        static ulong Fnv(byte[] bytes)
        {
            var h = 0xCBF29CE484222325ul;
            foreach (var b in bytes)
            {
                h = (h ^ b) * 0x100000001B3ul;
            }

            return h;
        }

        static ulong[] Run(byte[] library, int workers)
        {
            var journal = Journal();
            using var tl = TlLibrary.FromBytes(library);
            using var world = new World("NorthStar2Unity");
            var manager = world.EntityManager;
            tl.Publish(manager);
            var group = (ComponentSystemGroup)typeof(TlSceneRun).GetMethod("AddSystems", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { world });
            var entities = Spawn(manager);
            using var stepQuery = manager.CreateEntityQuery(typeof(TimelineStep));
            stepQuery.SetSingleton(new TimelineStep { Forward = true });
            using var host = new Host(Rows, workers, manager, entities, group);
            var digests = new ulong[journal.Length];
            for (var t = 0; t < journal.Length; t++)
            {
                digests[t] = host.Step(journal[t]);
            }

            using var tickQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<TimelineKindTick>().WithOptions(EntityQueryOptions.IncludeSystems).Build(manager);
            var managed = tickQuery.GetSingleton<TimelineKindTick>().ManagedRuns;
            UnityEngine.Debug.Log($"svns2 tlKindTickManagedRuns={managed}");
            Assert.AreEqual(0, managed, "the tl kind tick job ran managed");
            return digests;
        }

        static Entity[] Spawn(EntityManager manager)
        {
            var registry = KindRegistry.Discover();
            var states = new List<Type> { typeof(HpDelta), typeof(Outgoing), typeof(ArmorPulse) };
            var types = new List<ComponentType> { typeof(global::Tl.Unity.Playback), typeof(TimelineRequest), typeof(TimelinePlaying), typeof(TimelineHorizon) };
            types.AddRange(states.Select(t => ComponentType.ReadWrite(t)));
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

            Assert.AreEqual(3, actives.Count, "Damage, Strike and ArmorEdge bind to the three [TlState] columns");
            var rows = new Entity[Rows];
            for (var e = 0; e < Rows; e++)
            {
                rows[e] = manager.CreateEntity(types.ToArray());
                manager.SetComponentEnabled<TimelinePlaying>(rows[e], false);
                foreach (var active in actives)
                {
                    manager.SetComponentEnabled(rows[e], active, false);
                }
            }

            return rows;
        }

        sealed class Host : IDisposable
        {
            readonly int rows;
            readonly int words;
            readonly int workers;
            readonly int capacity;
            readonly EntityManager manager;
            readonly Entity[] entities;
            readonly ComponentSystemGroup group;
            readonly Schema schema;
            readonly Board board;
            readonly StatId maxHp;
            readonly StatId armor;
            readonly int[] delta;
            readonly int[] outgoing;
            readonly int[] pulse;
            readonly ushort[] timelineOf;
            readonly int* hp;
            readonly int* floor;
            readonly int* ceiling;
            readonly float* maxHpHost;
            readonly float* armorHost;
            readonly int* targets;
            readonly ulong* changed;
            readonly ulong* low;
            readonly ulong* edges;
            readonly InboxBuffer inbox;
            readonly EventStreams streams;
            readonly Channel<LowHp> lowHp;
            readonly Channel<DenseEvent> dense;
            readonly EventDerive derive;
            readonly StatOp[] ops;
            readonly StatOp[] scratch;
            readonly StatOp[] next;
            readonly StatWrite[] writes;
            readonly ParallelOptions parallel;
            readonly GCHandle deltaPin;
            int nextCount;
            long tick;

            public Host(int rows, int workers, EntityManager manager, Entity[] entities, ComponentSystemGroup group)
            {
                this.rows = rows;
                this.workers = workers;
                this.manager = manager;
                this.entities = entities;
                this.group = group;
                words = (rows + 63) >> 6;
                capacity = words << 6;
                parallel = new ParallelOptions { MaxDegreeOfParallelism = workers };
                schema = SchemaBuilder.Create().Stat("max_hp", 100f).Stat("armor", 10f).Build();
                maxHp = schema.Stat("max_hp");
                armor = schema.Stat("armor");
                board = Board.Create(schema, rows);
                board.BindSink(maxHp, true);
                board.BindSink(armor, true);
                for (var i = 0; i < rows; i++)
                {
                    board.Acquire();
                }

                delta = new int[capacity];
                deltaPin = GCHandle.Alloc(delta, GCHandleType.Pinned);
                outgoing = new int[capacity];
                pulse = new int[capacity];
                timelineOf = new ushort[rows];
                hp = Alloc<int>(capacity);
                floor = Alloc<int>(capacity);
                ceiling = Alloc<int>(capacity);
                maxHpHost = Alloc<float>(capacity);
                armorHost = Alloc<float>(capacity);
                targets = Alloc<int>(capacity);
                changed = Alloc<ulong>(words);
                low = Alloc<ulong>(words);
                edges = Alloc<ulong>(words);
                inbox = new InboxBuffer(capacity);
                streams = EventStreams.Create(1, 1 << 16);
                lowHp = streams.CreateChannel<LowHp>();
                dense = streams.CreateChannel<DenseEvent>();
                derive = new EventDerive(lowHp.Id, EventDerive.DefaultCapacity);
                ops = new StatOp[rows * 2 + 4096];
                scratch = new StatOp[ops.Length];
                next = new StatOp[rows * 2];
                writes = new StatWrite[1024];
                for (var r = 0; r < rows; r++)
                {
                    hp[r] = 95 - (r % 7) * 10;
                    targets[r] = (int)(((long)r * 7 + 13) % rows);
                }
            }

            static T* Alloc<T>(int count) where T : unmanaged
            {
                var p = (T*)UnsafeUtility.Malloc(count * (long)sizeof(T), 64, Allocator.Persistent);
                UnsafeUtility.MemClear(p, count * (long)sizeof(T));
                return p;
            }

            public ulong Step(List<Command> journal)
            {
                var count = 0;
                for (var i = 0; i < nextCount; i++)
                {
                    ops[count++] = next[i];
                }

                nextCount = 0;
                var inboxCells = inbox.Pointer;
                foreach (var command in journal)
                {
                    switch (command.Kind)
                    {
                        case Kind.Start:
                            manager.GetBuffer<TimelineRequest>(entities[command.Slot]).Add(new TimelineRequest { Slot = -1, Timeline = (ushort)command.Amount, At = 0, Op = RequestOp.Start });
                            timelineOf[command.Slot] = (ushort)command.Amount;
                            break;
                        case Kind.Add:
                            ops[count++] = StatOp.Add(command.Slot, schema.Stat(schema.Stats[command.Stat].Name), command.Modifier, command.Value, command.Source, command.ExpiresAt);
                            break;
                        case Kind.Remove:
                            ops[count++] = StatOp.RemoveSource(command.Slot, command.Source);
                            break;
                        case Kind.Send:
                            Inbox.Send(inboxCells, command.Slot, command.Amount);
                            break;
                        case Kind.Local:
                            delta[command.Slot] = unchecked(delta[command.Slot] + command.Amount);
                            break;
                    }
                }

                board.ExpireModifiers(tick);
                var batch = new Span<StatOp>(ops, 0, count);
                StatOps.Canonicalize(batch, scratch);
                board.Apply(batch);
                board.Recalculate();
                Scatter();
                FoldAll();
                for (var r = 0; r < rows; r++)
                {
                    manager.SetComponentData(entities[r], new HpDelta { Value = delta[r] });
                    manager.SetComponentData(entities[r], new Outgoing { Value = outgoing[r] });
                    manager.SetComponentData(entities[r], new ArmorPulse { Value = pulse[r] });
                }

                group.Update();
                manager.CompleteAllTrackedJobs();
                for (var r = 0; r < rows; r++)
                {
                    delta[r] = manager.GetComponentData<HpDelta>(entities[r]).Value;
                    outgoing[r] = manager.GetComponentData<Outgoing>(entities[r]).Value;
                    pulse[r] = manager.GetComponentData<ArmorPulse>(entities[r]).Value;
                }

                SendAll();
                var maker = new LowHpMaker();
                derive.Derive(streams, lowHp, dense, tick, edges, low, words, ref maker);
                DerivePulses();
                streams.Swap();
                var digest = Digest();
                tick++;
                return digest;
            }

            void Scatter()
            {
                while (true)
                {
                    var taken = board.TakeBroadcast(writes);
                    if (taken == 0)
                    {
                        return;
                    }

                    for (var i = 0; i < taken; i++)
                    {
                        var w = writes[i];
                        var bits = w.Bits;
                        var value = *(float*)&bits;
                        if (w.Stat == maxHp.Index)
                        {
                            maxHpHost[w.Slot] = value;
                            ceiling[w.Slot] = Clamp.BoundOf(value);
                        }
                        else if (w.Stat == armor.Index)
                        {
                            armorHost[w.Slot] = value;
                        }
                    }
                }
            }

            (int Start, int Count) Partition(int part)
            {
                var first = (int)((long)part * words / workers);
                var last = (int)((long)(part + 1) * words / workers);
                var start = first << 6;
                var end = Math.Min(rows, last << 6);
                return (start, Math.Max(0, end - start));
            }

            void FoldAll()
            {
                derive.Begin();
                if (workers == 1)
                {
                    FoldPart(0);
                    return;
                }

                Parallel.For(0, workers, parallel, FoldPart);
            }

            void FoldPart(int part)
            {
                var (start, count) = Partition(part);
                if (count == 0)
                {
                    return;
                }

                var d = (int*)deltaPin.AddrOfPinnedObject();
                var first = start >> 6;
                var wordCount = (count + 63) >> 6;
                Kernel.Fold(new Column<int>(hp, capacity), Window.Range(start, count), new Column<int>(d, capacity), Bounds.Rows(floor, ceiling), new Column<ulong>(changed + first, wordCount), inbox.Column);
                long flips = 0;
                for (var w = first; w < first + wordCount; w++)
                {
                    var n = Math.Min(64, rows - (w << 6));
                    ulong enter = 0;
                    ulong stay = 0;
                    for (var r = 0; r < n; r++)
                    {
                        var slot = (w << 6) + r;
                        enter |= (Condition.Below(hp[slot], ceiling[slot], 3, 10) ? 1ul : 0ul) << r;
                        stay |= (Condition.Below(hp[slot], ceiling[slot], 4, 10) ? 1ul : 0ul) << r;
                    }

                    var state = low[w];
                    var nextState = Condition.Next(state, enter, stay);
                    edges[w] = Condition.Edges(state, nextState);
                    low[w] = nextState;
                    flips += Bits.PopCount(edges[w]);
                }

                derive.AddCount(flips);
            }

            void SendAll()
            {
                var cells = inbox.Pointer;
                for (var row = 0; row < rows; row++)
                {
                    var amount = outgoing[row];
                    if (amount != 0)
                    {
                        Inbox.Send(cells, targets[row], amount);
                        outgoing[row] = 0;
                    }
                }
            }

            void DerivePulses()
            {
                for (var row = 0; row < rows; row++)
                {
                    var value = pulse[row];
                    if (value == 0)
                    {
                        continue;
                    }

                    var source = 0x40000000 | (timelineOf[row] << 8);
                    next[nextCount++] = value > 0 ? StatOp.Add(row, armor, ModifierKind.Added, value, source) : StatOp.RemoveSource(row, source);
                    pulse[row] = 0;
                }
            }

            ulong Digest()
            {
                ulong state = 0;
                var cells = inbox.Pointer;
                unchecked
                {
                    for (var slot = 0; slot < rows; slot++)
                    {
                        state += CellDigest.Key(ColumnHp, slot, (uint)hp[slot]);
                        state += CellDigest.Key(ColumnDelta, slot, (uint)delta[slot]);
                        var m = maxHpHost[slot];
                        var a = armorHost[slot];
                        state += CellDigest.Key(ColumnMaxHp, slot, *(uint*)&m);
                        state += CellDigest.Key(ColumnArmor, slot, *(uint*)&a);
                        state += CellDigest.Key(ColumnLow, slot, (uint)((low[slot >> 6] >> (slot & 63)) & 1ul));
                        state += CellDigest.Key(ColumnInbox, slot, (uint)cells[slot]);
                        state += CellDigest.Key(ColumnInbox + 1, slot, (uint)(cells[slot] >> 32));
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

                    var pending = (ulong)nextCount;
                    for (var i = 0; i < nextCount; i++)
                    {
                        var op = next[i];
                        pending = (pending ^ (ulong)op.Slot ^ ((ulong)op.ValueBits << 32) ^ ((ulong)op.Op << 62)) * 0x100000001B3ul;
                    }

                    return Mix(Mix(Mix(state, events), board.Digest), pending);
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
                derive.Dispose();
                streams.Dispose();
                inbox.Dispose();
                board.Dispose();
                deltaPin.Free();
                UnsafeUtility.Free(hp, Allocator.Persistent);
                UnsafeUtility.Free(floor, Allocator.Persistent);
                UnsafeUtility.Free(ceiling, Allocator.Persistent);
                UnsafeUtility.Free(maxHpHost, Allocator.Persistent);
                UnsafeUtility.Free(armorHost, Allocator.Persistent);
                UnsafeUtility.Free(targets, Allocator.Persistent);
                UnsafeUtility.Free(changed, Allocator.Persistent);
                UnsafeUtility.Free(low, Allocator.Persistent);
                UnsafeUtility.Free(edges, Allocator.Persistent);
            }
        }
    }
}
