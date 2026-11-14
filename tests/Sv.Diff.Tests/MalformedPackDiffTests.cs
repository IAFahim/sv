using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Sv.Rules;
using Xunit;
using Xunit.Abstractions;

namespace DiffTests
{
    public sealed class MalformedPackDiffTests
    {
        public static readonly int First = 2001 + Seeds.Offset;
        public const int Count = 100;

        static readonly Regex NumberToken = new Regex(@"(?<=[:\[,]\s*)-?\d+(?:\.\d+)?(?=\s*[,\]\}])", RegexOptions.CultureInvariant);
        static readonly Regex KeyValueToken = new Regex(@"""(schema|nextLane|id|name|class|layer|value|channel|then|when|rows)""\s*:", RegexOptions.CultureInvariant);
        static readonly Regex RowNameToken = new Regex(@"""name""\s*:\s*""([^""]+)""", RegexOptions.CultureInvariant);

        readonly ITestOutputHelper output;

        public MalformedPackDiffTests(ITestOutputHelper output) => this.output = output;

        void Verdict(string test, int checkedCount, List<string> failures)
        {
            output.WriteLine(test + ": " + checkedCount + " attempts checked, " + failures.Count + " failed, seeds " + First + "-" + (First + Count - 1));
            foreach (var failure in failures)
            {
                output.WriteLine(failure);
            }

            Assert.True(checkedCount > 0, test + ": no attempts reached this check");
            Assert.True(failures.Count == 0, failures.Count + " of " + checkedCount + " attempts failed; first: " + failures.FirstOrDefault());
        }

        [Fact]
        public void Mutated_packs_reject_or_diagnose_without_crashing_or_hanging()
        {
            var clock = Stopwatch.StartNew();
            var failures = new List<string>();
            var checkedCount = 0;
            var accepted = new List<(string Label, GeneratedCase Case)>();

            for (var seed = First; seed < First + Count; seed++)
            {
                var options = new GeneratorOptions
                {
                    Bricks = (seed & 1) == 0,
                    CardModifiers = (seed & 2) == 0,
                    StatOps = (seed & 4) == 0,
                    Absorb = (seed & 8) != 0,
                };
                var made = PackGenerator.Make(seed, options);
                var random = new Random(seed ^ 0x5A17);

                for (var kind = 0; kind < 10; kind++)
                {
                    var mutated = Mutate(made, kind, random, out var label, out var mustReject);
                    if (mutated == null)
                    {
                        continue;
                    }

                    checkedCount++;
                    var generated = new GeneratedCase
                    {
                        Seed = seed,
                        Namespace = made.Namespace + "_M" + kind,
                        Values = mutated.Value.Values,
                        Rules = mutated.Value.Rules,
                        Cards = mutated.Value.Cards,
                        Templates = mutated.Value.Templates,
                        Scenario = made.Scenario,
                    };
                    var finding = CheckCompile(mutated.Value, mustReject, out var ok);
                    if (finding.Length > 0)
                    {
                        failures.Add(Seeds.Receipt("malformed-" + label, new CompiledCase { Case = generated }, label + ": " + finding));
                    }
                    else if (ok)
                    {
                        accepted.Add((label, generated));
                    }
                }
            }

            if (accepted.Count > 0)
            {
                var built = BuildAccepted(accepted, out var fallbackFailures);
                if (fallbackFailures != null)
                {
                    failures.AddRange(fallbackFailures);
                }
                else
                {
                    for (var i = 0; i < built.Count; i++)
                    {
                        var compiled = built[i];
                        if (!compiled.Ready)
                        {
                            var label = accepted[i].Label;
                            failures.Add(Seeds.Receipt("malformed-" + label, compiled, label + ": " + compiled.Failure));
                        }
                    }
                }
            }

            output.WriteLine("malformed accepted compiled: " + accepted.Count + ", elapsed: " + clock.ElapsedMilliseconds + " ms");
            Verdict("malformed", checkedCount, failures);
        }

        static List<CompiledCase> BuildAccepted(List<(string Label, GeneratedCase Case)> accepted, out List<string>? fallbackFailures)
        {
            try
            {
                fallbackFailures = null;
                return KernelCompiler.Build(accepted.Select(entry => entry.Case));
            }
            catch
            {
                fallbackFailures = new List<string>();
                foreach (var entry in accepted)
                {
                    try
                    {
                        var single = KernelCompiler.Build(new[] { entry.Case })[0];
                        if (!single.Ready)
                        {
                            fallbackFailures.Add(Seeds.Receipt("malformed-" + entry.Label, single, entry.Label + ": " + single.Failure));
                        }
                    }
                    catch (Exception error)
                    {
                        var failed = new CompiledCase { Case = entry.Case };
                        fallbackFailures.Add(Seeds.Receipt("malformed-" + entry.Label, failed, entry.Label + ": kernel compile threw: " + error.Message));
                    }
                }

                return new List<CompiledCase>();
            }
        }

        static readonly TimeSpan CompileTimeout = TimeSpan.FromSeconds(60);

        static string CheckCompile(PackSource source, bool mustReject, out bool ok)
        {
            ok = false;
            Checked<PackPlan> compiled;
            try
            {
                var copy = source;
                Checked<PackPlan>? result = null;
                Exception? thrown = null;
                var worker = new Thread(() =>
                {
                    try
                    {
                        result = PackCompiler.Compile(copy);
                    }
                    catch (Exception error)
                    {
                        thrown = error;
                    }
                })
                {
                    IsBackground = true,
                };
                worker.Start();
                if (!worker.Join(CompileTimeout))
                {
                    return "compile hung: no result after " + CompileTimeout.TotalSeconds + " s on its own thread";
                }

                if (thrown != null)
                {
                    return "unexpected exception: " + thrown.GetType().Name + ": " + thrown.Message;
                }

                compiled = result!.Value;
            }
            catch (Exception error)
            {
                return "unexpected exception: " + error.GetType().Name + ": " + error.Message;
            }

            if (mustReject && compiled.Ok)
            {
                return "accepted pack referencing a missing name";
            }

            if (!compiled.Ok && compiled.Diagnostics.Length == 0)
            {
                return "rejected pack with 0 diagnostics";
            }

            ok = compiled.Ok;
            return string.Empty;
        }

        static PackSource? Mutate(GeneratedCase made, int kind, Random random, out string label, out bool mustReject)
        {
            mustReject = false;
            var fileIndex = random.Next(4);
            var files = new[] { made.Values, made.Rules, made.Cards, made.Templates };

            switch (kind)
            {
                case 0:
                    label = "delete-row";
                    var delText = DeleteRandomRow(files, random, out var delIdx);
                    return delText == null ? null : WithFile(files, delIdx, delText);
                case 1:
                    label = "duplicate-row";
                    var dupText = DuplicateRandomRow(files, random, out var dupIdx);
                    return dupText == null ? null : WithFile(files, dupIdx, dupText);
                case 2:
                    label = "neg-number";
                    return MutateNumber(files, random, "-1");
                case 3:
                    label = "huge-number";
                    return MutateNumber(files, random, "2147483648");
                case 4:
                    label = "nan-number";
                    return MutateNumber(files, random, "NaN");
                case 5:
                    label = "string-number";
                    return MutateNumber(files, random, "\"bad_num\"");
                case 6:
                    label = "remove-key";
                    return RemoveRequiredKey(files, random);
                case 7:
                    label = "missing-ref";
                    mustReject = true;
                    return BreakReferencedName(made, random, out mustReject);
                case 8:
                    label = "truncate";
                    var tText = files[fileIndex];
                    var cut = tText.Length <= 1 ? 0 : random.Next(tText.Length);
                    return WithFile(files, fileIndex, tText.Substring(0, cut));
                default:
                    label = "garbage";
                    var gText = files[fileIndex];
                    var pos = random.Next(gText.Length + 1);
                    return WithFile(files, fileIndex, gText.Insert(pos, GarbageText(random)));
            }
        }

        static int PickNonEmptyRowFile(string[] files, Random random)
        {
            var start = random.Next(files.Length);
            for (var i = 0; i < files.Length; i++)
            {
                var idx = (start + i) % files.Length;
                if (FindRows(files[idx]).Count > 0)
                {
                    return idx;
                }
            }

            return 0;
        }

        static PackSource WithFile(string[] files, int index, string text) => new PackSource(
            index == 0 ? text : files[0],
            index == 1 ? text : files[1],
            index == 2 ? text : files[2],
            index == 3 ? text : files[3]);

        static string? DeleteRandomRow(string[] files, Random random, out int pickedFile)
        {
            pickedFile = PickNonEmptyRowFile(files, random);
            var json = files[pickedFile];
            var rows = FindRows(json);
            if (rows.Count == 0)
            {
                return null;
            }

            var (start, length) = rows[random.Next(rows.Count)];
            var end = start + length;
            var removeFrom = start;
            var removeTo = end;

            var after = SkipWhite(json, end);
            if (after < json.Length && json[after] == ',')
            {
                removeTo = after + 1;
            }
            else
            {
                var before = SkipWhiteBack(json, start - 1);
                if (before >= 0 && json[before] == ',')
                {
                    removeFrom = before;
                }
            }

            return json.Remove(removeFrom, removeTo - removeFrom);
        }

        static string? DuplicateRandomRow(string[] files, Random random, out int pickedFile)
        {
            pickedFile = PickNonEmptyRowFile(files, random);
            var json = files[pickedFile];
            var rows = FindRows(json);
            if (rows.Count == 0)
            {
                return null;
            }

            var (start, length) = rows[random.Next(rows.Count)];
            var rowText = json.Substring(start, length);
            return json.Insert(start + length, "," + rowText);
        }

        static PackSource? MutateNumber(string[] files, Random random, string replacement)
        {
            var startFile = random.Next(files.Length);
            for (var i = 0; i < files.Length; i++)
            {
                var idx = (startFile + i) % files.Length;
                var matches = NumberToken.Matches(files[idx]);
                if (matches.Count > 0)
                {
                    var match = matches[random.Next(matches.Count)];
                    return WithFile(files, idx, files[idx].Remove(match.Index, match.Length).Insert(match.Index, replacement));
                }
            }

            return null;
        }

        static PackSource? RemoveRequiredKey(string[] files, Random random)
        {
            var idx = random.Next(files.Length);
            var json = files[idx];
            var matches = KeyValueToken.Matches(json);
            if (matches.Count == 0)
            {
                return null;
            }

            var match = matches[random.Next(matches.Count)];
            var valueStart = SkipWhite(json, match.Index + match.Length);
            var valueEnd = SkipJsonValue(json, valueStart);
            var removeFrom = match.Index;
            var removeTo = valueEnd;

            var after = SkipWhite(json, valueEnd);
            if (after < json.Length && json[after] == ',')
            {
                removeTo = after + 1;
            }
            else
            {
                var before = SkipWhiteBack(json, match.Index - 1);
                if (before >= 0 && json[before] == ',')
                {
                    removeFrom = before;
                }
            }

            return WithFile(files, idx, json.Remove(removeFrom, removeTo - removeFrom));
        }

        static PackSource? BreakReferencedName(GeneratedCase made, Random random, out bool mustReject)
        {
            mustReject = true;
            var declared = new List<string>();
            foreach (Match m in RowNameToken.Matches(made.Values))
            {
                var name = m.Groups[1].Value;
                if (made.Rules.IndexOf("\"" + name + "\"", StringComparison.Ordinal) >= 0)
                {
                    declared.Add(name);
                }
            }

            if (declared.Count == 0)
            {
                mustReject = false;
                return null;
            }

            var target = declared[random.Next(declared.Count)];
            var missing = "__missing_" + target + "__";
            var token = "\"" + target + "\"";
            var pos = made.Rules.IndexOf(token, StringComparison.Ordinal);
            if (pos < 0)
            {
                mustReject = false;
                return null;
            }

            var mutatedRules = made.Rules.Remove(pos, token.Length).Insert(pos, "\"" + missing + "\"");
            return new PackSource(made.Values, mutatedRules, made.Cards, made.Templates);
        }

        static string GarbageText(Random random)
        {
            var sb = new StringBuilder();
            var len = 1 + random.Next(8);
            var samples = "\0\x01\x1f\x7f\ufffd{[}]:,\"\\!@#";
            for (var i = 0; i < len; i++)
            {
                sb.Append(samples[random.Next(samples.Length)]);
            }

            return sb.ToString();
        }

        static List<(int Start, int Length)> FindRows(string json)
        {
            var rows = new List<(int Start, int Length)>();
            var keyIdx = json.IndexOf("\"rows\"", StringComparison.Ordinal);
            if (keyIdx < 0)
            {
                return rows;
            }

            var arrStart = json.IndexOf('[', keyIdx);
            if (arrStart < 0)
            {
                return rows;
            }

            var i = arrStart + 1;
            var inString = false;
            var escape = false;
            var depth = 0;
            var objStart = -1;

            while (i < json.Length)
            {
                var c = json[i];
                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                    }
                    else if (c == '\\')
                    {
                        escape = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inString = true;
                    }
                    else if (c == '{')
                    {
                        if (depth == 0)
                        {
                            objStart = i;
                        }

                        depth++;
                    }
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0 && objStart >= 0)
                        {
                            rows.Add((objStart, i - objStart + 1));
                            objStart = -1;
                        }
                    }
                    else if (c == ']' && depth == 0)
                    {
                        break;
                    }
                }

                i++;
            }

            return rows;
        }

        static int SkipWhite(string s, int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }

            return i;
        }

        static int SkipWhiteBack(string s, int i)
        {
            while (i >= 0 && char.IsWhiteSpace(s[i]))
            {
                i--;
            }

            return i;
        }

        static int SkipJsonValue(string s, int i)
        {
            if (i >= s.Length)
            {
                return i;
            }

            var startChar = s[i];
            if (startChar == '"')
            {
                i++;
                while (i < s.Length)
                {
                    if (s[i] == '\\')
                    {
                        i += 2;
                        continue;
                    }

                    if (s[i] == '"')
                    {
                        return i + 1;
                    }

                    i++;
                }

                return s.Length;
            }

            if (startChar == '{' || startChar == '[')
            {
                var depth = 0;
                var inString = false;
                while (i < s.Length)
                {
                    var c = s[i];
                    if (inString)
                    {
                        if (c == '\\')
                        {
                            i += 2;
                            continue;
                        }

                        if (c == '"')
                        {
                            inString = false;
                        }
                    }
                    else
                    {
                        if (c == '"')
                        {
                            inString = true;
                        }
                        else if (c == '{' || c == '[')
                        {
                            depth++;
                        }
                        else if (c == '}' || c == ']')
                        {
                            depth--;
                            if (depth == 0)
                            {
                                return i + 1;
                            }
                        }
                    }

                    i++;
                }

                return s.Length;
            }

            while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']' && !char.IsWhiteSpace(s[i]))
            {
                i++;
            }

            return i;
        }
    }
}
