namespace Sv.Rules
{
    public enum ValueClass : byte
    {
        Stat = 0,
        Intrinsic = 1,
        Event = 2,
        Channel = 3,
        Kind = 4,
    }

    public enum IntrinsicRole : byte
    {
        Plain = 0,
        Kind = 1,
        Owner = 2,
        Reference = 3,
    }

    public enum ShapeKind : byte
    {
        Added = 0,
        Additive = 1,
        Multi = 2,
        Cap = 3,
    }

    public static class LaneNumbers
    {
        public const int EventWords = 32;
        public const int EventWordShift = 5;
        public const int MaxLanes = 65536 >> EventWordShift;
        public const int MaxChannels = 256;
        public const int MaxKinds = 256;
        public const int ChannelWord = 64;

        public static int LaneOfEvent(int eventId) => eventId >> EventWordShift;

        public static int WordOfEvent(int eventId) => eventId & (EventWords - 1);

        public static int EventId(int lane, int word) => (lane << EventWordShift) | (word & (EventWords - 1));
    }

    public sealed class ValueDef
    {
        public int Id { get; }

        public string Name { get; }

        public ValueClass Class { get; }

        public IntrinsicRole Role { get; }

        public int IntDefault { get; }

        public float StatDefault { get; }

        public bool EventDefault { get; }

        public bool Timeline { get; }

        public int Into { get; }

        public int Offset { get; }

        public ValueDef(int id, string name, ValueClass valueClass, IntrinsicRole role, int intDefault, float statDefault, bool eventDefault, bool timeline, int into, int offset)
        {
            Id = id;
            Name = name;
            Class = valueClass;
            Role = role;
            IntDefault = intDefault;
            StatDefault = statDefault;
            EventDefault = eventDefault;
            Timeline = timeline;
            Into = into;
            Offset = offset;
        }

        public int Lane => Class == ValueClass.Event ? LaneNumbers.LaneOfEvent(Id) : Id;
    }

    public readonly struct StatModifier
    {
        public int Stat { get; }

        public ShapeKind Kind { get; }

        public float Value { get; }

        public StatModifier(int stat, ShapeKind kind, float value)
        {
            Stat = stat;
            Kind = kind;
            Value = value;
        }
    }

    public sealed class CardDef
    {
        public int Id { get; }

        public string Name { get; }

        public int Intrinsic { get; }

        public StatModifier[] Modifiers { get; }

        public int Offset { get; }

        public CardDef(int id, string name, int intrinsic, StatModifier[] modifiers, int offset)
        {
            Id = id;
            Name = name;
            Intrinsic = intrinsic;
            Modifiers = modifiers;
            Offset = offset;
        }
    }

    public readonly struct ValueSetting
    {
        public ValueClass Class { get; }

        public int Id { get; }

        public int Int { get; }

        public float Real { get; }

        public bool Level { get; }

        public ValueSetting(ValueClass valueClass, int id, int intValue, float real, bool level)
        {
            Class = valueClass;
            Id = id;
            Int = intValue;
            Real = real;
            Level = level;
        }
    }

    public readonly struct Forward
    {
        public int Channel { get; }

        public int Via { get; }

        public Forward(int channel, int via)
        {
            Channel = channel;
            Via = via;
        }
    }

    public sealed class TemplateDef
    {
        public int Id { get; }

        public string Name { get; }

        public ValueSetting[] Settings { get; }

        public Forward[] Forwards { get; }

        public int Offset { get; }

        public TemplateDef(int id, string name, ValueSetting[] settings, Forward[] forwards, int offset)
        {
            Id = id;
            Name = name;
            Settings = settings;
            Forwards = forwards;
            Offset = offset;
        }
    }

    public sealed class PackSchema
    {
        public int NextLane { get; }

        public ValueDef[] Stats { get; }

        public ValueDef[] Intrinsics { get; }

        public ValueDef[] Events { get; }

        public ValueDef[] Channels { get; }

        public ValueDef[] Kinds { get; }

        public string[] Tags { get; }

        public CardDef[] Cards { get; }

        public TemplateDef[] Templates { get; }

        public int KindIntrinsic { get; }

        public int OwnerIntrinsic { get; }

        public PackSchema(int nextLane, ValueDef[] stats, ValueDef[] intrinsics, ValueDef[] events, ValueDef[] channels, ValueDef[] kinds, string[] tags, CardDef[] cards, TemplateDef[] templates)
        {
            NextLane = nextLane;
            Stats = stats;
            Intrinsics = intrinsics;
            Events = events;
            Channels = channels;
            Kinds = kinds;
            Tags = tags;
            Cards = cards;
            Templates = templates;
            KindIntrinsic = RoleHolder(intrinsics, IntrinsicRole.Kind);
            OwnerIntrinsic = RoleHolder(intrinsics, IntrinsicRole.Owner);
        }

        static int RoleHolder(ValueDef[] intrinsics, IntrinsicRole role)
        {
            for (var i = 0; i < intrinsics.Length; i++)
            {
                if (intrinsics[i].Role == role)
                {
                    return intrinsics[i].Id;
                }
            }

            return -1;
        }

        public ValueDef[] Of(ValueClass valueClass)
        {
            switch (valueClass)
            {
                case ValueClass.Stat: return Stats;
                case ValueClass.Intrinsic: return Intrinsics;
                case ValueClass.Event: return Events;
                case ValueClass.Channel: return Channels;
                default: return Kinds;
            }
        }

        public int IndexOf(ValueClass valueClass, int id)
        {
            var rows = Of(valueClass);
            var low = 0;
            var high = rows.Length - 1;
            while (low <= high)
            {
                var middle = (low + high) >> 1;
                var at = rows[middle].Id;
                if (at == id)
                {
                    return middle;
                }

                if (at < id)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return -1;
        }

        public ValueDef? Find(ValueClass valueClass, int id)
        {
            var index = IndexOf(valueClass, id);
            return index < 0 ? null : Of(valueClass)[index];
        }

        public ValueDef? FindByName(ValueClass valueClass, string name)
        {
            var rows = Of(valueClass);
            for (var i = 0; i < rows.Length; i++)
            {
                if (string.CompareOrdinal(rows[i].Name, name) == 0)
                {
                    return rows[i];
                }
            }

            return null;
        }

        public string NameOf(ValueClass valueClass, int id)
        {
            var found = Find(valueClass, id);
            return found == null ? "?" + id.ToString(System.Globalization.CultureInfo.InvariantCulture) : found.Name;
        }

        public TemplateDef? Template(int id)
        {
            for (var i = 0; i < Templates.Length; i++)
            {
                if (Templates[i].Id == id)
                {
                    return Templates[i];
                }
            }

            return null;
        }

        public int TagOf(string name)
        {
            for (var i = 0; i < Tags.Length; i++)
            {
                if (string.CompareOrdinal(Tags[i], name) == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        public bool ChannelsFitWord => Channels.Length == 0 || Channels[Channels.Length - 1].Id < LaneNumbers.ChannelWord;
    }
}
