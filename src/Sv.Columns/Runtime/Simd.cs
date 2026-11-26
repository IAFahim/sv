using System.Numerics;
using System.Runtime.CompilerServices;

namespace Sv.Columns
{
    internal static unsafe class Simd
    {
        const int Rows = Law.Rows;

        public static bool Supported => Vector.IsHardwareAccelerated && Vector<int>.Count >= 4 && Rows % (4 * Vector<int>.Count) == 0;

        public static void AddOps(PackTables* pack, TemplateTable* template, int* lanes, SlotGate* gates, ulong alive, Scratch area)
        {
            if (template->GroupCount == 0)
            {
                return;
            }

            var masks = (int*)area.Masks;
            for (var p = 0; p < template->PredCount; p++)
            {
                CondMasks(pack, template->Preds[p], lanes, masks + p * Rows);
            }

            var spread = Spread(template, lanes);
            var gainSums = (int*)area.Work0;
            var lossSums = (int*)area.Work1;
            for (var g = 0; g < template->GroupCount; g++)
            {
                var group = template->Groups + g;
                if (group->CondBound + group->LaneAmount * spread > int.MaxValue)
                {
                    AddGroupScalar(pack, template, group, lanes, gates, alive, masks, area.Totals);
                    continue;
                }

                SumGroup(template->Terms + group->TermFirst, group->CondConstEnd, group->LaneEnd, group->TermCount, lanes, masks, gainSums, lossSums);
                var factors = pack->Factors + group->Channel * 256;
                var gainTotals = area.Totals + (group->Channel * 2) * Rows;
                var lossTotals = gainTotals + Rows;
                var shift = group->AdmitShift;
                for (var row = 0; row < Rows; row++)
                {
                    var gate = gates + row;
                    if (((alive >> row) & (gate->Admit >> shift) & 1ul) == 0)
                    {
                        continue;
                    }

                    var factor = factors[(int)(gate->Flags & (gate->Flags >> 8) & 255u)];
                    gainTotals[row] += (group->ConstGain + gainSums[row]) * factor;
                    lossTotals[row] += (group->ConstLoss + lossSums[row]) * factor;
                }
            }
        }

        static void SumGroup(OpTerm* terms, int condConstEnd, int laneEnd, int termCount, int* lanes, int* masks, int* gainSums, int* lossSums)
        {
            var width = Vector<int>.Count;
            var zero = Vector<int>.Zero;
            for (var row = 0; row < Rows; row += 4 * width)
            {
                var g0 = zero;
                var g1 = zero;
                var g2 = zero;
                var g3 = zero;
                var l0 = zero;
                var l1 = zero;
                var l2 = zero;
                var l3 = zero;
                var rowMasks = masks + row;
                for (var t = 0; t < condConstEnd; t++)
                {
                    var term = terms + t;
                    var mask = rowMasks + term->Pred * Rows;
                    var gain = new Vector<int>((int)term->Gain);
                    var loss = new Vector<int>((int)term->Loss);
                    var m0 = Load(mask);
                    var m1 = Load(mask + width);
                    var m2 = Load(mask + 2 * width);
                    var m3 = Load(mask + 3 * width);
                    g0 += gain & m0;
                    g1 += gain & m1;
                    g2 += gain & m2;
                    g3 += gain & m3;
                    l0 += loss & m0;
                    l1 += loss & m1;
                    l2 += loss & m2;
                    l3 += loss & m3;
                }

                for (var t = condConstEnd; t < laneEnd; t++)
                {
                    var term = terms + t;
                    var lane = lanes + term->Lane * Rows + row;
                    var amount = new Vector<int>((int)term->Amount);
                    var v0 = Load(lane) * amount;
                    var v1 = Load(lane + width) * amount;
                    var v2 = Load(lane + 2 * width) * amount;
                    var v3 = Load(lane + 3 * width) * amount;
                    var n0 = v0 & Vector.LessThan(v0, zero);
                    var n1 = v1 & Vector.LessThan(v1, zero);
                    var n2 = v2 & Vector.LessThan(v2, zero);
                    var n3 = v3 & Vector.LessThan(v3, zero);
                    g0 += v0 - n0;
                    g1 += v1 - n1;
                    g2 += v2 - n2;
                    g3 += v3 - n3;
                    l0 += n0;
                    l1 += n1;
                    l2 += n2;
                    l3 += n3;
                }

                for (var t = laneEnd; t < termCount; t++)
                {
                    var term = terms + t;
                    var lane = lanes + term->Lane * Rows + row;
                    var mask = rowMasks + term->Pred * Rows;
                    var amount = new Vector<int>((int)term->Amount);
                    var v0 = Load(lane) * amount & Load(mask);
                    var v1 = Load(lane + width) * amount & Load(mask + width);
                    var v2 = Load(lane + 2 * width) * amount & Load(mask + 2 * width);
                    var v3 = Load(lane + 3 * width) * amount & Load(mask + 3 * width);
                    var n0 = v0 & Vector.LessThan(v0, zero);
                    var n1 = v1 & Vector.LessThan(v1, zero);
                    var n2 = v2 & Vector.LessThan(v2, zero);
                    var n3 = v3 & Vector.LessThan(v3, zero);
                    g0 += v0 - n0;
                    g1 += v1 - n1;
                    g2 += v2 - n2;
                    g3 += v3 - n3;
                    l0 += n0;
                    l1 += n1;
                    l2 += n2;
                    l3 += n3;
                }

                Store(gainSums + row, g0);
                Store(gainSums + row + width, g1);
                Store(gainSums + row + 2 * width, g2);
                Store(gainSums + row + 3 * width, g3);
                Store(lossSums + row, l0);
                Store(lossSums + row + width, l1);
                Store(lossSums + row + 2 * width, l2);
                Store(lossSums + row + 3 * width, l3);
            }
        }

