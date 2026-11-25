using System.Runtime.CompilerServices;

namespace Sv.Columns
{
    public struct SlotGate
    {
        public ulong Admit;
        public uint Flags;
        public int Kind;
    }

    public static unsafe class Law
    {
        public const int Rows = 64;
        public const ulong KindSpread = 0x1111111111111111ul;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AtomHolds(in Atom atom, int* lanes, int row)
        {
            long left = lanes[atom.Lane * Rows + row];
            long right = atom.Other >= 0 ? lanes[atom.Other * Rows + row] : atom.Value;
            bool holds;
            switch (atom.Op)
            {
                case AtomOp.Gt:
                    holds = left > right;
                    break;
                case AtomOp.Ge:
                    holds = left >= right;
                    break;
                case AtomOp.Lt:
                    holds = left < right;
                    break;
                case AtomOp.Le:
                    holds = left <= right;
                    break;
                case AtomOp.Eq:
                    holds = left == right;
                    break;
                case AtomOp.Ne:
                    holds = left != right;
                    break;
                case AtomOp.Below:
                    holds = left * 100 < right * atom.Value;
                    break;
                default:
                    holds = ((left >> atom.Value) & 1) != 0;
                    break;
            }

            return holds != (atom.Negated != 0);
        }

        public static bool Holds(PackTables* pack, int cond, int* lanes, int row)
        {
            if (cond < 0)
            {
                return true;
            }

            var range = pack->Conds[cond];
            for (var a = 0; a < range.Count; a++)
            {
                if (!AtomHolds(pack->Atoms[range.First + a], lanes, row))
                {
                    return false;
                }
            }

            return true;
        }

        public static ulong ChannelSpread(uint channelBits)
        {
            var spread = 0ul;
            for (var c = 0; c < 16; c++)
            {
                spread |= (ulong)((channelBits >> c) & 1u) * (0xFul << (c * PackLimits.Kinds));
            }

            return spread;
        }

