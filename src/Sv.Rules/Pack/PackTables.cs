using System;
using System.Collections.Generic;

namespace Sv.Rules
{
    public sealed class PackTables
    {
        public LaneClass[] Classes { get; }

        public int Channels { get; }

        public StatDef[] Stats { get; }

        public int[][] TemplateValues { get; }

        public CardTerm[] Cards { get; }

        public StatSource[] Sources { get; }

        public int[] CurveValues { get; }

        public ulong PackHash { get; }

        public int[] ChannelIds { get; private set; } = Array.Empty<int>();

        PackTables(LaneClass[] classes, int channels, StatDef[] stats, int[][] templateValues, CardTerm[] cards, StatSource[] sources, int[] curveValues, ulong packHash)
        {
            Classes = classes;
            Channels = channels;
            Stats = stats;
            TemplateValues = templateValues;
            Cards = cards;
            Sources = sources;
            CurveValues = curveValues;
            PackHash = packHash;
        }

        public static PackTables Of(PackPlan plan)
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

            SourcesOf(schema, out var sources, out var curves);
            var channelIds = new int[schema.Channels.Length];
            for (var c = 0; c < channelIds.Length; c++)
            {
                channelIds[c] = schema.Channels[c].Id;
            }

            return new PackTables(classes, schema.Channels.Length, stats, templates, CardsOf(schema), sources, curves, plan.Hash) { ChannelIds = channelIds };
        }

        static void SourcesOf(PackSchema schema, out StatSource[] sources, out int[] curveValues)
        {
            var sourceList = new List<StatSource>();
            var curveList = new List<int>();
            for (var i = 0; i < schema.Stats.Length; i++)
            {
                var stat = schema.Stats[i];
                for (var d = 0; d < stat.Derive.Length; d++)
                {
                    var derive = stat.Derive[d];
                    var fromIndex = schema.IndexOf(ValueClass.Stat, derive.From);
                    if (fromIndex < 0)
                    {
                        continue;
                    }

                    var fromLane = schema.Stats[fromIndex].Lane;
                    if (derive.IsCurve)
                    {
                        var start = curveList.Count;
                        for (var c = 0; c < derive.Curve.Length; c++)
                        {
                            curveList.Add(derive.Curve[c]);
                        }

                        sourceList.Add(StatSource.Curve(stat.Lane, fromLane, start, derive.Curve.Length, derive.CurveMin, derive.CurveStep));
                    }
                    else
                    {
                        sourceList.Add(StatSource.Linear(stat.Lane, fromLane, derive.Coefficient));
                    }
                }
            }

            sources = sourceList.Count == 0 ? Array.Empty<StatSource>() : sourceList.ToArray();
            curveValues = curveList.Count == 0 ? Array.Empty<int>() : curveList.ToArray();
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
                        cards[at++] = new CardTerm(schema.Stats[schema.IndexOf(ValueClass.Stat, modifier.Stat)].Lane, schema.Intrinsics[schema.IndexOf(ValueClass.Intrinsic, card.Intrinsic)].Lane, modifier.Kind, modifier.Value, modifier.PerCount, modifier.Layer);
                    }
                }
            }

            return cards;
        }

        static bool Holds(PackSchema schema, CardDef card, StatModifier modifier) =>
            card.Intrinsic >= 0 && schema.IndexOf(ValueClass.Intrinsic, card.Intrinsic) >= 0 && schema.IndexOf(ValueClass.Stat, modifier.Stat) >= 0;
    }
}
