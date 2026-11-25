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
            Refresh(pack, lanes, gates, alive, area);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector256<int> AtomMask8(Atom* atom, int* lanes, int row)
        {
            var leftAt = lanes + atom->Lane * Rows + row;
            Vector256<int> mask;
            if (atom->Op == AtomOp.Below)
            {
                var rightAt = lanes + atom->Other * Rows + row;
                var hundred = Vector256.Create(100);
                var percent = Vector256.Create(atom->Value);
                var low = Vector256.GreaterThan(Avx2.Multiply(Vector256.Load(rightAt).AsInt32(), percent).AsInt64(), Avx2.Multiply(Vector256.Load(leftAt).AsInt32(), hundred).AsInt64());
                var lowOdd = Vector256.GreaterThan(
                    Avx2.Multiply(Vector256.ShiftRightLogical(Vector256.Load((long*)rightAt), 32).AsInt32(), percent),
                    Avx2.Multiply(Vector256.ShiftRightLogical(Vector256.Load((long*)leftAt), 32).AsInt32(), hundred));
                mask = (low & Vector256.Create(0xFFFFFFFFL)).AsInt32() | (lowOdd & Vector256.Create(unchecked((long)0xFFFFFFFF00000000UL))).AsInt32();
            }
            else
            {
                var left = Vector256.Load(leftAt);
                if (atom->Op == AtomOp.Bit)
                {
                    var one = Vector256<int>.One;
                    mask = Vector256.Equals(Vector256.ShiftRightLogical(left, atom->Value) & one, one);
                }
                else
                {
                    var right = atom->Other >= 0 ? Vector256.Load(lanes + atom->Other * Rows + row) : Vector256.Create(atom->Value);
                    switch (atom->Op)
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
                        default:
                            mask = ~Vector256.Equals(left, right);
                            break;
                    }
                }
            }

            return atom->Negated != 0 ? ~mask : mask;
        }

        static void CondMasks(PackTables* pack, int cond, int* lanes, int* into)
        {
            var range = pack->Conds[cond];
            var atoms = pack->Atoms + range.First;
            for (var row = 0; row < Rows; row += 8)
            {
                AtomMask8(atoms, lanes, row).Store(into + row);
            }

            for (var a = 1; a < range.Count; a++)
            {
                for (var row = 0; row < Rows; row += 8)
                {
                    (Vector256.Load(into + row) & AtomMask8(atoms + a, lanes, row)).Store(into + row);
                }
            }
        }

        static void AddOps(PackTables* pack, TemplateTable* template, int* lanes, SlotGate* gates, Scratch area)
        {
            if (template->GroupCount == 0)
            {
                return;
            }

            var maskBase = (int*)area.Masks;
            for (var p = 0; p < template->PredCount; p++)
            {
                CondMasks(pack, template->Preds[p], lanes, maskBase + p * Rows);
            }

            var scaled = pack->ScaleCount > 0;
            if (scaled)
            {
                for (var row = 0; row < Rows; row++)
                {
                    var flags = gates[row].Flags;
                    area.OpFlags[row] = flags & (flags >> 8) & 255u;
                }
            }

            var one = Vector256<long>.One;
            for (var g = 0; g < template->GroupCount; g++)
            {
                var group = template->Groups + g;
                var terms = template->Terms + group->TermFirst;
                var condConstEnd = group->CondConstEnd;
                var laneEnd = group->LaneEnd;
                var termCount = group->TermCount;
                var factors = pack->Factors + group->Channel * 256;
                var gainTotals = area.Totals + (group->Channel * 2) * Rows;
                var lossTotals = gainTotals + Rows;
                var shift = group->AdmitShift;
                for (var row = 0; row < Rows; row += 16)
                {
                    var g0 = Vector256.Create(group->ConstGain);
                    var g1 = g0;
                    var g2 = g0;
                    var g3 = g0;
                    var l0 = Vector256.Create(group->ConstLoss);
                    var l1 = l0;
                    var l2 = l0;
                    var l3 = l0;
                    var masks = maskBase + row;
                    for (var t = 0; t < condConstEnd; t++)
                    {
                        var term = terms + t;
                        var mask = masks + term->Pred * Rows;
                        var gain = Vector256.Create(term->Gain);
                        var loss = Vector256.Create(term->Loss);
                        var m0 = Load(mask);
                        var m1 = Load(mask + 4);
                        var m2 = Load(mask + 8);
                        var m3 = Load(mask + 12);
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
                        var amount = Vector256.Create((int)term->Amount);
                        var v0 = Avx2.Multiply(Load(lane).AsInt32(), amount);
                        var v1 = Avx2.Multiply(Load(lane + 4).AsInt32(), amount);
                        var v2 = Avx2.Multiply(Load(lane + 8).AsInt32(), amount);
                        var v3 = Avx2.Multiply(Load(lane + 12).AsInt32(), amount);
                        var n0 = v0 & Negative(v0);
                        var n1 = v1 & Negative(v1);
                        var n2 = v2 & Negative(v2);
                        var n3 = v3 & Negative(v3);
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
                        var mask = masks + term->Pred * Rows;
                        var amount = Vector256.Create((int)term->Amount);
                        var v0 = Avx2.Multiply(Load(lane).AsInt32(), amount) & Load(mask);
                        var v1 = Avx2.Multiply(Load(lane + 4).AsInt32(), amount) & Load(mask + 4);
                        var v2 = Avx2.Multiply(Load(lane + 8).AsInt32(), amount) & Load(mask + 8);
                        var v3 = Avx2.Multiply(Load(lane + 12).AsInt32(), amount) & Load(mask + 12);
                        var n0 = v0 & Negative(v0);
                        var n1 = v1 & Negative(v1);
                        var n2 = v2 & Negative(v2);
                        var n3 = v3 & Negative(v3);
                        g0 += v0 - n0;
                        g1 += v1 - n1;
                        g2 += v2 - n2;
                        g3 += v3 - n3;
                        l0 += n0;
                        l1 += n1;
                        l2 += n2;
                        l3 += n3;
                    }

                    Commit(gates, area, factors, scaled, shift, gainTotals, lossTotals, row, g0, l0, one);
                    Commit(gates, area, factors, scaled, shift, gainTotals, lossTotals, row + 4, g1, l1, one);
                    Commit(gates, area, factors, scaled, shift, gainTotals, lossTotals, row + 8, g2, l2, one);
                    Commit(gates, area, factors, scaled, shift, gainTotals, lossTotals, row + 12, g3, l3, one);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Commit(SlotGate* gates, Scratch area, long* factors, bool scaled, int shift, long* gainTotals, long* lossTotals, int row, Vector256<long> gain, Vector256<long> loss, Vector256<long> one)
        {
            var admit = Vector256.Create(
                (long)(gates[row].Admit >> shift),
                (long)(gates[row + 1].Admit >> shift),
                (long)(gates[row + 2].Admit >> shift),
                (long)(gates[row + 3].Admit >> shift));
            var admitted = Vector256.Equals(admit & one, one);
            gain &= admitted;
            loss &= admitted;
            if (scaled)
            {
                var factor = Vector256.Create(factors[area.OpFlags[row]], factors[area.OpFlags[row + 1]], factors[area.OpFlags[row + 2]], factors[area.OpFlags[row + 3]]);
                gain = TimesSmall(gain, factor);
                loss = TimesSmall(loss, factor);
            }

            (Vector256.Load(gainTotals + row) + gain).Store(gainTotals + row);
            (Vector256.Load(lossTotals + row) + loss).Store(lossTotals + row);
        }

        static void MaskOf(PackTables* pack, int cond, int* lanes, int* into)
        {
            if (cond >= 0)
            {
                CondMasks(pack, cond, lanes, into);
                return;
            }

            var all = Vector256<int>.AllBitsSet;
            for (var row = 0; row < Rows; row += 8)
            {
                all.Store(into + row);
            }
        }

        static bool IsWritten(PackTables* pack, int lane)
        {
            for (var w = 0; w < pack->WrittenCount; w++)
            {
                if (pack->Written[w] == lane)
                {
                    return true;
                }
            }

            return false;
        }

        static void Fold(PackTables* pack, int* lanes, ulong alive, Scratch area)
        {
            var zero = Vector256<long>.Zero;
            var mask = (int*)area.Work0;
            var other = (int*)area.Work1;
            var wide = area.Wide;
            var gains = area.Gain;
            for (var c = 0; c < pack->ChannelCount; c++)
            {
                System.Buffer.MemoryCopy(area.Totals + c * 2 * Rows, gains + c * Rows, Rows * 8, Rows * 8);
            }

            for (var w = 0; w < pack->WrittenCount; w++)
            {
                var lane = pack->Written[w];
                for (var row = 0; row < Rows; row += 4)
                {
                    Load(lanes + lane * Rows + row).Store(wide + lane * Rows + row);
                }
            }

            for (var i = 0; i < pack->ConvertCount; i++)
            {
                var convert = pack->Converts + i;
                MaskOf(pack, convert->Cond, lanes, mask);
                var fromAt = gains + convert->From * Rows;
                var toAt = gains + convert->To * Rows;
                var numerator = Vector256.Create((long)convert->Numerator);
                var shift = convert->Shift;
                for (var row = 0; row < Rows; row += 4)
                {
                    var from = Vector256.Load(fromAt + row);
                    var moved = Vector256.ShiftRightLogical(TimesSmall(from, numerator), shift) & Load(mask + row);
                    (from - moved).Store(fromAt + row);
                    (Vector256.Load(toAt + row) + moved).Store(toAt + row);
                }
            }

            for (var i = 0; i < pack->AbsorbCount; i++)
            {
                var absorb = pack->Absorbs + i;
                var budgetAt = wide + absorb->Budget * Rows;
                var channels = pack->AbsorbChannels + absorb->First;
                for (var row = 0; row < Rows; row += 4)
                {
                    var budget = Vector256.Load(budgetAt + row);
                    var pool = zero;
                    for (var a = 0; a < absorb->Count; a++)
                    {
                        pool += Vector256.Load(gains + channels[a] * Rows + row);
                    }

                    var taken = Smaller(budget, pool) & Vector256.GreaterThan(budget, zero);
                    (budget - taken).Store(budgetAt + row);
                    for (var a = 0; a < absorb->Count; a++)
                    {
                        var gainAt = gains + channels[a] * Rows + row;
                        var gain = Vector256.Load(gainAt);
                        var drawn = Smaller(taken, gain);
                        (gain - drawn).Store(gainAt);
                        taken -= drawn;
                    }
                }
            }

            for (var i = 0; i < pack->FlowCount; i++)
            {
                var flow = pack->Flows + i;
                var gainAt = gains + flow->Channel * Rows;
                var lossAt = area.Totals + (flow->Channel * 2 + 1) * Rows;
                var at = wide + flow->Lane * Rows;
                if (flow->Sign > 0)
                {
                    for (var row = 0; row < Rows; row += 4)
                    {
                        (Vector256.Load(at + row) + Vector256.Load(gainAt + row) + Vector256.Load(lossAt + row)).Store(at + row);
                    }
                }
                else
                {
                    for (var row = 0; row < Rows; row += 4)
                    {
                        (Vector256.Load(at + row) - Vector256.Load(gainAt + row) - Vector256.Load(lossAt + row)).Store(at + row);
                    }
                }
            }

            var floors = (long*)mask;
            var ceilings = (long*)other;
            for (var i = 0; i < pack->BoundsCount; i++)
            {
                var bounds = pack->Bounds + i;
                var at = wide + bounds->Lane * Rows;
                if (bounds->HasFloor != 0)
                {
                    LimitOf(pack, lanes, wide, bounds->FloorLane, bounds->Floor, floors);
                }

                if (bounds->HasCeiling != 0)
                {
                    LimitOf(pack, lanes, wide, bounds->CeilingLane, bounds->Ceiling, ceilings);
                }

                for (var row = 0; row < Rows; row += 4)
                {
                    var value = Vector256.Load(at + row);
                    value = bounds->HasFloor != 0 ? Larger(value, Vector256.Load(floors + row)) : value;
                    value = bounds->HasCeiling != 0 ? Smaller(value, Vector256.Load(ceilings + row)) : value;
                    value.Store(at + row);
                }
            }

            var intMax = Vector256.Create((long)int.MaxValue);
            var intMin = Vector256.Create((long)int.MinValue);
            for (var w = 0; w < pack->WrittenCount; w++)
            {
                var lane = pack->Written[w];
                for (var row = 0; row < Rows; row += 4)
                {
                    var value = Larger(Smaller(Vector256.Load(wide + lane * Rows + row), intMax), intMin);
                    StoreLive(lanes + lane * Rows + row, value, RowMask(alive, row));
                }
            }

            if (pack->EventCount == 0)
            {
                return;
            }

            var eventsAt = lanes + pack->EventsLane * Rows;
            var next = area.OpFlags;
            for (var row = 0; row < Rows; row += 4)
            {
                zero.AsUInt64().Store(next + row);
            }

            var oneInt = Vector256<int>.One;
            for (var e = 0; e < pack->EventCount; e++)
            {
                var entry = pack->Events + e;
                MaskOf(pack, entry->Enter, lanes, mask);
                if (entry->Stay != -2)
                {
                    MaskOf(pack, entry->Stay, lanes, other);
                    for (var row = 0; row < Rows; row += 8)
                    {
                        var level = Vector256.Equals(Vector256.ShiftRightLogical(Vector256.Load(eventsAt + row), e) & oneInt, oneInt);
                        Vector256.ConditionalSelect(level, Vector256.Load(other + row), Vector256.Load(mask + row)).Store(mask + row);
                    }
                }

                var bit = Vector256.Create(1L << e).AsUInt64();
                for (var row = 0; row < Rows; row += 4)
                {
                    (Vector256.Load(next + row) | (Load(mask + row).AsUInt64() & bit)).Store(next + row);
                }
            }

            var enteredAt = lanes + pack->EnteredLane * Rows;
            for (var row = 0; row < Rows; row += 4)
            {
                var live = RowMask(alive, row);
                var levels = Load(eventsAt + row);
                var value = Vector256.Load(next + row).AsInt64();
                StoreLive(enteredAt + row, Vector256.AndNot(value, levels), live);
                StoreLive(eventsAt + row, value, live);
            }
        }

        static void LimitOf(PackTables* pack, int* lanes, long* wide, int boundLane, int constant, long* into)
        {
            if (boundLane >= 0 && IsWritten(pack, boundLane))
            {
                System.Buffer.MemoryCopy(wide + boundLane * Rows, into, Rows * 8, Rows * 8);
                return;
            }

            for (var row = 0; row < Rows; row += 4)
            {
                (boundLane >= 0 ? Load(lanes + boundLane * Rows + row) : Vector256.Create((long)constant)).Store(into + row);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void StoreLive(int* at, Vector256<long> value, Vector256<long> live)
        {
            var merged = Vector256.ConditionalSelect(live, value, Load(at));
            var packed = Avx2.PermuteVar8x32(merged.AsInt32(), Vector256.Create(0, 2, 4, 6, 0, 2, 4, 6));
            packed.GetLower().Store(at);
        }

        static void Refresh(PackTables* pack, int* lanes, SlotGate* gates, ulong alive, Scratch area)
        {
            var mask = (int*)area.Work0;
            var admit = area.Admit;
            var flags = area.OpFlags;
            var allAdmit = Vector256.Create(pack->AllAdmit);
            var zero = Vector256<ulong>.Zero;
            for (var row = 0; row < Rows; row += 4)
            {
                allAdmit.Store(admit + row);
                zero.Store(flags + row);
            }

            for (var d = 0; d < pack->DenyCount; d++)
            {
                var deny = pack->Denies + d;
                MaskOf(pack, deny->Cond, lanes, mask);
                var bits = Vector256.Create(deny->Bits);
                for (var row = 0; row < Rows; row += 4)
                {
                    Vector256.AndNot(Vector256.Load(admit + row), bits & Load(mask + row).AsUInt64()).Store(admit + row);
                }
            }

            var one = Vector256<long>.One;
            for (var m = 0; m < pack->MaskCount; m++)
            {
                var entry = pack->Masks + m;
                var laneAt = lanes + entry->Lane * Rows;
                for (var row = 0; row < Rows; row += 4)
                {
                    var bits = Load(laneAt + row);
                    Vector256<long> denied;
                    if (entry->ByKind != 0)
                    {
                        var nibble = Avx2.Multiply((bits & Vector256.Create(15L)).AsUInt32(), Vector256.Create(0x11111111u)).AsInt64();
                        denied = nibble | Vector256.ShiftLeft(nibble, 32);
                    }
                    else
                    {
                        denied = Vector256<long>.Zero;
                        for (var c = 0; c < pack->ChannelCount; c++)
                        {
                            var set = Vector256.Equals(Vector256.ShiftRightLogical(bits, c) & one, one);
                            denied |= set & Vector256.Create((long)(0xFul << (c * PackLimits.Kinds)));
                        }
                    }

                    Vector256.AndNot(Vector256.Load(admit + row), denied.AsUInt64()).Store(admit + row);
                }
            }

            for (var s = 0; s < pack->ScaleCount; s++)
            {
                var scale = pack->Scales + s;
                MaskOf(pack, scale->Cond, lanes, mask);
                var self = Vector256.Create(1UL << s);
                for (var row = 0; row < Rows; row += 4)
                {
                    (Vector256.Load(flags + row) | (Load(mask + row).AsUInt64() & self)).Store(flags + row);
                }

                MaskOf(pack, scale->SourceCond, lanes, mask);
                var source = Vector256.Create(1UL << (s + 8));
                for (var row = 0; row < Rows; row += 4)
                {
                    (Vector256.Load(flags + row) | (Load(mask + row).AsUInt64() & source)).Store(flags + row);
                }
            }

            var kindLane = pack->KindLane >= 0 ? lanes + pack->KindLane * Rows : null;
            var kindCount = pack->KindCount;
            var remaining = alive;
            while (remaining != 0)
            {
                var row = BitIndex(remaining);
                remaining &= remaining - 1;
                var gate = gates + row;
                gate->Admit = admit[row];
                gate->Flags = (uint)flags[row];
                var kind = kindLane != null ? kindLane[row] : 0;
                gate->Kind = kind >= 0 && kind < kindCount ? kind : 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int BitIndex(ulong value) => System.Numerics.BitOperations.TrailingZeroCount(value);
#else
        public static bool Supported => false;

        public static void ResolveChunk(PackTables* pack, TemplateTable* template, int* lanes, SlotGate* gates, ulong alive, Scratch area)
        {
        }
#endif
    }
}
