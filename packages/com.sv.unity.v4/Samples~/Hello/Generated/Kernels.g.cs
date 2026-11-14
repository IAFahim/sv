using Sv;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Hello.Sv
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
        public int* L2;
        public ulong C3;
        public ulong M0;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Present(in WorldView world, int capacity, long slot) => SourceChunks.Present(world, capacity, slot);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Read(in WorldView world, int capacity, long slot, int lane, int fallback) => SourceChunks.Read(world, capacity, slot, lane, fallback);

        public static bool Level(in WorldView world, int capacity, long slot, int lane, int word, bool fallback) =>
            Present(world, capacity, slot) ? ((Lanes.WordsOf(world, (int)slot >> 6, lane)[word] >> ((int)slot & 63)) & 1ul) != 0ul : fallback;
    }

    public static unsafe class Kernels
    {
        public static readonly int Channels = 3;

        public static readonly int DeltaLanes = 2;

        public static readonly int ScratchLanes = 2;

        public static readonly int TotalLanes = 3;

        public static readonly int RecordBytes = sizeof(Outcome) + sizeof(int);

        public static readonly int EventCount = 1;

        public static readonly int LanesWrittenPerChunk = 3;

        public static int ChannelIndex(int channel) => (uint)channel < 3u ? channel : -1;

        static ulong ChannelBit(int channel) => channel < 64 ? 1ul << channel : ulong.MaxValue;

        public static int DeltaSlotOf(int intrinsic)
        {
            switch (intrinsic)
            {
                case 1: return 0;
                case 2: return 1;
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
                    default: totalled |= Target<Mixed>(world, ref v, a, ref next, target, row, self, first, at, present, terms); break;
                }
            }

            return totalled;
        }

        static ulong Target<TPresent>(in WorldView world, ref ChunkView v, in DecideArgs a, ref EffectWriter next, int target, int row, ulong self, int first, int end, ulong present, ShapeTerm* terms) where TPresent : struct, ILone
        {
            present = default(TPresent).Lone ? default(TPresent).Bits : present;
            var untaken = -1;
            var used2 = 0L;
            var reshaped = ((v.M0 & self) != 0ul && (present & 0x0000000000000003ul) != 0ul);
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

                if ((v.M0 & self) != 0ul && (present & 0x0000000000000003ul) != 0ul)
                {
                    var candidates = 0;
                    for (var r = first; r < end; r++)
                    {
                        var effect = a.Effects[r];
                        if (a.Outcomes[r].Admitted != 0 && (v.M0 & self) != 0ul && In0(((long)EffectKey.Channel(effect.Key))))
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

                    var budget = (long)v.L2[row];
                    for (var k = 0; k < candidates; k++)
                    {
                        var remaining = (budget < 0L ? 0L : budget) - used2;
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
                        used2 += take;
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

            var written = present;

            var nonzero = 0ul;
            var pending = written;
            while (pending != 0ul)
            {
                var channel = Bits.Tzcnt(pending);
                pending &= pending - 1ul;
                var index = ChannelIndex(channel);
                nonzero |= index >= 0 && a.Totals[(index << 6) + row] != 0L ? 1ul << channel : 0ul;
            }

            if ((nonzero & 0x0000000000000007ul) != 0ul || reshaped)
            {
                var delta0 = 0L;
                var delta1 = 0L;

                if ((nonzero & 0x0000000000000007ul) != 0ul)
                {
                    delta0 = RuleMath.Add(delta0, a.Totals[128 + row]);
                    delta0 = RuleMath.Add(delta0, RuleMath.Negate(a.Totals[0 + row]));
                    delta0 = RuleMath.Add(delta0, RuleMath.Negate(a.Totals[64 + row]));
                }

                if ((nonzero & 0x0000000000000000ul) != 0ul)
                {
                }

                delta1 = RuleMath.Add(delta1, -used2);
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
                case 0: break;
                case 1: AdmitChannel1(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 2: break;
                default: break;
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

        static void AdmitChannel1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C3 & self) != 0ul)
            {
                AdmitChannel1Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void AdmitChannel1Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && true)
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
                case 0: break;
                case 1: SelfAdmitChannel1(world, ref v, a, row, self, effect, channel, ref state, terms); break;
                case 2: break;
                default: break;
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

        static void SelfAdmitChannel1(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if ((v.C3 & self) != 0ul)
            {
                SelfAdmitChannel1Group0(world, ref v, a, row, self, effect, channel, ref state, terms);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SelfAdmitChannel1Group0(in WorldView world, ref ChunkView v, in DecideArgs a, int row, ulong self, in Effect effect, int channel, ref GateState state, ShapeTerm* terms)
        {
            if (state.Decided == 0 && true)
            {
                state.Decided = 1;
                state.Admit = 0;
            }
        }

        public static int DeltaTarget(int deltaLane)
        {
            switch (deltaLane)
            {
                case 0: return 1;
                case 1: return 2;
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
                case 0: return 160;
                default: return -1;
            }
        }

        public static bool Forwards(int template)
        {
            switch (template)
            {
                default: return false;
            }
        }

        public static int Forward(in WorldView world, int slot, int channel)
        {
            var template = ChunkMeta.TemplateOf(Lanes.WordsOf(world, slot >> 6, world.Layout.MetaLane)[ChunkMeta.Template]);
            var via = -1;
            switch (template)
            {
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
                var state = Lanes.WordsOf(world, chunk, 5)[0];
                var enter = Mask.Below(Lanes.Ints(world, chunk, 1), Lanes.Ints(world, chunk, 0), 30, 100);
                var stay = Mask.Below(Lanes.Ints(world, chunk, 1), Lanes.Ints(world, chunk, 0), 40, 100);

                var next = (state & ~dirty) | (((~state & enter) | (state & stay)) & dirty);
                var edge = state ^ next;
                edges[0] = edge;
                any |= edge;
                if (edge != 0ul)
                {
                    ((ulong*)Lanes.Writable(world, ref writer, chunk, 5))[0] = next;
                }
            }

            return any;
        }

        public static void EdgeEmits(in WorldView world, int chunk, int capacity, ulong* edges, int* scratch, ref EffectWriter next)
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool In0(long value) => (ulong)value < 64ul && ((0x0000000000000003ul >> (int)value) & 1ul) != 0ul;

        public static ChunkView View(in WorldView world, int chunk)
        {
            var v = default(ChunkView);
            v.Chunk = chunk;
            v.L2 = Lanes.Ints(world, chunk, 2);
            return v;
        }

        public static void Masks(in WorldView world, ref ChunkView v)
        {
            v.C3 = Mask.Positive(Lanes.Ints(world, v.Chunk, 3));
            v.M0 = Mask.Positive(Lanes.Ints(world, v.Chunk, 2));
        }
    }
}
