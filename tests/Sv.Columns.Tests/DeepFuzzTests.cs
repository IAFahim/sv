namespace Sv.Columns.Tests;

public static class WidePacks
{
    static int Any(Dice dice) => (dice.Next(65536) << 16) | dice.Next(65536);

    public static ColumnPack Build(ulong seed)
    {
        var dice = new Dice(seed ^ 0x77A1DEul);
        var pack = new ColumnPack();
        var laneCount = dice.Range(3, 20);
        for (var l = 0; l < laneCount; l++)
        {
            pack.Lanes.Add(new LaneSpec("L" + l, dice.Chance(10) ? Any(dice) : dice.Range(-50, 500)));
        }

        var lanes = pack.Lanes.Select(l => l.Name).ToList();
        for (var c = dice.Range(1, PackLimits.Channels); c > 0; c--)
        {
            pack.Channels.Add("C" + pack.Channels.Count);
        }

        for (var k = dice.Range(0, PackLimits.Kinds); k > 0; k--)
        {
            pack.Kinds.Add("K" + pack.Kinds.Count);
        }

        var kinds = pack.Kinds.Count > 0 ? pack.Kinds : new List<string> { "none" };
        if (dice.Chance(60))
        {
            pack.KindLane = dice.Pick(lanes);
        }

        var eventNames = Enumerable.Range(0, dice.Range(0, 8)).Select(e => "E" + e).ToList();

        Cond Atom()
        {
            var roll = dice.Next(eventNames.Count > 0 ? 12 : 10);
            Cond atom = roll switch
            {
                < 3 => Cond.Compare((CondOp)dice.Next(6), dice.Pick(lanes), dice.Chance(10) ? Any(dice) : dice.Range(-100, 600)),
                < 6 => Cond.Compare((CondOp)dice.Next(6), dice.Pick(lanes), dice.Pick(lanes)),
                < 8 => Cond.Below(dice.Pick(lanes), dice.Pick(lanes), dice.Range(0, 100)),
                < 10 => Cond.Bit(dice.Pick(lanes), dice.Range(0, 31)),
                10 => Cond.Event(dice.Pick(eventNames)),
                _ => Cond.Entered(dice.Pick(eventNames)),
            };
            return dice.Chance(25) ? Cond.Not(atom) : atom;
        }

        Cond? Maybe(int percent)
        {
            if (!dice.Chance(percent))
            {
                return null;
            }

            var count = dice.Range(1, 4);
            return count == 1 ? Atom() : Cond.All(Enumerable.Range(0, count).Select(_ => Atom()).ToArray());
        }

        var budget = 0L;
        OpSpec Op()
        {
            var room = (int)Math.Min(PackLimits.OpAmount, Math.Max(0, (PackLimits.TemplateAmountBudget / 4) - budget));
            var size = dice.Chance(15) ? dice.Range(0, room) : dice.Range(0, Math.Min(room, 40));
            budget += size;
            return new OpSpec(
                dice.Pick(pack.Channels),
                dice.Chance(30) ? -size : size,
                dice.Chance(40) ? dice.Pick(lanes) : null,
                Maybe(40),
                dice.Chance(50) ? dice.Pick(kinds) : null);
        }

        for (var c = dice.Range(0, 4); c > 0; c--)
        {
            var card = new CardSpec("card" + pack.Cards.Count, dice.Pick(lanes));
            for (var o = dice.Range(0, 6); o > 0; o--)
            {
                card.Ops.Add(Op());
            }

            pack.Cards.Add(card);
        }

        var cardBudget = budget;
        for (var t = dice.Range(1, 5); t > 0; t--)
        {
            budget = cardBudget;
            var template = new TemplateSpec("T" + pack.Templates.Count);
            foreach (var lane in lanes)
            {
                if (dice.Chance(30))
                {
                    template.Values[lane] = dice.Chance(10) ? Any(dice) : dice.Range(-50, 800);
                }
            }

            foreach (var card in pack.Cards)
            {
                if (dice.Chance(40))
                {
                    template.Cards[card.Name] = dice.Range(0, 3);
                }
            }

            for (var o = dice.Range(0, 60); o > 0; o--)
            {
                template.Ops.Add(Op());
            }

            pack.Templates.Add(template);
        }

        string[] SomeChannels() => pack.Channels.Where(_ => dice.Chance(40)).DefaultIfEmpty(dice.Pick(pack.Channels)).ToArray();

        var rules = new List<RuleSpec>();
        for (var r = dice.Range(0, 3); r > 0; r--)
        {
            rules.Add(new DenyRule(dice.Chance(50) ? SomeChannels() : null, dice.Chance(50) ? kinds.Where(_ => dice.Chance(50)).DefaultIfEmpty(kinds[0]).ToArray() : null, Maybe(80)));
        }

        for (var r = dice.Range(0, 2); r > 0; r--)
        {
            rules.Add(new DenyByMaskRule(dice.Pick(lanes), dice.Chance(50)));
        }

        var product = new long[pack.Channels.Count];
        Array.Fill(product, 1L);
        for (var r = dice.Range(0, PackLimits.ScaleRules); r > 0; r--)
        {
            var times = dice.Range(0, 4);
            var channels = SomeChannels();
            if (channels.All(c => product[pack.Channels.IndexOf(c)] * Math.Max(1, times) <= PackLimits.ScaleProduct))
            {
                foreach (var c in channels)
                {
                    product[pack.Channels.IndexOf(c)] *= Math.Max(1, times);
                }

                rules.Add(new ScaleRule(channels, times, Maybe(60), Maybe(50)));
            }
        }

        if (pack.Channels.Count > 1)
        {
            for (var r = dice.Range(0, 3); r > 0; r--)
            {
                var from = dice.Pick(pack.Channels);
                var to = dice.Pick(pack.Channels.Where(c => c != from).ToList());
                var den = 1 << dice.Range(0, 16);
                rules.Add(new ConvertRule(from, to, dice.Chance(20) ? den - dice.Range(0, Math.Min(den, 3)) : dice.Range(0, den), den, Maybe(50)));
            }
        }

        for (var r = dice.Range(0, 3); r > 0; r--)
        {
            rules.Add(new AbsorbRule(dice.Pick(lanes), SomeChannels().OrderBy(_ => dice.Next(1000)).ToArray()));
        }

        for (var r = dice.Range(1, 5); r > 0; r--)
        {
            var listed = pack.Channels.Where(_ => dice.Chance(60)).ToList();
            var add = listed.Where(_ => dice.Chance(50)).ToArray();
            rules.Add(new FlowRule(dice.Pick(lanes), add, listed.Except(add).ToArray()));
        }

        for (var r = dice.Range(0, 4); r > 0; r--)
        {
            Bound Pick() => dice.Next(3) switch
            {
                0 => Bound.None,
                1 => Bound.Of(dice.Chance(10) ? Any(dice) : dice.Range(-100, 900)),
                _ => Bound.Of(dice.Pick(lanes)),
            };
            rules.Add(new BoundsRule(dice.Pick(lanes), Pick(), Pick()));
        }

        foreach (var name in eventNames)
        {
            rules.Add(new EventRule(name, Maybe(100)!, Maybe(50)));
        }

        foreach (var rule in rules.OrderBy(_ => dice.Next(1 << 20)))
        {
            pack.Rules.Add(rule);
        }

        return pack;
    }
}

