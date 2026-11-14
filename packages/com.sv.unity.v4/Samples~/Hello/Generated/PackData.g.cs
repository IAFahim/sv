using Sv;
using Sv.Host;

namespace Hello.Sv
{
    public static class PackData
    {
        public static HostPack Create()
        {
            var classes = new LaneClass[6];
            classes[0] = LaneClass.Stat;
            classes[1] = LaneClass.Intrinsic;
            classes[2] = LaneClass.Intrinsic;
            classes[3] = LaneClass.Intrinsic;
            classes[4] = LaneClass.Intrinsic;
            classes[5] = LaneClass.Event;
            var stats = new StatDef[1];
            stats[0] = new StatDef(0, 100, 1);
            var templates = new int[4][];
            templates[0] = new int[] { 100, 100, 0, 0, 0, 0 };
            templates[1] = new int[] { 100, 100, 15, 1, 0, 0 };
            templates[2] = new int[] { 100, 100, 0, 0, 1, 0 };
            templates[3] = new int[] { 100, 100, 0, 0, 2, 0 };
            var cards = new CardTerm[0];
            var sources = new StatSource[0];
            var curves = new int[0];
            return new HostPack(classes, 3, stats, templates, cards, sources, curves, 0x0EB68FE438A2EF3Dul);
        }
    }
}
