namespace Sv.Rules.Emit
{
    public sealed partial class KernelEmitter
    {
        static readonly string[] RuleMathLines =
        {
            "public static unsafe class RuleMath",
            "{",
            "    public static long Add(long left, long right)",
            "    {",
            "        var sum = unchecked(left + right);",
            "        return ((left ^ sum) & (right ^ sum)) < 0L ? (left < 0L ? long.MinValue : long.MaxValue) : sum;",
            "    }",
            "",
            "    public static long Negate(long value) => value == long.MinValue ? long.MaxValue : -value;",
            "",
            "    public static long Subtract(long left, long right) => Add(left, Negate(right));",
            "",
            "    public static long Multiply(long left, long right)",
            "    {",
            "        if (left == 0L || right == 0L)",
            "        {",
            "            return 0L;",
            "        }",
            "",
            "        if (right == -1L)",
            "        {",
            "            return Negate(left);",
            "        }",
            "",
            "        if (left == -1L)",
            "        {",
            "            return Negate(right);",
            "        }",
            "",
            "        var negative = (left < 0L) != (right < 0L);",
            "        if (left == long.MinValue || right == long.MinValue)",
            "        {",
            "            return negative ? long.MinValue : long.MaxValue;",
            "        }",
            "",
            "        var product = unchecked(left * right);",
            "        return product / right != left ? (negative ? long.MinValue : long.MaxValue) : product;",
            "    }",
            "",
            "    public static long Min(long left, long right) => left < right ? left : right;",
            "",
            "    public static long Max(long left, long right) => left > right ? left : right;",
            "",
            "    public static float AddFloat(float left, float right) => (float)(left + right);",
            "",
            "    public static float SubtractFloat(float left, float right) => (float)(left - right);",
            "",
            "    public static float MultiplyFloat(float left, float right) => (float)(left * right);",
            "",
            "    public static float MinFloat(float left, float right) => left < right ? left : right;",
            "",
            "    public static float MaxFloat(float left, float right) => left > right ? left : right;",
            "",
            "    public static bool Differ(float left, float right) => left < right || left > right;",
            "",
            "    public static bool Bit(long value, long index) => index >= 0L && index < 64L && ((value >> (int)index) & 1L) != 0L;",
            "",
            "    public static bool Below(long value, long whole, long numerator, long denominator) => Multiply(value, denominator) < Multiply(whole, numerator);",
            "",
            "    public static int Step(int before, int delta, int floor, int ceiling)",
            "    {",
            "        var low = floor < ceiling ? floor : ceiling;",
            "        var moved = (long)before + delta;",
            "        return moved < low ? low : moved > ceiling ? ceiling : (int)moved;",
            "    }",
            "",
            "    public static int KindOf(int kind) => kind < 0 ? 0 : kind > 255 ? 255 : kind;",
            "",
            "    public static long Gross(in Outcome outcome) => EffectLaw.Term(outcome.TakenPerHit, outcome.TakenHits);",
            "",
            "    public static long Offered(in Effect effect) => EffectLaw.Term(effect.Amount, effect.Hits);",
            "",
            "    public static long Taken(in Outcome outcome) => Gross(outcome) - outcome.Absorbed;",
            "",
            "    public static long NotTaken(in Effect effect, in Outcome outcome) => Add(Offered(effect), Negate(Taken(outcome)));",
            "",
            "    public static long Consumed(in Effect effect, in Outcome outcome, int measure) =>",
            "        measure == 0 ? Offered(effect) : measure == 1 ? Taken(outcome) : NotTaken(effect, outcome);",
            "",
            "    public static void PerHit(in Effect effect, in Outcome outcome, int measure, out int amount, out int hits)",
            "    {",
            "        var whole = outcome.Absorbed == 0L;",
            "        if (measure == 0)",
            "        {",
            "            amount = effect.Amount;",
            "            hits = effect.Hits;",
            "            return;",
            "        }",
            "",
            "        if (measure == 1)",
            "        {",
            "            amount = whole ? outcome.TakenPerHit : EffectLaw.Saturate32(Taken(outcome));",
            "            hits = whole ? outcome.TakenHits : 1;",
            "            return;",
            "        }",
            "",
            "        if (whole && outcome.TakenHits == effect.Hits)",
            "        {",
            "            amount = EffectLaw.Saturate32((long)effect.Amount - outcome.TakenPerHit);",
            "            hits = effect.Hits;",
            "            return;",
            "        }",
            "",
            "        amount = EffectLaw.Saturate32(NotTaken(effect, outcome));",
            "        hits = 1;",
            "    }",
            "",
            "    public static bool Present(in WorldView world, int capacity, long slot)",
            "    {",
            "        if ((ulong)slot >= (ulong)capacity)",
            "        {",
            "            return false;",
            "        }",
            "",
            "        var at = (int)slot;",
            "        var meta = Lanes.WordsOf(world, at >> 6, world.Layout.MetaLane);",
            "        return (((meta[ChunkMeta.Live] | meta[ChunkMeta.Dying]) >> (at & 63)) & 1ul) != 0ul;",
            "    }",
            "",
            "    public static int Read(in WorldView world, int capacity, long slot, int lane, int fallback) =>",
            "        Present(world, capacity, slot) ? Lanes.Read(world, (int)slot, lane) : fallback;",
            "",
            "    public static bool Level(in WorldView world, int capacity, long slot, int lane, int word, bool fallback) =>",
            "        Present(world, capacity, slot) ? ((Lanes.WordsOf(world, (int)slot >> 6, lane)[word] >> ((int)slot & 63)) & 1ul) != 0ul : fallback;",
            "}",
        };

        void WriteSupport(CodeWriter file)
        {
            file.Line("[StructLayout(LayoutKind.Sequential, Size = 24)]");
            file.Open("public struct Outcome");
            file.Line("public long Absorbed;");
            file.Line("public int TakenPerHit;");
            file.Line("public int TakenHits;");
            file.Line("public int Admitted;");
            file.Line("public int Survives;");
            file.Close();
            file.Blank();
            file.Open("public struct GateState");
            file.Line("public ulong Groups;");
            file.Line("public int Decided;");
            file.Line("public int Admit;");
            file.Line("public int Terms;");
            file.Close();
            file.Blank();
            file.Open("public unsafe struct DecideArgs");
            file.Line("public Effect* Effects;");
            file.Line("public Outcome* Outcomes;");
            file.Line("public int* Order;");
            file.Line("public long* Totals;");
            file.Line("public int* Delta;");
            file.Line("public long* Faults;");
            file.Line("public int Capacity;");
            file.Close();
            file.Blank();
            file.Open("public unsafe struct ChunkView");
            file.Line("public int Chunk;");
            foreach (var lane in viewLanes)
            {
                file.Line("public int* L" + Text(lane) + ";");
            }

            foreach (var lane in viewEvents)
            {
                file.Line("public ulong* E" + Text(lane) + ";");
            }

            for (var g = 0; g < kernel.Guards.Count; g++)
            {
                file.Line("public ulong C" + Text(kernel.Guards[g]) + ";");
            }

            for (var m = 0; m < kernel.Masks.Count; m++)
            {
                file.Line("public ulong M" + Text(m) + ";");
            }

            file.Close();
            file.Blank();
            for (var i = 0; i < RuleMathLines.Length; i++)
            {
                if (RuleMathLines[i].Length == 0)
                {
                    file.Blank();
                    continue;
                }

                file.Line(RuleMathLines[i]);
            }
        }
    }
}
