#if NET
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif

namespace Sv.Columns
{
    internal static unsafe class Kernels
    {
        const int Rows = Law.Rows;

#if NET
        public static bool Supported => Avx2.IsSupported && Vector256.IsHardwareAccelerated;

        static readonly Vector256<long> RowBits = Vector256.Create(1L, 2L, 4L, 8L);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector256<long> Load(int* at) => Avx2.ConvertToVector256Int64(at);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector256<long> RowMask(ulong bits, int row)
        {
            var four = Vector256.Create((long)((bits >> row) & 15ul));
            return Vector256.Equals(four & RowBits, RowBits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector256<long> TimesSmall(Vector256<long> value, Vector256<long> small)
        {
            var low = Avx2.Multiply(value.AsUInt32(), small.AsUInt32()).AsInt64();
            var high = Avx2.Multiply(Vector256.ShiftRightLogical(value, 32).AsInt32(), small.AsInt32());
            return low + Vector256.ShiftLeft(high, 32);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector256<long> TimesInt(Vector256<long> narrow, int amount) => Avx2.Multiply(narrow.AsInt32(), Vector256.Create(amount));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector256<long> Negative(Vector256<long> value) => Vector256.GreaterThan(Vector256<long>.Zero, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector256<long> Smaller(Vector256<long> a, Vector256<long> b) => Vector256.ConditionalSelect(Vector256.GreaterThan(a, b), b, a);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector256<long> Larger(Vector256<long> a, Vector256<long> b) => Vector256.ConditionalSelect(Vector256.GreaterThan(a, b), a, b);

        static Vector256<long> AtomMask(in Atom atom, int* lanes, int row)
        {
            var left = Load(lanes + atom.Lane * Rows + row);
            var right = atom.Other >= 0 ? Load(lanes + atom.Other * Rows + row) : Vector256.Create((long)atom.Value);
            Vector256<long> mask;
            switch (atom.Op)
            {
                case AtomOp.Gt:
                    mask = Vector256.GreaterThan(left, right);
                    break;
                case AtomOp.Ge:
                    mask = ~Vector256.GreaterThan(right, left);
                    break;
                case AtomOp.Lt:
                    mask = Vector256.GreaterThan(right, left);
                    break;
                case AtomOp.Le:
                    mask = ~Vector256.GreaterThan(left, right);
                    break;
                case AtomOp.Eq:
                    mask = Vector256.Equals(left, right);
                    break;
                case AtomOp.Ne:
                    mask = ~Vector256.Equals(left, right);
                    break;
                case AtomOp.Below:
                    mask = Vector256.GreaterThan(TimesInt(right, atom.Value), TimesInt(left, 100));
                    break;
                default:
                    var one = Vector256<long>.One;
                    mask = Vector256.Equals(Vector256.ShiftRightLogical(left, atom.Value) & one, one);
                    break;
            }

            return atom.Negated != 0 ? ~mask : mask;
        }

        static Vector256<long> CondMask(PackTables* pack, int cond, int* lanes, int row)
        {
            var mask = Vector256<long>.AllBitsSet;
            if (cond < 0)
            {
                return mask;
            }

            var range = pack->Conds[cond];
            for (var a = 0; a < range.Count; a++)
            {
                mask &= AtomMask(pack->Atoms[range.First + a], lanes, row);
            }

            return mask;
        }

        public static void ResolveChunk(PackTables* pack, TemplateTable* template, int* lanes, SlotGate* gates, ulong alive, Scratch area)
        {
            AddOps(pack, template, lanes, gates, area);
            Fold(pack, lanes, alive, area);
            Refresh(pack, lanes, gates, alive);
        }

        static void AddOps(PackTables* pack, TemplateTable* template, int* lanes, SlotGate* gates, Scratch area)
        {
            if (template->GroupCount == 0)
            {
                return;
            }

            for (var p = 0; p < template->PredCount; p++)
            {
                for (var row = 0; row < Rows; row += 4)
                {
                    CondMask(pack, template->Preds[p], lanes, row).Store(area.Masks + p * Rows + row);
                }
            }

            for (var row = 0; row < Rows; row++)
            {
                var gate = gates[row];
                area.Admit[row] = gate.Admit;
                area.OpFlags[row] = gate.Flags & (gate.Flags >> 8) & 255u;
            }

            var scaled = pack->ScaleCount > 0;
            var zero = Vector256<long>.Zero;
            var one = Vector256<long>.One;
            for (var row = 0; row < Rows; row += 4)
            {
                var admit = Vector256.Load(area.Admit + row).AsInt64();
                for (var g = 0; g < template->GroupCount; g++)
                {
                    var group = template->Groups[g];
                    var all = zero;
                    var loss = zero;
                    var terms = template->Terms + group.TermFirst;
                    for (var t = 0; t < group.TermCount; t++)
                    {
                        var term = terms[t];
                        var pred = term.Pred >= 0 ? Vector256.Load(area.Masks + term.Pred * Rows + row) : Vector256<long>.AllBitsSet;
                        if (term.Lane < 0)
                        {
                            all += Vector256.Create(term.Gain + term.Loss) & pred;
                            loss += Vector256.Create(term.Loss) & pred;
                        }
                        else
                        {
                            var value = TimesInt(Load(lanes + term.Lane * Rows + row), (int)term.Amount) & pred;
                            all += value;
                            loss += value & Negative(value);
                        }
                    }

                    var admitted = Vector256.Equals(Vector256.ShiftRightLogical(admit, group.AdmitShift) & one, one);
                    var gain = (all - loss) & admitted;
                    loss &= admitted;
                    if (scaled)
                    {
                        var factors = pack->Factors + group.Channel * 256;
                        var factor = Vector256.Create(factors[area.OpFlags[row]], factors[area.OpFlags[row + 1]], factors[area.OpFlags[row + 2]], factors[area.OpFlags[row + 3]]);
                        gain = TimesSmall(gain, factor);
                        loss = TimesSmall(loss, factor);
                    }

                    var gainAt = area.Totals + (group.Channel * 2) * Rows + row;
                    var lossAt = gainAt + Rows;
                    (Vector256.Load(gainAt) + gain).Store(gainAt);
                    (Vector256.Load(lossAt) + loss).Store(lossAt);
                }
            }
        }

        static void Fold(PackTables* pack, int* lanes, ulong alive, Scratch area)
        {
            var zero = Vector256<long>.Zero;
            var intMax = Vector256.Create((long)int.MaxValue);
            var intMin = Vector256.Create((long)int.MinValue);
            for (var row = 0; row < Rows; row += 4)
            {
                var live = RowMask(alive, row);
                if (live == zero)
                {
                    continue;
                }

                for (var c = 0; c < pack->ChannelCount; c++)
                {
                    Vector256.Load(area.Totals + (c * 2) * Rows + row).Store(area.Gain + c * Rows + row);
                }

                for (var w = 0; w < pack->WrittenCount; w++)
                {
                    var lane = pack->Written[w];
                    Load(lanes + lane * Rows + row).Store(area.Wide + lane * Rows + row);
                }

                for (var i = 0; i < pack->ConvertCount; i++)
                {
                    var convert = pack->Converts[i];
                    var mask = CondMask(pack, convert.Cond, lanes, row);
                    var fromAt = area.Gain + convert.From * Rows + row;
                    var toAt = area.Gain + convert.To * Rows + row;
                    var from = Vector256.Load(fromAt);
                    var moved = Vector256.ShiftRightLogical(TimesSmall(from, Vector256.Create((long)convert.Numerator)), convert.Shift) & mask;
                    (from - moved).Store(fromAt);
                    (Vector256.Load(toAt) + moved).Store(toAt);
                }

                for (var i = 0; i < pack->AbsorbCount; i++)
                {
                    var absorb = pack->Absorbs[i];
                    var budgetAt = area.Wide + absorb.Budget * Rows + row;
                    var budget = Vector256.Load(budgetAt);
                    var pool = zero;
                    for (var a = 0; a < absorb.Count; a++)
                    {
                        pool += Vector256.Load(area.Gain + pack->AbsorbChannels[absorb.First + a] * Rows + row);
                    }

                    var taken = Smaller(budget, pool) & Vector256.GreaterThan(budget, zero);
                    (budget - taken).Store(budgetAt);
                    for (var a = 0; a < absorb.Count; a++)
                    {
                        var gainAt = area.Gain + pack->AbsorbChannels[absorb.First + a] * Rows + row;
                        var gain = Vector256.Load(gainAt);
                        var drawn = Smaller(taken, gain);
                        (gain - drawn).Store(gainAt);
                        taken -= drawn;
                    }
                }

                for (var i = 0; i < pack->FlowCount; i++)
                {
                    var flow = pack->Flows[i];
                    var net = Vector256.Load(area.Gain + flow.Channel * Rows + row) + Vector256.Load(area.Totals + (flow.Channel * 2 + 1) * Rows + row);
                    var at = area.Wide + flow.Lane * Rows + row;
                    (flow.Sign > 0 ? Vector256.Load(at) + net : Vector256.Load(at) - net).Store(at);
                }

                for (var i = 0; i < pack->BoundsCount; i++)
                {
                    var bounds = pack->Bounds[i];
                    var at = area.Wide + bounds.Lane * Rows + row;
                    var value = Vector256.Load(at);
                    var floor = bounds.FloorLane >= 0 ? Current(pack, lanes, area, bounds.FloorLane, row) : Vector256.Create((long)bounds.Floor);
                    var ceiling = bounds.CeilingLane >= 0 ? Current(pack, lanes, area, bounds.CeilingLane, row) : Vector256.Create((long)bounds.Ceiling);
                    value = bounds.HasFloor != 0 ? Larger(value, floor) : value;
                    value = bounds.HasCeiling != 0 ? Smaller(value, ceiling) : value;
                    value.Store(at);
                }

                for (var w = 0; w < pack->WrittenCount; w++)
                {
                    var lane = pack->Written[w];
                    var at = lanes + lane * Rows + row;
                    var value = Larger(Smaller(Vector256.Load(area.Wide + lane * Rows + row), intMax), intMin);
                    StoreLive(at, value, live);
                }

                var eventsAt = lanes + pack->EventsLane * Rows + row;
                var levels = Load(eventsAt);
                var next = zero;
                var one = Vector256<long>.One;
                for (var e = 0; e < pack->EventCount; e++)
                {
                    var entry = pack->Events[e];
                    var enter = CondMask(pack, entry.Enter, lanes, row);
                    var holds = enter;
                    if (entry.Stay != -2)
                    {
                        var level = Vector256.Equals(Vector256.ShiftRightLogical(levels, e) & one, one);
                        holds = Vector256.ConditionalSelect(level, CondMask(pack, entry.Stay, lanes, row), enter);
                    }

                    next |= holds & Vector256.Create(1L << e);
                }

                StoreLive(eventsAt, next, live);
                StoreLive(lanes + pack->EnteredLane * Rows + row, Vector256.AndNot(next, levels), live);
            }
        }

        static Vector256<long> Current(PackTables* pack, int* lanes, Scratch area, int lane, int row)
        {
            for (var w = 0; w < pack->WrittenCount; w++)
            {
                if (pack->Written[w] == lane)
                {
                    return Vector256.Load(area.Wide + lane * Rows + row);
                }
            }

            return Load(lanes + lane * Rows + row);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void StoreLive(int* at, Vector256<long> value, Vector256<long> live)
        {
            var merged = Vector256.ConditionalSelect(live, value, Load(at));
            var packed = Avx2.PermuteVar8x32(merged.AsInt32(), Vector256.Create(0, 2, 4, 6, 0, 2, 4, 6));
            packed.GetLower().Store(at);
        }

        static void Refresh(PackTables* pack, int* lanes, SlotGate* gates, ulong alive)
        {
            var one = Vector256<long>.One;
            var zero = Vector256<long>.Zero;
            var kindCount = Vector256.Create((long)pack->KindCount);
            var allAdmit = Vector256.Create((long)pack->AllAdmit);
            for (var row = 0; row < Rows; row += 4)
            {
                var live = RowMask(alive, row);
                if (live == zero)
                {
                    continue;
                }

                var admit = allAdmit;
                for (var d = 0; d < pack->DenyCount; d++)
                {
                    var deny = pack->Denies[d];
                    admit = Vector256.AndNot(admit, Vector256.Create((long)deny.Bits) & CondMask(pack, deny.Cond, lanes, row));
                }

                for (var m = 0; m < pack->MaskCount; m++)
                {
                    var mask = pack->Masks[m];
                    var bits = Load(lanes + mask.Lane * Rows + row);
                    Vector256<long> denied;
                    if (mask.ByKind != 0)
                    {
                        var nibble = Avx2.Multiply((bits & Vector256.Create(15L)).AsUInt32(), Vector256.Create(0x11111111u)).AsInt64();
                        denied = nibble | Vector256.ShiftLeft(nibble, 32);
                    }
                    else
                    {
                        denied = zero;
                        for (var c = 0; c < pack->ChannelCount; c++)
                        {
                            var set = Vector256.Equals(Vector256.ShiftRightLogical(bits, c) & one, one);
                            denied |= set & Vector256.Create((long)(0xFul << (c * PackLimits.Kinds)));
                        }
                    }

                    admit = Vector256.AndNot(admit, denied);
                }

                var flags = zero;
                for (var s = 0; s < pack->ScaleCount; s++)
                {
                    var scale = pack->Scales[s];
                    flags |= CondMask(pack, scale.Cond, lanes, row) & Vector256.Create(1L << s);
                    flags |= CondMask(pack, scale.SourceCond, lanes, row) & Vector256.Create(1L << (s + 8));
                }

                var kind = zero;
                if (pack->KindLane >= 0)
                {
                    kind = Load(lanes + pack->KindLane * Rows + row);
                    kind &= ~Negative(kind) & Vector256.GreaterThan(kindCount, kind);
                }

                for (var i = 0; i < 4; i++)
                {
                    if (((alive >> (row + i)) & 1ul) == 0)
                    {
                        continue;
                    }

                    var gate = gates + row + i;
                    gate->Admit = (ulong)admit.GetElement(i);
                    gate->Flags = (uint)flags.GetElement(i);
                    gate->Kind = (int)kind.GetElement(i);
                }
            }
        }
#else
        public static bool Supported => false;

        public static void ResolveChunk(PackTables* pack, TemplateTable* template, int* lanes, SlotGate* gates, ulong alive, Scratch area)
        {
        }
#endif
    }
}
