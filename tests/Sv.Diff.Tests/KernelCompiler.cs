using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sv.Pack;
using Sv.Rules;
using Sv.Rules.Emit;

namespace DiffTests
{
    public sealed class CompiledCase
    {
        public GeneratedCase Case = new GeneratedCase();
        public PackPlan? Plan;
        public List<KeyValuePair<string, string>> Files = new List<KeyValuePair<string, string>>();
        public Type? Kernels;
        public string Failure = string.Empty;
        public string Warnings = string.Empty;

        public bool Ready => Failure.Length == 0 && Plan != null && Kernels != null;
    }

    public static class KernelCompiler
    {
        static readonly Lazy<MetadataReference[]> references = new Lazy<MetadataReference[]>(() =>
        {
            var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
            return paths
                .Where(path => Path.GetFileName(path).StartsWith("System.", StringComparison.Ordinal)
                    || Path.GetFileName(path) is "System.dll" or "netstandard.dll" or "mscorlib.dll" or "Sv.Core.dll" or "Sv.Host.dll")
                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
                .ToArray();
        });

        public static List<CompiledCase> Build(IEnumerable<GeneratedCase> cases)
        {
            var compiled = new List<CompiledCase>();
            foreach (var generated in cases)
            {
                compiled.Add(Plan(generated));
            }

            var pending = compiled.Where(item => item.Failure.Length == 0).ToList();
            while (pending.Count > 0)
            {
                var failed = Compile(pending, out var assembly);
                if (failed.Count == 0)
                {
                    foreach (var item in pending)
                    {
                        item.Kernels = assembly!.GetType(item.Case.Namespace + "." + HostGlue.TypeName, true);
                    }

                    break;
                }

                pending = pending.Where(item => item.Failure.Length == 0).ToList();
            }

            return compiled;
        }

        static CompiledCase Plan(GeneratedCase generated)
        {
            var item = new CompiledCase { Case = generated };
            var checkedPlan = PackCompiler.Compile(generated.Source);
            if (checkedPlan.Value == null)
            {
                item.Failure = "pack rejected:\n" + string.Join("\n", checkedPlan.Diagnostics.Where(d => d.Severity == Severity.Error));
                return item;
            }

            item.Plan = checkedPlan.Value;
            try
            {
                item.Files = PackEmitter.Emit(item.Plan, generated.Namespace);
                var glue = HostGlue.Emit(generated.Namespace);
                item.Files.Add(new KeyValuePair<string, string>(glue.Name, glue.Text));
            }
            catch (Exception error)
            {
                item.Failure = "emitter threw: " + error.Message;
            }

            return item;
        }

        static List<CompiledCase> Compile(List<CompiledCase> items, out Assembly? assembly)
        {
            var parse = new CSharpParseOptions(LanguageVersion.CSharp9);
            var trees = new List<SyntaxTree>();
            var owner = new Dictionary<SyntaxTree, CompiledCase>();
            foreach (var item in items)
            {
                foreach (var file in item.Files.Where(file => file.Key.EndsWith(".g.cs", StringComparison.Ordinal)))
                {
                    var tree = CSharpSyntaxTree.ParseText(file.Value, parse, item.Case.Namespace + "/" + file.Key);
                    trees.Add(tree);
                    owner.Add(tree, item);
                }
            }

            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithAllowUnsafe(true)
                .WithOptimizationLevel(OptimizationLevel.Release)
                .WithNullableContextOptions(NullableContextOptions.Disable)
                .WithSpecificDiagnosticOptions(new Dictionary<string, ReportDiagnostic> { ["CS1701"] = ReportDiagnostic.Suppress, ["CS1702"] = ReportDiagnostic.Suppress });
            var compilation = CSharpCompilation.Create("Diff.Kernels." + Guid.NewGuid().ToString("N"), trees, references.Value, options);
            using var image = new MemoryStream();
            var result = compilation.Emit(image);
            var failed = new List<CompiledCase>();
            if (!result.Success)
            {
                foreach (var diagnostic in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                {
                    var tree = diagnostic.Location.SourceTree;
                    if (tree == null || !owner.TryGetValue(tree, out var item))
                    {
                        throw new InvalidOperationException("kernel compile failed outside any generated file: " + diagnostic);
                    }

                    if (item.Failure.Length == 0)
                    {
                        failed.Add(item);
                    }

                    item.Failure += (item.Failure.Length == 0 ? "generated C# does not compile:\n" : string.Empty) + diagnostic + "\n";
                }

                assembly = null;
                return failed;
            }

            foreach (var diagnostic in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning))
            {
                var tree = diagnostic.Location.SourceTree;
                if (tree != null && owner.TryGetValue(tree, out var item))
                {
                    item.Warnings += diagnostic + "\n";
                }
            }

            assembly = Assembly.Load(image.ToArray());
            return failed;
        }
    }
}