        static void AddGroupScalar(PackTables* pack, TemplateTable* template, OpGroup* group, int* lanes, SlotGate* gates, ulong alive, int* masks, long* totals)
        {
            var terms = template->Terms + group->TermFirst;
            for (var row = 0; row < Rows; row++)
            {
                var gate = gates[row];
                if (((alive >> row) & (gate.Admit >> group->AdmitShift) & 1ul) == 0)
                {
                    continue;
                }

                var all = group->ConstGain + group->ConstLoss;
                var loss = group->ConstLoss;
                for (var t = 0; t < group->TermCount; t++)
                {
                    var term = terms + t;
                    if (term->Pred >= 0 && masks[term->Pred * Rows + row] == 0)
                    {
                        continue;
                    }

                    if (term->Lane < 0)
                    {
                        all += term->Gain + term->Loss;
                        loss += term->Loss;
                    }
                    else
                    {
                        var value = lanes[term->Lane * Rows + row] * term->Amount;
                        all += value;
                        loss += value < 0 ? value : 0;
                    }
                }

                var factor = Law.FactorOf(pack, group->Channel, gate.Flags & (gate.Flags >> 8));
                totals[(group->Channel * 2) * Rows + row] += (all - loss) * factor;
                totals[(group->Channel * 2 + 1) * Rows + row] += loss * factor;
            }
        }

        static long Spread(TemplateTable* template, int* lanes)
        {
            var width = Vector<int>.Count;
            var high = new Vector<int>(int.MinValue);
            var low = new Vector<int>(int.MaxValue);
            for (var i = 0; i < template->TimesLaneCount; i++)
            {
                var lane = lanes + template->TimesLanes[i] * Rows;
                for (var row = 0; row < Rows; row += width)
                {
                    var value = Load(lane + row);
                    high = Vector.Max(high, value);
                    low = Vector.Min(low, value);
                }
            }

            long spread = 0;
            for (var i = 0; i < width; i++)
            {
                spread = System.Math.Max(spread, System.Math.Max((long)high[i], -(long)low[i]));
            }

            return spread;
        }

        static void CondMasks(PackTables* pack, int cond, int* lanes, int* into)
        {
            var width = Vector<int>.Count;
            var range = pack->Conds[cond];
            var atoms = pack->Atoms + range.First;
            for (var row = 0; row < Rows; row += width)
            {
                Store(into + row, AtomMask(atoms, lanes, row));
            }

            for (var a = 1; a < range.Count; a++)
            {
                for (var row = 0; row < Rows; row += width)
                {
                    Store(into + row, Load(into + row) & AtomMask(atoms + a, lanes, row));
                }
            }
        }

        static Vector<int> AtomMask(Atom* atom, int* lanes, int row)
        {
            var left = Load(lanes + atom->Lane * Rows + row);
            Vector<int> mask;
            if (atom->Op == AtomOp.Below)
            {
                Vector.Widen(left, out var leftLow, out var leftHigh);
                Vector.Widen(Load(lanes + atom->Other * Rows + row), out var rightLow, out var rightHigh);
                var hundred = new Vector<long>(100);
                var percent = new Vector<long>(atom->Value);
                mask = Vector.Narrow(Vector.LessThan(leftLow * hundred, rightLow * percent), Vector.LessThan(leftHigh * hundred, rightHigh * percent));
            }
            else if (atom->Op == AtomOp.Bit)
            {
                var bit = new Vector<int>(1 << atom->Value);
                mask = Vector.Equals(left & bit, bit);
            }
            else
            {
                var right = atom->Other >= 0 ? Load(lanes + atom->Other * Rows + row) : new Vector<int>(atom->Value);
                switch (atom->Op)
                {
                    case AtomOp.Gt:
                        mask = Vector.GreaterThan(left, right);
                        break;
                    case AtomOp.Ge:
                        mask = ~Vector.LessThan(left, right);
                        break;
                    case AtomOp.Lt:
                        mask = Vector.LessThan(left, right);
                        break;
                    case AtomOp.Le:
                        mask = ~Vector.GreaterThan(left, right);
                        break;
                    case AtomOp.Eq:
                        mask = Vector.Equals(left, right);
                        break;
                    default:
                        mask = ~Vector.Equals(left, right);
                        break;
                }
            }

            return atom->Negated != 0 ? ~mask : mask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector<int> Load(int* at) => *(Vector<int>*)at;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Store(int* at, Vector<int> value) => *(Vector<int>*)at = value;
    }
}
