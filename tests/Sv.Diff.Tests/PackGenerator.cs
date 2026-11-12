using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sv.Pack;
using Sv.Rules;
using SvScenario = Sv.Pack.Scenario;

namespace DiffTests
{
    public sealed class GeneratorOptions
    {
        public bool CardModifiers;
        public bool StatOps;
        public bool Absorb = true;
    }

    public sealed class GeneratedCase
    {
        public int Seed;
        public string Namespace = string.Empty;
        public string Values = string.Empty;
        public string Rules = string.Empty;
        public string Cards = string.Empty;
        public string Templates = string.Empty;
        public string Scenario = string.Empty;

        public PackSource Source => new PackSource(Values, Rules, Cards, Templates);

        public string Receipt =>
            "seed " + Seed + "\n--- values.sv.json\n" + Values + "\n--- rules.sv.json\n" + Rules + "\n--- cards.sv.json\n" + Cards
            + "\n--- templates.sv.json\n" + Templates + "\n--- scenario.json\n" + Scenario + "\n";
    }

    public sealed class PackGenerator
    {
        static readonly int[] Scales = { 1, 10, 100 };
        static readonly string[] Origins = { "Direct", "Timeline", "Reflected", "Triggered" };
        static readonly string[] Percents = { "0.25", "-0.2", "0.5", "1.0", "-0.5", "0.1" };
        static readonly string[] Shapes = { "Added", "Additive", "Multi", "Cap" };

        readonly Random random;
        readonly GeneratorOptions options;
        readonly int stats;
        readonly int cards;
        readonly int flags;
        readonly int damage;
        readonly int kinds;
        readonly int events;
        readonly int chains;
        readonly int[] statScale;
        readonly List<string> grants = new List<string>();
        readonly List<string> templateNames = new List<string>();
        int ruleId = 1000;

        PackGenerator(int seed, GeneratorOptions options)
        {
            random = new Random(seed);
            this.options = options;
            stats = 1 + random.Next(3);
            cards = 1 + random.Next(3);
            flags = 1 + random.Next(3);
            damage = 3 + random.Next(4);
            kinds = 2 + random.Next(3);
            events = random.Next(3);
            chains = random.Next(3);
            statScale = new int[stats];
            for (var s = 0; s < stats; s++)
            {
                statScale[s] = Scales[random.Next(Scales.Length)];
            }
        }

        public static GeneratedCase Make(int seed, GeneratorOptions options)
        {
            var generator = new PackGenerator(seed, options);
            var made = new GeneratedCase { Seed = seed, Namespace = "Diff.Seed" + seed.ToString(CultureInfo.InvariantCulture) };
            made.Values = generator.Values();
            made.Rules = generator.Rules();
            made.Cards = generator.Cards();
            made.Templates = generator.Templates();
            made.Scenario = generator.ScenarioText();
            return made;
        }

        bool Chance(int percent) => random.Next(100) < percent;

        T Pick<T>(IReadOnlyList<T> items) => items[random.Next(items.Count)];

        static string N(long value) => value.ToString(CultureInfo.InvariantCulture);

        static string Q(string text) => "\"" + text + "\"";

        static string Decimal(long cell, int scale)
        {
            if (scale == 1)
            {
                return N(cell) + ".0";
            }

            var digits = scale == 10 ? 1 : 2;
            var negative = cell < 0;
            var magnitude = Math.Abs(cell);
            var text = N(magnitude / scale) + "." + N(magnitude % scale).PadLeft(digits, '0');
            return negative ? "-" + text : text;
        }

        string Damage(int index) => "C" + N(index);

        string RandomDamage() => Damage(random.Next(damage));

        int HealthLane => stats;

        int FirstCardLane => stats + 3;

        int FirstFlagLane => FirstCardLane + cards;

        int BudgetLane => FirstFlagLane + flags;

        int MaskLane => BudgetLane + 1;

        int EventLane => MaskLane + 1;

