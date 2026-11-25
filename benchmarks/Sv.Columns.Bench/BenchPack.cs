using System;
using Sv.Columns;

namespace Sv.Columns.Bench
{
    public static class BenchPack
    {
        public static readonly string[] Stats = { "Strength", "Agility", "Intellect", "Armor", "Rage", "Mana" };

        public static ColumnPack Build(int templates, int opsPerTemplate, ulong seed)
        {
            var pack = new ColumnPack { KindLane = "Kind" };
            pack.Lanes.Add(new LaneSpec("Health", 1_000_000));
            pack.Lanes.Add(new LaneSpec("MaxHealth", 1_000_000));
            pack.Lanes.Add(new LaneSpec("Shield", 5_000));
            pack.Lanes.Add(new LaneSpec("Kind", 0));
            pack.Lanes.Add(new LaneSpec("Execute", 0));
            pack.Lanes.Add(new LaneSpec("Burning", 0));
            foreach (var stat in Stats)
            {
                pack.Lanes.Add(new LaneSpec(stat, 10));
            }

            pack.Channels.AddRange(new[] { "Physical", "Fire", "Poison", "Healing", "Mana" });
            pack.Kinds.AddRange(new[] { "None", "Melee", "Ranged", "Spell" });
            var state = seed | 1;
            int Next(int bound)
            {
                state ^= state << 13;
                state ^= state >> 7;
                state ^= state << 17;
                return (int)(state % (ulong)bound);
            }

            for (var t = 0; t < templates; t++)
            {
                var template = new TemplateSpec("t" + t);
                template.Values["Kind"] = t % 4;
                template.Values["Execute"] = t % 3 == 0 ? 1 : 0;
                template.Values["Burning"] = t % 2;
                foreach (var stat in Stats)
                {
                    template.Values[stat] = 5 + Next(20);
                }

                for (var o = 0; o < opsPerTemplate; o++)
                {
                    var channel = pack.Channels[Next(pack.Channels.Count)];
                    var amount = Next(2) == 0 ? -(1 + Next(30)) : 1 + Next(30);
                    var stat = Stats[Next(Stats.Length)];
                    switch (Next(4))
                    {
                        case 0:
                            template.Ops.Add(new OpSpec(channel, amount));
                            break;
                        case 1:
                            template.Ops.Add(new OpSpec(channel, amount, times: stat));
                            break;
                        case 2:
                            template.Ops.Add(new OpSpec(channel, amount, when: Cond.Gt(stat, 8 + Next(10))));
                            break;
                        default:
                            template.Ops.Add(new OpSpec(channel, amount, when: Cond.Below("Health", "MaxHealth", 20 + Next(60))));
                            break;
                    }
                }

                pack.Templates.Add(template);
            }

            pack.Rules.Add(new DenyRule(new[] { "Poison" }, new[] { "Spell" }, Cond.Gt("Intellect", 20)));
            pack.Rules.Add(new ScaleRule(new[] { "Physical" }, 2, Cond.Below("Health", "MaxHealth", 20), Cond.Gt("Execute", 0)));
            pack.Rules.Add(new ConvertRule("Fire", "Healing", 1, 4, Cond.Gt("Burning", 0)));
            pack.Rules.Add(new AbsorbRule("Shield", "Physical", "Fire"));
            pack.Rules.Add(new FlowRule("Health", new[] { "Healing" }, new[] { "Physical", "Fire", "Poison" }));
            pack.Rules.Add(new FlowRule("Mana", new[] { "Mana" }, Array.Empty<string>()));
            pack.Rules.Add(new BoundsRule("Health", Bound.Of(0), Bound.Of("MaxHealth")));
            pack.Rules.Add(new BoundsRule("Mana", Bound.Of(0), Bound.Of(1000)));
            pack.Rules.Add(new EventRule("Dead", Cond.Le("Health", 0)));
            pack.Rules.Add(new EventRule("Low", Cond.Below("Health", "MaxHealth", 25), Cond.Below("Health", "MaxHealth", 35)));
            return pack;
        }
    }
}
