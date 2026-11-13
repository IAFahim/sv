using Sv;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sv.Kernels.Reference
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
        public int Capacity;
    }

    public unsafe struct ChunkView
    {
        public int Chunk;
        public int* L3;
        public int* L15;
        public int* L16;
        public int* L19;
        public ulong C5;
        public ulong C6;
        public ulong C7;
        public ulong C9;
        public ulong C10;
        public ulong C14;
        public ulong C17;
        public ulong C18;
        public ulong C20;
        public ulong M0;
        public ulong M1;
        public ulong M2;
        public ulong M3;
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
        public static readonly int Channels = 5;

        public static readonly int DeltaLanes = 3;

        public static readonly int ScratchLanes = 3;

        public static readonly int TotalLanes = 5;

        public static readonly int RecordBytes = sizeof(Outcome) + sizeof(int);

        public static readonly int EventCount = 1;

        public static readonly int LanesWrittenPerChunk = 4;

        public static int ChannelIndex(int channel) => (uint)channel < 5u ? channel : -1;

        static ulong ChannelBit(int channel) => channel < 64 ? 1ul << channel : ulong.MaxValue;

        public static int DeltaSlotOf(int intrinsic)
        {
            switch (intrinsic)
            {
                case 2: return 0;
                case 16: return 1;
                case 19: return 2;
                default: return -1;
            }
        }

        public interface ILone
        {
            bool Lone { get; }

            int Channel { get; }

            ulong Bits { get; }
        }

        public struct Mixed : ILone
        {
            public bool Lone => false;

            public int Channel => -1;

            public ulong Bits => ulong.MaxValue;
        }

        public struct Lone0 : ILone
        {
            public bool Lone => true;

            public int Channel => 0;

            public ulong Bits => 0x0000000000000001ul;
        }

        public struct Lone1 : ILone
        {
            public bool Lone => true;

            public int Channel => 1;

            public ulong Bits => 0x0000000000000002ul;
        }

        public struct Lone2 : ILone
        {
            public bool Lone => true;

            public int Channel => 2;

            public ulong Bits => 0x0000000000000004ul;
        }

        public struct Lone3 : ILone
        {
            public bool Lone => true;

            public int Channel => 3;

            public ulong Bits => 0x0000000000000008ul;
        }

        public struct Lone4 : ILone
        {
            public bool Lone => true;

            public int Channel => 4;

            public ulong Bits => 0x0000000000000010ul;
        }

        public static ulong Decide(in WorldView world, int chunk, int capacity, Effect* effects, int count, int* scratch, ulong* written, long* totals, byte* records, ref EffectWriter next)
        {
            var v = View(world, chunk);
            Masks(world, ref v);
            var a = new DecideArgs { Effects = effects, Outcomes = (Outcome*)records, Order = (int*)(records + (long)count * sizeof(Outcome)), Totals = totals, Delta = scratch, Written = written, Capacity = capacity };
            var terms = stackalloc ShapeTerm[1];
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
                switch ((present & (present - 1ul)) == 0ul ? Bits.Tzcnt(present) : -1)
                {
                    case 0: totalled |= Target<Lone0>(world, ref v, a, ref next, target, row, self, first, at, present, terms); break;
                    case 1: totalled |= Target<Lone1>(world, ref v, a, ref next, target, row, self, first, at, present, terms); break;
                    case 2: totalled |= Target<Lone2>(world, ref v, a, ref next, target, row, self, first, at, present, terms); break;
                    case 3: totalled |= Target<Lone3>(world, ref v, a, ref next, target, row, self, first, at, present, terms); break;
                    case 4: totalled |= Target<Lone4>(world, ref v, a, ref next, target, row, self, first, at, present, terms); break;
                    default: totalled |= Target<Mixed>(world, ref v, a, ref next, target, row, self, first, at, present, terms); break;
                }
            }

            return totalled;
        }

        static ulong Target<TPresent>(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ulong present, ShapeTerm* terms) where TPresent : struct, ILone
        {
            present = default(TPresent).Lone ? default(TPresent).Bits : present;
            var untaken = -1;
            untaken = (v.C17 & self) != 0ul && untaken < 0 ? 0 : untaken;
            untaken = (v.C18 & self) != 0ul && untaken < 0 ? 0 : untaken;
            var used16 = 0L;
            var used19 = 0L;
            var reshaped = ((v.C20 & self) != 0ul && (present & 0x0000000000000007ul) != 0ul) || ((v.M3 & self) != 0ul && (present & 0x0000000000000007ul) != 0ul);
            for (var r = first; r < end; r++)
            {
                Gate<TPresent>(world, ref v, a, row, self, r, terms);
                if (!reshaped)
                {
                    var channel = default(TPresent).Lone ? default(TPresent).Channel : EffectKey.Channel(a.Effects[r].Key);
                    var survives = RuleMath.Taken(a.Outcomes[r]) != 0L || EffectKey.Depth(a.Effects[r].Key) <= untaken;
                    a.Outcomes[r].Survives = survives ? 1 : 0;
                    if (survives)
                    {
                        var index = ChannelIndex(channel);
                        if (index >= 0)
                        {
                            var at = (index << 6) + row;
                            a.Totals[at] = RuleMath.Add(a.Totals[at], RuleMath.Taken(a.Outcomes[r]));
                        }
                    }
                }
            }

            if (reshaped)
            {

                if ((v.C20 & self) != 0ul && (present & 0x0000000000000007ul) != 0ul)
                {
                    var candidates = 0;
                    for (var r = first; r < end; r++)
                    {
                        var effect = a.Effects[r];
                        if (a.Outcomes[r].Admitted != 0 && In0(((long)EffectKey.Channel(effect.Key))))
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

                if ((v.M3 & self) != 0ul && (present & 0x0000000000000007ul) != 0ul)
                {
                    var candidates = 0;
                    for (var r = first; r < end; r++)
                    {
                        var effect = a.Effects[r];
                        if (a.Outcomes[r].Admitted != 0 && (v.M3 & self) != 0ul && In0(((long)EffectKey.Channel(effect.Key))))
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

                    var budget = (long)v.L19[row];
                    for (var k = 0; k < candidates; k++)
                    {
                        var remaining = (budget < 0L ? 0L : budget) - used19;
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
                        used19 += take;
                    }
                }

                for (var r = first; r < end; r++)
                {
                    var channel = default(TPresent).Lone ? default(TPresent).Channel : EffectKey.Channel(a.Effects[r].Key);
                    var survives = RuleMath.Taken(a.Outcomes[r]) != 0L || EffectKey.Depth(a.Effects[r].Key) <= untaken;
                    a.Outcomes[r].Survives = survives ? 1 : 0;
                    if (survives)
                    {
                        var index = ChannelIndex(channel);
                        if (index >= 0)
                        {
                            var at = (index << 6) + row;
                            a.Totals[at] = RuleMath.Add(a.Totals[at], RuleMath.Taken(a.Outcomes[r]));
                        }
                    }
                }
            }

            if ((v.C6 & self) != 0ul && (present & 0x0000000000000007ul) != 0ul)
            {
                EmitRule4102(world, ref v, a, ref next, target, row, self, first, end, terms);
            }

            if ((v.C17 & self) != 0ul && (present & 0x0000000000000007ul) != 0ul)
            {
                EmitRule4112(world, ref v, a, ref next, target, row, self, first, end, terms);
            }

            if ((v.C18 & self) != 0ul && (present & 0x0000000000000007ul) != 0ul)
            {
                EmitRule4113(world, ref v, a, ref next, target, row, self, first, end, terms);
            }

            var written = present;

            if ((v.C7 & self) != 0ul && (present & 0x0000000000000007ul) != 0ul)
            {
                written |= 0x0000000000000008ul;
                SelfRule4103(world, ref v, a, ref next, target, row, self, first, end, terms);
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

            if ((nonzero & 0x000000000000001Ful) != 0ul || reshaped)
            {
                var delta0 = 0L;
                var delta1 = 0L;
                var delta2 = 0L;

                if ((nonzero & 0x000000000000000Ful) != 0ul)
                {
                    delta0 = RuleMath.Add(delta0, a.Totals[192 + row]);
                    delta0 = RuleMath.Add(delta0, RuleMath.Negate(a.Totals[0 + row]));
                    delta0 = RuleMath.Add(delta0, RuleMath.Negate(a.Totals[64 + row]));
                    delta0 = RuleMath.Add(delta0, RuleMath.Negate(a.Totals[128 + row]));
                }

                if ((nonzero & 0x0000000000000010ul) != 0ul)
                {
                    delta1 = RuleMath.Add(delta1, a.Totals[256 + row]);
                }

                delta1 = RuleMath.Add(delta1, -used16);
                delta2 = RuleMath.Add(delta2, -used19);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Gate<TPresent>(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, int r, ShapeTerm* terms) where TPresent : struct, ILone
        {
            var effect = a.Effects[r];
            var state = default(GateState);
            var channel = default(TPresent).Lone ? default(TPresent).Channel : (int)EffectKey.Channel(effect.Key);
            switch (channel)
            {
                case 0: AdmitChannel0(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 1: AdmitChannel1(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 2: AdmitChannel2(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 3: AdmitChannel3(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 4: AdmitChannel4(world, ref v, a, row, self, effect, channel, ref state, terms); break;
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
            if ((v.C5 & self) != 0ul)
            {
                AdmitChannel0Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            if ((v.C9 & self) != 0ul)
            {
                AdmitChannel0Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel0Group2(world, ref v, a, row, self, effect, channel, ref state, terms);
            if ((v.C10 & self) != 0ul)
            {
                AdmitChannel0Group3(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
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
            if (state.Decided == 0 && true)
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel0Group2(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L15[row]), 0L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel0Group3(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && true)
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C5 & self) != 0ul)
            {
                AdmitChannel1Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel1Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
            if ((v.C10 & self) != 0ul)
            {
                AdmitChannel1Group2(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
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
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L15[row]), 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel1Group2(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && true)
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel2(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C5 & self) != 0ul)
            {
                AdmitChannel2Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel2Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
            if ((v.C10 & self) != 0ul)
            {
                AdmitChannel2Group2(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
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
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L15[row]), 2L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel2Group2(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && true)
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel3(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C5 & self) != 0ul)
            {
                AdmitChannel3Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel3Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
            if ((v.C14 & self) != 0ul)
            {
                AdmitChannel3Group2(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            AdmitChannel3Group3(world, ref v, a, row, self, effect, channel, ref state, terms);
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
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L15[row]), 3L))
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel3Group2(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.M1 & self) != 0ul)
            {
                if (state.Decided == 0)
                {
                    state.Decided = 1;
                    state.Admit = 1;
                }
                terms[state.Terms] = new ShapeTerm(ShapeKind.Multi, 10000);
                state.Terms++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel3Group3(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (v.M2 & self) != 0ul)
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitChannel4(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C5 & self) != 0ul)
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
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L15[row]), 4L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void AdmitAny(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C5 & self) != 0ul)
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
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L15[row]), ((long)EffectKey.Channel(effect.Key))))
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
            if ((v.C9 & self) != 0ul)
            {
                SelfAdmitChannel0Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            SelfAdmitChannel0Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
            if ((v.C10 & self) != 0ul)
            {
                SelfAdmitChannel0Group2(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel0Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && true)
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel0Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L15[row]), 0L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel0Group2(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && true)
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel1Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            if ((v.C10 & self) != 0ul)
            {
                SelfAdmitChannel1Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel1Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L15[row]), 1L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel1Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && true)
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel2(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel2Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            if ((v.C10 & self) != 0ul)
            {
                SelfAdmitChannel2Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel2Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L15[row]), 2L))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel2Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && true)
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void SelfAdmitChannel3(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            SelfAdmitChannel3Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            if ((v.C14 & self) != 0ul)
            {
                SelfAdmitChannel3Group1(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
            SelfAdmitChannel3Group2(world, ref v, a, row, self, effect, channel, ref state, terms);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel3Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L15[row]), 3L))
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel3Group1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.M1 & self) != 0ul)
            {
                if (state.Decided == 0)
                {
                    state.Decided = 1;
                    state.Admit = 1;
                }
                terms[state.Terms] = new ShapeTerm(ShapeKind.Multi, 10000);
                state.Terms++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel3Group2(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && (v.M2 & self) != 0ul)
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
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L15[row]), 4L))
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
            if (state.Decided == 0 && RuleMath.Bit(((long)v.L15[row]), ((long)channel)))
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        static void EmitRule4102(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)
        {
            var kind = RuleMath.KindOf(v.L3[row]);
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
                    if (outcome.Survives != 0 && depth <= 0 && In0(((long)EffectKey.Channel(effect.Key))))
                    {
                        if (true)
                        {
                            var channel = (int)EffectKey.Channel(effect.Key);
                            var amount = 0;
                            var hits = 1;
                            RuleMath.PerHit(effect, outcome, 1, out amount, out hits);
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

        static void SelfRule4103(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)
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
                            }
                        }
                    }
                }

                groupFrom = groupTo;
            }
        }

        static void EmitRule4112(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)
        {
            var kind = RuleMath.KindOf(v.L3[row]);
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
                    if (outcome.Survives != 0 && depth <= 0 && In0(((long)EffectKey.Channel(effect.Key))))
                    {
                        if (true)
                        {
                            var channel = (int)EffectKey.Channel(effect.Key);
                            var amount = 0;
                            var hits = 1;
                            RuleMath.PerHit(effect, outcome, 2, out amount, out hits);
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

        static void EmitRule4113(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ShapeTerm* terms)
        {
            var kind = RuleMath.KindOf(v.L3[row]);
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
                    if (outcome.Survives != 0 && depth <= 0 && In0(((long)EffectKey.Channel(effect.Key))))
                    {
                        if (true)
                        {
                            var channel = (int)EffectKey.Channel(effect.Key);
                            var amount = 0;
                            var hits = 1;
                            RuleMath.PerHit(effect, outcome, 0, out amount, out hits);
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

        public static int DeltaTarget(int deltaLane)
        {
            switch (deltaLane)
            {
                case 0: return 2;
                case 1: return 16;
                case 2: return 19;
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
                    LaneMath.Fill(floor, int.MinValue);
                    LaneMath.Fill(ceiling, int.MaxValue);
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
                case 0: return 256;
                default: return -1;
            }
        }

        public static bool Forwards(int template)
        {
            switch (template)
            {
                case 4: return true;
                case 5: return true;
                default: return false;
            }
        }

        public static int Forward(in WorldView world, int slot, int channel)
        {
            var template = ChunkMeta.TemplateOf(Lanes.WordsOf(world, slot >> 6, world.Layout.MetaLane)[ChunkMeta.Template]);
            var via = -1;
            switch (template)
            {
                case 4:
                switch (channel)
                {
                    case 3: via = 4; break;
                }

                break;
                case 5:
                switch (channel)
                {
                    case 1: via = 4; break;
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

            {
                var state = Lanes.WordsOf(world, chunk, 8)[0];
                var enter = Mask.Below(Lanes.Ints(world, chunk, 2), Lanes.Ints(world, chunk, 0), 30, 100);
                var stay = Mask.Below(Lanes.Ints(world, chunk, 2), Lanes.Ints(world, chunk, 0), 40, 100);

                var next = (state & ~dirty) | (((~state & enter) | (state & stay)) & dirty);
                var edge = state ^ next;
                edges[0] = edge;
                any |= edge;
                if (edge != 0ul)
                {
                    ((ulong*)Lanes.Writable(world, ref writer, chunk, 8))[0] = next;
                }
            }

            return any;
        }

        public static void EdgeEmits(in WorldView world, int chunk, int capacity, ulong* edges, int* scratch, ref EffectWriter next)
        {
            var v = View(world, chunk);
            Masks(world, ref v);

            {
                var rows = edges[0] & Lanes.WordsOf(world, chunk, 8)[0];
                while (rows != 0ul)
                {
                    var row = Bits.Tzcnt(rows);
                    rows &= rows - 1ul;
                    var self = 1ul << row;
                    if (true)
                    {
                        var slot = (chunk << 6) | row;
                        var kind = RuleMath.KindOf(v.L3[row]);
                        {
                            var amount = 1;
                            var to = (long)slot;
                            if (amount != 0)
                            {
                                if ((ulong)to < (ulong)capacity)
                                {
                                    next.Send(EffectKey.Pack((int)to, slot, (byte)kind, (byte)4, (Origin)3, 0), amount, 1);
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
                if (outcomes[r].Survives != 0 && EffectKey.Depth(effects[r].Key) <= depthLimit && In0((long)EffectKey.Channel(effects[r].Key)))
                {
                    total = RuleMath.Add(total, RuleMath.Consumed(effects[r], outcomes[r], 1));
                }
            }

            return total;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool In0(long value) => (ulong)value < 64ul && ((0x0000000000000007ul >> (int)value) & 1ul) != 0ul;

        public static ChunkView View(in WorldView world, int chunk)
        {
            var v = default(ChunkView);
            v.Chunk = chunk;
            v.L3 = Lanes.Ints(world, chunk, 3);
            v.L15 = Lanes.Ints(world, chunk, 15);
            v.L16 = Lanes.Ints(world, chunk, 16);
            v.L19 = Lanes.Ints(world, chunk, 19);
            return v;
        }

        public static void Masks(in WorldView world, ref ChunkView v)
        {
            v.C5 = Mask.Positive(Lanes.Ints(world, v.Chunk, 5));
            v.C6 = Mask.Positive(Lanes.Ints(world, v.Chunk, 6));
            v.C7 = Mask.Positive(Lanes.Ints(world, v.Chunk, 7));
            v.C9 = Mask.Positive(Lanes.Ints(world, v.Chunk, 9));
            v.C10 = Mask.Positive(Lanes.Ints(world, v.Chunk, 10));
            v.C14 = Mask.Positive(Lanes.Ints(world, v.Chunk, 14));
            v.C17 = Mask.Positive(Lanes.Ints(world, v.Chunk, 17));
            v.C18 = Mask.Positive(Lanes.Ints(world, v.Chunk, 18));
            v.C20 = Mask.Positive(Lanes.Ints(world, v.Chunk, 20));
            v.M0 = Mask.Positive(Lanes.Ints(world, v.Chunk, 11));
            v.M1 = v.C14 != 0ul ? Mask.Positive(Lanes.Ints(world, v.Chunk, 13)) : 0ul;
            v.M2 = Mask.Positive(Lanes.Ints(world, v.Chunk, 12));
            v.M3 = Mask.Positive(Lanes.Ints(world, v.Chunk, 19));
        }
    }
}
