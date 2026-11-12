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

        readonly StatOp[] ops;
        readonly StatOp[] scratch;
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

        public bool Filter { get; set; } = true;

        public ulong Digest { get; internal set; }

        public ulong CellDigestValue { get; internal set; }

        public int LastApplied { get; private set; }

        public int LastExpired { get; private set; }

        public ulong LinkHash { get; }

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
                board.BindSink(links.Stats[s].Stat);
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
            PendingSlots = (int*)Alloc(Capacity * 4L);
        }

        public static SvHost Install(World world, Board board, SvLinkRegistry links, int writeCapacity = 1 << 16, int opCapacity = 1 << 16)
        {
            var host = new SvHost(board, links, writeCapacity, opCapacity);
            host.world = world;
            host.self = GCHandle.Alloc(host);
            var manager = world.EntityManager;
            host.hostEntity = manager.CreateEntity(typeof(SvHostRef));
            manager.SetComponentData(host.hostEntity, new SvHostRef { Handle = GCHandle.ToIntPtr(host.self) });
            var group = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
            group.AddSystemToUpdateList(world.CreateSystem<SvBeginSystem>());
            group.AddSystemToUpdateList(world.CreateSystem<SvEndSystem>());
            group.SortSystems();
            return host;
        }

        internal static SvHost From(IntPtr handle) => (SvHost)GCHandle.FromIntPtr(handle).Target;

        public int Bind(Entity entity)
        {
            var slot = Board.Acquire();
            Fresh(slot);
            PendingSlots[PendingEntities.Length] = slot;
            PendingEntities.AddNoResize(entity);
            return slot;
        }

        public void Spawn(EntityManager manager, Entity prefab, int templateSlot, NativeArray<Entity> spawned)
        {
            manager.Instantiate(prefab, spawned);
            var at = PendingEntities.Length;
            Board.Spawn(templateSlot, spawned.Length, PendingSlots + at);
            for (var i = 0; i < spawned.Length; i++)
            {
                Fresh(PendingSlots[at + i]);
                PendingEntities.AddNoResize(spawned[i]);
            }
        }

        public void Unbind(int slot)
        {
            Board.Release(slot);
            SlotEntity[slot] = Entity.Null;
            Fresh(slot);
        }

        public void Enqueue(in StatOp op)
        {
            if (opCount == ops.Length)
            {
                throw new InvalidOperationException("sv.unity: StatOp queue is full");
            }

            ops[opCount++] = op;
        }

        public SvInboxWriter InboxWriter(int intrinsic)
        {
            if (Inboxes[intrinsic] == null)
            {
                throw new InvalidOperationException($"sv.unity: intrinsic {intrinsic} has no inbox; register it with remote: true");
            }

            return new SvInboxWriter { Cells = Inboxes[intrinsic], Sent = Sent + intrinsic * (Line / 4) };
        }

        public SvChannel Channel(int intrinsic) => *ChannelPtr(intrinsic);

        public ReadOnlySpan<SvEvent> Events(int intrinsic)
        {
            var channel = Channel(intrinsic);
            return new ReadOnlySpan<SvEvent>(EventBuffers[intrinsic], channel.Delivered);
        }

        public bool EdgeBit(int intrinsic, int slot) => Edges[intrinsic] != null && (Edges[intrinsic][slot >> 6] & (1ul << (slot & 63))) != 0;

        public bool ConditionBit(int intrinsic, int slot) => States[intrinsic] != null && (States[intrinsic][slot >> 6] & (1ul << (slot & 63))) != 0;

        public Entity EntityOf(int slot) => SlotEntity[slot];

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
            var pending = Board.PendingBroadcast;
            if (pending > WriteCapacity)
            {
                throw new InvalidOperationException($"sv.unity: {pending} stat writes exceed the scatter capacity {WriteCapacity}");
            }

            *WriteCount = Board.TakeBroadcast(new Span<StatWrite>(Writes, WriteCapacity));
            var t4 = SvClock.Now;
            ExpireTicks = t1 - t0;
            ApplyTicks = t2 - t1;
            RecalcTicks = t3 - t2;
            TakeTicks = t4 - t3;
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

        static void* Alloc(long bytes)
        {
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

            self.Free();
        }
    }
}
