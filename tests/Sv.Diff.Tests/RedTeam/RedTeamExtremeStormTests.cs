using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Sv;
using Sv.Rules;
using Xunit;
using Xunit.Abstractions;

namespace DiffTests.RedTeam
{
    public sealed class RedTeamExtremeStormTests
    {
        static readonly int[] Amounts = { int.MaxValue, int.MinValue, -1, 1, 7, 1_000_000_000, -1_000_000_000, 46341, 65536 };
        static readonly int[] Hits = { 1, 2, 1000, int.MaxValue, 65536 };
        static readonly int[] ModifierValues = { int.MaxValue, int.MinValue, -10000, -20000, 10000, 1_000_000, 1, -1, 0 };
        static readonly string[] Shapes = { "Added", "Additive", "Multi", "Cap" };
        static readonly string[] Origins = { "Direct", "Timeline" };

        readonly ITestOutputHelper output;

        public RedTeamExtremeStormTests(ITestOutputHelper output) => this.output = output;

        static string Root
        {
            get
            {
                var folder = new DirectoryInfo(AppContext.BaseDirectory);
                while (folder != null && !Directory.Exists(Path.Combine(folder.FullName, "packs", "lego")))
                {
                    folder = folder.Parent;
                }

                return folder?.FullName ?? throw new DirectoryNotFoundException("packs/lego");
            }
        }

        static string[] Names(string json, string key, string valueClass)
        {
            var parse = JsonReader.Parse(json);
            var names = new List<string>();
            foreach (var row in parse.Value!.Find("rows")!.Items)
            {
                var classOf = row.Find("class");
                if (valueClass.Length == 0 || (classOf != null && classOf.Text == valueClass))
                {
                    names.Add(row.Find(key)!.Text);
                }
            }

            return names.ToArray();
        }

        static string Storm(int seed, string[] templates, string[] channels, string[] stats, int frames, bool shuffled = false)
        {
            var random = new Random(seed);
            var text = new StringBuilder();
            var entities = new List<(string Name, int Frame)>();
            for (var at = 0; at < templates.Length; at++)
            {
                entities.Add(("E" + at, 0));
            }

            for (var at = 0; at < 4; at++)
            {
                entities.Add(("L" + at, 2 + random.Next(frames - 2)));
            }

            text.Append("{\"schema\":1,\"name\":\"storm\",\"frames\":").Append(frames).Append(",\"entities\":[");
            text.Append(string.Join(",", entities.Select((entity, at) => "{\"name\":\"" + entity.Name + "\",\"template\":\"" + templates[at % templates.Length] + "\",\"frame\":" + entity.Frame + "}")));
            text.Append("],\"sends\":[");
            var sends = new List<string>();
            for (var frame = 0; frame < frames; frame++)
            {
                var alive = entities.Where(entity => entity.Frame <= frame).ToList();
                for (var at = 0; at < alive.Count * 4; at++)
                {
                    var target = alive[random.Next(alive.Count)].Name;
                    var source = alive[random.Next(alive.Count)].Name;
                    var channel = channels[random.Next(channels.Length)];
                    var amount = Amounts[random.Next(Amounts.Length)];
                    var hits = Hits[random.Next(Hits.Length)];
                    var origin = Origins[random.Next(Origins.Length)];
                    var withSource = random.Next(4) != 0 ? ",\"source\":\"" + source + "\"" : string.Empty;
                    sends.Add("{\"frame\":" + frame + ",\"target\":\"" + target + "\"" + withSource + ",\"channel\":\"" + channel + "\",\"amount\":" + amount.ToString(CultureInfo.InvariantCulture) + ",\"hits\":" + hits + ",\"origin\":\"" + origin + "\"}");
                }
            }

            if (shuffled)
            {
                var order = new Random(seed ^ 0x5EED);
                sends = sends.OrderBy(_ => order.Next()).ToList();
            }

            text.Append(string.Join(",", sends));
            text.Append("],\"kills\":[");
            var kills = new List<string>();
            for (var at = 0; at < 3; at++)
            {
                kills.Add("{\"frame\":" + (1 + random.Next(frames - 1)) + ",\"entity\":\"E" + random.Next(templates.Length) + "\"}");
            }

            text.Append(string.Join(",", kills.Distinct()));
            text.Append("],\"modifiers\":[");
            var modifiers = new List<string>();
            if (stats.Length > 0)
            {
                for (var at = 0; at < 40; at++)
                {
                    var remove = random.Next(5) == 0;
                    var entity = "E" + random.Next(templates.Length);
                    var frame = (random.Next(frames / 2) * 2) + (remove ? 1 : 0);
                    var sourceId = random.Next(4);
                    modifiers.Add(remove
                        ? "{\"frame\":" + frame + ",\"entity\":\"" + entity + "\",\"stat\":\"" + stats[random.Next(stats.Length)] + "\",\"source\":" + sourceId + ",\"op\":\"remove\"}"
                        : "{\"frame\":" + frame + ",\"entity\":\"" + entity + "\",\"stat\":\"" + stats[random.Next(stats.Length)] + "\",\"shape\":\"" + Shapes[random.Next(Shapes.Length)] + "\",\"value\":" + ModifierValues[random.Next(ModifierValues.Length)].ToString(CultureInfo.InvariantCulture) + ",\"source\":" + sourceId + "}");
                }
            }

            text.Append(string.Join(",", modifiers));
            text.Append("]}");
            return text.ToString();
        }