public sealed class DeepFuzzTests
{
    static readonly long[] Extremes =
    {
        long.MinValue, long.MaxValue, Record.AmountMax, Record.AmountMin, Record.AmountMax + 1, Record.AmountMin - 1,
        int.MaxValue, int.MinValue, 0, 1, -1, 1L << 40, -(1L << 40),
    };

    static readonly int[] OddHits = { 0, -1, -3, int.MaxValue, int.MinValue, 1 << 20, 2 };

    static int Rounds => int.TryParse(Environment.GetEnvironmentVariable("SV_COLUMNS_FUZZ_ROUNDS"), out var rounds) && rounds > 0 ? rounds : 24;

    static ulong Offset => ulong.TryParse(Environment.GetEnvironmentVariable("SV_COLUMNS_FUZZ_OFFSET"), out var offset) ? offset : 0;

    static int AnyInt(Dice dice) => (dice.Next(65536) << 16) | dice.Next(65536);

    static long AnyLong(Dice dice) => ((long)AnyInt(dice) << 32) | (uint)AnyInt(dice);

    static long Amount(Dice dice) => dice.Next(100) switch
    {
        < 70 => dice.Range(-60, 120),
        < 85 => Extremes[dice.Next(Extremes.Length)],
        < 95 => dice.Range(-40000, 40000),
        _ => AnyLong(dice),
    };

    static int Hits(Dice dice) => dice.Chance(90) ? dice.Range(1, 4) : OddHits[dice.Next(OddHits.Length)];

    static int Stray(Dice dice, int capacity) => dice.Next(5) switch
    {
        0 => capacity + dice.Next(64),
        1 => Record.SlotLimit + dice.Next(capacity),
        2 => -1 - dice.Next(capacity),
        3 => int.MinValue + dice.Next(capacity),
        _ => int.MaxValue - dice.Next(64),
    };

    static int Value(Dice dice) => dice.Next(100) switch
    {
        < 70 => dice.Range(-60, 900),
        < 80 => dice.Chance(50) ? int.MaxValue : int.MinValue,
        < 90 => dice.Range(-40, 40) + (dice.Chance(50) ? int.MaxValue / 2 : int.MinValue / 2),
        _ => AnyInt(dice),
    };

