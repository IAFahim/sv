using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace Sv.Unity
{
    public struct SvHostRef : IComponentData
    {
        public IntPtr Handle;
    }

    public sealed unsafe class SvHost : IDisposable
    {
        const int Line = 64;

        StatOp[] ops;
        StatOp[] scratch;
        int opCount;
        GCHandle self;
        Entity hostEntity;
        World world;

        public Board Board { get; }

        public SvLinkRegistry Links { get; }

        public int Capacity { get; }

        public int Words { get; }

        public long Tick { get; internal set; }

        public int DigestEvery { get; set; } = 8;

        public long DigestTick { get; internal set; } = -1;

        public bool Filter { get; set; } = true;

        public ulong Digest { get; internal set; }

        public ulong CellDigestValue { get; internal set; }

        public int LastApplied { get; private set; }

        public int LastExpired { get; private set; }

        public ulong LinkHash { get; }

        public long NativeBytes { get; private set; }

        internal Entity* SlotEntity;
        internal StatWrite* Writes;
        internal int WriteCapacity;
        internal int* WriteCount;
        internal sbyte* HandleOfStat;
        internal long*[] Inboxes = new long*[SvLinkRegistry.MaxIntrinsics];
        internal int* Sent;
        internal ulong*[] States = new ulong*[SvLinkRegistry.MaxIntrinsics];
        internal ulong*[] Edges = new ulong*[SvLinkRegistry.MaxIntrinsics];
        internal int* EdgeCount;
        internal SvEvent*[] EventBuffers = new SvEvent*[SvLinkRegistry.MaxIntrinsics];
        internal SvChannel* Channels;
        internal ulong* Partial;
        internal int PartialCount;
        internal NativeList<Entity> PendingEntities;
        internal int* PendingSlots;
        internal int* PendingIndex;
        internal NativeList<Entity> PendingRemovals;
        internal JobHandle FoldHandle;

        SvHost(Board board, SvLinkRegistry links, int writeCapacity, int opCapacity)
        {
            Board = board;
            Links = links;
            Capacity = board.Capacity;
            Words = (Capacity + 63) >> 6;
            var fault = links.Validate();
            if (fault != LinkFault.None)
            {
                throw new InvalidOperationException($"sv.unity: link table fault {fault}");
            }

            LinkHash = links.Hash();
            ops = new StatOp[opCapacity];
            scratch = new StatOp[opCapacity];
            SlotEntity = (Entity*)Alloc(Capacity * (long)sizeof(Entity));
            WriteCapacity = writeCapacity;
            Writes = (StatWrite*)Alloc(writeCapacity * (long)sizeof(StatWrite));
            WriteCount = (int*)Alloc(Line);
            HandleOfStat = (sbyte*)Alloc(SvLinkRegistry.MaxStatIndex);
            for (var i = 0; i < SvLinkRegistry.MaxStatIndex; i++)
            {
                HandleOfStat[i] = -1;
            }

            for (var s = 0; s < links.Stats.Count; s++)
            {
                HandleOfStat[links.Stats[s].Stat.Index] = (sbyte)s;
            }

            Sent = (int*)Alloc(Line * SvLinkRegistry.MaxIntrinsics);
            EdgeCount = (int*)Alloc(Line * SvLinkRegistry.MaxIntrinsics);
            Channels = (SvChannel*)Alloc(Line * SvLinkRegistry.MaxIntrinsics);
            for (var k = 0; k < links.Intrinsics.Count; k++)
            {
                var spec = links.Intrinsics[k];
                if (spec.Remote)
                {
                    Inboxes[k] = (long*)Alloc(Capacity * 8L);
                }

                if (spec.HasCondition)
                {
                    States[k] = (ulong*)Alloc(Words * 8L);
                    Edges[k] = (ulong*)Alloc(Words * 8L);
                    EventBuffers[k] = (SvEvent*)Alloc(Math.Max(1, spec.Condition.Capacity) * (long)sizeof(SvEvent));
                }
            }

            PartialCount = JobsUtility.ThreadIndexCount * 8;
            Partial = (ulong*)Alloc(PartialCount * 8L);
            PendingEntities = new NativeList<Entity>(Capacity, Allocator.Persistent);
            NativeBytes += Capacity * (long)sizeof(Entity);
            PendingSlots = (int*)Alloc(Capacity * 4L);
            PendingIndex = (int*)Alloc(Capacity * 4L);
            PendingRemovals = new NativeList<Entity>(64, Allocator.Persistent);
        }

        public static SvHost Install(World world, Board board, SvLinkRegistry links, int writeCapacity = 1 << 16, int opCapacity = 1 << 16, int digestEvery = 8)
        {
            if (writeCapacity < 1 || opCapacity < 1 || digestEvery < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(writeCapacity), "sv.unity: writeCapacity and opCapacity must be >= 1 and digestEvery >= 0");
            }

            var manager = world.EntityManager;
            using (var existing = manager.CreateEntityQuery(ComponentType.ReadOnly<SvHostRef>()))
            {
                if (!existing.IsEmptyIgnoreFilter)
                {
                    throw new InvalidOperationException($"sv.unity: world '{world.Name}' already has an SvHost; one SvHost per world, register every intrinsic and stat in one SvLinkRegistry");
                }
            }

            var host = new SvHost(board, links, writeCapacity, opCapacity);
            host.DigestEvery = digestEvery;
            host.world = world;
            host.self = GCHandle.Alloc(host);
            host.Adopt(manager);
            host.hostEntity = manager.CreateEntity(typeof(SvHostRef));
            manager.SetComponentData(host.hostEntity, new SvHostRef { Handle = GCHandle.ToIntPtr(host.self) });
            var group = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
            group.AddSystemToUpdateList(world.CreateSystem<SvBeginSystem>());
            group.AddSystemToUpdateList(world.CreateSystem<SvEndSystem>());
            group.SortSystems();
            return host;
        }

        internal static SvHost From(IntPtr handle) => (SvHost)GCHandle.FromIntPtr(handle).Target;

        void Adopt(EntityManager manager)
        {
            using (var query = manager.CreateEntityQuery(ComponentType.ReadOnly<SvSlot>()))
            using (var entities = query.ToEntityArray(Allocator.Temp))
            using (var slots = query.ToComponentDataArray<SvSlot>(Allocator.Temp))
            {
                for (var i = 0; i < entities.Length; i++)
                {
                    var slot = slots[i].Slot;
                    if (slot < 0 || slot >= Capacity || (Board.LiveWord(slot >> 6) & (1ul << (slot & 63))) == 0)
                    {
                        continue;
                    }

                    if (PendingIndex[slot] != 0)
                    {
                        throw new InvalidOperationException($"sv.unity: entities {PendingEntities[PendingIndex[slot] - 1]} and {entities[i]} both carry SvSlot {slot}");
                    }

                    Queue(entities[i], slot);
                }
            }

            for (var s = 0; s < Links.Stats.Count; s++)
            {
                Board.BindSink(Links.Stats[s].Stat, true);
            }
        }

        public int Bind(Entity entity)
        {
            var slot = Board.Acquire();
            Fresh(slot);
            Queue(entity, slot);
            return slot;
        }

        public void Spawn(EntityManager manager, Entity prefab, int templateSlot, NativeArray<Entity> spawned)
        {
            manager.Instantiate(prefab, spawned);
            var at = PendingEntities.Length;
            Board.Spawn(templateSlot, spawned.Length, PendingSlots + at);
            for (var i = 0; i < spawned.Length; i++)
            {
                var slot = PendingSlots[at + i];
                Fresh(slot);
                Queue(spawned[i], slot);
            }
        }

        public void Unbind(int slot)
        {
            var entity = SlotEntity[slot];
            if (PendingIndex[slot] != 0)
            {
                entity = PendingEntities[PendingIndex[slot] - 1];
                Dequeue(slot);
            }

            Free(slot);
            if (entity != Entity.Null)
            {
                PendingRemovals.Add(entity);
            }
        }

        public void Enqueue(in StatOp op)
        {
            if (opCount == ops.Length)
            {
                Array.Resize(ref ops, ops.Length * 2);
                scratch = new StatOp[ops.Length];
            }

            ops[opCount++] = op;
        }

        public int QueuedOps => opCount;

        public int OpCapacity => ops.Length;

        public SvInboxWriter InboxWriter(int intrinsic)
        {
            if (Inboxes[intrinsic] == null)
            {
                throw new InvalidOperationException($"sv.unity: intrinsic {intrinsic} has no inbox; register it with remote: true");
            }

            return new SvInboxWriter { Cells = Inboxes[intrinsic], Sent = Sent + intrinsic * (Line / 4) };
        }

        public long InboxCell(int intrinsic, int slot) => Inboxes[intrinsic] == null ? 0L : Inboxes[intrinsic][slot];

        public SvChannel Channel(int intrinsic) => *ChannelPtr(intrinsic);

        public ReadOnlySpan<SvEvent> Events(int intrinsic)
        {
            var channel = Channel(intrinsic);
            return new ReadOnlySpan<SvEvent>(EventBuffers[intrinsic], channel.Delivered);
        }

        public bool EdgeBit(int intrinsic, int slot) => Edges[intrinsic] != null && (Edges[intrinsic][slot >> 6] & (1ul << (slot & 63))) != 0;

        public bool ConditionBit(int intrinsic, int slot) => States[intrinsic] != null && (States[intrinsic][slot >> 6] & (1ul << (slot & 63))) != 0;

        public ReadOnlySpan<ulong> EdgeWords(int intrinsic) => Edges[intrinsic] == null ? default : new ReadOnlySpan<ulong>(Edges[intrinsic], Words);

        public ReadOnlySpan<ulong> ConditionWords(int intrinsic) => States[intrinsic] == null ? default : new ReadOnlySpan<ulong>(States[intrinsic], Words);

        public Entity EntityOf(int slot) => SlotEntity[slot];

        public bool IsPending(int slot) => PendingIndex[slot] != 0;

        internal SvChannel* ChannelPtr(int intrinsic) => (SvChannel*)((byte*)Channels + intrinsic * Line);

        internal int* SentPtr(int intrinsic) => Inboxes[intrinsic] == null ? null : Sent + intrinsic * (Line / 4);

        internal int* EdgeCountPtr(int intrinsic) => EdgeCount + intrinsic * (Line / 4);

        public long ExpireTicks { get; private set; }

        public long ApplyTicks { get; private set; }

        public long RecalcTicks { get; private set; }

        public long TakeTicks { get; private set; }

        internal void Sparse()
        {
            var t0 = SvClock.Now;
            LastExpired = Board.ExpireModifiers(Tick);
            var t1 = SvClock.Now;
            if (opCount > 0)
            {
                StatOps.Canonicalize(new Span<StatOp>(ops, 0, opCount), new Span<StatOp>(scratch, 0, opCount));
                LastApplied = Board.Apply(new ReadOnlySpan<StatOp>(ops, 0, opCount));
                opCount = 0;
            }
            else
            {
                LastApplied = 0;
            }

            var t2 = SvClock.Now;
            Board.Recalculate();
            var t3 = SvClock.Now;
            ExpireTicks = t1 - t0;
            ApplyTicks = t2 - t1;
            RecalcTicks = t3 - t2;
            TakeTicks = 0;
        }

        internal int TakeWrites()
        {
            var t0 = SvClock.Now;
            *WriteCount = Board.TakeBroadcast(new Span<StatWrite>(Writes, WriteCapacity));
            TakeTicks += SvClock.Now - t0;
            return *WriteCount;
        }

        internal void ClearEdges()
        {
            for (var k = 0; k < Links.Intrinsics.Count; k++)
            {
                if (Edges[k] != null)
                {
                    UnsafeUtility.MemClear(Edges[k], Words * 8L);
                }

                *EdgeCountPtr(k) = 0;
            }
        }

        internal void Free(int slot)
        {
            Board.Release(slot);
            SlotEntity[slot] = Entity.Null;
            Fresh(slot);
        }

        internal void DropPending(int index)
        {
            var slot = PendingSlots[index];
            Dequeue(slot);
            Free(slot);
        }

        void Queue(Entity entity, int slot)
        {
            PendingIndex[slot] = PendingEntities.Length + 1;
            PendingSlots[PendingEntities.Length] = slot;
            PendingEntities.AddNoResize(entity);
        }

        void Dequeue(int slot)
        {
            var at = PendingIndex[slot] - 1;
            var last = PendingEntities.Length - 1;
            if (at != last)
            {
                var moved = PendingSlots[last];
                PendingSlots[at] = moved;
                PendingEntities[at] = PendingEntities[last];
                PendingIndex[moved] = at + 1;
            }

            PendingEntities.RemoveAt(last);
            PendingIndex[slot] = 0;
        }

        internal void ClearPending()
        {
            for (var i = 0; i < PendingEntities.Length; i++)
            {
                PendingIndex[PendingSlots[i]] = 0;
            }

            PendingEntities.Clear();
        }

        void Fresh(int slot)
        {
            for (var k = 0; k < Links.Intrinsics.Count; k++)
            {
                if (Inboxes[k] != null)
                {
                    Inboxes[k][slot] = 0;
                }

                if (States[k] != null)
                {
                    States[k][slot >> 6] &= ~(1ul << (slot & 63));
                }
            }
        }

        void* Alloc(long bytes)
        {
            NativeBytes += bytes;
            var p = UnsafeUtility.Malloc(bytes, Line, Allocator.Persistent);
            UnsafeUtility.MemClear(p, bytes);
            return p;
        }

        static void Free<T>(ref T* p) where T : unmanaged
        {
            if (p != null)
            {
                UnsafeUtility.Free(p, Allocator.Persistent);
                p = null;
            }
        }

        public void Dispose()
        {
            if (!self.IsAllocated)
            {
                return;
            }

            FoldHandle.Complete();
            if (world != null && world.IsCreated)
            {
                world.EntityManager.CompleteAllTrackedJobs();
            }

            if (world != null && world.IsCreated && world.EntityManager.Exists(hostEntity))
            {
                world.EntityManager.DestroyEntity(hostEntity);
            }

            Free(ref SlotEntity);
            Free(ref Writes);
            Free(ref WriteCount);
            Free(ref HandleOfStat);
            Free(ref Sent);
            Free(ref EdgeCount);
            Free(ref Channels);
            Free(ref Partial);
            Free(ref PendingSlots);
            Free(ref PendingIndex);
            for (var k = 0; k < SvLinkRegistry.MaxIntrinsics; k++)
            {
                Free(ref Inboxes[k]);
                Free(ref States[k]);
                Free(ref Edges[k]);
                Free(ref EventBuffers[k]);
            }

            if (PendingEntities.IsCreated)
            {
                PendingEntities.Dispose();
            }

            if (PendingRemovals.IsCreated)
            {
                PendingRemovals.Dispose();
            }

            self.Free();
        }
    }
}
