using System;
using System.Collections.Generic;

namespace Sv.Columns
{
    public sealed class ReferenceWorld
    {
        readonly ColumnPack pack;
        readonly Dictionary<string, int> lanes = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<string, int> events = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly SortedDictionary<int, Entity> entities = new SortedDictionary<int, Entity>();
        readonly List<Sent> sent = new List<Sent>();
        readonly List<ScaleRule> scales = new List<ScaleRule>();

        sealed class Entity
        {
            public int Template;
            public int[] Lanes = Array.Empty<int>();
        }

        readonly struct Sent
        {
            public Sent(int target, int channel, int kind, bool[] sourceHolds, long amount)
            {
                Target = target;
                Channel = channel;
                Kind = kind;
                SourceHolds = sourceHolds;
                Amount = amount;
            }

            public int Target { get; }

            public int Channel { get; }

            public int Kind { get; }

            public bool[] SourceHolds { get; }

            public long Amount { get; }
        }

        public ReferenceWorld(ColumnPack pack)
        {
            this.pack = pack;
            foreach (var lane in pack.Lanes)
            {
                lanes[lane.Name] = lanes.Count;
            }

            lanes[PackLimits.EventsLane] = lanes.Count;
            lanes[PackLimits.EnteredLane] = lanes.Count;
            foreach (var rule in pack.Rules)
            {
                if (rule is EventRule e)
                {
                    events[e.Name] = events.Count;
                }

                if (rule is ScaleRule s)
                {
                    scales.Add(s);
                }
            }
        }

        int KindCount => Math.Max(1, pack.Kinds.Count);

        int Channel(string name) => pack.Channels.IndexOf(name);

        int Kind(string? name) => name == null ? 0 : pack.Kinds.IndexOf(name);

        public void Spawn(int slot, string template)
        {
            var index = pack.Templates.FindIndex(t => t.Name == template);
            var spec = pack.Templates[index];
            var values = new int[lanes.Count];
            for (var l = 0; l < pack.Lanes.Count; l++)
            {
                values[l] = pack.Lanes[l].Default;
            }

            foreach (var pair in spec.Values)
            {
                values[lanes[pair.Key]] = pair.Value;
            }

            foreach (var pair in spec.Cards)
            {
                values[lanes[pack.Cards.Find(c => c.Name == pair.Key)!.Lane]] = pair.Value;
            }

            entities[slot] = new Entity { Template = index, Lanes = values };
        }

        public void Despawn(int slot) => entities.Remove(slot);

        public void Set(int slot, string lane, int value) => entities[slot].Lanes[lanes[lane]] = value;

        public int Get(int slot, string lane) => entities[slot].Lanes[lanes[lane]];

        public bool Event(int slot, string name) => (entities[slot].Lanes[lanes[PackLimits.EventsLane]] >> events[name] & 1) != 0;

        public bool Entered(int slot, string name) => (entities[slot].Lanes[lanes[PackLimits.EnteredLane]] >> events[name] & 1) != 0;

        public void Send(int target, string channel, long amount, int hits = 1)
        {
            var holds = new bool[scales.Count];
            for (var r = 0; r < scales.Count; r++)
            {
                holds[r] = scales[r].SourceWhen == null;
            }

            sent.Add(new Sent(target, Channel(channel), 0, holds, Saturate(amount * hits)));
        }

        public void Send(int target, int source, string channel, long amount, int hits = 1)
        {
            var values = entities.TryGetValue(source, out var from) ? from.Lanes : null;
            var holds = new bool[scales.Count];
            var kind = 0;
            if (values != null)
            {
                for (var r = 0; r < scales.Count; r++)
                {
                    holds[r] = Holds(scales[r].SourceWhen, values);
                }

                if (pack.KindLane != null)
                {
                    var k = values[lanes[pack.KindLane]];
                    kind = k >= 0 && k < KindCount ? k : 0;
                }
            }

            sent.Add(new Sent(target, Channel(channel), kind, holds, Saturate(amount * hits)));
        }

        static long Saturate(long amount) => Math.Max(Record.AmountMin, Math.Min(Record.AmountMax, amount));

