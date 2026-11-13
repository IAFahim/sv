using Sv;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sv.Kernels.Lego
{
    [StructLayout(LayoutKind.Sequential, Size = 24)]
    public struct Outcome
    {
        public long Absorbed;
        public int TakenPerHit;
        public int TakenHits;
        public int Admitted;
        public int Survives;
    }

    public struct GateState
    {
        public ulong Groups;
        public int Decided;
        public int Admit;
        public int Terms;
    }

    public unsafe struct DecideArgs
    {
        public Effect* Effects;
        public Outcome* Outcomes;
        public int* Order;
        public long* Totals;
        public int* Delta;
        public ulong* Written;
        public long* Faults;
        public long* Assign;
        public int Capacity;
    }

    public unsafe struct ChunkView
    {
        public int Chunk;
        public int* L0;
        public int* L1;
        public int* L2;
        public int* L3;
        public int* L4;
        public int* L5;
        public int* L6;
        public int* L8;
        public int* L9;
        public int* L10;
        public int* L13;
        public int* L16;
        public int* L17;
        public int* L18;
        public int* L19;
        public int* L21;
        public int* L25;
        public int* L32;
        public ulong C12;
        public ulong C14;
        public ulong C15;
        public ulong C16;
        public ulong C26;
        public ulong C28;
        public ulong C29;
        public ulong C33;
        public ulong C34;
        public ulong C35;
        public ulong C36;
        public ulong M0;
        public ulong M1;
        public ulong M2;
        public ulong M3;
        public ulong M4;
    }

    public static unsafe class RuleMath
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Add(long left, long right)
        {
            var sum = unchecked(left + right);
            return ((left ^ sum) & (right ^ sum)) < 0L ? (left < 0L ? long.MinValue : long.MaxValue) : sum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Negate(long value) => value == long.MinValue ? long.MaxValue : -value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Subtract(long left, long right) => Add(left, Negate(right));

        public static long Multiply(long left, long right)
        {
            if (left == 0L || right == 0L)
            {
                return 0L;
            }

            if (right == -1L)
            {
                return Negate(left);
            }

            if (left == -1L)
            {
                return Negate(right);
            }

            var negative = (left < 0L) != (right < 0L);
            if (left == long.MinValue || right == long.MinValue)
            {
                return negative ? long.MinValue : long.MaxValue;
            }

            var product = unchecked(left * right);
            return product / right != left ? (negative ? long.MinValue : long.MaxValue) : product;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Min(long left, long right) => left < right ? left : right;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Max(long left, long right) => left > right ? left : right;

        public static float AddFloat(float left, float right) => (float)(left + right);

        public static float SubtractFloat(float left, float right) => (float)(left - right);

        public static float MultiplyFloat(float left, float right) => (float)(left * right);

        public static float MinFloat(float left, float right) => left < right ? left : right;

        public static float MaxFloat(float left, float right) => left > right ? left : right;

        public static bool Differ(float left, float right) => left < right || left > right;

        public static bool Bit(long value, long index) => index >= 0L && index < 64L && ((value >> (int)index) & 1L) != 0L;

        public static bool Below(long value, long whole, long numerator, long denominator) => Multiply(value, denominator) < Multiply(whole, numerator);

        public static int Step(int before, int delta, int floor, int ceiling)
        {
            var low = floor < ceiling ? floor : ceiling;
            var moved = (long)before + delta;
            return moved < low ? low : moved > ceiling ? ceiling : (int)moved;
        }

        public static int KindOf(int kind) => kind < 0 ? 0 : kind > 255 ? 255 : kind;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Gross(in Outcome outcome) => EffectLaw.Term(outcome.TakenPerHit, outcome.TakenHits);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Offered(in Effect effect) => EffectLaw.Term(effect.Amount, effect.Hits);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Taken(in Outcome outcome) => Gross(outcome) - outcome.Absorbed;

        public static long NotTaken(in Effect effect, in Outcome outcome) => Add(Offered(effect), Negate(Taken(outcome)));

        public static long Consumed(in Effect effect, in Outcome outcome, int measure) =>
            measure == 0 ? Offered(effect) : measure == 1 ? Taken(outcome) : NotTaken(effect, outcome);

        public static void PerHit(in Effect effect, in Outcome outcome, int measure, out int amount, out int hits)
        {
            var whole = outcome.Absorbed == 0L;
            if (measure == 0)
            {
                amount = effect.Amount;
                hits = effect.Hits;
                return;
            }

            if (measure == 1)
            {
                amount = whole ? outcome.TakenPerHit : EffectLaw.Saturate32(Taken(outcome));
                hits = whole ? outcome.TakenHits : 1;
                return;
            }

            if (whole && outcome.TakenHits == effect.Hits)
            {
                amount = EffectLaw.Saturate32((long)effect.Amount - outcome.TakenPerHit);
                hits = effect.Hits;
                return;
            }

            amount = EffectLaw.Saturate32(NotTaken(effect, outcome));
            hits = 1;
        }

        public static bool Present(in WorldView world, int capacity, long slot)
        {
            if ((ulong)slot >= (ulong)capacity)
            {
                return false;
            }

            var at = (int)slot;
            var meta = Lanes.WordsOf(world, at >> 6, world.Layout.MetaLane);
            return (((meta[ChunkMeta.Live] | meta[ChunkMeta.Dying]) >> (at & 63)) & 1ul) != 0ul;
        }

        public static int Read(in WorldView world, int capacity, long slot, int lane, int fallback) =>
            Present(world, capacity, slot) ? Lanes.Read(world, (int)slot, lane) : fallback;

        public static bool Level(in WorldView world, int capacity, long slot, int lane, int word, bool fallback) =>
            Present(world, capacity, slot) ? ((Lanes.WordsOf(world, (int)slot >> 6, lane)[word] >> ((int)slot & 63)) & 1ul) != 0ul : fallback;
    }

    public static unsafe class Kernels
    {
        public static readonly int Channels = 28;

        public static readonly int DeltaLanes = 18;

        public static readonly int ScratchLanes = 18;

        public static readonly int TotalLanes = 28;

        public static readonly int RecordBytes = sizeof(Outcome) + sizeof(int);

        public static readonly int EventCount = 3;

        public static readonly int LanesWrittenPerChunk = 19;

        public static int ChannelIndex(int channel) => (uint)channel < 28u ? channel : -1;

        static ulong ChannelBit(int channel) => channel < 64 ? 1ul << channel : ulong.MaxValue;

        public static int DeltaSlotOf(int intrinsic)
        {
            switch (intrinsic)
            {
                case 8: return 0;
                case 10: return 1;
                case 11: return 2;
                case 13: return 3;
                case 16: return 4;
                case 19: return 5;
                case 20: return 6;
                case 21: return 7;
                case 22: return 8;
                case 23: return 9;
                case 24: return 10;
                case 25: return 11;
                case 28: return 12;
                case 30: return 13;
                case 31: return 14;
                case 32: return 15;
                case 40: return 16;
                case 41: return 17;
                default: return -1;
            }
        }

        public static ulong Decide(in WorldView world, int chunk, int capacity, Effect* effects, int count, int* scratch, ulong* written, long* totals, byte* records, ref EffectWriter next)
        {
            var v = View(world, chunk);
            Masks(world, ref v);
            var assign = stackalloc long[4];
            var a = new DecideArgs { Effects = effects, Outcomes = (Outcome*)records, Order = (int*)(records + (long)count * sizeof(Outcome)), Totals = totals, Delta = scratch, Written = written, Capacity = capacity, Assign = assign };
            var terms = stackalloc ShapeTerm[10];
            var totalled = 0ul;
            var at = 0;
            while (at < count)
            {
                var first = at;
                var target = EffectKey.Target(effects[at].Key);
                var present = ChannelBit(EffectKey.Channel(effects[at].Key));
                at++;
                while (at < count && EffectKey.Target(effects[at].Key) == target)
                {
                    present |= ChannelBit(EffectKey.Channel(effects[at].Key));
                    at++;
                }

                var row = target & 63;
                var self = 1ul << row;
                totalled |= Target(world, ref v, a, ref next, target, row, self, first, at, present, terms);
            }

            return totalled;
        }

        static ulong Target(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ulong present, ShapeTerm* terms)
        {
            var untaken = -1;
            untaken = (v.C29 & self) != 0ul && untaken < 3 ? 3 : untaken;
            var used13 = 0L;
            var used16 = 0L;
            var used21 = 0L;
            var reshaped = ((v.C16 & self) != 0ul && (present & 0x0000000000000007ul) != 0ul) || ((v.M2 & self) != 0ul && (present & 0x0000000000000007ul) != 0ul) || ((present & 0x0000000000800000ul) != 0ul);
            var assigned = false;
            a.Assign[0] = v.L10[row];
            a.Assign[1] = v.L19[row];
            a.Assign[2] = v.L21[row];
            a.Assign[3] = v.L32[row];
            for (var r = first; r < end; r++)
            {
                Gate(world, ref v, a, row, self, r, terms);
                if (!reshaped)
                {
                    var survives = RuleMath.Taken(a.Outcomes[r]) != 0L || EffectKey.Depth(a.Effects[r].Key) <= untaken;
                    a.Outcomes[r].Survives = survives ? 1 : 0;
                    if (survives)
                    {
                        var index = ChannelIndex(EffectKey.Channel(a.Effects[r].Key));
                        if (index >= 0)
                        {
                            var at = (index << 6) + row;
                            a.Totals[at] = RuleMath.Add(a.Totals[at], RuleMath.Taken(a.Outcomes[r]));
                        }
                    }

                    if (In0((long)EffectKey.Channel(a.Effects[r].Key)) && RuleMath.Taken(a.Outcomes[r]) != 0L)
                    {
                        RuleMath.PerHit(a.Effects[r], a.Outcomes[r], 1, out var perHit, out _);
                        var from = EffectKey.Source(a.Effects[r].Key);
                        Assign(a, EffectKey.Channel(a.Effects[r].Key), perHit, from == EffectKey.NoSource ? -1L : from);
                        assigned = true;
                    }
                }
            }

            if (reshaped)
            {

                if ((v.C16 & self) != 0ul && (present & 0x0000000000000007ul) != 0ul)
                {
                    var candidates = 0;
                    for (var r = first; r < end; r++)
                    {
                        var effect = a.Effects[r];
                        if (a.Outcomes[r].Admitted != 0 && In1(((long)EffectKey.Channel(effect.Key))))
                        {
                            a.Order[candidates] = r;
                            candidates++;
                        }
                    }

                    var budget = (long)v.L16[row];
                    for (var k = 0; k < candidates; k++)
                    {
                        var remaining = (budget < 0L ? 0L : budget) - used16;
                        if (remaining <= 0L)
                        {
                            break;
                        }

                        var outcome = a.Outcomes + a.Order[k];
                        var take = remaining < outcome->TakenHits ? remaining : outcome->TakenHits;
                        outcome->TakenHits -= (int)take;
                        used16 += take;
                    }
                }

                if ((v.M2 & self) != 0ul && (present & 0x0000000000000007ul) != 0ul)
                {
                    var candidates = 0;
                    for (var r = first; r < end; r++)
                    {
                        var effect = a.Effects[r];
                        if (a.Outcomes[r].Admitted != 0 && (v.M2 & self) != 0ul && In1(((long)EffectKey.Channel(effect.Key))))
                        {
                            a.Order[candidates] = r;
                            candidates++;
                        }
                    }

                    for (var k = 1; k < candidates; k++)
                    {
                        var at = k;
                        while (at > 0 && RuleMath.Gross(a.Outcomes[a.Order[at - 1]]) < RuleMath.Gross(a.Outcomes[a.Order[at]]))
                        {
                            var swapped = a.Order[at - 1];
                            a.Order[at - 1] = a.Order[at];
                            a.Order[at] = swapped;
                            at--;
                        }
                    }

                    var budget = (long)v.L13[row];
                    for (var k = 0; k < candidates; k++)
                    {
                        var remaining = (budget < 0L ? 0L : budget) - used13;
                        if (remaining <= 0L)
                        {
                            break;
                        }

                        var outcome = a.Outcomes + a.Order[k];
                        var available = RuleMath.Gross(*outcome) - outcome->Absorbed;
                        if (available <= 0L)
                        {
                            continue;
                        }

                        var take = remaining < available ? remaining : available;
                        outcome->Absorbed += take;
                        used13 += take;
                    }
                }

                if ((present & 0x0000000000800000ul) != 0ul)
                {
                    var budget = (long)v.L21[row];
                    for (var r = first; r < end; r++)
                    {
                        var effect = a.Effects[r];
                        if (a.Outcomes[r].Admitted != 0 && (((long)EffectKey.Channel(effect.Key)) == 23L))
                        {
                            var remaining = (budget < 0L ? 0L : budget) - used21;
                            var outcome = a.Outcomes + r;
                            var take = remaining <= 0L ? 0L : remaining < outcome->TakenHits ? remaining : outcome->TakenHits;
                            outcome->TakenHits = (int)take;
                            used21 += take;
                        }
                    }
                }

                for (var r = first; r < end; r++)
                {
                    var survives = RuleMath.Taken(a.Outcomes[r]) != 0L || EffectKey.Depth(a.Effects[r].Key) <= untaken;
                    a.Outcomes[r].Survives = survives ? 1 : 0;
                    if (survives)
                    {
                        var index = ChannelIndex(EffectKey.Channel(a.Effects[r].Key));
                        if (index >= 0)
                        {
                            var at = (index << 6) + row;
                            a.Totals[at] = RuleMath.Add(a.Totals[at], RuleMath.Taken(a.Outcomes[r]));
                        }
                    }

                    if (In0((long)EffectKey.Channel(a.Effects[r].Key)) && RuleMath.Taken(a.Outcomes[r]) != 0L)
                    {
                        RuleMath.PerHit(a.Effects[r], a.Outcomes[r], 1, out var perHit, out _);
                        var from = EffectKey.Source(a.Effects[r].Key);
                        Assign(a, EffectKey.Channel(a.Effects[r].Key), perHit, from == EffectKey.NoSource ? -1L : from);
                        assigned = true;
                    }
                }
            }

            if ((present & 0x0000000000000001ul) != 0ul)
            {
                EmitRule3007(world, ref v, a, ref next, target, row, self, first, end, terms);
            }

            if ((v.C15 & self) != 0ul && (present & 0x0000000000000007ul) != 0ul)
            {
                EmitRule3009(world, ref v, a, ref next, target, row, self, first, end, terms);
            }

            if ((present & 0x0000000000010000ul) != 0ul)
            {
                EmitRule3013(world, ref v, a, ref next, target, row, self, first, end, terms);
            }

            if ((present & 0x0000000002000000ul) != 0ul)
            {
                EmitRule3021(world, ref v, a, ref next, target, row, self, first, end, terms);
            }

            var written = present;

            if ((v.M3 & self) != 0ul && (present & 0x0000000000000010ul) != 0ul)
            {
                written |= 0x0000000000000022ul;
                SelfRule3001(world, ref v, a, ref next, target, row, self, first, end, terms);
            }

            if ((v.C12 & self) != 0ul && (present & 0x0000000000000010ul) != 0ul)
            {
                written |= 0x0000000000000008ul;
                SelfRule3002(world, ref v, a, ref next, target, row, self, first, end, terms);
            }

            if ((v.M0 & self) != 0ul && (present & 0x0000000000000010ul) != 0ul)
            {
                written |= 0x0000000000000040ul;
                SelfRule3003(world, ref v, a, ref next, target, row, self, first, end, terms);
            }

            if ((v.M1 & self) != 0ul && (present & 0x0000000000000010ul) != 0ul)
            {
                written |= 0x0000000000001000ul;
                SelfRule3004(world, ref v, a, ref next, target, row, self, first, end, terms);
            }

            if ((present & 0x0000000000000800ul) != 0ul)
            {
                written |= 0x0000000000001000ul;
                SelfRule3005(world, ref v, a, ref next, target, row, self, first, end, terms);
            }

            if ((present & 0x0000000000000200ul) != 0ul)
            {
                written |= 0x0000000000000400ul;
                SelfRule3006(world, ref v, a, ref next, target, row, self, first, end, terms);
            }

            if ((v.C14 & self) != 0ul && (present & 0x0000000000080000ul) != 0ul)
            {
                written |= 0x0000000000000008ul;
                SelfRule3008(world, ref v, a, ref next, target, row, self, first, end, terms);
            }

            if ((v.C26 & self) != 0ul && (present & 0x0000000000000001ul) != 0ul)
            {
                written |= 0x0000000000002000ul;
                SelfRule3010(world, ref v, a, ref next, target, row, self, first, end, terms);
            }

            if ((v.C26 & self) != 0ul && (present & 0x0000000000000001ul) != 0ul)
            {
                written |= 0x0000000000002000ul;
                SelfRule3011(world, ref v, a, ref next, target, row, self, first, end, terms);
            }

            if ((v.C29 & self) != 0ul && (present & 0x0000000000000001ul) != 0ul)
            {
                written |= 0x0000000000000004ul;
                SelfRule3012(world, ref v, a, ref next, target, row, self, first, end, terms);
            }

            if ((v.C33 & self) != 0ul && (present & 0x0000000000000007ul) != 0ul)
            {
                written |= 0x0000000000000008ul;
                SelfRule3014(world, ref v, a, ref next, target, row, self, first, end, terms);
            }

            if ((v.C36 & self) != 0ul && (present & 0x0000000000000007ul) != 0ul)
            {
                written |= 0x0000000000000008ul;
                SelfRule3015(world, ref v, a, ref next, target, row, self, first, end, terms);
            }

            if ((present & 0x0000000000100000ul) != 0ul)
            {
                written |= 0x0000000000200000ul;
                assigned = true;
                SelfRule3020(world, ref v, a, ref next, target, row, self, first, end, terms);
            }

            if ((v.M4 & self) != 0ul && (present & 0x0000000000000010ul) != 0ul)
            {
                written |= 0x0000000000000002ul;
                SelfRule3022(world, ref v, a, ref next, target, row, self, first, end, terms);
            }

            var nonzero = 0ul;
            var pending = written;
            while (pending != 0ul)
            {
                var channel = Bits.Tzcnt(pending);
                pending &= pending - 1ul;
                var index = ChannelIndex(channel);
                nonzero |= index >= 0 && a.Totals[(index << 6) + row] != 0L ? 1ul << channel : 0ul;
            }

            if ((nonzero & 0x000000000886FDEFul) != 0ul || reshaped || assigned)
            {
                var delta0 = 0L;
                var delta1 = 0L;
                var delta2 = 0L;
                var delta3 = 0L;
                var delta4 = 0L;
                var delta5 = 0L;
                var delta6 = 0L;
                var delta7 = 0L;
                var delta8 = 0L;
                var delta9 = 0L;
                var delta10 = 0L;
                var delta11 = 0L;
                var delta12 = 0L;
                var delta13 = 0L;
                var delta14 = 0L;
                var delta15 = 0L;
                var delta16 = 0L;
                var delta17 = 0L;

                if ((nonzero & 0x000000000000000Ful) != 0ul)
                {
                    delta0 = RuleMath.Add(delta0, a.Totals[192 + row]);
                    delta0 = RuleMath.Add(delta0, RuleMath.Negate(a.Totals[0 + row]));
                    delta0 = RuleMath.Add(delta0, RuleMath.Negate(a.Totals[64 + row]));
                    delta0 = RuleMath.Add(delta0, RuleMath.Negate(a.Totals[128 + row]));
                }

                if ((nonzero & 0x0000000000000080ul) != 0ul)
                {
                    delta6 = RuleMath.Add(delta6, a.Totals[448 + row]);
                }

                if ((nonzero & 0x0000000000000100ul) != 0ul)
                {
                    delta7 = RuleMath.Add(delta7, RuleMath.Negate(a.Totals[512 + row]));
                    delta8 = RuleMath.Add(delta8, a.Totals[512 + row]);
                }

                if ((nonzero & 0x0000000000000800ul) != 0ul)
                {
                    delta10 = RuleMath.Add(delta10, a.Totals[704 + row]);
                }

                if ((nonzero & 0x0000000000000004ul) != 0ul)
                {
                    delta13 = RuleMath.Add(delta13, a.Totals[128 + row]);
                }

                if ((nonzero & 0x0000000000800000ul) != 0ul)
                {
                    delta16 = RuleMath.Add(delta16, a.Totals[1472 + row]);
                }

                if ((nonzero & 0x000000000806F460ul) != 0ul)
                {
                    delta2 = RuleMath.Add(delta2, a.Totals[320 + row]);
                    delta5 = RuleMath.Add(delta5, a.Totals[384 + row]);
                    delta7 = RuleMath.Add(delta7, a.Totals[640 + row]);
                    delta9 = RuleMath.Add(delta9, a.Totals[768 + row]);
                    delta11 = RuleMath.Add(delta11, a.Totals[832 + row]);
                    delta12 = RuleMath.Add(delta12, a.Totals[896 + row]);
                    delta14 = RuleMath.Add(delta14, a.Totals[960 + row]);
                    delta15 = RuleMath.Add(delta15, a.Totals[1088 + row]);
                    delta1 = RuleMath.Add(delta1, a.Totals[1152 + row]);
                    delta17 = RuleMath.Add(delta17, a.Totals[1728 + row]);
                }

                delta3 = RuleMath.Add(delta3, -used13);
                delta4 = RuleMath.Add(delta4, -used16);
                delta7 = RuleMath.Add(delta7, -used21);
                delta1 = RuleMath.Add(delta1, RuleMath.Subtract(a.Assign[0], v.L10[row]));
                delta5 = RuleMath.Add(delta5, RuleMath.Subtract(a.Assign[1], v.L19[row]));
                delta7 = RuleMath.Add(delta7, RuleMath.Subtract(a.Assign[2], v.L21[row]));
                delta15 = RuleMath.Add(delta15, RuleMath.Subtract(a.Assign[3], v.L32[row]));
                if (delta0 != 0L)
                {
                    Touch(a, 0);
                    a.Delta[0 + row] = EffectLaw.Saturate32(delta0);
                }
                if (delta1 != 0L)
                {
                    Touch(a, 1);
                    a.Delta[64 + row] = EffectLaw.Saturate32(delta1);
                }
                if (delta2 != 0L)
                {
                    Touch(a, 2);
                    a.Delta[128 + row] = EffectLaw.Saturate32(delta2);
                }
                if (delta3 != 0L)
                {
                    Touch(a, 3);
                    a.Delta[192 + row] = EffectLaw.Saturate32(delta3);
                }
                if (delta4 != 0L)
                {
                    Touch(a, 4);
                    a.Delta[256 + row] = EffectLaw.Saturate32(delta4);
                }
                if (delta5 != 0L)
                {
                    Touch(a, 5);
                    a.Delta[320 + row] = EffectLaw.Saturate32(delta5);
                }
                if (delta6 != 0L)
                {
                    Touch(a, 6);
                    a.Delta[384 + row] = EffectLaw.Saturate32(delta6);
                }
                if (delta7 != 0L)
                {
                    Touch(a, 7);
                    a.Delta[448 + row] = EffectLaw.Saturate32(delta7);
                }
                if (delta8 != 0L)
                {
                    Touch(a, 8);
                    a.Delta[512 + row] = EffectLaw.Saturate32(delta8);
                }
                if (delta9 != 0L)
                {
                    Touch(a, 9);
                    a.Delta[576 + row] = EffectLaw.Saturate32(delta9);
                }
                if (delta10 != 0L)
                {
                    Touch(a, 10);
                    a.Delta[640 + row] = EffectLaw.Saturate32(delta10);
                }
                if (delta11 != 0L)
                {
                    Touch(a, 11);
                    a.Delta[704 + row] = EffectLaw.Saturate32(delta11);
                }
                if (delta12 != 0L)
                {
                    Touch(a, 12);
                    a.Delta[768 + row] = EffectLaw.Saturate32(delta12);
                }
                if (delta13 != 0L)
                {
                    Touch(a, 13);
                    a.Delta[832 + row] = EffectLaw.Saturate32(delta13);
                }
                if (delta14 != 0L)
                {
                    Touch(a, 14);
                    a.Delta[896 + row] = EffectLaw.Saturate32(delta14);
                }
                if (delta15 != 0L)
                {
                    Touch(a, 15);
                    a.Delta[960 + row] = EffectLaw.Saturate32(delta15);
                }
                if (delta16 != 0L)
                {
                    Touch(a, 16);
                    a.Delta[1024 + row] = EffectLaw.Saturate32(delta16);
                }
                if (delta17 != 0L)
                {
                    Touch(a, 17);
                    a.Delta[1088 + row] = EffectLaw.Saturate32(delta17);
                }
            }

            while (written != 0ul)
            {
                var index = ChannelIndex(Bits.Tzcnt(written));
                written &= written - 1ul;
                if (index >= 0)
                {
                    a.Totals[(index << 6) + row] = 0L;
                }
            }

            return nonzero != 0ul ? self : 0ul;
        }

        static void Touch(in DecideArgs a, int lane)
        {
            var word = a.Written + (lane >> 6);
            var bit = 1ul << (lane & 63);
            if ((*word & bit) == 0ul)
            {
                *word |= bit;
                LaneMath.Fill(a.Delta + (lane << 6), 0);
            }
        }

        static void Assign(in DecideArgs a, int channel, int amount, long source)
        {
            switch (channel)
            {
                case 21: a.Assign[2] = (long)amount; return;
                case 22: a.Assign[1] = RuleMath.Max(a.Assign[1], (long)amount); return;
                case 24: a.Assign[0] = source; return;
                case 26: a.Assign[3] = (long)amount; return;
                default: return;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Gate(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, int r, ShapeTerm* terms)
        {
            var effect = a.Effects[r];
            var state = default(GateState);
            var channel = (int)EffectKey.Channel(effect.Key);
            switch (channel)
            {
                case 0: AdmitChannel0(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 1: AdmitChannel1(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 2: AdmitChannel2(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 3: AdmitChannel3(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 4: AdmitChannel4(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 5: AdmitChannel5(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 6: AdmitChannel6(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 7: AdmitChannel7(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 8: AdmitChannel8(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 9: AdmitChannel9(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 10: AdmitChannel10(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 11: AdmitChannel11(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 12: AdmitChannel12(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 13: AdmitChannel13(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 14: AdmitChannel14(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 15: AdmitChannel15(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 16: AdmitChannel16(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 17: AdmitChannel17(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 18: AdmitChannel18(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 19: AdmitChannel19(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 20: AdmitChannel20(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 21: AdmitChannel21(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 22: AdmitChannel22(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 23: AdmitChannel23(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 24: AdmitChannel24(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 25: AdmitChannel25(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 26: AdmitChannel26(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 27: AdmitChannel27(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                default: AdmitAny(world, ref v, a, row, self, effect, channel, ref state, terms); break;
            }
            var admitted = state.Decided == 0 || state.Admit != 0;
            var taken = 0;
            if (admitted)
            {
                taken = effect.Amount;
                if (state.Terms > 0)
                {
                    StatLaw.Canonicalize(terms, state.Terms);
                    taken = StatLaw.Fold(effect.Amount, terms, state.Terms);
                }
            }

            var outcome = a.Outcomes + r;
            outcome->Admitted = admitted ? 1 : 0;
            outcome->TakenPerHit = taken;
            outcome->TakenHits = effect.Hits;
            outcome->Absorbed = 0L;
            outcome->Survives = 0;
        }

        static void AdmitChannel0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel0Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel0Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
            if ((v.C26 & self) != 0ul)
            {
                AdmitChannel0Group2(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel0Group3(world, ref v, a, row, self, effect, channel, ref state, terms);
            if ((v.C29 & self) != 0ul)
            {
                AdmitChannel0Group4(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel0Group5(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel0Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel0Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            var read0 = RuleMath.Read(world, a.Capacity, (long)EffectKey.Source(effect.Key), 31, 0);

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 0L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if ((((long)read0) >= 5L))
            {
                if ((state.Groups & (1ul << 0)) == 0ul)
                {
                    state.Groups |= (1ul << 0);
                    terms[state.Terms] = new ShapeTerm(ShapeKind.Additive, 5000);
                    state.Terms++;
                }
            }

            if ((((long)read0) >= 4L))
            {
                if ((state.Groups & (1ul << 0)) == 0ul)
                {
                    state.Groups |= (1ul << 0);
                    terms[state.Terms] = new ShapeTerm(ShapeKind.Additive, 4000);
                    state.Terms++;
                }
            }

            if ((((long)read0) >= 3L))
            {
                if ((state.Groups & (1ul << 0)) == 0ul)
                {
                    state.Groups |= (1ul << 0);
                    terms[state.Terms] = new ShapeTerm(ShapeKind.Additive, 3000);
                    state.Terms++;
                }
            }

            if ((((long)read0) >= 2L))
            {
                if ((state.Groups & (1ul << 0)) == 0ul)
                {
                    state.Groups |= (1ul << 0);
                    terms[state.Terms] = new ShapeTerm(ShapeKind.Additive, 2000);
                    state.Terms++;
                }
            }

            if ((((long)read0) >= 1L))
            {
                if ((state.Groups & (1ul << 0)) == 0ul)
                {
                    state.Groups |= (1ul << 0);
                    terms[state.Terms] = new ShapeTerm(ShapeKind.Additive, 1000);
                    state.Terms++;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel0Group2(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((((long)v.L25[row]) >= 2L))
            {
                terms[state.Terms] = new ShapeTerm(ShapeKind.Multi, 10000);
                state.Terms++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel0Group3(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (((((long)RuleMath.Read(world, a.Capacity, (long)EffectKey.Source(effect.Key), 27, 0)) > 0L) && RuleMath.Below(((long)v.L8[row]), ((long)StatLaw.Rescale(v.L0[row], 1, 1)), 20L, 100L)))
            {
                terms[state.Terms] = new ShapeTerm(ShapeKind.Added, 100000);
                state.Terms++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel0Group4(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (true)
            {
                terms[state.Terms] = new ShapeTerm(ShapeKind.Multi, -5000);
                state.Terms++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel0Group5(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((((long)RuleMath.Read(world, a.Capacity, (long)EffectKey.Source(effect.Key), 41, 0)) > 0L))
            {
                terms[state.Terms] = new ShapeTerm(ShapeKind.Additive, StatLaw.Rescale(RuleMath.Read(world, a.Capacity, (long)EffectKey.Source(effect.Key), 43, 0), 100, 10000));
                state.Terms++;
            }

            if (((((long)RuleMath.Read(world, a.Capacity, (long)EffectKey.Source(effect.Key), 42, 0)) > 0L) && Roll.Passes(0x0000000000000000ul, world.Frame, effect.Key, effect.Amount, 2033, 0, 2500)))
            {
                terms[state.Terms] = new ShapeTerm(ShapeKind.Multi, 10000);
                state.Terms++;
            }
        }

        static void AdmitChannel1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel1Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel1Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel1Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel1Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel2(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel2Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel2Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel2Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel2Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 2L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel3(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel3Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel3Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
            if ((v.C34 & self) != 0ul)
            {
                AdmitChannel3Group2(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel3Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel3Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 3L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel3Group2(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Source(effect.Key)) != ((long)v.L10[row])))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel4(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel4Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel4Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel4Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel4Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 4L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel5(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel5Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel5Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel5Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel5Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 5L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel6(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel6Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel6Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel6Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel6Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 6L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel7(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel7Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel7Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel7Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel7Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 7L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && (v.M0 & self) != 0ul)
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel8(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel8Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel8Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel8Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel8Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 8L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && (!(((long)v.L21[row]) > 0L)))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel9(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel9Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel9Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel9Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel9Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 9L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel10(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel10Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel10Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel10Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel10Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 10L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel11(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel11Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel11Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel11Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel11Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 11L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && (v.M1 & self) != 0ul)
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel12(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel12Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel12Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel12Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel12Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 12L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel13(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel13Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel13Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel13Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel13Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 13L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel14(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel14Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel14Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel14Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel14Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 14L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel15(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel15Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel15Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel15Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel15Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 15L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel16(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel16Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel16Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel16Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel16Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 16L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel17(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel17Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel17Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel17Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel17Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 17L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel18(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel18Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel18Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel18Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel18Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 18L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel19(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel19Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel19Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel19Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel19Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 19L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel20(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel20Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel20Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel20Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel20Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 20L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel21(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel21Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel21Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel21Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel21Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 21L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel22(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel22Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel22Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel22Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel22Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 22L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel23(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel23Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel23Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel23Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel23Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 23L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel24(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel24Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel24Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel24Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel24Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 24L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel25(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel25Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel25Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel25Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel25Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 25L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel26(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel26Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel26Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel26Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel26Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 26L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel27(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitChannel27Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel27Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel27Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel27Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 27L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitAny(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C35 & self) != 0ul)
            {
                AdmitAnyGroup0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitAnyGroup1(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitAnyGroup0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (((long)EffectKey.Kind(effect.Key)) == 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitAnyGroup1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), ((long)EffectKey.Channel(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && RuleMath.Bit(((long)v.L18[row]), ((long)EffectKey.Kind(effect.Key))))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static int SelfAdmit(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, int channel, int amount, ShapeTerm* terms)
        {
            var state = default(GateState);
            var effect = default(Effect);
            switch (channel)
            {
                case 0: SelfAdmitChannel0(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 1: SelfAdmitChannel1(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 2: SelfAdmitChannel2(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 3: SelfAdmitChannel3(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 4: SelfAdmitChannel4(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 5: SelfAdmitChannel5(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 6: SelfAdmitChannel6(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 7: SelfAdmitChannel7(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 8: SelfAdmitChannel8(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 9: SelfAdmitChannel9(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 10: SelfAdmitChannel10(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 11: SelfAdmitChannel11(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 12: SelfAdmitChannel12(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 13: SelfAdmitChannel13(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 14: SelfAdmitChannel14(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 15: SelfAdmitChannel15(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 16: SelfAdmitChannel16(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 17: SelfAdmitChannel17(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 18: SelfAdmitChannel18(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 19: SelfAdmitChannel19(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 20: SelfAdmitChannel20(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 21: SelfAdmitChannel21(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 22: SelfAdmitChannel22(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 23: SelfAdmitChannel23(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 24: SelfAdmitChannel24(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 25: SelfAdmitChannel25(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 26: SelfAdmitChannel26(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 27: SelfAdmitChannel27(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                default: SelfAdmitAny(world, ref v, a, row, self, effect, channel, ref state, terms); break;
            }
            if (state.Decided != 0 && state.Admit == 0)
            {
                return 0;
            }

            if (state.Terms == 0)
            {
                return amount;
            }

            StatLaw.Canonicalize(terms, state.Terms);
            return StatLaw.Fold(amount, terms, state.Terms);
        }

        static void SelfAdmitChannel0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel0Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            if ((v.C26 & self) != 0ul)
            {
                SelfAdmitChannel0Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            if ((v.C29 & self) != 0ul)
            {
                SelfAdmitChannel0Group2(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel0Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 0L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel0Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((((long)v.L25[row]) >= 2L))
            {
                terms[state.Terms] = new ShapeTerm(ShapeKind.Multi, 10000);
                state.Terms++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel0Group2(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (true)
            {
                terms[state.Terms] = new ShapeTerm(ShapeKind.Multi, -5000);
                state.Terms++;
            }
        }

        static void SelfAdmitChannel1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel1Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel1Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel2(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel2Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel2Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 2L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel3(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel3Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel3Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 3L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel4(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel4Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel4Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 4L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel5(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel5Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel5Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 5L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel6(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel6Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel6Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 6L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel7(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel7Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel7Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 7L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && (v.M0 & self) != 0ul)
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel8(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel8Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel8Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 8L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && (!(((long)v.L21[row]) > 0L)))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel9(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel9Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel9Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 9L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel10(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel10Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel10Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 10L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel11(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel11Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel11Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 11L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }

            if (state.Decided == 0 && (v.M1 & self) != 0ul)
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel12(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel12Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel12Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 12L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel13(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel13Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel13Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 13L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel14(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel14Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel14Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 14L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel15(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel15Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel15Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 15L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel16(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel16Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel16Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 16L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel17(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel17Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel17Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 17L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel18(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel18Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel18Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 18L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel19(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel19Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel19Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 19L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel20(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel20Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel20Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 20L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel21(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel21Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel21Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 21L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel22(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel22Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel22Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 22L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel23(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel23Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel23Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 23L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel24(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel24Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel24Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 24L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel25(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel25Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel25Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 25L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel26(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel26Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel26Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 26L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel27(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel27Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel27Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), 27L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitAny(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitAnyGroup0(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitAnyGroup0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L17[row]), ((long)channel)))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfRule3001(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)
        {
            var groupFrom = first;
            while (groupFrom < end)
            {
                var source = EffectKey.Source(a.Effects[groupFrom].Key);
                var groupTo = groupFrom + 1;
                while (groupTo < end && EffectKey.Source(a.Effects[groupTo].Key) == source)
                {
                    groupTo++;
                }

                for (var r = groupFrom; r < groupTo; r++)
                {
                    var effect = a.Effects[r];
                    var outcome = a.Outcomes[r];
                    var depth = EffectKey.Depth(effect.Key);
                    if (outcome.Survives != 0 && depth <= 3 && (v.M3 & self) != 0ul && (((long)EffectKey.Channel(effect.Key)) == 4L))
                    {
                        if (true)
                        {
                            var channel = 1;
                            var amount = 0;
                            var hits = 1;
                            amount = 4;
                            if (amount != 0 && hits != 0)
                            {
                                var shaped = SelfAdmit(world, ref v, a, row, self, channel, amount, terms);
                                var index = ChannelIndex(channel);
                                if (index >= 0)
                                {
                                    var at = (index << 6) + row;
                                    a.Totals[at] = RuleMath.Add(a.Totals[at], EffectLaw.Term(shaped, hits));
                                    if (EffectLaw.Term(shaped, hits) != 0L)
                                    {
                                        Assign(a, channel, shaped, target);
                                    }
                                }
                            }
                        }
                        if (true)
                        {
                            var channel = 5;
                            var amount = 0;
                            var hits = 1;
                            amount = -1;
                            if (amount != 0 && hits != 0)
                            {
                                var shaped = SelfAdmit(world, ref v, a, row, self, channel, amount, terms);
                                var index = ChannelIndex(channel);
                                if (index >= 0)
                                {
                                    var at = (index << 6) + row;
                                    a.Totals[at] = RuleMath.Add(a.Totals[at], EffectLaw.Term(shaped, hits));
                                    if (EffectLaw.Term(shaped, hits) != 0L)
                                    {
                                        Assign(a, channel, shaped, target);
                                    }
                                }
                            }
                        }
                    }
                }

                groupFrom = groupTo;
            }
        }

        static void SelfRule3002(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)
        {
            var groupFrom = first;
            while (groupFrom < end)
            {
                var source = EffectKey.Source(a.Effects[groupFrom].Key);
                var groupTo = groupFrom + 1;
                while (groupTo < end && EffectKey.Source(a.Effects[groupTo].Key) == source)
                {
                    groupTo++;
                }

                for (var r = groupFrom; r < groupTo; r++)
                {
                    var effect = a.Effects[r];
                    var outcome = a.Outcomes[r];
                    var depth = EffectKey.Depth(effect.Key);
                    if (outcome.Survives != 0 && depth <= 3 && (((long)EffectKey.Channel(effect.Key)) == 4L))
                    {
                        if (true)
                        {
                            var channel = 3;
                            var amount = 0;
                            var hits = 1;
                            amount = 1;
                            amount = StatLaw.ScaleBy(amount, v.L3[row], 1);
                            if (amount != 0 && hits != 0)
                            {
                                var shaped = SelfAdmit(world, ref v, a, row, self, channel, amount, terms);
                                var index = ChannelIndex(channel);
                                if (index >= 0)
                                {
                                    var at = (index << 6) + row;
                                    a.Totals[at] = RuleMath.Add(a.Totals[at], EffectLaw.Term(shaped, hits));
                                    if (EffectLaw.Term(shaped, hits) != 0L)
                                    {
                                        Assign(a, channel, shaped, target);
                                    }
                                }
                            }
                        }
                    }
                }

                groupFrom = groupTo;
            }
        }

        static void SelfRule3003(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)
        {
            var groupFrom = first;
            while (groupFrom < end)
            {
                var source = EffectKey.Source(a.Effects[groupFrom].Key);
                var groupTo = groupFrom + 1;
                while (groupTo < end && EffectKey.Source(a.Effects[groupTo].Key) == source)
                {
                    groupTo++;
                }

                for (var r = groupFrom; r < groupTo; r++)
                {
                    var effect = a.Effects[r];
                    var outcome = a.Outcomes[r];
                    var depth = EffectKey.Depth(effect.Key);
                    if (outcome.Survives != 0 && depth <= 3 && (v.M0 & self) != 0ul && (((long)EffectKey.Channel(effect.Key)) == 4L))
                    {
                        if (true)
                        {
                            var channel = 6;
                            var amount = 0;
                            var hits = 1;
                            amount = -1;
                            if (amount != 0 && hits != 0)
                            {
                                var shaped = SelfAdmit(world, ref v, a, row, self, channel, amount, terms);
                                var index = ChannelIndex(channel);
                                if (index >= 0)
                                {
                                    var at = (index << 6) + row;
                                    a.Totals[at] = RuleMath.Add(a.Totals[at], EffectLaw.Term(shaped, hits));
                                    if (EffectLaw.Term(shaped, hits) != 0L)
                                    {
                                        Assign(a, channel, shaped, target);
                                    }
                                }
                            }
                        }
                    }
                }

                groupFrom = groupTo;
            }
        }

        static void SelfRule3004(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)
        {
            var groupFrom = first;
            while (groupFrom < end)
            {
                var source = EffectKey.Source(a.Effects[groupFrom].Key);
                var groupTo = groupFrom + 1;
                while (groupTo < end && EffectKey.Source(a.Effects[groupTo].Key) == source)
                {
                    groupTo++;
                }

                for (var r = groupFrom; r < groupTo; r++)
                {
                    var effect = a.Effects[r];
                    var outcome = a.Outcomes[r];
                    var depth = EffectKey.Depth(effect.Key);
                    if (outcome.Survives != 0 && depth <= 3 && (v.M1 & self) != 0ul && (((long)EffectKey.Channel(effect.Key)) == 4L))
                    {
                        if (true)
                        {
                            var channel = 12;
                            var amount = 0;
                            var hits = 1;
                            amount = -1;
                            if (amount != 0 && hits != 0)
                            {
                                var shaped = SelfAdmit(world, ref v, a, row, self, channel, amount, terms);
                                var index = ChannelIndex(channel);
                                if (index >= 0)
                                {
                                    var at = (index << 6) + row;
                                    a.Totals[at] = RuleMath.Add(a.Totals[at], EffectLaw.Term(shaped, hits));
                                    if (EffectLaw.Term(shaped, hits) != 0L)
                                    {
                                        Assign(a, channel, shaped, target);
                                    }
                                }
                            }
                        }
                    }
                }

                groupFrom = groupTo;
            }
        }

        static void SelfRule3005(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)
        {
            var groupFrom = first;
            while (groupFrom < end)
            {
                var source = EffectKey.Source(a.Effects[groupFrom].Key);
                var groupTo = groupFrom + 1;
                while (groupTo < end && EffectKey.Source(a.Effects[groupTo].Key) == source)
                {
                    groupTo++;
                }

                for (var r = groupFrom; r < groupTo; r++)
                {
                    var effect = a.Effects[r];
                    var outcome = a.Outcomes[r];
                    var depth = EffectKey.Depth(effect.Key);
                    if (outcome.Survives != 0 && depth <= 3 && ((((long)EffectKey.Channel(effect.Key)) == 11L) && (RuleMath.Taken(outcome) > 0L)))
                    {
                        if (true)
                        {
                            var channel = 12;
                            var amount = 0;
                            var hits = 1;
                            amount = 3;
                            if (amount != 0 && hits != 0)
                            {
                                var shaped = SelfAdmit(world, ref v, a, row, self, channel, amount, terms);
                                var index = ChannelIndex(channel);
                                if (index >= 0)
                                {
                                    var at = (index << 6) + row;
                                    a.Totals[at] = RuleMath.Add(a.Totals[at], EffectLaw.Term(shaped, hits));
                                    if (EffectLaw.Term(shaped, hits) != 0L)
                                    {
                                        Assign(a, channel, shaped, target);
                                    }
                                }
                            }
                        }
                    }
                }

                groupFrom = groupTo;
            }
        }

        static void SelfRule3006(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)
        {
            var groupFrom = first;
            while (groupFrom < end)
            {
                var source = EffectKey.Source(a.Effects[groupFrom].Key);
                var groupTo = groupFrom + 1;
                while (groupTo < end && EffectKey.Source(a.Effects[groupTo].Key) == source)
                {
                    groupTo++;
                }

                for (var r = groupFrom; r < groupTo; r++)
                {
                    var effect = a.Effects[r];
                    var outcome = a.Outcomes[r];
                    var depth = EffectKey.Depth(effect.Key);
                    if (outcome.Survives != 0 && depth <= 3 && (((long)EffectKey.Channel(effect.Key)) == 9L))
                    {
                        if (true)
                        {
                            var channel = 10;
                            var amount = 0;
                            var hits = 1;
                            amount = 1;
                            amount = StatLaw.ScaleBy(amount, v.L2[row], 1);
                            if (amount != 0 && hits != 0)
                            {
                                var shaped = SelfAdmit(world, ref v, a, row, self, channel, amount, terms);
                                var index = ChannelIndex(channel);
                                if (index >= 0)
                                {
                                    var at = (index << 6) + row;
                                    a.Totals[at] = RuleMath.Add(a.Totals[at], EffectLaw.Term(shaped, hits));
                                    if (EffectLaw.Term(shaped, hits) != 0L)
                                    {
                                        Assign(a, channel, shaped, target);
                                    }
                                }
                            }
                        }
                    }
                }

                groupFrom = groupTo;
            }
        }

        static void EmitRule3007(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)
        {
            var kind = RuleMath.KindOf(v.L9[row]);
            var groupFrom = first;
            while (groupFrom < end)
            {
                var source = EffectKey.Source(a.Effects[groupFrom].Key);
                var groupTo = groupFrom + 1;
                while (groupTo < end && EffectKey.Source(a.Effects[groupTo].Key) == source)
                {
                    groupTo++;
                }

                for (var r = groupFrom; r < groupTo; r++)
                {
                    var effect = a.Effects[r];
                    var outcome = a.Outcomes[r];
                    var depth = EffectKey.Depth(effect.Key);
                    if (outcome.Survives != 0 && depth <= 0 && ((((long)EffectKey.Channel(effect.Key)) == 0L) && (((long)RuleMath.Read(world, a.Capacity, (long)source, 14, 0)) > 0L) && (RuleMath.Taken(outcome) > 0L)))
                    {
                        if (true)
                        {
                            var channel = 19;
                            var amount = 0;
                            var hits = 1;
                            RuleMath.PerHit(effect, outcome, 1, out amount, out hits);
                            if (amount != 0 && hits != 0)
                            {
                                var to = (source == EffectKey.NoSource ? -1L : (long)source);
                                if ((ulong)to < (ulong)a.Capacity)
                                {
                                    next.Send(EffectKey.Pack((int)to, target, (byte)kind, (byte)channel, (Origin)3, depth + 1), amount, hits);
                                }
                            }
                        }
                    }
                }

                groupFrom = groupTo;
            }
        }

        static void SelfRule3008(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)
        {
            var groupFrom = first;
            while (groupFrom < end)
            {
                var source = EffectKey.Source(a.Effects[groupFrom].Key);
                var groupTo = groupFrom + 1;
                while (groupTo < end && EffectKey.Source(a.Effects[groupTo].Key) == source)
                {
                    groupTo++;
                }

                for (var r = groupFrom; r < groupTo; r++)
                {
                    var effect = a.Effects[r];
                    var outcome = a.Outcomes[r];
                    var depth = EffectKey.Depth(effect.Key);
                    if (outcome.Survives != 0 && depth <= 3 && (((long)EffectKey.Channel(effect.Key)) == 19L))
                    {
                        if (true)
                        {
                            var channel = 3;
                            var amount = 0;
                            var hits = 1;
                            RuleMath.PerHit(effect, outcome, 1, out amount, out hits);
                            amount = StatLaw.ScaleBy(amount, v.L6[row], 100);
                            if (amount != 0 && hits != 0)
                            {
                                var shaped = SelfAdmit(world, ref v, a, row, self, channel, amount, terms);
                                var index = ChannelIndex(channel);
                                if (index >= 0)
                                {
                                    var at = (index << 6) + row;
                                    a.Totals[at] = RuleMath.Add(a.Totals[at], EffectLaw.Term(shaped, hits));
                                    if (EffectLaw.Term(shaped, hits) != 0L)
                                    {
                                        Assign(a, channel, shaped, target);
                                    }
                                }
                            }
                        }
                    }
                }

                groupFrom = groupTo;
            }
        }

        static void EmitRule3009(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)
        {
            var kind = RuleMath.KindOf(v.L9[row]);
            var groupFrom = first;
            while (groupFrom < end)
            {
                var source = EffectKey.Source(a.Effects[groupFrom].Key);
                var groupTo = groupFrom + 1;
                while (groupTo < end && EffectKey.Source(a.Effects[groupTo].Key) == source)
                {
                    groupTo++;
                }

                for (var r = groupFrom; r < groupTo; r++)
                {
                    var effect = a.Effects[r];
                    var outcome = a.Outcomes[r];
                    var depth = EffectKey.Depth(effect.Key);
                    if (outcome.Survives != 0 && depth <= 1 && In1(((long)EffectKey.Channel(effect.Key))))
                    {
                        if (true)
                        {
                            var channel = (int)EffectKey.Channel(effect.Key);
                            var amount = 0;
                            var hits = 1;
                            RuleMath.PerHit(effect, outcome, 1, out amount, out hits);
                            amount = StatLaw.ScaleBy(amount, v.L4[row], 100);
                            if (amount != 0 && hits != 0)
                            {
                                var to = (source == EffectKey.NoSource ? -1L : (long)source);
                                if ((ulong)to < (ulong)a.Capacity)
                                {
                                    next.Send(EffectKey.Pack((int)to, target, (byte)kind, (byte)channel, (Origin)2, depth + 1), amount, hits);
                                }
                            }
                        }
                    }
                }

                groupFrom = groupTo;
            }
        }

        static void SelfRule3010(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)
        {
            var groupFrom = first;
            while (groupFrom < end)
            {
                var source = EffectKey.Source(a.Effects[groupFrom].Key);
                var groupTo = groupFrom + 1;
                while (groupTo < end && EffectKey.Source(a.Effects[groupTo].Key) == source)
                {
                    groupTo++;
                }

                for (var r = groupFrom; r < groupTo; r++)
                {
                    var effect = a.Effects[r];
                    var outcome = a.Outcomes[r];
                    var depth = EffectKey.Depth(effect.Key);
                    if (outcome.Survives != 0 && depth <= 3 && (((long)EffectKey.Channel(effect.Key)) == 0L))
                    {
                        if (true)
                        {
                            var channel = 13;
                            var amount = 0;
                            var hits = 1;
                            amount = 1;
                            hits = effect.Hits;
                            if (amount != 0 && hits != 0)
                            {
                                var shaped = SelfAdmit(world, ref v, a, row, self, channel, amount, terms);
                                var index = ChannelIndex(channel);
                                if (index >= 0)
                                {
                                    var at = (index << 6) + row;
                                    a.Totals[at] = RuleMath.Add(a.Totals[at], EffectLaw.Term(shaped, hits));
                                    if (EffectLaw.Term(shaped, hits) != 0L)
                                    {
                                        Assign(a, channel, shaped, target);
                                    }
                                }
                            }
                        }
                    }
                }

                groupFrom = groupTo;
            }
        }

        static void SelfRule3011(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)
        {
            var groupFrom = first;
            while (groupFrom < end)
            {
                var source = EffectKey.Source(a.Effects[groupFrom].Key);
                var groupTo = groupFrom + 1;
                while (groupTo < end && EffectKey.Source(a.Effects[groupTo].Key) == source)
                {
                    groupTo++;
                }

                for (var r = groupFrom; r < groupTo; r++)
                {
                    var effect = a.Effects[r];
                    var outcome = a.Outcomes[r];
                    var depth = EffectKey.Depth(effect.Key);
                    if (outcome.Survives != 0 && depth <= 3 && ((((long)EffectKey.Channel(effect.Key)) == 0L) && (((long)v.L25[row]) >= 2L)))
                    {
                        if (true)
                        {
                            var channel = 13;
                            var amount = 0;
                            var hits = 1;
                            amount = -3;
                            if (amount != 0 && hits != 0)
                            {
                                var shaped = SelfAdmit(world, ref v, a, row, self, channel, amount, terms);
                                var index = ChannelIndex(channel);
                                if (index >= 0)
                                {
                                    var at = (index << 6) + row;
                                    a.Totals[at] = RuleMath.Add(a.Totals[at], EffectLaw.Term(shaped, hits));
                                    if (EffectLaw.Term(shaped, hits) != 0L)
                                    {
                                        Assign(a, channel, shaped, target);
                                    }
                                }
                            }
                        }
                    }
                }

                groupFrom = groupTo;
            }
        }

        static void SelfRule3012(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)
        {
            var groupFrom = first;
            while (groupFrom < end)
            {
                var source = EffectKey.Source(a.Effects[groupFrom].Key);
                var groupTo = groupFrom + 1;
                while (groupTo < end && EffectKey.Source(a.Effects[groupTo].Key) == source)
                {
                    groupTo++;
                }

                for (var r = groupFrom; r < groupTo; r++)
                {
                    var effect = a.Effects[r];
                    var outcome = a.Outcomes[r];
                    var depth = EffectKey.Depth(effect.Key);
                    if (outcome.Survives != 0 && depth <= 3 && (((long)EffectKey.Channel(effect.Key)) == 0L))
                    {
                        if (true)
                        {
                            var channel = 2;
                            var amount = 0;
                            var hits = 1;
                            RuleMath.PerHit(effect, outcome, 0, out amount, out hits);
                            amount = StatLaw.ScaleBy(amount, v.L5[row], 100);
                            if (amount != 0 && hits != 0)
                            {
                                var shaped = SelfAdmit(world, ref v, a, row, self, channel, amount, terms);
                                var index = ChannelIndex(channel);
                                if (index >= 0)
                                {
                                    var at = (index << 6) + row;
                                    a.Totals[at] = RuleMath.Add(a.Totals[at], EffectLaw.Term(shaped, hits));
                                    if (EffectLaw.Term(shaped, hits) != 0L)
                                    {
                                        Assign(a, channel, shaped, target);
                                    }
                                }
                            }
                        }
                    }
                }

                groupFrom = groupTo;
            }
        }

        static void EmitRule3013(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)
        {
            var kind = RuleMath.KindOf(v.L9[row]);
            var groupFrom = first;
            while (groupFrom < end)
            {
                var source = EffectKey.Source(a.Effects[groupFrom].Key);
                var groupTo = groupFrom + 1;
                while (groupTo < end && EffectKey.Source(a.Effects[groupTo].Key) == source)
                {
                    groupTo++;
                }

                for (var r = groupFrom; r < groupTo; r++)
                {
                    var effect = a.Effects[r];
                    var outcome = a.Outcomes[r];
                    var depth = EffectKey.Depth(effect.Key);
                    if (outcome.Survives != 0 && depth <= 0 && (((long)EffectKey.Channel(effect.Key)) == 16L))
                    {
                        if (true)
                        {
                            var channel = 17;
                            var amount = 0;
                            var hits = 1;
                            amount = 1;
                            amount = StatLaw.ScaleBy(amount, v.L1[row], 1);
                            if (amount != 0 && hits != 0)
                            {
                                var to = (source == EffectKey.NoSource ? -1L : (long)source);
                                if ((ulong)to < (ulong)a.Capacity)
                                {
                                    next.Send(EffectKey.Pack((int)to, target, (byte)kind, (byte)channel, (Origin)3, depth + 1), amount, hits);
                                }
                            }
                        }
                    }
                }

                groupFrom = groupTo;
            }
        }

        static void SelfRule3014(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)
        {
            var groupFrom = first;
            while (groupFrom < end)
            {
                var source = EffectKey.Source(a.Effects[groupFrom].Key);
                var groupTo = groupFrom + 1;
                while (groupTo < end && EffectKey.Source(a.Effects[groupTo].Key) == source)
                {
                    groupTo++;
                }

                for (var r = groupFrom; r < groupTo; r++)
                {
                    var effect = a.Effects[r];
                    var outcome = a.Outcomes[r];
                    var depth = EffectKey.Depth(effect.Key);
                    if (outcome.Survives != 0 && depth <= 3 && (In1(((long)EffectKey.Channel(effect.Key))) && (RuleMath.Taken(outcome) > 0L)))
                    {
                        if (true)
                        {
                            var channel = 3;
                            var amount = 0;
                            var hits = 1;
                            amount = 5;
                            hits = effect.Hits;
                            if (amount != 0 && hits != 0)
                            {
                                var shaped = SelfAdmit(world, ref v, a, row, self, channel, amount, terms);
                                var index = ChannelIndex(channel);
                                if (index >= 0)
                                {
                                    var at = (index << 6) + row;
                                    a.Totals[at] = RuleMath.Add(a.Totals[at], EffectLaw.Term(shaped, hits));
                                    if (EffectLaw.Term(shaped, hits) != 0L)
                                    {
                                        Assign(a, channel, shaped, target);
                                    }
                                }
                            }
                        }
                    }
                }

                groupFrom = groupTo;
            }
        }

        static void SelfRule3015(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)
        {
            var groupFrom = first;
            while (groupFrom < end)
            {
                var source = EffectKey.Source(a.Effects[groupFrom].Key);
                var groupTo = groupFrom + 1;
                while (groupTo < end && EffectKey.Source(a.Effects[groupTo].Key) == source)
                {
                    groupTo++;
                }

                var depth = -1;
                for (var r = groupFrom; r < groupTo; r++)
                {
                    var at = EffectKey.Depth(a.Effects[r].Key);
                    depth = a.Outcomes[r].Survives != 0 && at <= 3 && at > depth ? at : depth;
                }

                if (depth >= 0 && (Sum0(a.Effects, a.Outcomes, groupFrom, groupTo, 3) > 0L))
                {
                    if (true)
                    {
                        var channel = 3;
                        var amount = 0;
                        var hits = 1;
                        amount = 5;
                        if (amount != 0 && hits != 0)
                        {
                            var shaped = SelfAdmit(world, ref v, a, row, self, channel, amount, terms);
                            var index = ChannelIndex(channel);
                            if (index >= 0)
                            {
                                var at = (index << 6) + row;
                                a.Totals[at] = RuleMath.Add(a.Totals[at], EffectLaw.Term(shaped, hits));
                                if (EffectLaw.Term(shaped, hits) != 0L)
                                {
                                    Assign(a, channel, shaped, target);
                                }
                            }
                        }
                    }
                }

                groupFrom = groupTo;
            }
        }

        static void SelfRule3020(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)
        {
            var groupFrom = first;
            while (groupFrom < end)
            {
                var source = EffectKey.Source(a.Effects[groupFrom].Key);
                var groupTo = groupFrom + 1;
                while (groupTo < end && EffectKey.Source(a.Effects[groupTo].Key) == source)
                {
                    groupTo++;
                }

                for (var r = groupFrom; r < groupTo; r++)
                {
                    var effect = a.Effects[r];
                    var outcome = a.Outcomes[r];
                    var depth = EffectKey.Depth(effect.Key);
                    if (outcome.Survives != 0 && depth <= 3 && (((long)EffectKey.Channel(effect.Key)) == 20L))
                    {
                        if (true)
                        {
                            var channel = 21;
                            var amount = 0;
                            var hits = 1;
                            amount = StatLaw.Rescale(v.L2[row], 1, 1);
                            if (amount != 0 && hits != 0)
                            {
                                var shaped = SelfAdmit(world, ref v, a, row, self, channel, amount, terms);
                                var index = ChannelIndex(channel);
                                if (index >= 0)
                                {
                                    var at = (index << 6) + row;
                                    a.Totals[at] = RuleMath.Add(a.Totals[at], EffectLaw.Term(shaped, hits));
                                    if (EffectLaw.Term(shaped, hits) != 0L)
                                    {
                                        Assign(a, channel, shaped, target);
                                    }
                                }
                            }
                        }
                    }
                }

                groupFrom = groupTo;
            }
        }

        static void EmitRule3021(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)
        {
            var kind = RuleMath.KindOf(v.L9[row]);
            var groupFrom = first;
            while (groupFrom < end)
            {
                var source = EffectKey.Source(a.Effects[groupFrom].Key);
                var groupTo = groupFrom + 1;
                while (groupTo < end && EffectKey.Source(a.Effects[groupTo].Key) == source)
                {
                    groupTo++;
                }

                for (var r = groupFrom; r < groupTo; r++)
                {
                    var effect = a.Effects[r];
                    var outcome = a.Outcomes[r];
                    var depth = EffectKey.Depth(effect.Key);
                    if (outcome.Survives != 0 && depth <= 0 && (((long)EffectKey.Channel(effect.Key)) == 25L))
                    {
                        if (true)
                        {
                            var channel = 26;
                            var amount = 0;
                            var hits = 1;
                            amount = StatLaw.Rescale(v.L1[row], 1, 1);
                            if (amount != 0 && hits != 0)
                            {
                                var to = (source == EffectKey.NoSource ? -1L : (long)source);
                                if ((ulong)to < (ulong)a.Capacity)
                                {
                                    next.Send(EffectKey.Pack((int)to, target, (byte)kind, (byte)channel, (Origin)3, depth + 1), amount, hits);
                                }
                            }
                        }
                    }
                }

                groupFrom = groupTo;
            }
        }

        static void SelfRule3022(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)
        {
            var groupFrom = first;
            while (groupFrom < end)
            {
                var source = EffectKey.Source(a.Effects[groupFrom].Key);
                var groupTo = groupFrom + 1;
                while (groupTo < end && EffectKey.Source(a.Effects[groupTo].Key) == source)
                {
                    groupTo++;
                }

                for (var r = groupFrom; r < groupTo; r++)
                {
                    var effect = a.Effects[r];
                    var outcome = a.Outcomes[r];
                    var depth = EffectKey.Depth(effect.Key);
                    if (outcome.Survives != 0 && depth <= 3 && (v.M4 & self) != 0ul && (((long)EffectKey.Channel(effect.Key)) == 4L))
                    {
                        if (true)
                        {
                            var channel = 1;
                            var amount = 0;
                            var hits = 1;
                            amount = v.L32[row];
                            if (amount != 0 && hits != 0)
                            {
                                var shaped = SelfAdmit(world, ref v, a, row, self, channel, amount, terms);
                                var index = ChannelIndex(channel);
                                if (index >= 0)
                                {
                                    var at = (index << 6) + row;
                                    a.Totals[at] = RuleMath.Add(a.Totals[at], EffectLaw.Term(shaped, hits));
                                    if (EffectLaw.Term(shaped, hits) != 0L)
                                    {
                                        Assign(a, channel, shaped, target);
                                    }
                                }
                            }
                        }
                    }
                }

                groupFrom = groupTo;
            }
        }

        public static int DeltaTarget(int deltaLane)
        {
            switch (deltaLane)
            {
                case 0: return 8;
                case 1: return 10;
                case 2: return 11;
                case 3: return 13;
                case 4: return 16;
                case 5: return 19;
                case 6: return 20;
                case 7: return 21;
                case 8: return 22;
                case 9: return 23;
                case 10: return 24;
                case 11: return 25;
                case 12: return 28;
                case 13: return 30;
                case 14: return 31;
                case 15: return 32;
                case 16: return 40;
                case 17: return 41;
                default: return -1;
            }
        }

        public static int FloorStat(int deltaLane)
        {
            switch (deltaLane)
            {
                default: return -1;
            }
        }

        public static int CeilingStat(int deltaLane)
        {
            switch (deltaLane)
            {
                case 0: return 0;
                case 7: return 2;
                default: return -1;
            }
        }

        public static void Bounds(in WorldView world, int chunk, int deltaLane, int* floor, int* ceiling)
        {
            switch (deltaLane)
            {
                case 0:
                    LaneMath.Fill(floor, 0);
                    StatLaw.Rescale(Lanes.Ints(world, chunk, 0), 1, 1, ceiling);
                    return;
                case 1:
                    LaneMath.Fill(floor, int.MinValue);
                    LaneMath.Fill(ceiling, int.MaxValue);
                    return;
                case 2:
                    LaneMath.Fill(floor, 0);
                    LaneMath.Fill(ceiling, int.MaxValue);
                    return;
                case 3:
                    LaneMath.Fill(floor, int.MinValue);
                    LaneMath.Fill(ceiling, int.MaxValue);
                    return;
                case 4:
                    LaneMath.Fill(floor, int.MinValue);
                    LaneMath.Fill(ceiling, int.MaxValue);
                    return;
                case 5:
                    LaneMath.Fill(floor, 0);
                    LaneMath.Fill(ceiling, int.MaxValue);
                    return;
                case 6:
                    LaneMath.Fill(floor, int.MinValue);
                    LaneMath.Fill(ceiling, int.MaxValue);
                    return;
                case 7:
                    LaneMath.Fill(floor, 0);
                    StatLaw.Rescale(Lanes.Ints(world, chunk, 2), 1, 1, ceiling);
                    return;
                case 8:
                    LaneMath.Fill(floor, int.MinValue);
                    LaneMath.Fill(ceiling, int.MaxValue);
                    return;
                case 9:
                    LaneMath.Fill(floor, 0);
                    LaneMath.Fill(ceiling, int.MaxValue);
                    return;
                case 10:
                    LaneMath.Fill(floor, int.MinValue);
                    LaneMath.Fill(ceiling, int.MaxValue);
                    return;
                case 11:
                    LaneMath.Fill(floor, int.MinValue);
                    LaneMath.Fill(ceiling, int.MaxValue);
                    return;
                case 12:
                    LaneMath.Fill(floor, int.MinValue);
                    LaneMath.Fill(ceiling, int.MaxValue);
                    return;
                case 13:
                    LaneMath.Fill(floor, int.MinValue);
                    LaneMath.Fill(ceiling, int.MaxValue);
                    return;
                case 14:
                    LaneMath.Fill(floor, 0);
                    LaneMath.Fill(ceiling, 5);
                    return;
                case 15:
                    LaneMath.Fill(floor, int.MinValue);
                    LaneMath.Fill(ceiling, int.MaxValue);
                    return;
                case 16:
                    LaneMath.Fill(floor, int.MinValue);
                    LaneMath.Fill(ceiling, int.MaxValue);
                    return;
                case 17:
                    LaneMath.Fill(floor, 0);
                    LaneMath.Fill(ceiling, 5);
                    return;
                default:
                    LaneMath.Fill(floor, int.MinValue);
                    LaneMath.Fill(ceiling, int.MaxValue);
                    return;
            }
        }

        public static int EventId(int ordinal)
        {
            switch (ordinal)
            {
                case 0: return 1216;
                case 1: return 1217;
                case 2: return 1218;
                default: return -1;
            }
        }

        public static bool Forwards(int template)
        {
            switch (template)
            {
                case 23: return true;
                default: return false;
            }
        }

        public static int Forward(in WorldView world, int slot, int channel)
        {
            var template = ChunkMeta.TemplateOf(Lanes.WordsOf(world, slot >> 6, world.Layout.MetaLane)[ChunkMeta.Template]);
            var via = -1;
            switch (template)
            {
                case 23:
                switch (channel)
                {
                    case 3: via = 10; break;
                }

                break;
                default: break;
            }

            if (via < 0)
            {
                return -1;
            }

            var to = Lanes.Read(world, slot, via);
            return to < 0 ? EffectKey.NoSource : to;
        }

        public static ulong Events(in WorldView world, ref LaneWriter writer, int chunk, ulong recheck, int* scratch, ulong* edges)
        {
            var any = 0ul;
            var dirty = recheck;
            var v = View(world, chunk);

            {
                var state = Lanes.WordsOf(world, chunk, 38)[0];
                var enter = 0ul;
                var stay = 0ul;
                var rows = dirty;
                while (rows != 0ul)
                {
                    var row = Bits.Tzcnt(rows);
                    rows &= rows - 1ul;
                    enter |= (((long)v.L19[row]) > 0L) ? 1ul << row : 0ul;
                    stay |= (((long)v.L19[row]) > 0L) ? 1ul << row : 0ul;
                }

                var next = (state & ~dirty) | (((~state & enter) | (state & stay)) & dirty);
                var edge = state ^ next;
                edges[0] = edge;
                any |= edge;
                if (edge != 0ul)
                {
                    ((ulong*)Lanes.Writable(world, ref writer, chunk, 38))[0] = next;
                }
            }

            {
                var state = Lanes.WordsOf(world, chunk, 38)[1];
                var enter = 0ul;
                var stay = 0ul;
                var rows = dirty;
                while (rows != 0ul)
                {
                    var row = Bits.Tzcnt(rows);
                    rows &= rows - 1ul;
                    enter |= (((long)v.L8[row]) <= 0L) ? 1ul << row : 0ul;
                    stay |= (((long)v.L8[row]) <= 0L) ? 1ul << row : 0ul;
                }

                var next = (state & ~dirty) | (((~state & enter) | (state & stay)) & dirty);
                var edge = state ^ next;
                edges[1] = edge;
                any |= edge;
                if (edge != 0ul)
                {
                    ((ulong*)Lanes.Writable(world, ref writer, chunk, 38))[1] = next;
                }
            }

            {
                var state = Lanes.WordsOf(world, chunk, 38)[2];
                var enter = 0ul;
                var stay = 0ul;
                var rows = dirty;
                while (rows != 0ul)
                {
                    var row = Bits.Tzcnt(rows);
                    rows &= rows - 1ul;
                    enter |= RuleMath.Below(((long)v.L8[row]), ((long)StatLaw.Rescale(v.L0[row], 1, 1)), 30L, 100L) ? 1ul << row : 0ul;
                    stay |= RuleMath.Below(((long)v.L8[row]), ((long)StatLaw.Rescale(v.L0[row], 1, 1)), 40L, 100L) ? 1ul << row : 0ul;
                }

                var next = (state & ~dirty) | (((~state & enter) | (state & stay)) & dirty);
                var edge = state ^ next;
                edges[2] = edge;
                any |= edge;
                if (edge != 0ul)
                {
                    ((ulong*)Lanes.Writable(world, ref writer, chunk, 38))[2] = next;
                }
            }

            return any;
        }

        public static void EdgeEmits(in WorldView world, int chunk, int capacity, ulong* edges, int* scratch, ref EffectWriter next)
        {
            var v = View(world, chunk);
            Masks(world, ref v);

            {
                var rows = edges[1] & Lanes.WordsOf(world, chunk, 38)[1];
                while (rows != 0ul)
                {
                    var row = Bits.Tzcnt(rows);
                    rows &= rows - 1ul;
                    var self = 1ul << row;
                    if ((v.C28 & self) != 0ul)
                    {
                        var slot = (chunk << 6) | row;
                        var kind = RuleMath.KindOf(v.L9[row]);
                        {
                            var amount = 50;
                            var to = (long)slot;
                            if (amount != 0)
                            {
                                if ((ulong)to < (ulong)capacity)
                                {
                                    next.Send(EffectKey.Pack((int)to, slot, (byte)kind, (byte)3, (Origin)3, 0), amount, 1);
                                }
                            }
                        }
                        {
                            var amount = -1;
                            var to = (long)slot;
                            if (amount != 0)
                            {
                                if ((ulong)to < (ulong)capacity)
                                {
                                    next.Send(EffectKey.Pack((int)to, slot, (byte)kind, (byte)14, (Origin)3, 0), amount, 1);
                                }
                            }
                        }
                    }
                }
            }
        }

        static long Sum0(Effect* effects, Outcome* outcomes, int from, int to, int depthLimit)
        {
            var total = 0L;
            for (var r = from; r < to; r++)
            {
                if (outcomes[r].Survives != 0 && EffectKey.Depth(effects[r].Key) <= depthLimit && In1((long)EffectKey.Channel(effects[r].Key)))
                {
                    total = RuleMath.Add(total, RuleMath.Consumed(effects[r], outcomes[r], 1));
                }
            }

            return total;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool In0(long value) => (ulong)value < 64ul && ((0x0000000005600000ul >> (int)value) & 1ul) != 0ul;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool In1(long value) => (ulong)value < 64ul && ((0x0000000000000007ul >> (int)value) & 1ul) != 0ul;

        public static ChunkView View(in WorldView world, int chunk)
        {
            var v = default(ChunkView);
            v.Chunk = chunk;
            v.L0 = Lanes.Ints(world, chunk, 0);
            v.L1 = Lanes.Ints(world, chunk, 1);
            v.L2 = Lanes.Ints(world, chunk, 2);
            v.L3 = Lanes.Ints(world, chunk, 3);
            v.L4 = Lanes.Ints(world, chunk, 4);
            v.L5 = Lanes.Ints(world, chunk, 5);
            v.L6 = Lanes.Ints(world, chunk, 6);
            v.L8 = Lanes.Ints(world, chunk, 8);
            v.L9 = Lanes.Ints(world, chunk, 9);
            v.L10 = Lanes.Ints(world, chunk, 10);
            v.L13 = Lanes.Ints(world, chunk, 13);
            v.L16 = Lanes.Ints(world, chunk, 16);
            v.L17 = Lanes.Ints(world, chunk, 17);
            v.L18 = Lanes.Ints(world, chunk, 18);
            v.L19 = Lanes.Ints(world, chunk, 19);
            v.L21 = Lanes.Ints(world, chunk, 21);
            v.L25 = Lanes.Ints(world, chunk, 25);
            v.L32 = Lanes.Ints(world, chunk, 32);
            return v;
        }

        public static void Masks(in WorldView world, ref ChunkView v)
        {
            v.C12 = Mask.Positive(Lanes.Ints(world, v.Chunk, 12));
            v.C14 = Mask.Positive(Lanes.Ints(world, v.Chunk, 14));
            v.C15 = Mask.Positive(Lanes.Ints(world, v.Chunk, 15));
            v.C16 = Mask.Positive(Lanes.Ints(world, v.Chunk, 16));
            v.C26 = Mask.Positive(Lanes.Ints(world, v.Chunk, 26));
            v.C28 = Mask.Positive(Lanes.Ints(world, v.Chunk, 28));
            v.C29 = Mask.Positive(Lanes.Ints(world, v.Chunk, 29));
            v.C33 = Mask.Positive(Lanes.Ints(world, v.Chunk, 33));
            v.C34 = Mask.Positive(Lanes.Ints(world, v.Chunk, 34));
            v.C35 = Mask.Positive(Lanes.Ints(world, v.Chunk, 35));
            v.C36 = Mask.Positive(Lanes.Ints(world, v.Chunk, 36));
            v.M0 = Mask.Positive(Lanes.Ints(world, v.Chunk, 19));
            v.M1 = Mask.Positive(Lanes.Ints(world, v.Chunk, 23));
            v.M2 = Mask.Positive(Lanes.Ints(world, v.Chunk, 13));
            v.M3 = Mask.Positive(Lanes.Ints(world, v.Chunk, 11));
            v.M4 = Mask.Positive(Lanes.Ints(world, v.Chunk, 32));
        }
    }
}
