using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Core;
using Unity.Jobs;
using Unity.Mathematics;

namespace Unity.Entities
{
    public interface IComponentData
    {
    }

    public interface IBufferElementData
    {
    }

    public interface IJobChunk
    {
        void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask);
    }

    public interface ISystem
    {
        void OnCreate(ref SystemState state)
        {
        }

        void OnDestroy(ref SystemState state)
        {
        }

        void OnUpdate(ref SystemState state)
        {
        }
    }

    public sealed record FakeChunk(int Count, ImmutableDictionary<Type, IntPtr> Columns, v128? EnabledMask, Entity[]? Entities = null);

    public sealed class FakeSystem
    {
        public FakeSystem(object system, SystemState state)
        {
            System = system;
            State = state;
        }

        public object System { get; }

        public SystemState State;
    }

    public sealed class FakeWorld
    {
        public const int ChunkCapacity = 128;

        public TimeData Time;

        internal readonly Dictionary<Type, HashSet<int>> Present = new();

        public List<FakeChunk> Chunks { get; } = new();

        public List<(int Chunk, int Row)> Locations { get; } = new();

        public List<FakeSystem> Systems { get; } = new();

        private List<byte[]> Storage { get; } = new();

        public unsafe IntPtr Allocate(Type type, int capacity)
        {
            var size = (int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!.MakeGenericMethod(type).Invoke(null, null)!;
            var storage = GC.AllocateArray<byte>(size * capacity, pinned: true);
            Storage.Add(storage);
            return (IntPtr)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(storage));
        }
    }

    public enum WorldFlags : int
    {
        None = 0,
        Live = 1,
        Editor = 1 << 1 | Live,
        Game = 1 << 2 | Live,
        Simulation = 1 << 3 | Live,
    }

    public sealed class World : IDisposable
    {
        private readonly FakeWorld world = new();

        public World(string name, WorldFlags flags = WorldFlags.Simulation)
        {
            Name = name;
            Flags = flags;
        }

        public string Name { get; }

        public WorldFlags Flags { get; }

        public EntityManager EntityManager => new(world);

        public WorldUnmanaged Unmanaged => new(world);

        public void Dispose()
        {
        }
    }

    public static class WorldExtensions
    {
        public static SystemHandle CreateSystem<T>(this World self) where T : unmanaged, ISystem
        {
            var world = self.Unmanaged.World;
            var entry = new FakeSystem(new T(), new SystemState(world));
            ((ISystem)entry.System).OnCreate(ref entry.State);
            world.Systems.Add(entry);
            return new SystemHandle(world.Systems.Count - 1);
        }
    }

    public struct SystemHandle : IEquatable<SystemHandle>
    {
        internal SystemHandle(int index) => Index = index;

        internal int Index { get; }

        public void Update(WorldUnmanaged world)
        {
            var entry = world.World.Systems[Index];
            ((ISystem)entry.System).OnUpdate(ref entry.State);
        }

        public bool Equals(SystemHandle other) => Index == other.Index;

        public override bool Equals(object? obj) => obj is SystemHandle other && Equals(other);

        public override int GetHashCode() => Index;
    }

    public readonly struct WorldUnmanaged
    {
        private readonly FakeWorld world;

        public WorldUnmanaged(FakeWorld world) => this.world = world;

        public ref TimeData Time => ref world.Time;

        internal FakeWorld World => world;

        public ref T GetUnsafeSystemRef<T>(SystemHandle id) where T : unmanaged, ISystem => ref Unsafe.Unbox<T>(world.Systems[id.Index].System);
    }

    public struct Entity : IEquatable<Entity>, IComparable<Entity>
    {
        public int Index;
        public int Version;

        public bool Equals(Entity other) => Index == other.Index && Version == other.Version;

        public override bool Equals(object? obj) => obj is Entity other && Equals(other);

        public override int GetHashCode() => Index;

        public int CompareTo(Entity other) => Index != other.Index ? Index.CompareTo(other.Index) : Version.CompareTo(other.Version);
    }

    public readonly struct ComponentType
    {
        private ComponentType(Type type) => Type = type;

        public Type Type { get; }

        public static implicit operator ComponentType(Type type) => new(type);
    }

    public readonly struct EntityArchetype
    {
        internal EntityArchetype(ImmutableArray<Type> types) => Types = types;

        internal ImmutableArray<Type> Types { get; }
    }

    public readonly unsafe struct EntityManager
    {
        private readonly FakeWorld world;

        internal EntityManager(FakeWorld world) => this.world = world;

        public EntityArchetype CreateArchetype(params ComponentType[] types) => new(types.Select(type => type.Type).ToImmutableArray());

        public NativeArray<Entity> CreateEntity(EntityArchetype archetype, int entityCount, AllocatorManager.AllocatorHandle allocator)
        {
            var entities = new Entity[entityCount];
            for (var created = 0; created < entityCount; created += FakeWorld.ChunkCapacity)
            {
                var count = Math.Min(FakeWorld.ChunkCapacity, entityCount - created);
                var owner = world;
                var chunkEntities = new Entity[count];
                for (var row = 0; row < count; row++)
                {
                    chunkEntities[row] = new Entity { Index = world.Locations.Count, Version = 1 };
                    world.Locations.Add((world.Chunks.Count, row));
                }

                world.Chunks.Add(new FakeChunk(count, archetype.Types.ToImmutableDictionary(type => type, type => owner.Allocate(type, FakeWorld.ChunkCapacity)), null, chunkEntities));
                foreach (var type in archetype.Types)
                {
                    world.Present.TryAdd(type, new HashSet<int>());
                    for (var row = 0; row < count; row++)
                    {
                        world.Present[type].Add(chunkEntities[row].Index);
                    }
                }

                chunkEntities.CopyTo(entities, created);
            }

            return new NativeArray<Entity>(entities, Allocator.Temp);
        }

        public void SetComponentData<T>(Entity entity, T componentData) where T : unmanaged, IComponentData
        {
            var (chunk, row) = world.Locations[entity.Index];
            ((T*)world.Chunks[chunk].Columns[typeof(T)])[row] = componentData;
        }

        public T GetComponentData<T>(Entity entity) where T : unmanaged, IComponentData
        {
            if (!world.Present.TryGetValue(typeof(T), out var present) || !present.Contains(entity.Index))
            {
                throw new ArgumentException($"Entity {entity.Index} does not have a {typeof(T)} component");
            }

            var (chunk, row) = world.Locations[entity.Index];
            return ((T*)world.Chunks[chunk].Columns[typeof(T)])[row];
        }

        public bool HasComponent<T>(Entity entity) where T : unmanaged, IComponentData =>
            world.Present.TryGetValue(typeof(T), out var present) && present.Contains(entity.Index);

        public void AddComponent<T>(Entity entity, T componentData) where T : unmanaged, IComponentData
        {
            var owner = world;
            for (var index = 0; index < world.Chunks.Count; index++)
            {
                if (!world.Chunks[index].Columns.ContainsKey(typeof(T)))
                {
                    var chunk = world.Chunks[index];
                    world.Chunks[index] = new FakeChunk(chunk.Count, chunk.Columns.Add(typeof(T), owner.Allocate(typeof(T), FakeWorld.ChunkCapacity)), chunk.EnabledMask, chunk.Entities);
                }
            }

            var (chunkIndex, row) = world.Locations[entity.Index];
            ((T*)world.Chunks[chunkIndex].Columns[typeof(T)])[row] = componentData;
            world.Present.TryAdd(typeof(T), new HashSet<int>());
            world.Present[typeof(T)].Add(entity.Index);
        }

        public void RemoveComponent<T>(Entity entity) where T : unmanaged, IComponentData
        {
            if (!world.Present.TryGetValue(typeof(T), out var present) || !present.Remove(entity.Index))
            {
                return;
            }

            var (chunkIndex, row) = world.Locations[entity.Index];
            var chunk = world.Chunks[chunkIndex];
            var last = chunk.Count - 1;
            if (row != last)
            {
                foreach (var (type, pointer) in chunk.Columns)
                {
                    var size = SizeOf(type);
                    Buffer.MemoryCopy((void*)(pointer + size * last), (void*)(pointer + size * row), size, size);
                }

                var entities = chunk.Entities!;
                var movedEntity = entities[last];
                world.Locations[movedEntity.Index] = (chunkIndex, row);
                var swapped = (Entity[])entities.Clone();
                swapped[row] = movedEntity;
                world.Chunks[chunkIndex] = new FakeChunk(chunk.Count - 1, chunk.Columns, chunk.EnabledMask, swapped);
            }
            else
            {
                world.Chunks[chunkIndex] = new FakeChunk(chunk.Count - 1, chunk.Columns, chunk.EnabledMask, chunk.Entities);
            }

            foreach (var type in chunk.Columns.Keys)
            {
                if (world.Present.TryGetValue(type, out var removed))
                {
                    removed.Remove(entity.Index);
                }
            }
        }

        internal static int SizeOf(Type type) =>
            (int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!.MakeGenericMethod(type).Invoke(null, null)!;

        public ComponentTypeHandle<T> GetComponentTypeHandle<T>(bool isReadOnly = false)
            where T : unmanaged, IComponentData =>
            new(isReadOnly);

        public EntityTypeHandle GetEntityTypeHandle() => new();

        public ComponentLookup<T> GetComponentLookup<T>(bool isReadOnly = false)
            where T : unmanaged, IComponentData =>
            new(world);

        public NativeArray<ArchetypeChunk> GetAllChunks()
        {
            var chunks = new ArchetypeChunk[world.Chunks.Count];
            for (var index = 0; index < world.Chunks.Count; index++)
            {
                var chunk = world.Chunks[index];
                chunks[index] = new ArchetypeChunk(chunk.Count, chunk.Columns, chunk.Entities);
            }

            return new NativeArray<ArchetypeChunk>(chunks, Allocator.Temp);
        }

        public ComponentDataFromEntity<T> GetComponentDataFromEntity<T>(bool isReadOnly = false)
            where T : unmanaged, IComponentData =>
            new(world);
    }

    public struct EntityTypeHandle
    {
        public EntityTypeHandle Update(ref SystemState state) => this;
    }

    public unsafe struct ComponentLookup<T>
        where T : unmanaged, IComponentData
    {
        private readonly FakeWorld world;

        internal ComponentLookup(FakeWorld world) => this.world = world;

        public ComponentLookup<T> Update(ref SystemState state) => this;

        public bool HasComponent(Entity entity) =>
            world.Present.TryGetValue(typeof(T), out var present) && present.Contains(entity.Index);

        public T this[Entity entity]
        {
            get
            {
                var (chunk, row) = world.Locations[entity.Index];
                return ((T*)world.Chunks[chunk].Columns[typeof(T)])[row];
            }

            set
            {
                var (chunk, row) = world.Locations[entity.Index];
                ((T*)world.Chunks[chunk].Columns[typeof(T)])[row] = value;
            }
        }
    }

    public readonly unsafe struct ComponentDataFromEntity<T>
        where T : unmanaged, IComponentData
    {
        private readonly FakeWorld world;

        internal ComponentDataFromEntity(FakeWorld world) => this.world = world;

        public T this[Entity entity]
        {
            get
            {
                var (chunk, row) = world.Locations[entity.Index];
                return ((T*)world.Chunks[chunk].Columns[typeof(T)])[row];
            }

            set
            {
                var (chunk, row) = world.Locations[entity.Index];
                ((T*)world.Chunks[chunk].Columns[typeof(T)])[row] = value;
            }
        }
    }

    public struct SystemState
    {
        private readonly FakeWorld world;

        public SystemState(FakeWorld world)
        {
            this.world = world;
            Dependency = default;
            RequiredQueries = ImmutableArray<EntityQuery>.Empty;
        }

        public WorldUnmanaged WorldUnmanaged => new(world);

        public JobHandle Dependency { get; set; }

        public ImmutableArray<EntityQuery> RequiredQueries { get; private set; }

        internal FakeWorld World => world;

        public ComponentTypeHandle<T> GetComponentTypeHandle<T>(bool isReadOnly = false)
            where T : unmanaged, IComponentData =>
            new(isReadOnly);

        public EntityTypeHandle GetEntityTypeHandle() => new();

        public ComponentDataFromEntity<T> GetComponentDataFromEntity<T>(bool isReadOnly = false)
            where T : unmanaged, IComponentData =>
            new(world);

        public ComponentLookup<T> GetComponentLookup<T>(bool isReadOnly = false)
            where T : unmanaged, IComponentData =>
            new(world);

        public void RequireForUpdate(EntityQuery query) => RequiredQueries = RequiredQueries.Add(query);
    }

    public struct ComponentTypeHandle<T>
    {
        internal ComponentTypeHandle(bool isReadOnly) => IsReadOnly = isReadOnly;

        public bool IsReadOnly { get; }

        public void Update(ref SystemState state)
        {
        }
    }

    public readonly unsafe struct ArchetypeChunk
    {
        private readonly ImmutableDictionary<Type, IntPtr> columns;
        private readonly Entity[]? entities;

        public ArchetypeChunk(int count, ImmutableDictionary<Type, IntPtr> columns, Entity[]? entities = null)
        {
            Count = count;
            this.columns = columns;
            this.entities = entities;
        }

        public int Count { get; }

        public NativeArray<Entity> GetNativeArray(ref EntityTypeHandle typeHandle) =>
            new(entities ?? Array.Empty<Entity>(), Allocator.Temp);

        public T* GetComponentDataPtrRW<T>(ref ComponentTypeHandle<T> typeHandle)
            where T : unmanaged, IComponentData =>
            typeHandle.IsReadOnly
                ? throw new InvalidOperationException("Provided ComponentTypeHandle is read-only; can't get a read/write pointer to component data")
                : (T*)columns[typeof(T)];

        public T* GetComponentDataPtrRO<T>(ref ComponentTypeHandle<T> typeHandle)
            where T : unmanaged, IComponentData =>
            (T*)columns[typeof(T)];

        public bool Has<T>() where T : unmanaged, IComponentData => columns.ContainsKey(typeof(T));
    }

    public readonly struct EntityQuery
    {
        private static readonly List<(FakeWorld World, ImmutableArray<(Type Type, bool Writes)> Components)> Registry = new();

        private readonly int handle;

        internal EntityQuery(FakeWorld world, ImmutableArray<(Type Type, bool Writes)> components)
        {
            lock (Registry)
            {
                Registry.Add((world, components));
                handle = Registry.Count - 1;
            }
        }

        public FakeWorld World => Entry.World;

        public ImmutableArray<(Type Type, bool Writes)> Components => Entry.Components;

        private (FakeWorld World, ImmutableArray<(Type Type, bool Writes)> Components) Entry
        {
            get
            {
                lock (Registry)
                {
                    return Registry[handle];
                }
            }
        }

        public int CalculateChunkCountWithoutFiltering() => World.Chunks.Count(Matches);

        internal bool Matches(FakeChunk chunk) => Components.All(component => chunk.Columns.ContainsKey(component.Type));
    }

    public struct EntityQueryBuilder : IDisposable
    {
        private ImmutableArray<(Type Type, bool Writes)> components;

        public EntityQueryBuilder(AllocatorManager.AllocatorHandle allocator) => components = ImmutableArray<(Type, bool)>.Empty;

        public EntityQueryBuilder WithAll<T1>() => Add((typeof(T1), false));

        public EntityQueryBuilder WithAll<T1, T2>() => Add((typeof(T1), false), (typeof(T2), false));

        public EntityQueryBuilder WithAllRW<T1>() => Add((typeof(T1), true));

        public EntityQueryBuilder WithAllRW<T1, T2>() => Add((typeof(T1), true), (typeof(T2), true));

        public EntityQuery Build(ref SystemState systemState) => new(systemState.World, components);

        public void Dispose()
        {
        }

        private EntityQueryBuilder Add(params (Type Type, bool Writes)[] added)
        {
            var next = this;
            next.components = components.AddRange(added);
            return next;
        }
    }

    public static class JobChunkExtensions
    {
        public static JobHandle ScheduleParallel<T>(this T jobData, EntityQuery query, JobHandle dependsOn)
            where T : struct, IJobChunk
        {
            var matching = query.World.Chunks.Where(query.Matches).ToArray();
            foreach (var (chunk, index) in matching.Select((chunk, index) => (chunk, index)))
            {
                jobData.Execute(new ArchetypeChunk(chunk.Count, chunk.Columns, chunk.Entities), index, chunk.EnabledMask.HasValue, chunk.EnabledMask ?? default);
            }

            return dependsOn;
        }
    }

    public struct ChunkEntityEnumerator
    {
        private ulong mask64_0;
        private ulong mask64_1;

        public ChunkEntityEnumerator(bool useEnabledMask, v128 chunkEnabledMask, int chunkEntityCount)
        {
            mask64_0 = useEnabledMask ? chunkEnabledMask.ULong0 : ~0ul;
            mask64_1 = useEnabledMask ? chunkEnabledMask.ULong1 : ~0ul;

            if (chunkEntityCount < 128)
            {
                if (chunkEntityCount < 64)
                {
                    mask64_0 &= ~(~0ul << chunkEntityCount);
                    mask64_1 = 0;
                }
                else
                {
                    mask64_1 &= ~(~0ul << (chunkEntityCount - 64));
                }
            }
        }

        public bool NextEntityIndex(out int nextIndex)
        {
            if (mask64_0 != 0)
            {
                nextIndex = math.tzcnt(mask64_0);
                mask64_0 &= mask64_0 - 1;
                return true;
            }

            if (mask64_1 != 0)
            {
                nextIndex = math.tzcnt(mask64_1) + 64;
                mask64_1 &= mask64_1 - 1;
                return true;
            }

            nextIndex = -1;
            return false;
        }

        public bool NextEntityIndexBatch(out int startIndex, out int count)
        {
            if (!EnabledBitUtility.TryGetNextRange(new v128(mask64_0, mask64_1), 0, out startIndex, out var end))
            {
                startIndex = -1;
                count = 0;
                return false;
            }

            count = end - startIndex;
            mask64_0 = end >= 64 ? 0ul : mask64_0 & (~0ul << end);
            mask64_1 = end <= 64 ? mask64_1 : (end >= 128 ? 0ul : mask64_1 & (~0ul << (end - 64)));
            return true;
        }
    }

    internal static class EnabledBitUtility
    {
        internal static bool TryGetNextRange(v128 mask, int firstIndexToCheck, out int nextRangeBegin, out int nextRangeEnd)
        {
            mask = ShiftRight(mask, firstIndexToCheck);
            var start = TrailingZeroCount(mask);
            mask = ShiftRight(mask, start);
            var negatedMask = new v128(~mask.ULong0, ~mask.ULong1);
            var count = TrailingZeroCount(negatedMask);
            nextRangeBegin = firstIndexToCheck + start;
            nextRangeEnd = nextRangeBegin + count;
            return count != 0;
        }

        private static v128 ShiftRight(in v128 v, int n) => n switch
        {
            >= 128 => default,
            0 => v,
            >= 64 => new v128(v.ULong1 >> (n - 64), 0),
            _ => new v128((v.ULong0 >> n) | (v.ULong1 << (64 - n)), v.ULong1 >> n),
        };

        private static int TrailingZeroCount(v128 u)
        {
            var high = math.tzcnt(u.ULong1) + 64;
            var low = math.tzcnt(u.ULong0);
            return low == 64 ? high : low;
        }
    }
}

