using Sv;
using Sv.Host;

namespace Sv.Kernels.Lego
{
    public static class PackData
    {
        public static HostPack Create()
        {
            var classes = new LaneClass[44];
            classes[0] = LaneClass.Stat;
            classes[1] = LaneClass.Stat;
            classes[2] = LaneClass.Stat;
            classes[3] = LaneClass.Stat;
            classes[4] = LaneClass.Stat;
            classes[5] = LaneClass.Stat;
            classes[6] = LaneClass.Stat;
            classes[7] = LaneClass.Stat;
            classes[8] = LaneClass.Intrinsic;
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
            classes[21] = LaneClass.Intrinsic;
            classes[22] = LaneClass.Intrinsic;
            classes[23] = LaneClass.Intrinsic;
            classes[24] = LaneClass.Intrinsic;
            classes[25] = LaneClass.Intrinsic;
            classes[26] = LaneClass.Intrinsic;
            classes[27] = LaneClass.Intrinsic;
            classes[28] = LaneClass.Intrinsic;
            classes[29] = LaneClass.Intrinsic;
            classes[30] = LaneClass.Intrinsic;
            classes[31] = LaneClass.Intrinsic;
            classes[32] = LaneClass.Intrinsic;
            classes[33] = LaneClass.Intrinsic;
            classes[34] = LaneClass.Intrinsic;
            classes[35] = LaneClass.Intrinsic;
            classes[36] = LaneClass.Intrinsic;
            classes[37] = LaneClass.Intrinsic;
            classes[38] = LaneClass.Event;
            classes[40] = LaneClass.Intrinsic;
            classes[41] = LaneClass.Intrinsic;
            classes[42] = LaneClass.Intrinsic;
            classes[43] = LaneClass.Stat;
            var stats = new StatDef[9];
            stats[0] = new StatDef(0, 100, 1);
            stats[1] = new StatDef(1, 10, 1);
            stats[2] = new StatDef(2, 2, 1);
            stats[3] = new StatDef(3, 0, 1);
            stats[4] = new StatDef(4, 100, 100);
            stats[5] = new StatDef(5, 50, 100);
            stats[6] = new StatDef(6, 50, 100);
            stats[7] = new StatDef(7, 100, 1);
            stats[8] = new StatDef(43, 0, 100);
            var templates = new int[29][];
            templates[0] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 100, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[1] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 100, 1, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[2] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 100, 2, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[3] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 100, 3, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[4] = new int[] { 100, 10, 2, 4, 100, 50, 50, 100, 25, 0, -1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[5] = new int[] { 100, 10, 2, 4, 100, 50, 50, 100, 98, 0, -1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[6] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 100, 0, -1, 0, 0, 30, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[7] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 50, 0, -1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[8] = new int[] { 100, 10, 2, 0, 50, 50, 50, 100, 100, 0, -1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[9] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 100, 0, -1, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[10] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 100, 0, -1, 0, 0, 0, 0, 0, 0, 2, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[11] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 100, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[12] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 100, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[13] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 100, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[14] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 100, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[15] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 25, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[16] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 30, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[17] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 100, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[18] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 100, 0, -1, 0, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[19] = new int[] { 500, 10, 2, 0, 100, 50, 50, 100, 500, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[20] = new int[] { 100, 12, 2, 0, 100, 50, 50, 100, 100, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[21] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 100, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
            templates[22] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 50, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[23] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 100, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[24] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 50, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[25] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 100, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[26] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 100, 0, -1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0 };
            templates[27] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 50, 0, -1, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            templates[28] = new int[] { 100, 10, 2, 0, 100, 50, 50, 100, 100, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0 };
            var cards = new CardTerm[3];
            cards[0] = new CardTerm(7, 37, ShapeKind.Added, 20, false, StatLayer.Core);
            cards[1] = new CardTerm(7, 37, ShapeKind.Additive, 5000, false, StatLayer.Core);
            cards[2] = new CardTerm(43, 41, ShapeKind.Added, 10, true, StatLayer.Core);
            var sources = new StatSource[0];
            var curves = new int[0];
            return new HostPack(classes, 28, stats, templates, cards, sources, curves, 0x9A043301EA06000Eul);
        }
    }
}
