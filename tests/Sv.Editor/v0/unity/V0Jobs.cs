using System.Threading;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;

namespace SvV0
{
    [BurstCompile(FloatMode = FloatMode.Strict, CompileSynchronously = true)]
    public unsafe struct V0TouchJob : IJobChunk
    {
        [ReadOnly] public ComponentTypeHandle<V0Hp> Hp;
        [ReadOnly] public ComponentTypeHandle<V0HpDelta> HpDelta;
        [ReadOnly] public ComponentTypeHandle<V0HpMax> HpMax;
        [ReadOnly] public ComponentTypeHandle<V0Mana> Mana;
        [ReadOnly] public ComponentTypeHandle<V0ManaDelta> ManaDelta;
        [ReadOnly] public ComponentTypeHandle<V0ManaMax> ManaMax;
        [NativeDisableParallelForRestriction] public NativeArray<long> Sums;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            V0Probe.Mark();
            var hp = (int*)chunk.GetRequiredComponentDataPtrRO(ref Hp);
            var hpd = (int*)chunk.GetRequiredComponentDataPtrRO(ref HpDelta);
            var hpm = (int*)chunk.GetRequiredComponentDataPtrRO(ref HpMax);
            var mp = (int*)chunk.GetRequiredComponentDataPtrRO(ref Mana);
            var mpd = (int*)chunk.GetRequiredComponentDataPtrRO(ref ManaDelta);
            var mpm = (int*)chunk.GetRequiredComponentDataPtrRO(ref ManaMax);
            long sum = 0;
            var count = chunk.Count;
            for (var i = 0; i < count; i++)
            {
                sum += hp[i] + hpd[i] + hpm[i] + mp[i] + mpd[i] + mpm[i];
            }
            Sums[unfilteredChunkIndex] = sum;
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, CompileSynchronously = true)]
    public unsafe struct V0FoldJob : IJobChunk
    {
        public ComponentTypeHandle<V0Hp> Hp;
        public ComponentTypeHandle<V0HpDelta> HpDelta;
        [ReadOnly] public ComponentTypeHandle<V0HpMax> HpMax;
        public ComponentTypeHandle<V0Mana> Mana;
        public ComponentTypeHandle<V0ManaDelta> ManaDelta;
        [ReadOnly] public ComponentTypeHandle<V0ManaMax> ManaMax;
        [ReadOnly] public ComponentTypeHandle<V0Flag> Flag;
        public uint LastVersion;
        public bool Filter;
        public int Intrinsics;
        [NativeDisableParallelForRestriction] public NativeArray<int> Visited;
        [NativeDisableParallelForRestriction] public NativeArray<int> FlagChanged;
        [NativeDisableParallelForRestriction] public NativeArray<int> MaxChanged;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            V0Probe.Mark();
            FlagChanged[unfilteredChunkIndex] = chunk.DidChange(ref Flag, LastVersion) ? 1 : 0;
            MaxChanged[unfilteredChunkIndex] = chunk.DidChange(ref HpMax, LastVersion) ? 1 : 0;
            if (Filter && !chunk.DidChange(ref HpDelta, LastVersion) && !chunk.DidChange(ref ManaDelta, LastVersion) && !chunk.DidChange(ref HpMax, LastVersion) && !chunk.DidChange(ref ManaMax, LastVersion))
            {
                Visited[unfilteredChunkIndex] = 0;
                return;
            }
            var count = chunk.Count;
            var changed = 0;
            changed += Column((int*)chunk.GetRequiredComponentDataPtrRW(ref Hp), (int*)chunk.GetRequiredComponentDataPtrRW(ref HpDelta), (float*)chunk.GetRequiredComponentDataPtrRO(ref HpMax), count);
            if (Intrinsics > 1)
            {
                changed += Column((int*)chunk.GetRequiredComponentDataPtrRW(ref Mana), (int*)chunk.GetRequiredComponentDataPtrRW(ref ManaDelta), (float*)chunk.GetRequiredComponentDataPtrRO(ref ManaMax), count);
            }
            Visited[unfilteredChunkIndex] = 1 + changed;
        }

        static int Column(int* value, int* delta, float* ceiling, int count)
        {
            var changed = 0;
            for (var row = 0; row < count; row++)
            {
                var before = value[row];
                var after = V0Law.Step(before, delta[row], 0, V0Law.BoundOf(ceiling[row]));
                value[row] = after;
                delta[row] = 0;
                changed += after != before ? 1 : 0;
            }
            return changed;
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, CompileSynchronously = true)]
    public unsafe struct V0DeltaJob : IJobChunk
    {
        public ComponentTypeHandle<V0HpDelta> HpDelta;
        public ComponentTypeHandle<V0ManaDelta> ManaDelta;
        public int Amount;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            V0Probe.Mark();
            var hpd = (int*)chunk.GetRequiredComponentDataPtrRW(ref HpDelta);
            var mpd = (int*)chunk.GetRequiredComponentDataPtrRW(ref ManaDelta);
            var count = chunk.Count;
            for (var i = 0; i < count; i++)
            {
                hpd[i] += ((i & 1) == 0 ? Amount : -Amount);
                mpd[i] += Amount;
            }
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, CompileSynchronously = true)]
    public unsafe struct V0WriterJob : IJobChunk
    {
        public ComponentTypeHandle<V0HpDelta> HpDelta;
        [ReadOnly] public ComponentTypeHandle<V0HpMax> HpMax;
        public ComponentTypeHandle<V0Flag> Flag;
        public int Mode;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            V0Probe.Mark();
            if (Mode == 1 && (unfilteredChunkIndex & 1) == 0)
            {
                var p = (int*)chunk.GetRequiredComponentDataPtrRW(ref HpDelta);
                if (p == null) Mode = -1;
            }
            if (Mode == 2)
            {
                var p = (int*)chunk.GetRequiredComponentDataPtrRO(ref HpDelta);
                var q = (float*)chunk.GetRequiredComponentDataPtrRO(ref HpMax);
                if (p == null || q == null) Mode = -1;
            }
            if (Mode == 5 && (unfilteredChunkIndex & 3) == 0)
            {
                chunk.SetComponentEnabled(ref Flag, 0, false);
            }
            if (Mode == 6 && (unfilteredChunkIndex & 3) == 1)
            {
                var mask = chunk.GetEnabledMask(ref Flag);
                mask[1] = false;
            }
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, CompileSynchronously = true)]
    public struct V0ScatterJob : IJob
    {
        public ComponentLookup<V0HpMax> Lookup;
        [ReadOnly] public NativeArray<Entity> Targets;
        public int Count;

        public void Execute()
        {
            V0Probe.Mark();
            for (var i = 0; i < Count; i++)
            {
                Lookup[Targets[i]] = new V0HpMax { Value = 100f + (i & 255) };
            }
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, CompileSynchronously = true)]
    public struct V0EnableLookupJob : IJob
    {
        public ComponentLookup<V0Flag> Lookup;
        [ReadOnly] public NativeArray<Entity> Targets;
        public int Count;

        public void Execute()
        {
            V0Probe.Mark();
            for (var i = 0; i < Count; i++)
            {
                Lookup.SetComponentEnabled(Targets[i], false);
            }
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    public unsafe struct V0InboxJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public long* Inbox;
        public int Slots;
        public int Mode;

        public void Execute(int index)
        {
            V0Probe.Mark();
            var target = Mode >= 2 ? 0 : (int)((uint)index * 0x9E3779B1u % (uint)Slots);
            if (Mode == 1)
            {
                Inbox[target] += 1;
            }
            else
            {
                Interlocked.Add(ref Inbox[target], 1L);
            }
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, CompileSynchronously = true)]
    public unsafe struct V0StatFoldStrictJob : IJob
    {
        [NativeDisableUnsafePtrRestriction] public float* Bases;
        [NativeDisableUnsafePtrRestriction] public byte* Counts;
        [NativeDisableUnsafePtrRestriction] public ProbeStatEntry* Entries;
        [NativeDisableUnsafePtrRestriction] public uint* Results;

        public void Execute()
        {
            V0Probe.Mark();
            StatFoldSets.FoldAll(Bases, Counts, Entries, Results);
        }
    }

    [BurstCompile(FloatMode = FloatMode.Default, CompileSynchronously = true)]
    public unsafe struct V0StatFoldDefaultJob : IJob
    {
        [NativeDisableUnsafePtrRestriction] public float* Bases;
        [NativeDisableUnsafePtrRestriction] public byte* Counts;
        [NativeDisableUnsafePtrRestriction] public ProbeStatEntry* Entries;
        [NativeDisableUnsafePtrRestriction] public uint* Results;

        public void Execute()
        {
            V0Probe.Mark();
            StatFoldSets.FoldAll(Bases, Counts, Entries, Results);
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
    public unsafe struct V0StatFoldFastJob : IJob
    {
        [NativeDisableUnsafePtrRestriction] public float* Bases;
        [NativeDisableUnsafePtrRestriction] public byte* Counts;
        [NativeDisableUnsafePtrRestriction] public ProbeStatEntry* Entries;
        [NativeDisableUnsafePtrRestriction] public uint* Results;

        public void Execute()
        {
            V0Probe.Mark();
            StatFoldSets.FoldAll(Bases, Counts, Entries, Results);
        }
    }
}
