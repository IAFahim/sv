using System;
using System.Collections.Generic;

namespace Sv.V4
{
    public static class ColumnOpKind
    {
        public const int AddConstant = 0;
        public const int AddLaneTimes = 1;
        public const int AddIfLaneAbove = 2;
        public const int AddIfHealthBelowPercent = 3;
    }

    public struct ColumnOp
    {
        public int Kind;
        public int Channel;
        public int SourceKind;
        public int Executes;
        public int Lane;
        public int Amount;
        public int Threshold;
    }

    public struct GateGroup
    {
        public int Channel;
        public int AdmitShift;
        public int Doubles;
        public long ConstantGain;
        public long ConstantLoss;
        public int TermFirst;
        public int TermCount;
        public int AboveFirst;
        public int AboveCount;
        public int BelowFirst;
        public int BelowCount;
    }

    public struct LaneTerm
    {
        public int Group;
        public int Lane;
        public long Amount;
    }

    public struct LaneTest
    {
        public int Group;
        public int Lane;
        public long Threshold;
        public long Gain;
        public long Loss;
    }

    public static class ColumnPrograms
    {
        public const int AmountLimit = short.MaxValue;
        static readonly int[] ReadableLanes = { Lane.PoisonTicks, Lane.RegenRate, Lane.Converter, Lane.Execute, Lane.RegenCard };

        public static ColumnOp[][] Author(int opsPerEntity)
        {
            var programs = new ColumnOp[Templates.Count][];
            for (var template = 0; template < Templates.Count; template++)
            {
                var state = 0x2545F491u ^ (uint)(template * 2654435761u) | 1u;
                var program = new ColumnOp[opsPerEntity];
                for (var at = 0; at < opsPerEntity; at++)
                {
                    var roll = Next(ref state);
                    var kindRoll = roll % 20u;
                    var kind = kindRoll < 8u ? ColumnOpKind.AddConstant : kindRoll < 14u ? ColumnOpKind.AddLaneTimes : kindRoll < 17u ? ColumnOpKind.AddIfLaneAbove : ColumnOpKind.AddIfHealthBelowPercent;
                    var channel = (int)((roll >> 5) % 5u);
                    var amount = 1 + (int)((roll >> 12) & 7u);
                    program[at] = new ColumnOp
                    {
                        Kind = kind,
                        Channel = channel,
                        SourceKind = (int)((roll >> 9) & 3u),
                        Executes = channel == Channel.Physical ? (int)((roll >> 15) & 1u) : 0,
                        Lane = ReadableLanes[(roll >> 16) % (uint)ReadableLanes.Length],
                        Amount = ((roll >> 29) & 7u) == 0u ? -amount : amount,
                        Threshold = kind == ColumnOpKind.AddIfHealthBelowPercent ? 10 + (int)((roll >> 20) % 40u) : (int)((roll >> 20) & 63u),
                    };
                }

                programs[template] = program;
            }

            return programs;
        }

        public static void Compile(ColumnOp[] program, List<GateGroup> groups, List<LaneTerm> laneTerms, List<LaneTest> laneAbove, List<LaneTest> healthBelow)
        {
            var groupOf = new Dictionary<(int, int, int), int>();
            var terms = new SortedDictionary<(int, int, int), long>();
            var above = new SortedDictionary<(int, int, int), (long, long)>();
            var below = new SortedDictionary<(int, int), (long, long)>();
            foreach (var op in program)
            {
                if (op.Amount < -AmountLimit || op.Amount > AmountLimit)
                {
                    throw new ArgumentOutOfRangeException(nameof(program), "column op amounts are limited to 16 bits so folded sums stay exact");
                }

                var gate = (op.Channel, op.SourceKind, op.Executes);
                if (!groupOf.TryGetValue(gate, out var group))
                {
                    group = groups.Count;
                    groupOf[gate] = group;
                    groups.Add(new GateGroup { Channel = op.Channel, AdmitShift = TargetContext.AdmitShift(op.Channel, op.SourceKind), Doubles = op.Executes });
                }

                var gain = op.Amount > 0 ? op.Amount : 0L;
                var loss = op.Amount < 0 ? op.Amount : 0L;
                switch (op.Kind)
                {
                    case ColumnOpKind.AddConstant:
                        var g = groups[group];
                        g.ConstantGain += gain;
                        g.ConstantLoss += loss;
                        groups[group] = g;
                        break;
                    case ColumnOpKind.AddLaneTimes:
                        var termKey = (group, op.Lane, op.Amount > 0 ? 1 : -1);
                        terms[termKey] = (terms.TryGetValue(termKey, out var sum) ? sum : 0L) + op.Amount;
                        break;
                    case ColumnOpKind.AddIfLaneAbove:
                        var aboveKey = (group, op.Lane, op.Threshold);
                        var (aboveGain, aboveLoss) = above.TryGetValue(aboveKey, out var a) ? a : (0L, 0L);
                        above[aboveKey] = (aboveGain + gain, aboveLoss + loss);
                        break;
                    default:
                        var belowKey = (group, op.Threshold);
                        var (belowGain, belowLoss) = below.TryGetValue(belowKey, out var b) ? b : (0L, 0L);
                        below[belowKey] = (belowGain + gain, belowLoss + loss);
                        break;
                }
            }

            foreach (var pair in terms)
            {
                laneTerms.Add(new LaneTerm { Group = pair.Key.Item1, Lane = pair.Key.Item2, Amount = pair.Value });
            }

            foreach (var pair in above)
            {
                laneAbove.Add(new LaneTest { Group = pair.Key.Item1, Lane = pair.Key.Item2, Threshold = pair.Key.Item3, Gain = pair.Value.Item1, Loss = pair.Value.Item2 });
            }

            foreach (var pair in below)
            {
                healthBelow.Add(new LaneTest { Group = pair.Key.Item1, Lane = Lane.Health, Threshold = pair.Key.Item2, Gain = pair.Value.Item1, Loss = pair.Value.Item2 });
            }

            for (var group = 0; group < groups.Count; group++)
            {
                var g = groups[group];
                (g.TermFirst, g.TermCount) = RangeOf(laneTerms, group, t => t.Group);
                (g.AboveFirst, g.AboveCount) = RangeOf(laneAbove, group, t => t.Group);
                (g.BelowFirst, g.BelowCount) = RangeOf(healthBelow, group, t => t.Group);
                groups[group] = g;
            }
        }

        static (int First, int Count) RangeOf<T>(List<T> entries, int group, Func<T, int> groupOf)
        {
            var first = entries.FindIndex(e => groupOf(e) == group);
            return first < 0 ? (0, 0) : (first, entries.FindLastIndex(e => groupOf(e) == group) - first + 1);
        }

        static uint Next(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }
    }
}