namespace Unity.Core
{
    public readonly struct TimeData
    {
        public readonly double ElapsedTime;

        public readonly float DeltaTime;

        public TimeData(double elapsedTime, float deltaTime)
        {
            ElapsedTime = elapsedTime;
            DeltaTime = deltaTime;
        }
    }
}

namespace Unity.Jobs
{
    public readonly struct JobHandle
    {
        public void Complete()
        {
        }
    }

    public interface IJob
    {
        void Execute();
    }

    public static class IJobExtensions
    {
        public static void Run<T>(this T jobData)
            where T : struct, IJob =>
            jobData.Execute();

        public static JobHandle Schedule<T>(this T jobData, JobHandle dependsOn = default)
            where T : struct, IJob
        {
            jobData.Execute();
            return dependsOn;
        }
    }
}

namespace Unity.Collections
{
    public enum Allocator
    {
        Invalid = 0,
        None = 1,
        Temp = 2,
        TempJob = 3,
        Persistent = 4,
    }

    public static class AllocatorManager
    {
        public readonly struct AllocatorHandle
        {
            public static implicit operator AllocatorHandle(Allocator allocator) => default;
        }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter)]
    public sealed class ReadOnlyAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class NativeDisableParallelForRestrictionAttribute : Attribute
    {
    }

    public enum NativeArrayOptions
    {
        UninitializedMemory = 0,
        ClearMemory = 1,
    }

    public struct NativeArray<T> : IDisposable
        where T : struct
    {
        private readonly T[] items;

        public NativeArray(T[] array, Allocator allocator)
        {
            items = GC.AllocateArray<T>(array.Length, pinned: true);
            array.CopyTo(items, 0);
        }

        public NativeArray(int length, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory) =>
            items = GC.AllocateArray<T>(length, pinned: true);

        public int Length => items.Length;

        public T this[int index]
        {
            get => items[index];
            set => items[index] = value;
        }

        public T[] ToArray() => (T[])items.Clone();

        public void Dispose()
        {
        }

        internal T[] Items => items;
    }

    public struct NativeSlice<T> : IDisposable
        where T : struct
    {
        private readonly T[] items;

        public NativeSlice(T[] array, int start, int length)
        {
            items = GC.AllocateArray<T>(length, pinned: true);
            global::System.Array.Copy(array, start, items, 0, length);
        }

        public int Length => items.Length;

        public T this[int index]
        {
            get => items[index];
            set => items[index] = value;
        }

        public void Dispose()
        {
        }

        internal T[] Items => items;
    }

    public struct NativeQueue<T> : IDisposable
        where T : struct
    {
        private readonly Queue<T> items;

        public NativeQueue(Allocator allocator) => items = new Queue<T>();

        public int Count => items.Count;

        public void Enqueue(T item) => items.Enqueue(item);

        public bool TryDequeue(out T item)
        {
            if (items.Count == 0)
            {
                item = default!;
                return false;
            }

            item = items.Dequeue();
            return true;
        }

        public ParallelWriter AsParallelWriter() => new(items);

        public void Dispose()
        {
        }

        public readonly struct ParallelWriter
        {
            private readonly Queue<T> items;

            internal ParallelWriter(Queue<T> items) => this.items = items;

            public void Enqueue(T item) => items.Enqueue(item);
        }
    }
}

namespace Unity.Collections.LowLevel.Unsafe
{
    public static class NativeArrayUnsafeUtility
    {
        public static unsafe void* GetUnsafePtr<T>(this NativeArray<T> nativeArray)
            where T : struct =>
            global::System.Runtime.CompilerServices.Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(nativeArray.Items));
    }

    public static class NativeSliceUnsafeUtility
    {
        public static unsafe void* GetUnsafePtr<T>(this NativeSlice<T> slice)
            where T : struct =>
            global::System.Runtime.CompilerServices.Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(slice.Items));
    }
}