        bool Holds(Cond? cond, int[] values)
        {
            if (cond == null)
            {
                return true;
            }

            bool holds;
            switch (cond.Op)
            {
                case CondOp.All:
                    foreach (var part in cond.Parts)
                    {
                        if (!Holds(part, values))
                        {
                            return false;
                        }
                    }

                    return true;
                case CondOp.Event:
                    holds = (values[lanes[PackLimits.EventsLane]] >> events[cond.Lane] & 1) != 0;
                    break;
                case CondOp.Entered:
                    holds = (values[lanes[PackLimits.EnteredLane]] >> events[cond.Lane] & 1) != 0;
                    break;
                case CondOp.Bit:
                    holds = (values[lanes[cond.Lane]] >> cond.Value & 1) != 0;
                    break;
                case CondOp.Below:
                    holds = (long)values[lanes[cond.Lane]] * 100 < (long)values[lanes[cond.Other!]] * cond.Value;
                    break;
                default:
                    long left = values[lanes[cond.Lane]];
                    long right = cond.Other != null ? values[lanes[cond.Other]] : cond.Value;
                    holds = cond.Op switch
                    {
                        CondOp.Gt => left > right,
                        CondOp.Ge => left >= right,
                        CondOp.Lt => left < right,
                        CondOp.Le => left <= right,
                        CondOp.Eq => left == right,
                        _ => left != right,
                    };
                    break;
            }

            return holds != cond.Negated;
        }

        bool Admitted(int channel, int kind, int[] values)
        {
            foreach (var rule in pack.Rules)
            {
                switch (rule)
                {
                    case DenyRule deny:
                        var channelListed = deny.Channels == null || Array.IndexOf(deny.Channels, pack.Channels[channel]) >= 0;
                        var kindListed = deny.Kinds == null || Array.Exists(deny.Kinds, k => Kind(k) == kind);
                        if (channelListed && kindListed && Holds(deny.When, values))
                        {
                            return false;
                        }

                        break;
                    case DenyByMaskRule mask:
                        var bits = values[lanes[mask.Lane]];
                        if ((bits >> (mask.ByKind ? kind : channel) & 1) != 0)
                        {
                            return false;
                        }

                        break;
                }
            }

            return true;
        }

        long Factor(int channel, int[] target, Func<int, bool> sourceHolds)
        {
            var factor = 1L;
            for (var r = 0; r < scales.Count; r++)
            {
                var scale = scales[r];
                if (Array.IndexOf(scale.Channels, pack.Channels[channel]) >= 0 && Holds(scale.When, target) && sourceHolds(r))
                {
                    factor *= scale.Times;
                }
            }

            return factor;
        }

        public void Step()
        {
            var frozen = new Dictionary<int, int[]>();
            foreach (var pair in entities)
            {
                frozen[pair.Key] = (int[])pair.Value.Lanes.Clone();
            }

            var gains = new Dictionary<int, long[]>();
            var losses = new Dictionary<int, long[]>();
            foreach (var slot in entities.Keys)
            {
                gains[slot] = new long[pack.Channels.Count];
                losses[slot] = new long[pack.Channels.Count];
            }

            foreach (var record in sent)
            {
                if (!entities.ContainsKey(record.Target))
                {
                    continue;
                }

                var values = frozen[record.Target];
                var term = Admitted(record.Channel, record.Kind, values) ? record.Amount * Factor(record.Channel, values, r => record.SourceHolds[r]) : 0L;
                Add(gains[record.Target], losses[record.Target], record.Channel, term);
            }

            sent.Clear();
            foreach (var pair in entities)
            {
                var slot = pair.Key;
                var values = frozen[slot];
                var ops = new List<(OpSpec Op, Cond? Card)>();
                foreach (var op in pack.Templates[pair.Value.Template].Ops)
                {
                    ops.Add((op, null));
                }

                foreach (var card in pack.Cards)
                {
                    foreach (var op in card.Ops)
                    {
                        ops.Add((op, Cond.Gt(card.Lane, 0)));
                    }
                }

                foreach (var (op, card) in ops)
                {
                    if (!Holds(card, values) || !Holds(op.When, values))
                    {
                        continue;
                    }

                    var channel = Channel(op.Channel);
                    var multiplier = op.Times != null ? values[lanes[op.Times]] : 1L;
                    var term = Admitted(channel, Kind(op.Kind), values) ? op.Amount * multiplier * Factor(channel, values, r => Holds(scales[r].SourceWhen, values)) : 0L;
                    Add(gains[slot], losses[slot], channel, term);
                }

                Resolve(pair.Value.Lanes, values, gains[slot], losses[slot]);
            }
        }

