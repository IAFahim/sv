using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Sv.Columns;
using Sv.Columns.Tests;
using Sv.Pack;

namespace Sv.Fuzz;

public sealed class Finding : Exception
{
    public Finding(string check, string message)
        : base(check + ": " + message)
    {
        Check = check;
    }

    public string Check { get; }
}

public static class ColumnsTarget
{
    static readonly long[] Extremes =
    {
        long.MinValue, long.MaxValue, Record.AmountMax, Record.AmountMin, Record.AmountMax + 1, Record.AmountMin - 1, int.MaxValue, int.MinValue, 0, 1, -1,
    };

    static readonly int[] OddHits = { 0, -1, -3, int.MaxValue, int.MinValue, 1 << 20, 2 };

    public static List<JsonNode> Seeds(string repo, ulong seed, int count)
    {
        var seeds = new List<JsonNode> { JsonNode.Parse(File.ReadAllText(Path.Combine(repo, "packs", "columns", "arena.json")))! };
        for (var i = 0; i < count; i++)
        {
            var pack = RandomPacks.Build(seed + (ulong)i);
            AddGlue(pack, new Dice(seed * 31 + (ulong)i));
            seeds.Add(PackJsonText.ToNode(pack));
        }

        return seeds;
    }

    static void AddGlue(ColumnPack pack, Dice dice)
    {
        foreach (var card in pack.Cards)
        {
            if (dice.Chance(60))
            {
                pack.Timelines.Cards.Add(card.Name);
            }
        }

        foreach (var channel in pack.Channels)
        {
            if (dice.Chance(40))
            {
                pack.Timelines.Outbox.Add(new TimelineOutbox(channel, dice.Chance(50) ? pack.Lanes[dice.Next(pack.Lanes.Count)].Name : null));
            }
        }

        foreach (var rule in pack.Rules)
        {
            if (rule is EventRule e && dice.Chance(60))
            {
                pack.Timelines.Starts.Add(new TimelineStart(e.Name, "Timelines/" + e.Name));
            }
        }
    }

    public static string Run(string json, ulong seed)
    {
        ColumnPack source;
        try
        {
            source = PackJson.Read(json);
        }
        catch (PackException error)
        {
            Problems(error);
            return "rejected-read";
        }

        CompiledPack pack;
        try
        {
            pack = source.Compile();
        }
        catch (PackException error)
        {
            Problems(error);
            return "rejected-compile";
        }

        using (pack)
        {
            Glue(source);
            Drive(pack, seed, 10);
        }

        return "accepted";
    }

    static void Problems(PackException error)
    {
        if (error.Problems.Count == 0)
        {
            throw new Finding("empty-diagnostics", "a pack was refused with no problem listed");
        }
    }

    static readonly Regex TypeLine = new Regex(@"public (?:partial )?(?:struct|class) (\S+)", RegexOptions.Compiled);

    static void Glue(ColumnPack source)
    {
        foreach (var unity in new[] { false, true })
        {
            List<Sv.Pack.EmittedFile> files;
            try
            {
                files = ColumnsGlue.Files(source, "Fz", unity);
            }
            catch (PackException error)
            {
                Problems(error);
                continue;
            }

            foreach (var file in files)
            {
                CheckCode(file.Text, file.Name + (unity ? " (unity)" : " (dotnet)"));
            }
        }
    }

    public static void CheckCode(string code, string where)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lines = code.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            foreach (var c in line)
            {
                if ((c < 32 && c != '\r' && c != '\t') || c == '\u0085' || c == '\u2028' || c == '\u2029' || char.IsSurrogate(c))
                {
                    throw new Finding("glue-raw-char", where + " line " + (i + 1) + " holds raw U+" + ((int)c).ToString("X4") + ": " + PackJsonText.Hex(line.Trim()));
                }
            }

            if (!QuotesBalanced(line))
            {
                throw new Finding("glue-literal", where + " line " + (i + 1) + " has an unterminated or injected string literal: " + PackJsonText.Hex(line.Trim()));
            }