    static void Drive(CompiledPack pack, ulong seed, int frames)
    {
        var dice = new Dice(seed ^ 0xF0221Eul);
        var capacity = new[] { 64, 128, 192, 320 }[dice.Next(4)];
        var writers = dice.Range(1, 4);
        using var step = new Lockstep(pack, capacity, writers, dice.Range(1, 5));
        var live = new List<int>();
        var gone = new List<int>();
        var templates = pack.TemplateNames;
        var lanes = pack.LaneNames.Where(n => n[0] != '@').ToList();
        var channels = pack.ChannelNames;
        void Spawn()
        {
            var slot = step.TrySpawn(dice.Pick(templates));
            if (slot >= 0)
            {
                live.Add(slot);
                gone.Remove(slot);
            }
        }

        void Despawn(int slot)
        {
            live.Remove(slot);
            gone.Add(slot);
            step.Despawn(slot);
        }

        for (var i = dice.Range(1, capacity + 8); i > 0; i--)
        {
            Spawn();
        }

        for (var f = 0; f < frames; f++)
        {
            for (var i = dice.Range(0, 500); i > 0; i--)
            {
                var roll = dice.Next(100);
                var target = live.Count > 0 && roll < 85 ? dice.Pick(live) : gone.Count > 0 && roll < 92 ? dice.Pick(gone) : Stray(dice, capacity);
                var channel = dice.Pick(channels);
                var writer = dice.Next(writers);
                if (dice.Chance(2))
                {
                    step.SendStray(target, dice.Chance(50) ? channels.Count + dice.Next(40) : -1 - dice.Next(40), Amount(dice), writer);
                }
                else if (dice.Chance(50))
                {
                    var source = live.Count > 0 && dice.Chance(85) ? dice.Pick(live) : gone.Count > 0 && dice.Chance(50) ? dice.Pick(gone) : Stray(dice, capacity);
                    step.SendFrom(source, target, channel, Amount(dice), writer, Hits(dice));
                }
                else
                {
                    step.Send(target, channel, Amount(dice), writer, Hits(dice));
                }
            }

            if (dice.Chance(4) && live.Count > 0)
            {
                var flooded = dice.Pick(live);
                var channel = dice.Pick(channels);
                for (var i = 0; i < 1 << 13; i++)
                {
                    step.Send(flooded, channel, Record.AmountMax, i % writers);
                }
            }

            for (var i = dice.Range(0, 12); i > 0 && live.Count > 0 && lanes.Count > 0; i--)
            {
                step.Set(dice.Pick(live), dice.Pick(lanes), Value(dice));
            }

            if (dice.Chance(10) && live.Count > 0)
            {
                var chunk = dice.Pick(live) / ColumnWorld.Rows;
                foreach (var slot in live.Where(s => s / ColumnWorld.Rows == chunk).ToList())
                {
                    Despawn(slot);
                }
            }

            for (var i = dice.Range(0, 3); i > 0 && live.Count > 0; i--)
            {
                if (dice.Chance(40))
                {
                    Despawn(dice.Pick(live));
                }
            }

            for (var i = dice.Range(0, 70); i > 0; i--)
            {
                if (dice.Chance(50))
                {
                    Spawn();
                }
            }

            step.Step();
            _ = step.Dropped;
        }
    }

    static void Run(Func<ulong, ColumnPack> build, ulong seed, int frames)
    {
        CompiledPack pack;
        try
        {
            pack = build(seed).Compile();
        }
        catch (PackException)
        {
            return;
        }

        using (pack)
        {
            try
            {
                Drive(pack, seed, frames);
            }
            catch (Exception e) when (e is not PackException)
            {
                throw new Exception("seed " + seed + Environment.NewLine + PackText.Describe(pack.Source), e);
            }
        }
    }

    [Fact]
    public void WidePacksMatchTheReferenceAtTheEdges()
    {
        var compiled = 0;
        for (var seed = Offset + 1; seed <= Offset + (ulong)Rounds; seed++)
        {
            try
            {
                WidePacks.Build(seed).Compile().Dispose();
                compiled++;
            }
            catch (PackException)
            {
            }

            Run(WidePacks.Build, seed, 16);
        }

        Assert.True(compiled * 4 > Rounds * 3, "only " + compiled + " of " + Rounds + " wide packs compiled");
    }

    [Fact]
    public void RandomPacksMatchTheReferenceAtTheEdges()
    {
        for (var seed = Offset + 1; seed <= Offset + (ulong)Rounds; seed++)
        {
            Run(RandomPacks.Build, seed, 12);
        }
    }

    [Fact]
    public void TheArenaMatchesTheReferenceAtTheEdges()
    {
        using var pack = ArenaPack.Build().Compile();
        for (var seed = Offset + 1; seed <= Offset + (ulong)Math.Max(4, Rounds / 4); seed++)
        {
            Drive(pack, seed, 24);
        }
    }
}
