[assembly: global::Unity.Entities.RegisterGenericSystemType(typeof(global::Sv.Unity.SvOutboxSystem<global::Sv.Kernels.NorthStar3.Glue.PoisonOut>))]
[assembly: global::Unity.Entities.RegisterGenericSystemType(typeof(global::Sv.Unity.SvOutboxSystem<global::Sv.Kernels.NorthStar3.Glue.BleedingOut>))]

namespace Sv.Kernels.NorthStar3.Glue
{
    public partial struct PoisonOut : global::Sv.Unity.ISvOutboxCell
    {
        public int Channel => (int)global::Sv.Kernels.NorthStar3.Channel.Poison;

        public int KindLane => (int)global::Sv.Kernels.NorthStar3.Intrinsic.Kind;

        public int TargetLane => (int)global::Sv.Kernels.NorthStar3.Intrinsic.Target;

        public int Amount
        {
            get => Value;
            set => Value = value;
        }
    }

    public partial struct BleedingOut : global::Sv.Unity.ISvOutboxCell
    {
        public int Channel => (int)global::Sv.Kernels.NorthStar3.Channel.Bleeding;

        public int KindLane => (int)global::Sv.Kernels.NorthStar3.Intrinsic.Kind;

        public int TargetLane => (int)global::Sv.Kernels.NorthStar3.Intrinsic.Target;

        public int Amount
        {
            get => Value;
            set => Value = value;
        }
    }

    public static class TimelineRules
    {
        public static void AddTo(global::Unity.Entities.DynamicBuffer<global::Sv.Unity.Timelines.SvTimelineRule> rules, global::System.Func<string, ushort> findTimeline)
        {
            rules.Add(new global::Sv.Unity.Timelines.SvTimelineRule { Event = (int)global::Sv.Kernels.NorthStar3.Event.LowHealth, Timeline = findTimeline("poison") });
        }
    }
}
