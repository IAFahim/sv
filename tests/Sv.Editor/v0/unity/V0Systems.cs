using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace SvV0
{
    [DisableAutoCreation]
    public partial struct V0FoldSystem : ISystem
    {
        public bool Filter;
        public int Intrinsics;
        public NativeArray<int> Visited;
        public NativeArray<int> FlagChanged;
        public NativeArray<int> MaxChanged;
        EntityQuery query;
        ComponentTypeHandle<V0Hp> hp;
        ComponentTypeHandle<V0HpDelta> hpd;
        ComponentTypeHandle<V0HpMax> hpm;
        ComponentTypeHandle<V0Mana> mp;
        ComponentTypeHandle<V0ManaDelta> mpd;
        ComponentTypeHandle<V0ManaMax> mpm;
        ComponentTypeHandle<V0Flag> flag;

        public void OnCreate(ref SystemState state)
        {
            query = new EntityQueryBuilder(Allocator.Temp).WithAll<V0Hp>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build(ref state);
            hp = state.GetComponentTypeHandle<V0Hp>(false);
            hpd = state.GetComponentTypeHandle<V0HpDelta>(false);
            hpm = state.GetComponentTypeHandle<V0HpMax>(true);
            mp = state.GetComponentTypeHandle<V0Mana>(false);
            mpd = state.GetComponentTypeHandle<V0ManaDelta>(false);
            mpm = state.GetComponentTypeHandle<V0ManaMax>(true);
            flag = state.GetComponentTypeHandle<V0Flag>(true);
            Intrinsics = 2;
        }

        public void OnUpdate(ref SystemState state)
        {
            hp.Update(ref state);
            hpd.Update(ref state);
            hpm.Update(ref state);
            mp.Update(ref state);
            mpd.Update(ref state);
            mpm.Update(ref state);
            flag.Update(ref state);
            var job = new V0FoldJob { Hp = hp, HpDelta = hpd, HpMax = hpm, Mana = mp, ManaDelta = mpd, ManaMax = mpm, Flag = flag, LastVersion = state.LastSystemVersion, Filter = Filter, Intrinsics = Intrinsics, Visited = Visited, FlagChanged = FlagChanged, MaxChanged = MaxChanged };
            state.Dependency = job.ScheduleParallel(query, state.Dependency);
            state.Dependency.Complete();
        }
    }

    [DisableAutoCreation]
    public partial struct V0TouchSystem : ISystem
    {
        public NativeArray<long> Sums;
        EntityQuery query;
        ComponentTypeHandle<V0Hp> hp;
        ComponentTypeHandle<V0HpDelta> hpd;
        ComponentTypeHandle<V0HpMax> hpm;
        ComponentTypeHandle<V0Mana> mp;
        ComponentTypeHandle<V0ManaDelta> mpd;
        ComponentTypeHandle<V0ManaMax> mpm;

        public void OnCreate(ref SystemState state)
        {
            query = new EntityQueryBuilder(Allocator.Temp).WithAll<V0Hp>().Build(ref state);
            hp = state.GetComponentTypeHandle<V0Hp>(true);
            hpd = state.GetComponentTypeHandle<V0HpDelta>(true);
            hpm = state.GetComponentTypeHandle<V0HpMax>(true);
            mp = state.GetComponentTypeHandle<V0Mana>(true);
            mpd = state.GetComponentTypeHandle<V0ManaDelta>(true);
            mpm = state.GetComponentTypeHandle<V0ManaMax>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            hp.Update(ref state);
            hpd.Update(ref state);
            hpm.Update(ref state);
            mp.Update(ref state);
            mpd.Update(ref state);
            mpm.Update(ref state);
            var job = new V0TouchJob { Hp = hp, HpDelta = hpd, HpMax = hpm, Mana = mp, ManaDelta = mpd, ManaMax = mpm, Sums = Sums };
            state.Dependency = job.ScheduleParallel(query, state.Dependency);
            state.Dependency.Complete();
        }
    }

    [DisableAutoCreation]
    public partial struct V0DeltaSystem : ISystem
    {
        public int Amount;
        EntityQuery query;
        ComponentTypeHandle<V0HpDelta> hpd;
        ComponentTypeHandle<V0ManaDelta> mpd;

        public void OnCreate(ref SystemState state)
        {
            query = new EntityQueryBuilder(Allocator.Temp).WithAll<V0Hp>().Build(ref state);
            hpd = state.GetComponentTypeHandle<V0HpDelta>(false);
            mpd = state.GetComponentTypeHandle<V0ManaDelta>(false);
            Amount = 3;
        }

        public void OnUpdate(ref SystemState state)
        {
            hpd.Update(ref state);
            mpd.Update(ref state);
            var job = new V0DeltaJob { HpDelta = hpd, ManaDelta = mpd, Amount = Amount };
            state.Dependency = job.ScheduleParallel(query, state.Dependency);
            state.Dependency.Complete();
        }
    }

    [DisableAutoCreation]
    public partial struct V0WriterSystem : ISystem
    {
        public int Mode;
        public int Count;
        public NativeArray<Entity> Targets;
        EntityQuery query;
        ComponentTypeHandle<V0HpDelta> hpd;
        ComponentTypeHandle<V0HpMax> hpm;
        ComponentTypeHandle<V0Flag> flag;
        ComponentLookup<V0HpMax> maxLookup;
        ComponentLookup<V0Flag> flagLookup;

        public void OnCreate(ref SystemState state)
        {
            query = new EntityQueryBuilder(Allocator.Temp).WithAll<V0Hp>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build(ref state);
            hpd = state.GetComponentTypeHandle<V0HpDelta>(false);
            hpm = state.GetComponentTypeHandle<V0HpMax>(true);
            flag = state.GetComponentTypeHandle<V0Flag>(false);
            maxLookup = state.GetComponentLookup<V0HpMax>(false);
            flagLookup = state.GetComponentLookup<V0Flag>(false);
        }

        public void OnUpdate(ref SystemState state)
        {
            if (Mode == 0)
            {
                return;
            }
            if (Mode == 3)
            {
                maxLookup.Update(ref state);
                state.Dependency = new V0ScatterJob { Lookup = maxLookup, Targets = Targets, Count = Count }.Schedule(state.Dependency);
                state.Dependency.Complete();
                return;
            }
            if (Mode == 4)
            {
                flagLookup.Update(ref state);
                state.Dependency = new V0EnableLookupJob { Lookup = flagLookup, Targets = Targets, Count = Count }.Schedule(state.Dependency);
                state.Dependency.Complete();
                return;
            }
            hpd.Update(ref state);
            hpm.Update(ref state);
            flag.Update(ref state);
            state.Dependency = new V0WriterJob { HpDelta = hpd, HpMax = hpm, Flag = flag, Mode = Mode }.ScheduleParallel(query, state.Dependency);
            state.Dependency.Complete();
        }
    }
}
