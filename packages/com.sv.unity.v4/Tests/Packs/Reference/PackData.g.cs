using Sv;
using Sv.Host;

namespace Sv.Kernels.Reference
{
    public static class PackData
    {
        public static HostPack Create()
        {
            var classes = new LaneClass[21];
            classes[0] = LaneClass.Stat;
            classes[1] = LaneClass.Stat;
            classes[2] = LaneClass.Intrinsic;
            classes[3] = LaneClass.Intrinsic;
            classes[4] = LaneClass.Intrinsic;
            classes[5] = LaneClass.Intrinsic;
            classes[6] = LaneClass.Intrinsic;
            classes[7] = LaneClass.Intrinsic;
            classes[8] = LaneClass.Event;
            classes[9] = LaneClass.Intrinsic;
            classes[10] = LaneClass.Intrinsic;
            classes[11] = LaneClass.Intrinsic;
            classes[12] = LaneClass.Intrinsic;
            classes[13] = LaneClass.Intrinsic;
            classes[14] = LaneClass.Intrinsic;
            classes[15] = LaneClass.Intrinsic;
            classes[16] = LaneClass.Intrinsic;
            classes[17] = LaneClass.Intrinsic;
            classes[18] = LaneClass.Intrinsic;
            classes[19] = LaneClass.Intrinsic;
            classes[20] = LaneClass.Intrinsic;
            var stats = new StatDef[2];
            stats[0] = new StatDef(0, 100, 1);
            stats[1] = new StatDef(1, 5, 1);
            var templates = new int[6][];
            templates[0] = new int[] { 100, 5, 100, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[1] = new int[] { 100, 5, 100, 0, -1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[2] = new int[] { 100, 5, 100, 1, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[3] = new int[] { 100, 5, 100, 2, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[4] = new int[] { 100, 5, 100, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[5] = new int[] { 100, 5, 100, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            var cards = new CardTerm[1];
            cards[0] = new CardTerm(1, 20, ShapeKind.Multi, -5000, false, StatLayer.Core);
            var sources = new StatSource[0];
            var curves = new int[0];
            return new HostPack(classes, 5, stats, templates, cards, sources, curves, 0xBD70627B1C671888ul);
        }
    }
}