            var match = TypeLine.Match(line);
            if (match.Success && !seen.Add(match.Groups[1].Value))
            {
                throw new Finding("glue-duplicate-type", where + " declares " + match.Groups[1].Value + " twice");
            }
        }
    }

    static bool QuotesBalanced(string line)
    {
        var inside = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\' && inside)
            {
                i++;
                continue;
            }

            if (line[i] == '"')
            {
                inside = !inside;
            }
        }

        return !inside;
    }

    static int AnyInt(Dice dice) => (dice.Next(65536) << 16) | dice.Next(65536);

    static long Amount(Dice dice) => dice.Next(100) switch
    {
        < 75 => dice.Range(-60, 120),
        < 90 => Extremes[dice.Next(Extremes.Length)],
        _ => ((long)AnyInt(dice) << 32) | (uint)AnyInt(dice),
    };

    static int Hits(Dice dice) => dice.Chance(90) ? dice.Range(1, 4) : OddHits[dice.Next(OddHits.Length)];

    static int Value(Dice dice) => dice.Next(100) switch
    {
        < 75 => dice.Range(-60, 900),
        < 85 => dice.Chance(50) ? int.MaxValue : int.MinValue,
        _ => AnyInt(dice),
    };

    public static void Drive(CompiledPack pack, ulong seed, int frames)
    {
        var dice = new Dice(seed ^ 0x5EEDul);
        var capacity = new[] { 64, 128, 192 }[dice.Next(3)];
        var writers = dice.Range(1, 3);
        var parts = dice.Range(1, 3);
        var kernels = new[] { KernelChoice.Scalar, KernelChoice.Vector, KernelChoice.Best, KernelChoice.Best, KernelChoice.Scalar };
        var fused = new[] { -1, -1, -1, (int)FrameKernel.Rows, (int)FrameKernel.Scalar };
        var worlds = kernels.Select(k => new ColumnWorld(pack, capacity, writers, parts, k)).ToArray();
        try
        {
            var reference = new ReferenceWorld(pack.Source);
            var live = new List<int>();
            var templates = pack.TemplateNames;
            var lanes = Enumerable.Range(0, pack.LaneNames.Count - 2).ToList();
            for (var f = 0; f < frames; f++)
            {
                for (var n = dice.Range(0, 12); n > 0; n--)
                {
                    var roll = dice.Next(100);
                    if (roll < 25 || live.Count == 0)
                    {
                        var template = dice.Next(templates.Count);
                        var slots = new int[worlds.Length];
                        for (var w = 0; w < worlds.Length; w++)
                        {
                            try
                            {
                                slots[w] = worlds[w].Spawn(template);
                            }
                            catch (InvalidOperationException)
                            {
                                slots[w] = -1;
                            }
                        }

                        if (slots.Distinct().Count() != 1)
                        {
                            throw new Finding("spawn-slot", "kernels gave different slots " + string.Join(",", slots));
                        }

                        if (slots[0] >= 0)
                        {
                            reference.Spawn(slots[0], templates[template]);
                            live.Add(slots[0]);
                        }
                    }
                    else if (roll < 32)
                    {
                        var slot = live[dice.Next(live.Count)];
                        foreach (var world in worlds)
                        {
                            world.Despawn(slot);
                        }

                        reference.Despawn(slot);
                        live.Remove(slot);
                    }
                    else if (roll < 45)
                    {
                        var slot = live[dice.Next(live.Count)];
                        var lane = lanes[dice.Next(lanes.Count)];
                        var value = Value(dice);
                        foreach (var world in worlds)
                        {
                            world.Set(slot, lane, value);
                        }

                        reference.Set(slot, pack.LaneNames[lane], value);
                    }
                }

                for (var n = dice.Range(0, 40); n > 0; n--)
                {
                    var writer = dice.Next(writers);
                    var channel = dice.Next(pack.ChannelNames.Count);
                    var amount = Amount(dice);
                    var hits = Hits(dice);
                    if (dice.Chance(5) || live.Count == 0)
                    {
                        var stray = dice.Next(3) switch { 0 => capacity + dice.Next(64), 1 => -1 - dice.Next(10), _ => int.MaxValue };
                        foreach (var world in worlds)
                        {
                            world.Writer(writer).Send(stray, channel, amount, hits);
                        }

                        continue;
                    }

                    var target = live[dice.Next(live.Count)];
                    if (dice.Chance(40))
                    {
                        var from = dice.Chance(85) ? live[dice.Next(live.Count)] : capacity + 3;
                        foreach (var world in worlds)
                        {
                            world.Writer(writer).SendFrom(from, target, channel, amount, hits);
                        }

                        reference.SendFrom(from, target, pack.ChannelNames[channel], amount, hits);
                    }
                    else
                    {
                        foreach (var world in worlds)
                        {
                            world.Writer(writer).Send(target, channel, amount, hits);
                        }

                        reference.Send(target, pack.ChannelNames[channel], amount, hits);
                    }
                }

                for (var w = 0; w < worlds.Length; w++)
                {
                    if (fused[w] < 0)
                    {
                        worlds[w].Step();
                    }
                    else
                    {
                        worlds[w].StepFused((FrameKernel)fused[w]);
                    }
                }

                reference.Step();
                var expected = reference.Digest();
                for (var w = 0; w < worlds.Length; w++)
                {
                    if (worlds[w].Digest() != expected)
                    {
                        throw new Finding("lockstep", "frame " + worlds[w].Frame + " world " + w + " (" + worlds[w].KernelPath + (fused[w] >= 0 ? " fused " + (FrameKernel)fused[w] : string.Empty) + ") differs from the reference: " + FirstDifference(pack, reference, worlds[w], live));
                    }
                }

                var dropped = worlds[0].Dropped;
                if (worlds.Any(w => w.Dropped != dropped))
                {
                    throw new Finding("dropped", "frame " + f + " dropped counts differ: " + string.Join(",", worlds.Select(w => w.Dropped)));
                }
            }
        }
        finally
        {
            foreach (var world in worlds)
            {
                world.Dispose();
            }
        }
    }

    static string FirstDifference(CompiledPack pack, ReferenceWorld reference, ColumnWorld world, List<int> live)
    {
        foreach (var slot in live.OrderBy(s => s))
        {
            foreach (var lane in pack.LaneNames)
            {
                var want = reference.Get(slot, lane);
                var got = world.Get(slot, pack.Lane(lane));
                if (want != got)
                {
                    return "slot " + slot + " lane " + lane + ": reference " + want + ", kernel " + got;
                }
            }
        }

        return "digests differ but no lane does";
    }
}