        string Values()
        {
            var rows = new List<string>();
            for (var s = 0; s < stats; s++)
            {
                var cell = s == 0 ? (50 + random.Next(250)) * (long)statScale[s] + random.Next(statScale[s]) : (1 + random.Next(20)) * (long)statScale[s];
                rows.Add("{\"id\":" + N(s) + ",\"name\":\"S" + N(s) + "\",\"class\":\"stat\",\"default\":" + Decimal(cell, statScale[s]) + ",\"scale\":" + N(statScale[s]) + "}");
            }

            rows.Add("{\"id\":" + N(HealthLane) + ",\"name\":\"Health\",\"class\":\"intrinsic\",\"default\":" + N(50 + random.Next(150)) + "}");
            rows.Add("{\"id\":" + N(HealthLane + 1) + ",\"name\":\"Kind\",\"class\":\"intrinsic\",\"default\":0,\"role\":\"kind\"}");
            rows.Add("{\"id\":" + N(HealthLane + 2) + ",\"name\":\"Owner\",\"class\":\"intrinsic\",\"default\":-1,\"role\":\"owner\"}");
            for (var c = 0; c < cards; c++)
            {
                rows.Add("{\"id\":" + N(FirstCardLane + c) + ",\"name\":\"Card" + N(c) + "\",\"class\":\"intrinsic\",\"default\":0}");
            }

            for (var f = 0; f < flags; f++)
            {
                rows.Add("{\"id\":" + N(FirstFlagLane + f) + ",\"name\":\"Flag" + N(f) + "\",\"class\":\"intrinsic\",\"default\":" + N(random.Next(2)) + "}");
            }

            rows.Add("{\"id\":" + N(BudgetLane) + ",\"name\":\"Budget\",\"class\":\"intrinsic\",\"default\":" + N(random.Next(3) * 5) + "}");
            rows.Add("{\"id\":" + N(MaskLane) + ",\"name\":\"Mask\",\"class\":\"intrinsic\",\"default\":0}");
            for (var e = 0; e < events; e++)
            {
                rows.Add("{\"lane\":" + N(EventLane) + ",\"word\":" + N(e) + ",\"name\":\"E" + N(e) + "\",\"class\":\"event\",\"default\":" + (Chance(20) ? "true" : "false") + "}");
            }

            var channel = 0;
            for (var d = 0; d < damage; d++)
            {
                rows.Add("{\"id\":" + N(channel++) + ",\"name\":\"" + Damage(d) + "\",\"class\":\"channel\"" + (Chance(30) ? ",\"timeline\":true" : string.Empty) + "}");
            }

            void Grant(string name, string into)
            {
                grants.Add(name);
                rows.Add("{\"id\":" + N(channel++) + ",\"name\":\"" + name + "\",\"class\":\"channel\",\"into\":\"" + into + "\"}");
            }

            for (var c = 0; c < cards; c++)
            {
                Grant("GiveCard" + N(c), "Card" + N(c));
            }

            for (var f = 0; f < flags; f++)
            {
                Grant("GiveFlag" + N(f), "Flag" + N(f));
            }

            Grant("GiveBudget", "Budget");
            Grant("GiveMask", "Mask");
            Grant("SetOwner", "Owner");
            for (var k = 0; k < kinds; k++)
            {
                rows.Add("{\"id\":" + N(k) + ",\"name\":\"K" + N(k) + "\",\"class\":\"kind\"}");
            }

            var nextLane = events > 0 ? EventLane + 1 : EventLane;
            return "{\"schema\":1,\"nextLane\":" + N(nextLane) + ",\"rows\":[\n" + string.Join(",\n", rows) + "\n]}";
        }

        string RecordAtom()
        {
            switch (random.Next(7))
            {
                case 0: return "{\"eq\":[\"record.channel\",\"" + RandomDamage() + "\"]}";
                case 1: return "{\"in\":[\"record.channel\",[\"" + RandomDamage() + "\",\"" + RandomDamage() + "\"]]}";
                case 2: return "{\"eq\":[\"record.kind\",\"K" + N(random.Next(kinds)) + "\"]}";
                case 3: return "{\"gt\":[\"record.amount\"," + N(random.Next(40)) + "]}";
                case 4: return "{\"eq\":[\"record.depth\"," + N(random.Next(2)) + "]}";
                case 5: return "{\"eq\":[\"record.origin\",\"" + Pick(Origins) + "\"]}";
                default: return "{\"bit\":[\"self.Mask\",\"record.channel\"]}";
            }
        }

