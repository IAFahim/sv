using System;
using Sv.Rules;

namespace Sv.Host
{
    public sealed class HostPack
    {
        public LaneClass[] Classes { get; }

        public int Channels { get; }

        public StatDef[] Stats { get; }

        public int[][] TemplateValues { get; }

        public CardTerm[] Cards { get; }

        public ulong PackHash { get; }

        public HostPack(LaneClass[] classes, int channels, StatDef[] stats, int[][] templateValues, CardTerm[] cards, ulong packHash)
        {
            Classes = classes;
            Channels = channels;
            Stats = stats;
            TemplateValues = templateValues;
            Cards = cards;
            PackHash = packHash;
        }

        public int Templates => TemplateValues.Length;

        public static HostPack From(PackPlan plan)
        {
            var schema = plan.Schema;
            var lanes = schema.NextLane;
            var classes = new LaneClass[lanes];
            var defaults = new int[lanes];
            for (var i = 0; i < schema.Stats.Length; i++)
            {
                var stat = schema.Stats[i];
                classes[stat.Lane] = LaneClass.Stat;
                defaults[stat.Lane] = stat.IntDefault;
            }

            for (var i = 0; i < schema.Intrinsics.Length; i++)
            {
                var intrinsic = schema.Intrinsics[i];
                classes[intrinsic.Lane] = LaneClass.Intrinsic;
                defaults[intrinsic.Lane] = intrinsic.IntDefault;
            }

            for (var i = 0; i < schema.Events.Length; i++)
            {
                var raised = schema.Events[i];
                classes[raised.Lane] = LaneClass.Event;
                defaults[raised.Lane] |= raised.EventDefault ? 1 << EventId.Word(raised.Id) : 0;
            }

            var stats = new StatDef[schema.Stats.Length];
            for (var i = 0; i < stats.Length; i++)
            {
                stats[i] = new StatDef(schema.Stats[i].Lane, schema.Stats[i].IntDefault, schema.Stats[i].Scale);
            }

            var templates = new int[schema.Templates.Length][];
            for (var t = 0; t < templates.Length; t++)
            {
                var values = (int[])defaults.Clone();
                var settings = schema.Templates[t].Settings;
                for (var s = 0; s < settings.Length; s++)
                {
                    var setting = settings[s];
                    if (setting.Class == ValueClass.Event)
                    {
                        var lane = EventId.Lane(setting.Id);
                        var bit = 1 << EventId.Word(setting.Id);
                        values[lane] = setting.Level ? values[lane] | bit : values[lane] & ~bit;
                    }
                    else if (setting.Class == ValueClass.Stat || setting.Class == ValueClass.Intrinsic)
                    {
                        values[setting.Id] = setting.Int;
                    }
                }

                templates[t] = values;
            }

            return new HostPack(classes, schema.Channels.Length, stats, templates, CardsOf(schema), plan.Hash);
        }

        static CardTerm[] CardsOf(PackSchema schema)
        {
            var count = 0;
            for (var c = 0; c < schema.Cards.Length; c++)
            {
                var card = schema.Cards[c];
                for (var m = 0; m < card.Modifiers.Length; m++)
                {
                    count += Holds(schema, card, card.Modifiers[m]) ? 1 : 0;
                }
            }

            var cards = new CardTerm[count];
            var at = 0;
            for (var c = 0; c < schema.Cards.Length; c++)
            {
                var card = schema.Cards[c];
                for (var m = 0; m < card.Modifiers.Length; m++)
                {
                    var modifier = card.Modifiers[m];
                    if (Holds(schema, card, modifier))
                    {
                        cards[at++] = new CardTerm(schema.Stats[schema.IndexOf(ValueClass.Stat, modifier.Stat)].Lane, schema.Intrinsics[schema.IndexOf(ValueClass.Intrinsic, card.Intrinsic)].Lane, modifier.Kind, modifier.Value, modifier.PerCount);
                    }
                }
            }

            return cards;
        }

        static bool Holds(PackSchema schema, CardDef card, StatModifier modifier) =>
            card.Intrinsic >= 0 && schema.IndexOf(ValueClass.Intrinsic, card.Intrinsic) >= 0 && schema.IndexOf(ValueClass.Stat, modifier.Stat) >= 0;

        public int TemplateIndexOf(PackSchema schema, int templateId)
        {
            for (var t = 0; t < schema.Templates.Length; t++)
            {
                if (schema.Templates[t].Id == templateId)
                {
                    return t;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(templateId), "sv: the pack has no template " + templateId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
