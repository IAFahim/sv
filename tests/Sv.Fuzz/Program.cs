using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Sv.Columns;
using Sv.Columns.Tests;

namespace Sv.Fuzz;

public static class Program
{
    sealed class Options
    {
        public string Mode = "all";
        public ulong Seed = 1;
        public int Seconds = 60;
        public string Out = "fuzz-out/sv-fuzz";
        public string Repo = Directory.GetCurrentDirectory();
        public string? Replay;
    }

    sealed class Tally
    {
        public long Cases;
        public readonly SortedDictionary<string, long> Outcomes = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> Findings = new(StringComparer.Ordinal);
    }

    public static int Main(string[] args)
    {
        var options = Parse(args);
        if (options.Replay != null)
        {
            return Replay(options);
        }

        var modes = options.Mode == "all" ? new[] { "v4json", "v4api", "v3json" } : options.Mode.Split(',');
        var failed = 0;
        foreach (var mode in modes)
        {
            var tally = new Tally();
            var clock = Stopwatch.StartNew();
            var budget = TimeSpan.FromSeconds(options.Seconds / (double)modes.Length);
            switch (mode)
            {
                case "v4json":
                    V4Json(options, tally, clock, budget);
                    break;
                case "v4api":
                    V4Api(options, tally, clock, budget);
                    break;
                case "v3json":
                    V3Json(options, tally, clock, budget);
                    break;
                default:
                    Console.Error.WriteLine("unknown mode " + mode + " (v4json, v4api, v3json, all)");
                    return 2;
            }

            Console.WriteLine(mode + ": " + tally.Cases + " cases in " + clock.Elapsed.TotalSeconds.ToString("F0") + " s, seed " + options.Seed + ", " + string.Join(", ", tally.Outcomes.Select(p => p.Key + " " + p.Value)));
            foreach (var finding in tally.Findings.OrderBy(p => p.Key))
            {
                Console.WriteLine("  FINDING x" + finding.Value + " " + finding.Key);
            }

            failed += tally.Findings.Count;
        }

        return failed == 0 ? 0 : 1;
    }

