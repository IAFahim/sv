[assembly: global::Unity.Entities.RegisterGenericSystemType(typeof(global::Sv.Columns.Entities.ColumnsCardSystem<global::Sv.Columns.Entities.TlTests.Glue.PoisonCard>))]
[assembly: global::Unity.Entities.RegisterGenericSystemType(typeof(global::Sv.Columns.Entities.ColumnsOutboxSystem<global::Sv.Columns.Entities.TlTests.Glue.FireOut>))]
[assembly: global::Unity.Entities.RegisterGenericSystemType(typeof(global::Sv.Columns.Entities.ColumnsOutboxSystem<global::Sv.Columns.Entities.TlTests.Glue.HealingOut>))]

namespace Sv.Columns.Entities.TlTests.Glue
{
    public partial struct PoisonCard : global::Unity.Entities.IComponentData, global::Sv.Columns.Entities.IColumnsCardCell
    {
        public int Value;

        public string Card => "poison";

        public int Level
        {
            get => Value;
            set => Value = value;
        }
    }

    public partial struct FireOut : global::Unity.Entities.IComponentData, global::Sv.Columns.Entities.IColumnsOutboxCell
    {
        public int Value;

        public string Channel => "Fire";

        public string TargetLane => "Aim";

        public int Amount
        {
            get => Value;
            set => Value = value;
        }
    }

    public partial struct HealingOut : global::Unity.Entities.IComponentData, global::Sv.Columns.Entities.IColumnsOutboxCell
    {
        public int Value;

        public string Channel => "Healing";

        public string TargetLane => "";

        public int Amount
        {
            get => Value;
            set => Value = value;
        }
    }

    public static class TimelineRules
    {
        public static void AddTo(global::Unity.Entities.DynamicBuffer<global::Sv.Columns.Entities.ColumnsTimelineRule> rules, global::Sv.Columns.CompiledPack pack, global::System.Func<string, ushort> findTimeline)
        {
            rules.Add(new global::Sv.Columns.Entities.ColumnsTimelineRule { Event = pack.Event("LowHealth"), Timeline = findTimeline("poison") });
        }
    }
}
