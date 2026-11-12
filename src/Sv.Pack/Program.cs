using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Sv.Host;
using Sv.Rules;
using Ref = Sv.Rules.Reference;

namespace Sv.Pack
{
    internal static class Program
    {
        const string ToolVersion = "3.0.0";
        const string KeyFile = ".svpack.key";
        const int Ok = 0;
        const int Failed = 1;
        const int Usage = 2;

        internal static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                return Help();
            }

            try
            {
                switch (args[0])
                {
                    case "pack": return PackCommand(new Arguments(args));
                    case "run": return RunCommand(new Arguments(args));
                    case "trace": return TraceCommand(new Arguments(args));
                    default: return Help();
                }
            }
            catch (ScenarioError error)
            {
                Console.Error.WriteLine(error.Message);
                return Failed;
            }
            catch (IOException error)
            {
                Console.Error.WriteLine("sv: " + error.Message);
                return Failed;
            }
            catch (UnauthorizedAccessException error)
            {
                Console.Error.WriteLine("sv: " + error.Message);
                return Failed;
            }
        }

        static int Help()
        {
            Console.Error.WriteLine("usage:");
            Console.Error.WriteLine("  sv pack <folder> [--out <dir>] [--namespace <ns>] [--diagnostics text|json]");
            Console.Error.WriteLine("  sv run <pack> <scenario|folder> [--host reference|compiled] [--kernels <dll>] [--workers <n>] [--parts <n>] [--policy shared|dense]");
            Console.Error.WriteLine("  sv trace <pack> <scenario> [--entity <name>] [--frame <n>]");
            return Usage;
        }

        static PackPlan? Plan(PackSource source, string file, string format)
        {
            var compiled = PackCompiler.Compile(source);
            Report(compiled.Diagnostics, file, format);
            return compiled.Value;
        }

        static void Report(Diagnostic[] diagnostics, string folder, string format)
        {
            if (format == "json")
            {
                var json = new JsonWriter();
                json.BeginArray();
                for (var i = 0; i < diagnostics.Length; i++)
                {
                    var diagnostic = diagnostics[i];
                    json.BeginObject()
                        .Key("file").String(Path.Combine(folder, diagnostic.File))
                        .Key("offset").Int(diagnostic.Offset)
                        .Key("severity").String(diagnostic.Severity == Severity.Error ? "error" : "warning")
                        .Key("code").String("SV" + ((int)diagnostic.Code).ToString("D4", CultureInfo.InvariantCulture))
                        .Key("rule").Int(diagnostic.RuleId)
                        .Key("other").Int(diagnostic.OtherRuleId)
                        .Key("message").String(diagnostic.Message)
                        .EndObject();
                }

                json.EndArray();
                Console.Out.WriteLine(json.ToString());
                return;
            }

            for (var i = 0; i < diagnostics.Length; i++)
            {
                var diagnostic = diagnostics[i];
                var text = Path.Combine(folder, diagnostic.File) + "(" + diagnostic.Offset.ToString(CultureInfo.InvariantCulture) + "): "
                    + (diagnostic.Severity == Severity.Error ? "error" : "warning") + " SV" + ((int)diagnostic.Code).ToString("D4", CultureInfo.InvariantCulture)
                    + ": " + diagnostic.Message;
                if (diagnostic.Severity == Severity.Error)
                {
                    Console.Error.WriteLine(text);
                }
                else
                {
                    Console.Out.WriteLine(text);
                }
            }
        }

        static int PackCommand(Arguments arguments)
        {
            var folder = arguments.Positional(1);
            if (folder == null)
            {
                return Help();
            }

            var output = arguments.Option("--out") ?? Path.Combine(folder, "Generated");
            var rootNamespace = arguments.Option("--namespace") ?? "Game.Sv";
            var format = arguments.Option("--diagnostics") ?? "text";
            var source = PackFolder.Read(folder);
            var key = PackFolder.InputKey(source, rootNamespace, ToolVersion);
            var keyPath = Path.Combine(output, KeyFile);
            if (File.Exists(keyPath) && File.ReadAllText(keyPath) == key)
            {
                Console.Out.WriteLine("sv pack: " + folder + " unchanged (" + key + ")");
                return Ok;
            }

            var plan = Plan(source, folder, format);
            if (plan == null)
            {
                return Failed;
            }

            var files = new List<EmittedFile>();
            if (!EmitBridge.TryEmit(plan, rootNamespace, files, out var error))
            {
                Console.Error.WriteLine(Path.Combine(folder, PackSource.RulesFile) + "(0): error SV0900: " + error);
                return Failed;
            }

            files.Add(HostGlue.Emit(rootNamespace));
            Directory.CreateDirectory(output);
            var written = 0;
            for (var i = 0; i < files.Count; i++)
            {
                written += PackFolder.WriteIfChanged(Path.Combine(output, files[i].Name), files[i].Text) ? 1 : 0;
            }

            written += PackFolder.WriteIfChanged(Path.Combine(output, PackBinary.FileName), PackBinary.Write(plan.Pack, plan.Hash)) ? 1 : 0;
            PackFolder.WriteIfChanged(keyPath, key);
            Console.Out.WriteLine("sv pack: " + folder + " -> " + output + ", " + (files.Count + 1).ToString(CultureInfo.InvariantCulture)
                + " files, " + written.ToString(CultureInfo.InvariantCulture) + " written, PackHash " + ScenarioRunner.Hex(plan.Hash));
            return Ok;
        }

        static List<string> ScenarioFiles(string path)
        {
            var files = new List<string>();
            if (Directory.Exists(path))
            {
                files.AddRange(Directory.GetFiles(path, "*" + Scenario.Suffix));
                files.Sort(string.CompareOrdinal);
            }
            else
            {
                files.Add(path);
            }

            return files;
        }

        static int RunCommand(Arguments arguments)
        {
            var packPath = arguments.Positional(1);
            var scenarioPath = arguments.Positional(2);
            if (packPath == null || scenarioPath == null)
            {
                return Help();
            }

            var plan = Plan(PackFolder.Read(packPath), packPath, "text");
            if (plan == null)
            {
                return Failed;
            }

            var host = arguments.Option("--host") ?? "reference";
            var kernels = arguments.Option("--kernels");
            if (host == "compiled" && kernels == null)
            {
                Console.Error.WriteLine("sv run: --host compiled needs --kernels <assembly built from the pack's generated sources>");
                return Usage;
            }

            var failures = 0;
            foreach (var file in ScenarioFiles(scenarioPath))
            {
                var scenario = ScenarioReader.Read(file, File.ReadAllText(file), plan.Schema);
                var outcome = host == "compiled"
                    ? ScenarioRunner.Compiled(kernels!, plan, scenario, Options(arguments))
                    : ScenarioRunner.Reference(plan, scenario);
                Console.Out.WriteLine("scenario " + scenario.Name + " (" + host + ")");
                for (var i = 0; i < outcome.Lines.Count; i++)
                {
                    Console.Out.WriteLine("  " + outcome.Lines[i]);
                }

                for (var i = 0; i < outcome.Failures.Count; i++)
                {
                    Console.Out.WriteLine("  FAIL " + outcome.Failures[i]);
                }

                failures += outcome.Passed ? 0 : 1;
            }

            return failures == 0 ? Ok : Failed;
        }

        static HostOptions Options(Arguments arguments)
        {
            var options = new HostOptions
            {
                Workers = arguments.Int("--workers", 1),
                Parts = arguments.Int("--parts", 64),
            };
            options.Policy = arguments.Option("--policy") == "dense" ? StoragePolicy.Dense : StoragePolicy.Shared;
            return options;
        }

        static int TraceCommand(Arguments arguments)
        {
            var packPath = arguments.Positional(1);
            var scenarioPath = arguments.Positional(2);
            if (packPath == null || scenarioPath == null)
            {
                return Help();
            }

            var plan = Plan(PackFolder.Read(packPath), packPath, "text");
            if (plan == null)
            {
                return Failed;
            }

            var scenario = ScenarioReader.Read(scenarioPath, File.ReadAllText(scenarioPath), plan.Schema);
            var onlyFrame = arguments.Option("--frame") == null ? -1L : arguments.Int("--frame", -1);
            var traces = new List<Ref.Trace>();
            var outcome = ScenarioRunner.Reference(plan, scenario, frame =>
            {
                if (onlyFrame >= 0 && frame != onlyFrame)
                {
                    return null;
                }

                var trace = new Ref.Trace();
                traces.Add(trace);
                return trace;
            });
            var entity = arguments.Option("--entity");
            var token = string.Empty;
            if (entity != null)
            {
                for (var e = 0; e < scenario.Entities.Count; e++)
                {
                    if (string.CompareOrdinal(scenario.Entities[e].Name, entity) == 0)
                    {
                        token = Ref.Trace.Slot(scenario.Entities[e].Slot);
                    }
                }

                if (token.Length == 0)
                {
                    Console.Error.WriteLine("sv trace: unknown entity '" + entity + "'");
                    return Usage;
                }
            }

            for (var t = 0; t < traces.Count; t++)
            {
                var lines = traces[t].Lines;
                for (var i = 0; i < lines.Count; i++)
                {
                    if (token.Length == 0 || i == 0 || Mentions(lines[i], token))
                    {
                        Console.Out.WriteLine(lines[i]);
                    }
                }
            }

            for (var i = 0; i < outcome.Lines.Count; i++)
            {
                Console.Out.WriteLine(outcome.Lines[i]);
            }

            for (var i = 0; i < outcome.Failures.Count; i++)
            {
                Console.Out.WriteLine("FAIL " + outcome.Failures[i]);
            }

            return outcome.Passed ? Ok : Failed;
        }

        static bool Mentions(string line, string token)
        {
            var at = line.IndexOf(token, StringComparison.Ordinal);
            while (at >= 0)
            {
                var end = at + token.Length;
                if (end == line.Length || !char.IsDigit(line[end]))
                {
                    return true;
                }

                at = line.IndexOf(token, end, StringComparison.Ordinal);
            }

            return false;
        }

        sealed class Arguments
        {
            readonly string[] args;

            public Arguments(string[] args)
            {
                this.args = args;
            }

            public string? Positional(int index)
            {
                var seen = 0;
                for (var i = 0; i < args.Length; i++)
                {
                    if (args[i].StartsWith("--", StringComparison.Ordinal))
                    {
                        i++;
                        continue;
                    }

                    if (seen == index)
                    {
                        return args[i];
                    }

                    seen++;
                }

                return null;
            }

            public string? Option(string name)
            {
                for (var i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] == name)
                    {
                        return args[i + 1];
                    }
                }

                return null;
            }

            public int Int(string name, int fallback)
            {
                var text = Option(name);
                return text != null && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
            }
        }
    }
}
