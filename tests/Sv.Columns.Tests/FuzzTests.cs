namespace Sv.Columns.Tests;

public sealed class FuzzTests
{
    static void Drive(CompiledPack pack, ulong seed, int frames, int writers, int parts)
    {
        var dice = new Dice(seed ^ 0xABCDEFul);
        using var twin = new Twin(pack, 1024, writers, parts);
        var live = new List<int>();
        var templates = pack.TemplateNames;
        var lanes = pack.LaneNames.Where(n => n[0] != '@').ToList();
        for (var i = dice.Range(1, 300); i > 0; i--)
        {
            live.Add(twin.Spawn(dice.Pick(templates)));
        }

        for (var f = 0; f < frames; f++)
        {
            for (var i = dice.Range(0, 400); i > 0 && live.Count > 0; i--)
            {
                var target = dice.Pick(live);
                var channel = dice.Pick(pack.ChannelNames);
                var amount = dice.Chance(5) ? (long)dice.Range(-1, 1) << 40 : dice.Range(-20, 40);
                var writer = dice.Next(writers);
                if (dice.Chance(60))
                {
                    twin.Send(target, dice.Pick(live), channel, amount, writer, dice.Range(1, 3));
                }
                else
                {
                    twin.Send(target, channel, amount, writer);
                }
            }

            for (var i = dice.Range(0, 10); i > 0 && live.Count > 0; i--)
            {
                twin.Set(dice.Pick(live), dice.Pick(lanes), dice.Chance(10) ? dice.Range(int.MinValue / 2, int.MaxValue / 2) : dice.Range(-10, 90));
            }

            if (dice.Chance(30) && live.Count > 1)
            {
                var gone = dice.Pick(live);
                live.Remove(gone);
                twin.Despawn(gone);
            }

            if (dice.Chance(30) && live.Count < 1000)
            {
                live.Add(twin.Spawn(dice.Pick(templates)));
            }

            twin.Step();
        }
    }

    [Fact]
    public void ArenaPackMatchesTheReferenceUnderRandomTraffic()
    {
        using var pack = ArenaPack.Build().Compile();
        for (ulong seed = 1; seed <= 12; seed++)
        {
            Drive(pack, seed, 24, 1 + (int)(seed % 3), 1 + (int)(seed % 4));
        }
    }

    [Fact]
    public void RandomPacksMatchTheReference()
    {
        var compiled = 0;
        for (ulong seed = 1; seed <= 200; seed++)
        {
            CompiledPack pack;
            try
            {
                pack = RandomPacks.Build(seed).Compile();
            }
            catch (PackException)
            {
                continue;
            }

            using (pack)
            {
                compiled++;
                try
                {
                    Drive(pack, seed, 12, 1 + (int)(seed % 3), 1 + (int)(seed % 3));
                }
                catch (Exception e) when (e is not PackException)
                {
                    throw new Exception("seed " + seed + Environment.NewLine + PackText.Describe(pack.Source), e);
                }
            }
        }

        Assert.True(compiled > 150, "only " + compiled + " of 200 random packs compiled");
    }
}
