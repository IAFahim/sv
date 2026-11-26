namespace Sv.Columns
{
    internal static unsafe class RowOps
    {
        const int Rows = Law.Rows;

        public static void AddOps(PackTables* pack, TemplateTable* template, int* lanes, SlotGate* gates, ulong alive, Scratch area)
        {
            if (template->GroupCount == 0)
            {
                return;
            }

            var masks = (int*)area.Masks;
            for (var p = 0; p < template->PredCount; p++)
            {
                CondMask(pack, template->Preds[p], lanes, masks + p * Rows);
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
            for (var row = 0; row < Rows; row++)
            {
                gainSums[row] = 0;
                lossSums[row] = 0;
            }

            for (var t = 0; t < condConstEnd; t++)
            {
                var term = terms + t;
                var mask = masks + term->Pred * Rows;
                var gain = (int)term->Gain;
                var loss = (int)term->Loss;
                for (var row = 0; row < Rows; row++)
                {
                    gainSums[row] += gain & mask[row];
                    lossSums[row] += loss & mask[row];
                }
            }

            for (var t = condConstEnd; t < laneEnd; t++)
            {
                var term = terms + t;
                var lane = lanes + term->Lane * Rows;
                var amount = (int)term->Amount;
                for (var row = 0; row < Rows; row++)
                {
                    var value = lane[row] * amount;
                    var negative = value & (value >> 31);
                    gainSums[row] += value - negative;
                    lossSums[row] += negative;
                }
            }

            for (var t = laneEnd; t < termCount; t++)
            {
                var term = terms + t;
                var lane = lanes + term->Lane * Rows;
                var mask = masks + term->Pred * Rows;
                var amount = (int)term->Amount;
                for (var row = 0; row < Rows; row++)
                {
                    var value = lane[row] * amount & mask[row];
                    var negative = value & (value >> 31);
                    gainSums[row] += value - negative;
                    lossSums[row] += negative;
                }
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
            var high = int.MinValue;
            var low = int.MaxValue;
            for (var i = 0; i < template->TimesLaneCount; i++)
            {
                var lane = lanes + template->TimesLanes[i] * Rows;
                for (var row = 0; row < Rows; row++)
                {
                    var value = lane[row];
                    high = value > high ? value : high;
                    low = value < low ? value : low;
                }
            }

            var up = (long)high;
            var down = -(long)low;
            var spread = up > down ? up : down;
            return spread > 0 ? spread : 0;
        }

        static void CondMask(PackTables* pack, int cond, int* lanes, int* into)
        {
            var range = pack->Conds[cond];
            var atoms = pack->Atoms + range.First;
            AtomMask(atoms, lanes, into, false);
            for (var a = 1; a < range.Count; a++)
            {
                AtomMask(atoms + a, lanes, into, true);
            }
        }

        static void AtomMask(Atom* atom, int* lanes, int* into, bool and)
        {
            var left = lanes + atom->Lane * Rows;
            var flip = atom->Negated != 0 ? -1 : 0;
            var keep = and ? 0 : -1;
            if (atom->Op == AtomOp.Below)
            {
                var other = lanes + atom->Other * Rows;
                var percent = (long)atom->Value;
                for (var row = 0; row < Rows; row++)
                {
                    var mask = ((long)left[row] * 100 < (long)other[row] * percent ? -1 : 0) ^ flip;
                    into[row] = (into[row] | keep) & mask;
                }

                return;
            }

            if (atom->Op == AtomOp.Bit)
            {
                var bit = 1 << atom->Value;
                for (var row = 0; row < Rows; row++)
                {
                    var mask = ((left[row] & bit) == bit ? -1 : 0) ^ flip;
                    into[row] = (into[row] | keep) & mask;
                }

                return;
            }

            if (atom->Other >= 0)
            {
                var right = lanes + atom->Other * Rows;
                switch (atom->Op)
                {
                    case AtomOp.Gt:
                        for (var row = 0; row < Rows; row++)
                        {
                            into[row] = (into[row] | keep) & ((left[row] > right[row] ? -1 : 0) ^ flip);
                        }

                        break;
                    case AtomOp.Ge:
                        for (var row = 0; row < Rows; row++)
                        {
                            into[row] = (into[row] | keep) & ((left[row] >= right[row] ? -1 : 0) ^ flip);
                        }

                        break;
                    case AtomOp.Lt:
                        for (var row = 0; row < Rows; row++)
                        {
                            into[row] = (into[row] | keep) & ((left[row] < right[row] ? -1 : 0) ^ flip);
                        }

                        break;
                    case AtomOp.Le:
                        for (var row = 0; row < Rows; row++)
                        {
                            into[row] = (into[row] | keep) & ((left[row] <= right[row] ? -1 : 0) ^ flip);
                        }

                        break;
                    case AtomOp.Eq:
                        for (var row = 0; row < Rows; row++)
                        {
                            into[row] = (into[row] | keep) & ((left[row] == right[row] ? -1 : 0) ^ flip);
                        }

                        break;
                    default:
                        for (var row = 0; row < Rows; row++)
                        {
                            into[row] = (into[row] | keep) & ((left[row] != right[row] ? -1 : 0) ^ flip);
                        }

                        break;
                }

                return;
            }

            var value = atom->Value;
            switch (atom->Op)
            {
                case AtomOp.Gt:
                    for (var row = 0; row < Rows; row++)
                    {
                        into[row] = (into[row] | keep) & ((left[row] > value ? -1 : 0) ^ flip);
                    }

                    break;
                case AtomOp.Ge:
                    for (var row = 0; row < Rows; row++)
                    {
                        into[row] = (into[row] | keep) & ((left[row] >= value ? -1 : 0) ^ flip);
                    }

                    break;
                case AtomOp.Lt:
                    for (var row = 0; row < Rows; row++)
                    {
                        into[row] = (into[row] | keep) & ((left[row] < value ? -1 : 0) ^ flip);
                    }

                    break;
                case AtomOp.Le:
                    for (var row = 0; row < Rows; row++)
                    {
                        into[row] = (into[row] | keep) & ((left[row] <= value ? -1 : 0) ^ flip);
                    }

                    break;
                case AtomOp.Eq:
                    for (var row = 0; row < Rows; row++)
                    {
                        into[row] = (into[row] | keep) & ((left[row] == value ? -1 : 0) ^ flip);
                    }

                    break;
                default:
                    for (var row = 0; row < Rows; row++)
                    {
                        into[row] = (into[row] | keep) & ((left[row] != value ? -1 : 0) ^ flip);
                    }

                    break;
            }
        }
    }
}