    static Options Parse(string[] args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mode":
                    options.Mode = args[++i];
                    break;
                case "--seed":
                    options.Seed = ulong.Parse(args[++i]);
                    break;
                case "--seconds":
                    options.Seconds = int.Parse(args[++i]);
                    break;
                case "--out":
                    options.Out = args[++i];
                    break;
                case "--repo":
                    options.Repo = args[++i];
                    break;
                case "--replay":
                    options.Replay = args[++i];
                    break;
                default:
                    throw new ArgumentException("unknown argument " + args[i]);
            }
        }

        return options;
    }

    static int Replay(Options options)
    {
        var files = Directory.Exists(options.Replay)
            ? Directory.GetFiles(options.Replay!, "*", SearchOption.AllDirectories).Where(f => f.EndsWith(".json", StringComparison.Ordinal) || f.EndsWith(".api", StringComparison.Ordinal)).ToArray()
            : new[] { options.Replay! };
        var failed = 0;
        foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
        {
            string outcome;
            try
            {
                var text = File.ReadAllText(file);
                outcome = file.EndsWith(".v3.json", StringComparison.Ordinal) ? V3Target.Run(SplitV3(text))
                    : file.EndsWith(".api", StringComparison.Ordinal) ? ReplayApi(text)
                    : ColumnsTarget.Run(text, 1);
            }
            catch (Exception error)
            {
                outcome = "FAIL " + error.GetType().Name + ": " + error.Message;
                failed++;
            }

            Console.WriteLine(Path.GetFileName(file) + ": " + outcome);
        }

        return failed == 0 ? 0 : 1;
    }

    static string ReplayApi(string text)
    {
        var mark = text.LastIndexOf("\n//seed ", StringComparison.Ordinal);
        var seed = ulong.Parse(text.Substring(mark + 8).Trim());
        using var pack = PackJson.Read(text.Substring(0, mark)).Compile();
        return ApiTarget.Run(pack, seed);
    }

    public static string JoinV3(string[] texts) => new JsonObject(V3Target.Files.Select((f, i) => new KeyValuePair<string, JsonNode?>(f, JsonValue.Create(texts[i])))).ToJsonString();

    public static string[] SplitV3(string text)
    {
        var node = JsonNode.Parse(text)!.AsObject();
        return V3Target.Files.Select(f => node[f]!.GetValue<string>()).ToArray();
    }

    static void Guard(Options options, Tally tally, string mode, string input, Func<string> body)
    {
        tally.Cases++;
        Exception? failure = null;
        string? outcome = null;
        var worker = new Thread(() =>
        {
            try
            {
                outcome = body();
            }
            catch (Exception error)
            {
                failure = error;
            }
        }, 64 << 20);
        worker.Start();
        if (!worker.Join(TimeSpan.FromSeconds(20)))
        {
            Save(options, mode, "hang", input, "the case ran longer than 20 s");
            Console.WriteLine("FINDING hang in " + mode + ", input saved; stopping");
            Environment.Exit(3);
        }

        if (failure == null)
        {
            tally.Outcomes[outcome!] = tally.Outcomes.TryGetValue(outcome!, out var n) ? n + 1 : 1;
            return;
        }

        var key = failure is Finding finding
            ? finding.Check + " | " + Shape(finding.Message)
            : "crash " + failure.GetType().Name + " | " + Shape(failure.Message) + " | " + (failure.StackTrace ?? string.Empty).Split('\n').FirstOrDefault()?.Trim();
        tally.Findings[key] = tally.Findings.TryGetValue(key, out var seen) ? seen + 1 : 1;
        if (tally.Findings[key] <= 3)
        {
            Save(options, mode, key, input, failure.ToString());
        }
    }

    static string Shape(string message)
    {
        var builder = new StringBuilder();
        foreach (var c in message)
        {
            builder.Append(char.IsDigit(c) ? '#' : c);
        }

        var text = builder.ToString();
        return text.Length > 140 ? text.Substring(0, 140) : text;
    }

    static void Save(Options options, string mode, string key, string input, string details)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).Substring(0, 10).ToLowerInvariant();
        var folder = Path.Combine(options.Out, mode, id);
        Directory.CreateDirectory(folder);
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).Substring(0, 12).ToLowerInvariant();
        var extension = mode == "v3json" ? ".v3.json" : mode == "v4api" ? ".api" : ".json";
        File.WriteAllText(Path.Combine(folder, name + extension), input);
        File.WriteAllText(Path.Combine(folder, name + ".txt"), key + "\n\n" + details + "\n");
    }

    static void V4Json(Options options, Tally tally, Stopwatch clock, TimeSpan budget)
    {
        var seeds = ColumnsTarget.Seeds(options.Repo, options.Seed, 40);
        var mutator = new Mutator(options.Seed);
        var round = 0ul;
        while (clock.Elapsed < budget)
        {
            round++;
            var seed = seeds[mutator.Dice.Next(seeds.Count)];
            var input = mutator.Dice.Chance(3) ? seed.ToJsonString() : mutator.Mutate(seed, mutator.Dice.Range(1, 4));
            var caseSeed = options.Seed * 1_000_003ul + round;
            Guard(options, tally, "v4json", input, () => ColumnsTarget.Run(input, caseSeed));
        }
    }

    static void V4Api(Options options, Tally tally, Stopwatch clock, TimeSpan budget)
    {
        var arena = PackJson.Read(File.ReadAllText(Path.Combine(options.Repo, "packs", "columns", "arena.json"))).Compile();
        var round = 0ul;
        while (clock.Elapsed < budget)
        {
            round++;
            var caseSeed = options.Seed * 1_000_003ul + round;
            var useArena = round % 3 == 0;
            var source = useArena ? arena.Source : RandomPacks.Build(caseSeed);
            var input = PackJsonText.ToNode(source).ToJsonString();
            Guard(options, tally, "v4api", input + "\n//seed " + caseSeed, () =>
            {
                if (useArena)
                {
                    return ApiTarget.Run(arena, caseSeed);
                }

                using var pack = source.Compile();
                return ApiTarget.Run(pack, caseSeed);
            });
        }
    }

    static void V3Json(Options options, Tally tally, Stopwatch clock, TimeSpan budget)
    {
        var seeds = V3Target.Seeds(options.Repo);
        var mutator = new Mutator(options.Seed ^ 0x33ul);
        while (clock.Elapsed < budget)
        {
            var seed = seeds[mutator.Dice.Next(seeds.Count)];
            var texts = seed.Select(n => n.ToJsonString()).ToArray();
            var file = mutator.Dice.Next(4);
            if (!mutator.Dice.Chance(3))
            {
                texts[file] = mutator.Mutate(seed[file], mutator.Dice.Range(1, 3));
                if (mutator.Dice.Chance(20))
                {
                    var other = mutator.Dice.Next(4);
                    texts[other] = mutator.Mutate(seed[other], 1);
                }
            }

            var input = JoinV3(texts);
            Guard(options, tally, "v3json", input, () => V3Target.Run(texts));
        }
    }
}
