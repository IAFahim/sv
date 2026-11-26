namespace Sv.Columns.Tests;

public sealed class Dice
{
    ulong state;

    public Dice(ulong seed)
    {
        state = seed * 0x9E3779B97F4A7C15ul | 1ul;
    }

    public int Next(int below)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return (int)(state % (ulong)below);
    }

    public int Range(int from, int to) => from + Next(to - from + 1);

    public bool Chance(int percent) => Next(100) < percent;

    public T Pick<T>(IReadOnlyList<T> items) => items[Next(items.Count)];
}

public static class RandomPacks
{
    public static ColumnPack Build(ulong seed)
    {
        var dice = new Dice(seed);
        var pack = new ColumnPack();
        var laneCount = dice.Range(4, 12);
        for (var l = 0; l < laneCount; l++)
        {
            pack.Lanes.Add(new LaneSpec("L" + l, dice.Range(-5, 60)));
        }

        var lanes = pack.Lanes.Select(l => l.Name).ToList();
        var channelCount = dice.Range(1, 8);
        for (var c = 0; c < channelCount; c++)
        {
            pack.Channels.Add("C" + c);
        }

        var kindCount = dice.Range(1, 4);
        for (var k = 0; k < kindCount; k++)
        {
            pack.Kinds.Add("K" + k);
        }

        if (dice.Chance(70))
        {
            pack.KindLane = dice.Pick(lanes);
        }

        var eventNames = new List<string>();
        var eventCount = dice.Range(0, 3);
        for (var e = 0; e < eventCount; e++)
        {
            eventNames.Add("E" + e);
        }

        Cond Atom()
        {
            var roll = dice.Next(eventNames.Count > 0 ? 11 : 9);
            Cond atom = roll switch
            {
                < 4 => Cond.Compare((CondOp)dice.Next(6), dice.Pick(lanes), dice.Range(-3, 50)),
                < 6 => Cond.Compare((CondOp)dice.Next(6), dice.Pick(lanes), dice.Pick(lanes)),
                7 => Cond.Below(dice.Pick(lanes), dice.Pick(lanes), dice.Range(0, 100)),
                8 => Cond.Bit(dice.Pick(lanes), dice.Range(0, 5)),
                9 => Cond.Event(dice.Pick(eventNames)),
                10 => Cond.Entered(dice.Pick(eventNames)),
                _ => Cond.Gt(dice.Pick(lanes), dice.Range(0, 30)),
            };
            return dice.Chance(20) ? Cond.Not(atom) : atom;
        }

        Cond? Maybe(int percent)
        {
            if (!dice.Chance(percent))
            {
                return null;
            }

            var count = dice.Range(1, 3);
            return count == 1 ? Atom() : Cond.All(Enumerable.Range(0, count).Select(_ => Atom()).ToArray());
        }

        OpSpec Op() => new OpSpec(
            dice.Pick(pack.Channels),
            dice.Chance(20) ? -dice.Range(0, 9) : dice.Range(0, 9),
            dice.Chance(40) ? dice.Pick(lanes) : null,
            Maybe(40),
            dice.Chance(50) ? dice.Pick(pack.Kinds) : null);

        var cardCount = dice.Range(0, 3);
        for (var c = 0; c < cardCount; c++)
        {
            var card = new CardSpec("card" + c, dice.Pick(lanes));
            for (var o = dice.Range(0, 4); o > 0; o--)
            {
                card.Ops.Add(Op());
            }

            pack.Cards.Add(card);
        }

        var templateCount = dice.Range(1, 4);
        for (var t = 0; t < templateCount; t++)
        {
            var template = new TemplateSpec("T" + t);
            foreach (var lane in lanes)
            {
                if (dice.Chance(30))
                {
                    template.Values[lane] = dice.Range(-5, 80);
                }
            }

            foreach (var card in pack.Cards)
            {
                if (dice.Chance(40))
                {
                    template.Cards[card.Name] = dice.Range(0, 3);
                }
            }

            for (var o = dice.Range(0, 30); o > 0; o--)
            {
                template.Ops.Add(Op());
            }

            pack.Templates.Add(template);
        }

        string[] SomeChannels() => pack.Channels.Where(_ => dice.Chance(50)).DefaultIfEmpty(pack.Channels[0]).ToArray();

        for (var r = dice.Range(0, 3); r > 0; r--)
        {
            pack.Rules.Add(new DenyRule(dice.Chance(50) ? SomeChannels() : null, dice.Chance(50) ? pack.Kinds.Where(_ => dice.Chance(50)).DefaultIfEmpty(pack.Kinds[0]).ToArray() : null, Maybe(80)));
        }

        if (dice.Chance(40))
        {
            pack.Rules.Add(new DenyByMaskRule(dice.Pick(lanes), dice.Chance(50)));
        }

        for (var r = dice.Range(0, 3); r > 0; r--)
        {
            pack.Rules.Add(new ScaleRule(SomeChannels(), dice.Range(0, 2), Maybe(60), Maybe(50)));
        }

        if (channelCount > 1)
        {
            for (var r = dice.Range(0, 2); r > 0; r--)
            {
                var from = dice.Pick(pack.Channels);
                var to = pack.Channels.First(c => c != from);
                var den = 1 << dice.Range(0, 4);
                pack.Rules.Add(new ConvertRule(from, to, dice.Range(0, den), den, Maybe(60)));
            }
        }

        for (var r = dice.Range(0, 2); r > 0; r--)
        {
            pack.Rules.Add(new AbsorbRule(dice.Pick(lanes), SomeChannels().OrderBy(_ => dice.Next(100)).ToArray()));
        }

        for (var r = dice.Range(1, 4); r > 0; r--)
        {
            var listed = pack.Channels.Where(_ => dice.Chance(60)).ToList();
            var add = listed.Where(_ => dice.Chance(50)).ToArray();
            var subtract = listed.Except(add).ToArray();
            pack.Rules.Add(new FlowRule(dice.Pick(lanes), add, subtract));
        }

        for (var r = dice.Range(0, 3); r > 0; r--)
        {
            Bound Pick() => dice.Next(3) switch
            {
                0 => Bound.None,
                1 => Bound.Of(dice.Range(-10, 90)),
                _ => Bound.Of(dice.Pick(lanes)),
            };
            pack.Rules.Add(new BoundsRule(dice.Pick(lanes), Pick(), Pick()));
        }

        foreach (var name in eventNames)
        {
            pack.Rules.Add(new EventRule(name, Maybe(100)!, Maybe(50)));
        }

        return pack;
    }
}