        string SelfAtom(bool chainsAllowed, int chainLimit)
        {
            var choice = random.Next(chainsAllowed && chainLimit > 0 ? 7 : 5);
            switch (choice)
            {
                case 0: return "{\"gt\":[\"self.Flag" + N(random.Next(flags)) + "\",0]}";
                case 1: return "{\"ge\":[\"self.Flag" + N(random.Next(flags)) + "\",2]}";
                case 2: return "{\"below\":[\"Health\",\"S0\"," + N(20 + random.Next(60)) + ",100]}";
                case 3: return "{\"held\":\"card" + N(random.Next(cards)) + "\"}";
                case 4: return "{\"gt\":[\"self.S" + N(random.Next(stats)) + "\"," + N(random.Next(30)) + "]}";
                case 5: return "{\"chain\":\"chain" + N(random.Next(chainLimit)) + "\"}";
                default: return "{\"any\":\"tag" + N(random.Next(chainLimit)) + "\"}";
            }
        }

        string OtherRowAtom()
        {
            switch (random.Next(3))
            {
                case 0: return "{\"gt\":[\"source.Flag" + N(random.Next(flags)) + "\",0]}";
                case 1: return "{\"eq\":[\"source.Kind\"," + N(random.Next(kinds)) + "]}";
                default: return "{\"gt\":[\"owner.Health\"," + N(20 + random.Next(80)) + "]}";
            }
        }

        string AggregateAtom()
        {
            var measure = Pick(new[] { "taken", "offered", "notTaken" });
            var list = Chance(30) ? "[]" : "[\"" + RandomDamage() + "\",\"" + RandomDamage() + "\"]";
            return "{\"gt\":[{\"" + measure + "\":" + list + "}," + N(random.Next(20)) + "]}";
        }

        string Condition(Func<string> atom, int depth)
        {
            if (depth <= 0 || Chance(55))
            {
                return atom();
            }

            switch (random.Next(3))
            {
                case 0: return "{\"and\":[" + Condition(atom, depth - 1) + "," + Condition(atom, depth - 1) + "]}";
                case 1: return "{\"or\":[" + Condition(atom, depth - 1) + "," + Condition(atom, depth - 1) + "]}";
                default: return "{\"not\":" + Condition(atom, depth - 1) + "}";
            }
        }

        string CardKey() => Chance(40) ? ",\"card\":\"card" + N(random.Next(cards)) + "\"" : string.Empty;

        string Rule(string body, string? name = null)
        {
            ruleId++;
            return "{\"id\":" + N(ruleId) + ",\"name\":\"" + (name ?? "rule" + N(ruleId)) + "\"," + body + "}";
        }

