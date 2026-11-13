using System.Collections.Generic;
using Sv.Rules.Passes;

namespace Sv.Rules.Emit
{
    public sealed partial class KernelEmitter
    {
        bool ChannelBits
        {
            get
            {
                for (var c = 0; c < schema.Channels.Length; c++)
                {
                    if (schema.Channels[c].Id < 0 || schema.Channels[c].Id >= 64)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        bool IdentityChannels
        {
            get
            {
                for (var c = 0; c < schema.Channels.Length; c++)
                {
                    if (schema.Channels[c].Id != c)
                    {
                        return false;
                    }
                }

                return schema.Channels.Length > 0;
            }
        }

        bool TotalsOutliveDecide
        {
            get
            {
                for (var i = 0; i < layers.Events.Count; i++)
                {
                    if (ExprTools.Reads(layers.Events[i].Enter).Totals || ExprTools.Reads(layers.Events[i].Stay).Totals)
                    {
                        return true;
                    }
                }

                for (var i = 0; i < layers.Edges.Count; i++)
                {
                    if (ExprTools.Reads(layers.Edges[i].When.Rest).Totals)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        static bool ChannelMask(long[] channels, out ulong mask)
        {
            mask = 0ul;
            for (var i = 0; i < channels.Length; i++)
            {
                if (channels[i] < 0)
                {
                    continue;
                }

                if (channels[i] >= 64)
                {
                    return false;
                }

                mask |= 1ul << (int)channels[i];
            }

            return true;
        }

        string? Presence(long[]? channels)
        {
            if (!ChannelBits || channels == null || !ChannelMask(channels, out var mask))
            {
                return null;
            }

            return "(present & " + Names.Hex(mask) + ") != 0ul";
        }

        string CallGuard(Condition when, long[]? channels)
        {
            var parts = new List<string>();
            if (when.Guard >= 0)
            {
                parts.Add(Held(when.Guard));
            }

            for (var m = 0; m < when.Masks.Length; m++)
            {
                parts.Add("(v.M" + Text(OptimisePass.MaskIndex(kernel, when.Masks[m])) + " & self) != 0ul");
            }

            var present = Presence(channels);
            if (present != null)
            {
                parts.Add(present);
            }

            return parts.Count == 0 ? "true" : string.Join(" && ", parts);
        }

        long[]? TriggerChannels(EmitIr emit, bool selfPart)
        {
            var rule = emit.Rule;
            if (rule.Per == EmitPer.Source || rule.Per == EmitPer.Frame)
            {
                return AggregateChannels(emit.When.Rest);
            }

            var channels = ExprTools.ChannelsOf(emit.When.Rest);
            var over = new List<long>();
            var acts = false;
            for (var i = 0; i < rule.Actions.Length; i++)
            {
                var action = rule.Actions[i];
                if (rule.IsSameFrameSelf(action) != selfPart)
                {
                    continue;
                }

                if (action.Over.Length == 0)
                {
                    return channels;
                }

                acts = true;
                over.AddRange(action.Over);
            }

            if (!acts)
            {
                return channels;
            }

            var covered = Expression.SortedSet(over.ToArray());
            return channels == null ? covered : Intersect(channels, covered);
        }

        static long[]? AggregateChannels(Expression rest)
        {
            if (rest.Op == Op.BoolConst && rest.Integer == 0)
            {
                return new long[0];
            }

            var parts = ExprTools.Conjuncts(rest);
            long[]? narrowest = null;
            for (var i = 0; i < parts.Length; i++)
            {
                var needed = AggregateNeeds(parts[i]);
                if (needed != null)
                {
                    narrowest = narrowest == null ? needed : Intersect(narrowest, needed);
                }
            }

            return narrowest;
        }

        static long[]? AggregateNeeds(Expression e)
        {
            if (e.Args.Length != 2)
            {
                return null;
            }

            var left = e.Args[0];
            var right = e.Args[1];
            Expression aggregate;
            long bound;
            Op op;
            if (left.Op == Op.Aggregate && right.Op == Op.IntConst)
            {
                aggregate = left;
                bound = right.Integer;
                op = e.Op;
            }
            else if (right.Op == Op.Aggregate && left.Op == Op.IntConst)
            {
                aggregate = right;
                bound = left.Integer;
                op = e.Op == Op.Lt ? Op.Gt : e.Op == Op.Le ? Op.Ge : e.Op == Op.Gt ? Op.Lt : e.Op == Op.Ge ? Op.Le : e.Op;
            }
            else
            {
                return null;
            }

            var zeroFails = (op == Op.Gt && bound >= 0) || (op == Op.Ge && bound > 0) || (op == Op.Ne && bound == 0)
                || (op == Op.Lt && bound <= 0) || (op == Op.Le && bound < 0) || (op == Op.Eq && bound != 0);
            if (!zeroFails || aggregate.Set.Length == 0)
            {
                return null;
            }

            return aggregate.Set;
        }

        static long[] Intersect(long[] a, long[] b)
        {
            var kept = new List<long>();
            for (var i = 0; i < a.Length; i++)
            {
                if (System.Array.BinarySearch(b, a[i]) >= 0)
                {
                    kept.Add(a[i]);
                }
            }

            return kept.ToArray();
        }

        string SelfWrites(EmitIr emit)
        {
            var rule = emit.Rule;
            var mask = 0ul;
            for (var i = 0; i < rule.Actions.Length; i++)
            {
                var action = rule.Actions[i];
                if (!rule.IsSameFrameSelf(action) || action.SameChannel)
                {
                    continue;
                }

                if (action.Channel < 0 || action.Channel >= 64)
                {
                    return "ulong.MaxValue";
                }

                mask |= 1ul << action.Channel;
            }

            return Names.Hex(mask);
        }

        string IntoChannels()
        {
            var mask = 0ul;
            for (var c = 0; c < schema.Channels.Length; c++)
            {
                var into = schema.Channels[c].Assigns ? -1 : schema.Channels[c].Into;
                if (into >= 0 && layers.DeltaIntrinsics.IndexOf(into) >= 0)
                {
                    mask |= 1ul << schema.Channels[c].Id;
                }
            }

            return Names.Hex(mask);
        }

        string FlowChannels(FlowIr flow)
        {
            var mask = 0ul;
            for (var t = 0; t < flow.Rule.Terms.Length; t++)
            {
                var channel = flow.Rule.Terms[t].Channel;
                if (ChannelIndex(channel) >= 0)
                {
                    mask |= 1ul << channel;
                }
            }

            return Names.Hex(mask);
        }
    }
}
