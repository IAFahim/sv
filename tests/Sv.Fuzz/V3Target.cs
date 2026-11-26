using System.Text.Json.Nodes;
using Sv.Rules;
using Sv.Rules.Emit;

namespace Sv.Fuzz;

public static class V3Target
{
    public static readonly string[] Files = { PackSource.ValuesFile, PackSource.RulesFile, PackSource.CardsFile, PackSource.TemplatesFile };

    public static List<JsonNode[]> Seeds(string repo)
    {
        var folders = new[] { "packs/reference", "packs/lego", "tests/Sv.Kernels.NorthStar3/pack", "tests/Sv.Kernels.Sentences/pack" };
        var seeds = new List<JsonNode[]>();
        foreach (var folder in folders)
        {
            var path = Path.Combine(repo, folder);
            if (Directory.Exists(path))
            {
                seeds.Add(Files.Select(f => JsonNode.Parse(File.ReadAllText(Path.Combine(path, f)))!).ToArray());
            }
        }

        return seeds;
    }

    public static string Run(string[] texts)
    {
        var source = new PackSource(texts[0], texts[1], texts[2], texts[3]);
        Checked<PackPlan> compiled;
        try
        {
            compiled = PackCompiler.Compile(source);
        }
        catch (Exception error)
        {
            throw new Finding("v3-compile-threw", error.GetType().Name + ": " + error.Message + " at " + Frame(error));
        }

        if (compiled.Value == null)
        {
            if (compiled.Diagnostics.Length == 0)
            {
                throw new Finding("v3-empty-diagnostics", "a pack was refused with no diagnostic");
            }

            return "rejected";
        }

        var plan = compiled.Value;
        PackSource written;
        try
        {
            written = PackWriter.Write(plan.Pack);
        }
        catch (Exception error)
        {
            throw new Finding("v3-write-threw", error.GetType().Name + ": " + error.Message + " at " + Frame(error));
        }

        var again = PackCompiler.Compile(written);
        if (again.Value == null)
        {
            throw new Finding("v3-roundtrip-refused", "the written pack does not compile: " + string.Join(" | ", again.Diagnostics.Select(d => d.ToString()).Take(3)));
        }

        if (again.Value.Hash != plan.Hash)
        {
            throw new Finding("v3-roundtrip-hash", "PackHash " + plan.Hash.ToString("X16") + " became " + again.Value.Hash.ToString("X16") + " after a write and read");
        }

        try
        {
            ColumnsTarget.CheckCode(TlGlueEmitter.Emit(plan, "Fz"), "v3 tl glue");
            ColumnsTarget.CheckCode(TlGlueEmitter.EmitEntities(plan, "Fz"), "v3 tl glue entities");
            foreach (var file in PackEmitter.Emit(plan, "Fz"))
            {
                if (file.Key.EndsWith(".txt", StringComparison.Ordinal))
                {
                    CheckMap(file.Value, "v3 " + file.Key);
                }
                else
                {
                    ColumnsTarget.CheckCode(file.Value, "v3 " + file.Key);
                }
            }
        }
        catch (Finding)
        {
            throw;
        }
        catch (Exception error)
        {
            throw new Finding("v3-emit-threw", error.GetType().Name + ": " + error.Message + " at " + Frame(error));
        }

        return "accepted";
    }

    static void CheckMap(string text, string where)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length - 1; i++)
        {
            var fields = lines[i].Split('\t');
            if (fields.Length != 6)
            {
                throw new Finding("map-fields", where + " line " + (i + 1) + " has " + fields.Length + " tab fields, not 6");
            }

            foreach (var c in lines[i])
            {
                if (c != '\t' && (c < ' ' || c == '\u007F' || c == '\u0085' || c == '\u2028' || c == '\u2029'))
                {
                    throw new Finding("map-raw-char", where + " line " + (i + 1) + " holds raw U+" + ((int)c).ToString("X4"));
                }
            }
        }
    }

    static string Frame(Exception error) => (error.StackTrace ?? string.Empty).Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
}