        string Rules()
        {
            var rows = new List<string>();
            var add = new List<string>();
            var subtract = new List<string>();
            for (var d = 0; d < damage; d++)
            {
                var roll = random.Next(10);
                (roll < 6 || d == 0 ? subtract : roll < 9 ? add : new List<string>()).Add(Q(Damage(d)));
            }

            var flowTerms = new List<string> { "{\"subtract\":[\"Health\"," + string.Join(",", subtract) + "]}" };
            if (add.Count > 0)
            {
                flowTerms.Add("{\"add\":[\"Health\"," + string.Join(",", add) + "]}");
            }

            rows.Add(Rule("\"layer\":\"flow\",\"then\":[" + string.Join(",", flowTerms) + "]"));
            if (Chance(50))
            {
                rows.Add(Rule("\"layer\":\"flow\"" + CardKey() + ",\"when\":" + Condition(() => SelfAtom(false, 0), 1)
                    + ",\"then\":[{\"add\":[\"Flag0\",\"" + RandomDamage() + "\"]}]"));
            }

            var floor = Chance(70) ? ",\"floor\":0" : Chance(50) ? ",\"floor\":-50" : string.Empty;
            var ceiling = Chance(60) ? ",\"ceiling\":{\"stat\":\"S0\"}" : Chance(50) ? ",\"ceiling\":" + N(150 + random.Next(250)) : string.Empty;
            rows.Add(Rule("\"layer\":\"fold\",\"value\":\"Health\"" + floor + ceiling));
            if (Chance(50))
            {
                rows.Add(Rule("\"layer\":\"fold\",\"value\":\"Flag0\",\"floor\":0,\"ceiling\":5"));
            }

            for (var c = 0; c < chains; c++)
            {
                var limit = c;
                rows.Add(Rule("\"layer\":\"chain\",\"tags\":[\"tag" + N(c) + "\"],\"when\":" + Condition(() => SelfAtom(true, limit), 1), "chain" + N(c)));
            }

            var priorities = new List<int>();
            for (var p = 1; p <= 100; p++)
            {
                priorities.Add(p);
            }

            var gates = random.Next(7);
            for (var g = 0; g < gates; g++)
            {
                var priority = priorities[random.Next(priorities.Count)];
                priorities.Remove(priority);
                var when = Condition(() => random.Next(4) switch
                {
                    0 => SelfAtom(true, chains),
                    1 => OtherRowAtom(),
                    _ => RecordAtom(),
                }, 2);
                rows.Add(Rule("\"layer\":\"gate\"" + CardKey() + ",\"priority\":" + N(priority) + ",\"when\":" + when + ",\"then\":[" + GateEffects() + "]"));
            }

            var emits = random.Next(5);
            for (var e = 0; e < emits; e++)
            {
                rows.Add(Emit());
            }

            for (var e = 0; e < events; e++)
            {
                var enter = Condition(() => SelfAtom(false, 0), 1);
                var stay = Chance(50) ? ",\"stay\":" + Condition(() => SelfAtom(false, 0), 1) : string.Empty;
                rows.Add(Rule("\"layer\":\"event\",\"event\":\"E" + N(e) + "\",\"enter\":" + enter + stay));
                if (Chance(60))
                {
                    var to = Chance(70) ? "Self" : "Owner";
                    var channel = Chance(50) ? RandomDamage() : Pick(grants);
                    rows.Add(Rule("\"layer\":\"edge\",\"event\":\"E" + N(e) + "\",\"edge\":\"" + (Chance(70) ? "enter" : "leave") + "\""
                        + CardKey() + ",\"then\":[{\"emit\":{\"to\":\"" + to + "\",\"channel\":\"" + channel + "\",\"amount\":" + N(1 + random.Next(9)) + "}}]"));
                }
            }

            return "{\"schema\":1,\"rows\":[\n" + string.Join(",\n", rows) + "\n]}";
        }

        string GateEffects()
        {
            var roll = random.Next(100);
            if (roll < 30)
            {
                return "{\"deny\":{}}";
            }

            if (roll < 42)
            {
                return "{\"allow\":{}}";
            }

            if (roll < 85 || !options.Absorb)
            {
                var effects = new List<string>();
                if (roll < 62)
                {
                    effects.Add("{\"allow\":{}}");
                }

                var count = 1 + random.Next(2);
                for (var i = 0; i < count; i++)
                {
                    switch (random.Next(4))
                    {
                        case 0: effects.Add("{\"added\":" + N(random.Next(16) - 5) + "}"); break;
                        case 1: effects.Add("{\"additive\":" + Pick(Percents) + "}"); break;
                        case 2: effects.Add("{\"multi\":" + Pick(Percents) + "}"); break;
                        default: effects.Add("{\"cap\":" + N(5 + random.Next(36)) + "}"); break;
                    }
                }

                return string.Join(",", effects);
            }

            return "{\"absorb\":{\"budget\":\"Budget\",\"per\":\"" + (Chance(50) ? "Hit" : "Amount") + "\",\"order\":\"" + (Chance(50) ? "BySource" : "ByAmountDescending") + "\"}}";
        }

