using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Sv.Rules.Passes;

namespace Sv.Rules.Emit
{
    public static class PackEmitter
    {
        public static CompileUnit Compile(in PackSource source, CompileOptions? options = null)
        {
            var unit = new CompileUnit(options ?? new CompileOptions());
            unit.Source = source;
            return PassPipeline.Standard().Run(unit);
        }

        public static CompileUnit Compile(ContentPack pack, CompileOptions? options = null)
        {
            var unit = new CompileUnit(options ?? new CompileOptions());
            unit.Pack = pack;
            return PassPipeline.Standard().Run(unit);
        }

        public static List<KeyValuePair<string, string>> Emit(PackPlan plan, string rootNamespace)
        {
            var options = new CompileOptions();
            if (!string.IsNullOrEmpty(rootNamespace))
            {
                options.Namespace = rootNamespace;
            }

            var unit = Compile(plan.Pack, options);
            if (unit.Diagnostics.Errors > 0)
            {
                var text = new StringBuilder();
                for (var i = 0; i < unit.Diagnostics.Count; i++)
                {
                    if (unit.Diagnostics[i].Severity == Severity.Error)
                    {
                        text.Append(unit.Diagnostics[i].ToString()).Append('\n');
                    }
                }

                throw new InvalidOperationException("sv: the pack does not compile\n" + text);
            }

            var files = new List<KeyValuePair<string, string>>(unit.Files.Count);
            for (var i = 0; i < unit.Files.Count; i++)
            {
                files.Add(new KeyValuePair<string, string>(unit.Files[i].Name, unit.Files[i].Text));
            }

            return files;
        }

        public static PackSource ReadFolder(string folder)
        {
            return new PackSource(
                ReadOrEmpty(Path.Combine(folder, PackSource.ValuesFile)),
                ReadOrEmpty(Path.Combine(folder, PackSource.RulesFile)),
                ReadOrEmpty(Path.Combine(folder, PackSource.CardsFile)),
                ReadOrEmpty(Path.Combine(folder, PackSource.TemplatesFile)));
        }

        static string ReadOrEmpty(string path) => File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;

        public static string CacheKey(in PackSource source, CompileOptions options)
        {
            var hash = 14695981039346656037ul;
            hash = Mix(hash, source.Values);
            hash = Mix(hash, source.Rules);
            hash = Mix(hash, source.Cards);
            hash = Mix(hash, source.Templates);
            hash = Mix(hash, options.Namespace);
            hash = Mix(hash, options.ToolVersion);
            hash = Mix(hash, options.MethodCap.ToString(CultureInfo.InvariantCulture));
            return hash.ToString("X16", CultureInfo.InvariantCulture);
        }

        static ulong Mix(ulong hash, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            for (var i = 0; i < bytes.Length; i++)
            {
                hash = unchecked((hash ^ bytes[i]) * 1099511628211ul);
            }

            return unchecked((hash ^ 0xFFul) * 1099511628211ul);
        }

        public static int WriteChanged(string folder, List<EmittedFile> files)
        {
            Directory.CreateDirectory(folder);
            var written = 0;
            var encoding = new UTF8Encoding(false);
            for (var i = 0; i < files.Count; i++)
            {
                var path = Path.Combine(folder, files[i].Name);
                var bytes = encoding.GetBytes(files[i].Text.Replace("\r\n", "\n"));
                if (File.Exists(path) && Same(File.ReadAllBytes(path), bytes))
                {
                    continue;
                }

                File.WriteAllBytes(path, bytes);
                written++;
            }

            return written;
        }

        static bool Same(byte[] first, byte[] second)
        {
            if (first.Length != second.Length)
            {
                return false;
            }

            for (var i = 0; i < first.Length; i++)
            {
                if (first[i] != second[i])
                {
                    return false;
                }
            }

            return true;
        }

        public static int Build(string packFolder, string outputFolder, CompileOptions options, out Diagnostic[] diagnostics)
        {
            var source = ReadFolder(packFolder);
            var key = CacheKey(source, options);
            var keyPath = Path.Combine(outputFolder, "pack.key");
            if (File.Exists(keyPath) && string.CompareOrdinal(File.ReadAllText(keyPath, Encoding.UTF8), key) == 0)
            {
                diagnostics = new Diagnostic[0];
                return 0;
            }

            var unit = Compile(source, options);
            diagnostics = unit.Diagnostics.ToArray();
            if (unit.Diagnostics.Errors > 0)
            {
                return -1;
            }

            var files = new List<EmittedFile>(unit.Files);
            files.Add(new EmittedFile("pack.key", key));
            return WriteChanged(outputFolder, files);
        }
    }
}