        static void Add(long[] gain, long[] loss, int channel, long term)
        {
            if (term > 0)
            {
                gain[channel] += term;
            }
            else
            {
                loss[channel] += term;
            }
        }

        void Resolve(int[] values, int[] frozen, long[] gain, long[] loss)
        {
            var wide = new long[values.Length];
            for (var l = 0; l < values.Length; l++)
            {
                wide[l] = values[l];
            }

            foreach (var rule in pack.Rules)
            {
                if (rule is ConvertRule convert && Holds(convert.When, frozen))
                {
                    var from = Channel(convert.From);
                    var moved = gain[from] * convert.Numerator / convert.Denominator;
                    gain[from] -= moved;
                    gain[Channel(convert.To)] += moved;
                }
            }

            foreach (var rule in pack.Rules)
            {
                if (rule is AbsorbRule absorb)
                {
                    var budget = wide[lanes[absorb.Budget]];
                    var pool = 0L;
                    foreach (var name in absorb.Channels)
                    {
                        pool += gain[Channel(name)];
                    }

                    var taken = Math.Max(0L, Math.Min(budget, pool));
                    wide[lanes[absorb.Budget]] = budget - taken;
                    foreach (var name in absorb.Channels)
                    {
                        var drawn = Math.Min(taken, gain[Channel(name)]);
                        gain[Channel(name)] -= drawn;
                        taken -= drawn;
                    }
                }
            }

            foreach (var rule in pack.Rules)
            {
                if (rule is FlowRule flow)
                {
                    foreach (var name in flow.Add)
                    {
                        wide[lanes[flow.Lane]] += gain[Channel(name)] + loss[Channel(name)];
                    }

                    foreach (var name in flow.Subtract)
                    {
                        wide[lanes[flow.Lane]] -= gain[Channel(name)] + loss[Channel(name)];
                    }
                }
            }

            foreach (var rule in pack.Rules)
            {
                if (rule is BoundsRule bounds)
                {
                    var lane = lanes[bounds.Lane];
                    var floor = bounds.Floor.Lane != null ? wide[lanes[bounds.Floor.Lane]] : bounds.Floor.Value;
                    var ceiling = bounds.Ceiling.Lane != null ? wide[lanes[bounds.Ceiling.Lane]] : bounds.Ceiling.Value;
                    var value = wide[lane];
                    value = bounds.Floor.Present ? Math.Max(value, floor) : value;
                    value = bounds.Ceiling.Present ? Math.Min(value, ceiling) : value;
                    wide[lane] = value;
                }
            }

            var eventsLane = lanes[PackLimits.EventsLane];
            var enteredLane = lanes[PackLimits.EnteredLane];
            for (var l = 0; l < values.Length; l++)
            {
                if (l != eventsLane && l != enteredLane)
                {
                    values[l] = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, wide[l]));
                }
            }

            var levels = values[eventsLane];
            var next = 0;
            foreach (var rule in pack.Rules)
            {
                if (rule is EventRule e)
                {
                    var bit = events[e.Name];
                    var level = (levels >> bit & 1) != 0;
                    next |= Holds(level && e.Stay != null ? e.Stay : e.Enter, values) ? 1 << bit : 0;
                }
            }

            values[eventsLane] = next;
            values[enteredLane] = next & ~levels;
        }

        public ulong Digest()
        {
            var hash = 0xCBF29CE484222325ul;
            foreach (var pair in entities)
            {
                hash = Mix(hash, (ulong)pair.Key);
                hash = Mix(hash, (ulong)pair.Value.Template);
                foreach (var value in pair.Value.Lanes)
                {
                    hash = Mix(hash, (ulong)(uint)value);
                }
            }

            return hash;
        }

        static ulong Mix(ulong hash, ulong value)
        {
            hash ^= value;
            hash *= 0x100000001B3ul;
            hash ^= hash >> 29;
            return hash;
        }
    }
}