        string Emit()
        {
            var perRoll = random.Next(100);
            var per = perRoll < 40 ? "Record" : perRoll < 55 ? "Hit" : perRoll < 80 ? "Source" : "Frame";
            var single = per == "Record" || per == "Hit";
            var nextFrame = Chance(15);
            var actions = new List<string>();
            var anyOther = nextFrame;
            var count = 1 + random.Next(2);
            for (var i = 0; i < count; i++)
            {
                var to = per == "Frame" ? (Chance(60) ? "Self" : "Owner") : Pick(new[] { "Source", "Source", "Self", "Owner" });
                anyOther |= to != "Self";
                var channel = single && Chance(40) ? "same" : Chance(80) ? RandomDamage() : Pick(grants);
                var amount = random.Next(4) switch
                {
                    0 => "\"Offered\"",
                    1 => "\"Taken\"",
                    2 => "\"NotTaken\"",
                    _ => N(1 + random.Next(10)),
                };
                var scale = Chance(15) ? ",\"scale\":\"S" + N(random.Next(stats)) + "\"" : string.Empty;
                actions.Add("{\"emit\":{\"to\":\"" + to + "\",\"channel\":\"" + channel + "\",\"amount\":" + amount + scale + ",\"per\":\"" + per + "\"}}");
            }

            Func<string> atom = per switch
            {
                "Record" or "Hit" => () => random.Next(4) switch { 0 => SelfAtom(true, chains), 1 => OtherRowAtom(), _ => RecordAtom() },
                "Source" => () => random.Next(3) switch { 0 => SelfAtom(true, chains), 1 => OtherRowAtom(), _ => AggregateAtom() },
                _ => () => Chance(50) ? SelfAtom(true, chains) : AggregateAtom(),
            };
            var depth = anyOther ? ",\"depthLimit\":" + N(random.Next(3)) : Chance(30) ? ",\"depthLimit\":" + N(random.Next(4)) : string.Empty;
            return Rule("\"layer\":\"emit\"" + CardKey() + depth + (nextFrame ? ",\"nextFrame\":true" : string.Empty) + ",\"when\":" + Condition(atom, 1) + ",\"then\":[" + string.Join(",", actions) + "]");
        }

        string Cards()
        {
            var rows = new List<string>();
            for (var c = 0; c < cards; c++)
            {
                var modifiers = string.Empty;
                if (options.CardModifiers && Chance(70))
                {
                    var stat = random.Next(stats);
                    var modifier = random.Next(4) switch
                    {
                        0 => "\"added\":" + Decimal((1 + random.Next(20)) * (long)statScale[stat], statScale[stat]),
                        1 => "\"additive\":" + Pick(Percents),
                        2 => "\"multi\":" + Pick(Percents),
                        _ => "\"cap\":" + Decimal((5 + random.Next(100)) * (long)statScale[stat], statScale[stat]),
                    };
                    modifiers = ",\"modifiers\":[{\"stat\":\"S" + N(stat) + "\"," + modifier + "}]";
                }

                rows.Add("{\"id\":" + N(c + 1) + ",\"name\":\"card" + N(c) + "\",\"value\":\"Card" + N(c) + "\"" + modifiers + "}");
            }

            return "{\"schema\":1,\"rows\":[\n" + string.Join(",\n", rows) + "\n]}";
        }

        string Templates()
        {
            var rows = new List<string>();
            void Add(string name, string body)
            {
                templateNames.Add(name);
                rows.Add("{\"id\":" + N(rows.Count + 1) + ",\"name\":\"" + name + "\"" + body + "}");
            }

            Add("base", string.Empty);
            Add("carded", ",\"values\":{\"Flag0\":" + N(1 + random.Next(3)) + ",\"S0\":" + Decimal((40 + random.Next(200)) * (long)statScale[0], statScale[0]) + "},\"cards\":{\"card0\":1}");
            Add("part", ",\"forwards\":[{\"channel\":\"" + RandomDamage() + "\",\"to\":\"Owner\"}]");
            Add("kinded", ",\"values\":{\"Kind\":1,\"Health\":" + N(30 + random.Next(100)) + ",\"Mask\":" + N(random.Next(8)) + "}");
            if (kinds > 2)
            {
                Add("kinded2", ",\"values\":{\"Kind\":2,\"Budget\":" + N(random.Next(20)) + "}");
            }

            return "{\"schema\":1,\"rows\":[\n" + string.Join(",\n", rows) + "\n]}";
        }