        void StormPack(string pack, int seeds)
        {
            var folder = Path.Combine(Root, "packs", pack);
            var values = File.ReadAllText(Path.Combine(folder, "values.sv.json"));
            var rules = File.ReadAllText(Path.Combine(folder, "rules.sv.json"));
            var cards = File.ReadAllText(Path.Combine(folder, "cards.sv.json"));
            var templates = File.ReadAllText(Path.Combine(folder, "templates.sv.json"));
            var templateNames = Names(templates, "name", string.Empty);
            var channels = Names(values, "name", "channel");
            var stats = Names(values, "name", "stat");
            var cases = Enumerable.Range(0, seeds).Select(seed => new GeneratedCase
            {
                Seed = 900000 + seed,
                Namespace = "Diff.RedTeam.Storm." + pack + seed,
                Values = values,
                Rules = rules,
                Cards = cards,
                Templates = templates,
                Scenario = Storm(900000 + seed, templateNames, channels, stats, 6),
            }).ToList();
            var built = KernelCompiler.Build(cases);
            var failures = new List<string>();
            var options = new[]
            {
                Seeds.Options(1, 64, StoragePolicy.Shared),
                Seeds.Options(4, 16, StoragePolicy.Dense),
                Seeds.Options(7, 3, StoragePolicy.Shared),
                Seeds.Options(32, 1, StoragePolicy.Dense),
                Seeds.Options(11, 1000, StoragePolicy.Shared),
            };
            foreach (var item in built)
            {
                Assert.True(item.Ready, item.Failure);
                var reference = Seeds.Reference(item);
                foreach (var option in options)
                {
                    var compiled = Seeds.Compiled(item, option);
                    var difference = RunTrace.FirstDifference(reference, compiled, true);
                    if (difference.Length > 0)
                    {
                        failures.Add(Seeds.Receipt("redteam-storm-" + pack + "-w" + option.Workers + "-" + option.Policy, item, difference));
                    }
                }
            }

            var shuffledCases = cases.Select(generated => new GeneratedCase
            {
                Seed = generated.Seed,
                Namespace = generated.Namespace + "Shuffled",
                Values = values,
                Rules = rules,
                Cards = cards,
                Templates = templates,
                Scenario = Storm(generated.Seed, templateNames, channels, stats, 6, true),
            }).ToList();
            var shuffledBuilt = KernelCompiler.Build(shuffledCases);
            for (var at = 0; at < built.Count; at++)
            {
                var straight = Seeds.Compiled(built[at], Seeds.Options(4, 64, StoragePolicy.Shared));
                var reordered = Seeds.Compiled(shuffledBuilt[at], Seeds.Options(4, 64, StoragePolicy.Shared));
                var difference = RunTrace.FirstDifference(straight, reordered, true);
                if (difference.Length > 0)
                {
                    failures.Add(Seeds.Receipt("redteam-storm-order-" + pack, shuffledBuilt[at], difference));
                }
            }

            output.WriteLine(pack + ": " + built.Count + " storms, " + failures.Count + " differences");
            Assert.True(failures.Count == 0, string.Join("\n", failures.Take(5)));
        }

        [Fact]
        public void Extreme_storms_on_the_lego_pack_match_the_reference_on_every_storage_and_worker_count() => StormPack("lego", 12);

        [Fact]
        public void Extreme_storms_on_the_reference_pack_match_the_reference_on_every_storage_and_worker_count() => StormPack("reference", 12);
    }
}