        public static SlotGate GateOf(PackTables* pack, int* lanes, int row)
        {
            var admit = pack->AllAdmit;
            for (var d = 0; d < pack->DenyCount; d++)
            {
                var deny = pack->Denies[d];
                if (Holds(pack, deny.Cond, lanes, row))
                {
                    admit &= ~deny.Bits;
                }
            }

            for (var m = 0; m < pack->MaskCount; m++)
            {
                var mask = pack->Masks[m];
                var bits = (uint)lanes[mask.Lane * Rows + row];
                admit &= ~(mask.ByKind != 0 ? (bits & 15u) * KindSpread : ChannelSpread(bits & 0xFFFFu));
            }

            var flags = 0u;
            for (var s = 0; s < pack->ScaleCount; s++)
            {
                var scale = pack->Scales[s];
                flags |= Holds(pack, scale.Cond, lanes, row) ? 1u << s : 0u;
                flags |= Holds(pack, scale.SourceCond, lanes, row) ? 1u << (s + 8) : 0u;
            }

            var kind = pack->KindLane >= 0 ? lanes[pack->KindLane * Rows + row] : 0;
            return new SlotGate { Admit = admit, Flags = flags, Kind = kind >= 0 && kind < pack->KindCount ? kind : 0 };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long FactorOf(PackTables* pack, int channel, uint mask) => pack->Factors[channel * 256 + (int)(mask & 255u)];

        public static void ReduceRecord(PackTables* pack, SlotGate* gates, ulong record, long* block)
        {
            var target = Record.Target(record);
            var channel = Record.Channel(record);
            var gate = gates[target];
            var shift = channel * PackLimits.Kinds + Record.Kind(record);
            var admitted = (long)((gate.Admit >> shift) & 1ul);
            var term = Record.Amount(record) * FactorOf(pack, channel, gate.Flags & Record.Flags(record)) * admitted;
            var loss = (int)((ulong)term >> 63);
            block[(channel * 2 + loss) * Rows + (target & (Rows - 1))] += term;
        }

        public static void AddOps(PackTables* pack, TemplateTable* template, int* lanes, SlotGate* gates, ulong alive, long* totals, bool* preds)
        {
            for (var row = 0; row < Rows; row++)
            {
                if (((alive >> row) & 1ul) == 0)
                {
                    continue;
                }

                var gate = gates[row];
                for (var p = 0; p < template->PredCount; p++)
                {
                    preds[p] = Holds(pack, template->Preds[p], lanes, row);
                }

                for (var g = 0; g < template->GroupCount; g++)
                {
                    var group = template->Groups[g];
                    if (((gate.Admit >> group.AdmitShift) & 1ul) == 0)
                    {
                        continue;
                    }

                    var all = group.ConstGain + group.ConstLoss;
                    var loss = group.ConstLoss;
                    for (var t = 0; t < group.TermCount; t++)
                    {
                        var term = template->Terms[group.TermFirst + t];
                        if (term.Pred >= 0 && !preds[term.Pred])
                        {
                            continue;
                        }

                        if (term.Lane < 0)
                        {
                            all += term.Gain + term.Loss;
                            loss += term.Loss;
                        }
                        else
                        {
                            var value = lanes[term.Lane * Rows + row] * term.Amount;
                            all += value;
                            loss += value < 0 ? value : 0;
                        }
                    }

                    var factor = FactorOf(pack, group.Channel, gate.Flags & (gate.Flags >> 8));
                    totals[(group.Channel * 2) * Rows + row] += (all - loss) * factor;
                    totals[(group.Channel * 2 + 1) * Rows + row] += loss * factor;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static long Saturate(long value) => value > int.MaxValue ? int.MaxValue : value < int.MinValue ? int.MinValue : value;

        public static void ResolveRow(PackTables* pack, int* lanes, long* totals, long* wide, long* gain, int row)
        {
            var channels = pack->ChannelCount;
            for (var c = 0; c < channels; c++)
            {
                gain[c] = totals[(c * 2) * Rows + row];
            }

            for (var w = 0; w < pack->WrittenCount; w++)
            {
                var lane = pack->Written[w];
                wide[lane] = lanes[lane * Rows + row];
            }

            for (var i = 0; i < pack->ConvertCount; i++)
            {
                var convert = pack->Converts[i];
                if (Holds(pack, convert.Cond, lanes, row))
                {
                    var moved = (gain[convert.From] * convert.Numerator) >> convert.Shift;
                    gain[convert.From] -= moved;
                    gain[convert.To] += moved;
                }
            }

            for (var i = 0; i < pack->AbsorbCount; i++)
            {
                var absorb = pack->Absorbs[i];
                var budget = wide[absorb.Budget];
                var pool = 0L;
                for (var a = 0; a < absorb.Count; a++)
                {
                    pool += gain[pack->AbsorbChannels[absorb.First + a]];
                }

                var taken = budget > 0 ? (budget < pool ? budget : pool) : 0L;
                wide[absorb.Budget] = budget - taken;
                for (var a = 0; a < absorb.Count; a++)
                {
                    var c = pack->AbsorbChannels[absorb.First + a];
                    var drawn = taken < gain[c] ? taken : gain[c];
                    gain[c] -= drawn;
                    taken -= drawn;
                }
            }

            for (var i = 0; i < pack->FlowCount; i++)
            {
                var flow = pack->Flows[i];
                var net = gain[flow.Channel] + totals[(flow.Channel * 2 + 1) * Rows + row];
                wide[flow.Lane] += flow.Sign > 0 ? net : -net;
            }

            for (var i = 0; i < pack->BoundsCount; i++)
            {
                var bounds = pack->Bounds[i];
                var value = wide[bounds.Lane];
                if (bounds.HasFloor != 0)
                {
                    var floor = bounds.FloorLane >= 0 ? Current(pack, lanes, wide, bounds.FloorLane, row) : bounds.Floor;
                    value = value < floor ? floor : value;
                }

                if (bounds.HasCeiling != 0)
                {
                    var ceiling = bounds.CeilingLane >= 0 ? Current(pack, lanes, wide, bounds.CeilingLane, row) : bounds.Ceiling;
                    value = value > ceiling ? ceiling : value;
                }

                wide[bounds.Lane] = value;
            }

            for (var w = 0; w < pack->WrittenCount; w++)
            {
                var lane = pack->Written[w];
                lanes[lane * Rows + row] = (int)Saturate(wide[lane]);
            }

            var levels = (uint)lanes[pack->EventsLane * Rows + row];
            var next = 0u;
            for (var e = 0; e < pack->EventCount; e++)
            {
                var entry = pack->Events[e];
                var level = ((levels >> e) & 1u) != 0;
                var cond = level && entry.Stay != -2 ? entry.Stay : entry.Enter;
                next |= Holds(pack, cond, lanes, row) ? 1u << e : 0u;
            }

            lanes[pack->EventsLane * Rows + row] = (int)next;
            lanes[pack->EnteredLane * Rows + row] = (int)(next & ~levels);
        }

        static long Current(PackTables* pack, int* lanes, long* wide, int lane, int row)
        {
            for (var w = 0; w < pack->WrittenCount; w++)
            {
                if (pack->Written[w] == lane)
                {
                    return wide[lane];
                }
            }

            return lanes[lane * Rows + row];
        }
    }
}
