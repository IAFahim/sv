using System;

namespace Sv.Host
{
    public sealed partial class HostPack
    {
        public LaneClass[] Classes { get; }

        public int Channels { get; }

        public StatDef[] Stats { get; }

        public int[][] TemplateValues { get; }

        public CardTerm[] Cards { get; }

        public StatSource[] Sources { get; }

        public int[] CurveValues { get; }

        public ulong PackHash { get; }

        public HostPack(LaneClass[] classes, int channels, StatDef[] stats, int[][] templateValues, CardTerm[] cards, ulong packHash)
            : this(classes, channels, stats, templateValues, cards, Array.Empty<StatSource>(), Array.Empty<int>(), packHash)
        {
        }

        public HostPack(LaneClass[] classes, int channels, StatDef[] stats, int[][] templateValues, CardTerm[] cards, StatSource[] sources, int[] curveValues, ulong packHash)
        {
            Classes = classes;
            Channels = channels;
            Stats = stats;
            TemplateValues = templateValues;
            Cards = cards;
            Sources = sources ?? Array.Empty<StatSource>();
            CurveValues = curveValues ?? Array.Empty<int>();
            PackHash = packHash;
        }

        public int Templates => TemplateValues.Length;
    }
}
