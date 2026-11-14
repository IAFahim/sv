using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Sv.Rules;
using Sv.Rules.Emit;

namespace Sv.Unity.Editor
{
    public sealed class SvPackAuthoring
    {
        public static readonly string[] FileNames =
        {
            PackSource.ValuesFile, PackSource.RulesFile, PackSource.CardsFile, PackSource.TemplatesFile,
        };

        readonly string[] texts = new string[FileNames.Length];

        public SvPackAuthoring(string folder)
        {
            Folder = folder;
            Load();
        }

        public string Folder { get; }

        public string Values { get => texts[0]; set => texts[0] = value; }

        public string Rules { get => texts[1]; set => texts[1] = value; }

        public string Cards { get => texts[2]; set => texts[2] = value; }

        public string Templates { get => texts[3]; set => texts[3] = value; }

        public string Text(int file) => texts[file];

        public void SetText(int file, string text) => texts[file] = text;

        public PackSource Source => new PackSource(Values, Rules, Cards, Templates);

        public void Load()
        {
            for (var i = 0; i < FileNames.Length; i++)
            {
                texts[i] = File.ReadAllText(Path.Combine(Folder, FileNames[i]));
            }
        }

        public void Save()
        {
            for (var i = 0; i < FileNames.Length; i++)
            {
                File.WriteAllText(Path.Combine(Folder, FileNames[i]), texts[i]);
            }
        }

        public Checked<PackPlan> Validate() => PackCompiler.Compile(Source);

        public static IEnumerable<string> Lines(Checked<PackPlan> result)
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                yield return diagnostic.File + "(" + diagnostic.Offset.ToString(CultureInfo.InvariantCulture) + "): "
                    + (diagnostic.Severity == Severity.Error ? "error " : "warning ") + diagnostic.Message;
            }
        }

        static readonly Regex NextLane = new Regex("\"nextLane\"\\s*:\\s*(\\d+)", RegexOptions.CultureInvariant);

        public void AddValue(string name, string valueClass, string defaultValue)
        {
            if (valueClass != "stat" && valueClass != "intrinsic")
            {
                throw new ArgumentException("add-value supports stat and intrinsic, got " + valueClass);
            }

            if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
            {
                throw new ArgumentException("value names are identifiers, got '" + name + "'");
            }

            var match = NextLane.Match(Values);
            if (!match.Success)
            {
                throw new InvalidOperationException("values.sv.json has no nextLane counter");
            }

            var id = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var close = Values.LastIndexOf(']');
            if (close < 0)
            {
                throw new InvalidOperationException("values.sv.json has no rows array");
            }

            var row = "{\"id\":" + id.ToString(CultureInfo.InvariantCulture) + ",\"name\":\"" + name + "\",\"class\":\"" + valueClass + "\",\"default\":" + defaultValue + "}";
            var before = Values.Substring(0, close).TrimEnd();
            var bumped = NextLane.Replace(before + ",\n" + row + "\n" + Values.Substring(close), "\"nextLane\":" + (id + 1).ToString(CultureInfo.InvariantCulture), 1);
            Values = bumped;
        }

        public List<KeyValuePair<string, string>>? Generate(string rootNamespace, out Checked<PackPlan> result)
        {
            result = Validate();
            return result.Value == null ? null : HostEmitter.Unity(result.Value, rootNamespace);
        }

        public static void WriteGenerated(string outputFolder, List<KeyValuePair<string, string>> files)
        {
            Directory.CreateDirectory(outputFolder);
            foreach (var file in files)
            {
                File.WriteAllBytes(Path.Combine(outputFolder, file.Key), new UTF8Encoding(false).GetBytes(file.Value.Replace("\r\n", "\n")));
            }
        }
    }
}