        string ScenarioText()
        {
            var frames = 8 + random.Next(7);
            var count = 3 + random.Next(6);
            var plan = new SvScenario();
            var entities = new List<string>();
            for (var e = 0; e < count; e++)
            {
                var template = random.Next(templateNames.Count);
                var frame = e > 1 && Chance(25) ? 1 + random.Next(frames / 2) : 0;
                plan.Entities.Add(new ScenarioEntity { Name = "E" + N(e), TemplateIndex = template, Frame = frame });
                entities.Add("{\"name\":\"E" + N(e) + "\",\"template\":\"" + templateNames[template] + "\"" + (frame > 0 ? ",\"frame\":" + N(frame) : string.Empty) + "}");
            }

            SlotPlanner.Plan(plan);
            var sends = new List<string>();
            for (var e = 0; e < count; e++)
            {
                if (templateNames[plan.Entities[e].TemplateIndex] == "part" || Chance(20))
                {
                    var owner = random.Next(count);
                    sends.Add("{\"frame\":" + N(plan.Entities[e].Frame) + ",\"target\":\"E" + N(e) + "\",\"channel\":\"SetOwner\",\"amount\":" + N(plan.Entities[owner].Slot + 1) + "}");
                }
            }

            for (var frame = 0; frame < frames; frame++)
            {
                var perFrame = random.Next(7);
                for (var s = 0; s < perFrame; s++)
                {
                    sends.Add(Send(frame, count));
                }
            }

            var kills = new List<string>();
            var killCount = random.Next(3);
            for (var k = 0; k < killCount; k++)
            {
                kills.Add("{\"frame\":" + N(1 + random.Next(frames - 1)) + ",\"entity\":\"E" + N(random.Next(count)) + "\"}");
            }

            var modifiers = new List<string>();
            if (options.StatOps)
            {
                var opCount = 1 + random.Next(5);
                var added = new List<(int Frame, int Entity, int Source)>();
                for (var m = 0; m < opCount; m++)
                {
                    if (added.Count > 0 && Chance(30))
                    {
                        var earlier = Pick(added);
                        added.Remove(earlier);
                        modifiers.Add("{\"frame\":" + N(earlier.Frame + 1 + random.Next(frames - earlier.Frame)) + ",\"entity\":\"E" + N(earlier.Entity) + "\",\"stat\":\"S0\",\"op\":\"remove\",\"source\":" + N(earlier.Source) + "}");
                        continue;
                    }

                    var shape = Pick(Shapes);
                    var stat = random.Next(stats);
                    var value = shape == "Additive" || shape == "Multi" ? (random.Next(41) - 20) * 500 : (1 + random.Next(30)) * statScale[stat];
                    var frame = random.Next(frames);
                    var entity = random.Next(count);
                    added.Add((frame, entity, m + 1));
                    modifiers.Add("{\"frame\":" + N(frame) + ",\"entity\":\"E" + N(entity) + "\",\"stat\":\"S" + N(stat) + "\",\"shape\":\"" + shape + "\",\"value\":" + N(value) + ",\"source\":" + N(m + 1) + "}");
                }
            }

            var text = new StringBuilder();
            text.Append("{\"schema\":1,\"name\":\"generated\",\"frames\":").Append(N(frames)).Append(",\n\"entities\":[\n").Append(string.Join(",\n", entities)).Append("],\n");
            text.Append("\"sends\":[\n").Append(string.Join(",\n", sends)).Append("],\n");
            text.Append("\"kills\":[").Append(string.Join(",", kills)).Append("],\n");
            text.Append("\"modifiers\":[").Append(string.Join(",\n", modifiers)).Append("]}");
            return text.ToString();
        }

        string Send(int frame, int count)
        {
            var text = new StringBuilder();
            text.Append("{\"frame\":").Append(N(frame)).Append(",\"target\":\"E").Append(N(random.Next(count))).Append('"');
            if (Chance(75))
            {
                text.Append(",\"source\":\"E").Append(N(random.Next(count))).Append('"');
            }

            var grant = Chance(25);
            var channel = grant ? Pick(grants) : RandomDamage();
            int amount;
            if (grant)
            {
                amount = channel == "GiveMask" ? random.Next(8) : channel == "GiveBudget" ? 1 + random.Next(30) : random.Next(5) - 1;
                amount = channel == "SetOwner" ? random.Next(3) - 1 : amount;
            }
            else
            {
                var roll = random.Next(100);
                amount = roll < 10 ? -1 - random.Next(20) : roll < 13 ? 1_000_000_000 : 1 + random.Next(60);
            }

            text.Append(",\"channel\":\"").Append(channel).Append("\",\"amount\":").Append(N(amount));
            var hitRoll = random.Next(100);
            if (hitRoll >= 70)
            {
                text.Append(",\"hits\":").Append(N(hitRoll >= 97 ? 100000 : 2 + random.Next(3)));
            }

            if (Chance(15))
            {
                text.Append(",\"kind\":\"K").Append(N(random.Next(kinds))).Append('"');
            }

            if (Chance(20))
            {
                text.Append(",\"origin\":\"Timeline\"");
            }

            return text.Append('}').ToString();
        }
    }
}
