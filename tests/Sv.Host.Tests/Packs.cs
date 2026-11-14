using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sv.Pack;
using Sv.Rules.Emit;

namespace Sv.Host.Tests;

public static class Packs
{
    static readonly Dictionary<string, PackPlan> plans = new();
    static readonly Dictionary<string, Type> kernels = new();

    public static string RepositoryRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder != null && !Directory.Exists(Path.Combine(folder.FullName, "packs", "reference")))
            {
                folder = folder.Parent;
            }

            return folder?.FullName ?? throw new DirectoryNotFoundException("no packs/reference above " + AppContext.BaseDirectory);
        }
    }

    public static PackPlan Reference => Folder(Path.Combine(RepositoryRoot, "packs", "reference"));

    public static PackPlan Folder(string folder)
    {
        lock (plans)
        {
            if (!plans.TryGetValue(folder, out var plan))
            {
                plan = Plan(PackFolder.Read(folder));
                plans.Add(folder, plan);
            }

            return plan;
        }
    }

    public static PackPlan Plan(PackSource source)
    {
        var compiled = PackCompiler.Compile(source);
        return compiled.Value ?? throw new InvalidOperationException("pack does not compile:\n" + string.Join("\n", compiled.Diagnostics));
    }

    public static Type Kernels(string rootNamespace, PackPlan plan)
    {
        lock (kernels)
        {
            if (kernels.TryGetValue(rootNamespace, out var type))
            {
                return type;
            }

            var files = PackEmitter.Emit(plan, rootNamespace);
            var glue = HostGlue.Emit(rootNamespace);
            files.Add(new KeyValuePair<string, string>(glue.Name, glue.Text));
            var parse = new CSharpParseOptions(LanguageVersion.CSharp9);
            var trees = files.Where(file => file.Key.EndsWith(".g.cs", StringComparison.Ordinal))
                .Select(file => CSharpSyntaxTree.ParseText(file.Value, parse, rootNamespace + "/" + file.Key))
                .ToList();
            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithAllowUnsafe(true)
                .WithOptimizationLevel(OptimizationLevel.Release)
                .WithNullableContextOptions(NullableContextOptions.Disable)
                .WithSpecificDiagnosticOptions(new Dictionary<string, ReportDiagnostic> { ["CS1701"] = ReportDiagnostic.Suppress, ["CS1702"] = ReportDiagnostic.Suppress });
            var compilation = CSharpCompilation.Create("HostTests." + rootNamespace, trees, References(), options);
            using var image = new MemoryStream();
            var result = compilation.Emit(image);
            if (!result.Success)
            {
                throw new InvalidOperationException("generated kernels do not compile:\n" + string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            }

            type = Assembly.Load(image.ToArray()).GetType(rootNamespace + "." + HostGlue.TypeName, true)!;
            kernels.Add(rootNamespace, type);
            return type;
        }
    }

    static MetadataReference[] References()
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        return paths
            .Where(path => Path.GetFileName(path).StartsWith("System.", StringComparison.Ordinal)
                || Path.GetFileName(path) is "netstandard.dll" or "mscorlib.dll" or "Sv.Core.dll" or "Sv.Host.dll")
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    public static object? Call(Type owner, string method, Type kernelsType, params object[] arguments)
    {
        var generic = owner.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(owner.Name, method);
        try
        {
            return generic.MakeGenericMethod(kernelsType).Invoke(null, arguments);
        }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    public static int Template(PackPlan plan, string name)
    {
        var templates = plan.Schema.Templates;
        for (var t = 0; t < templates.Length; t++)
        {
            if (string.CompareOrdinal(templates[t].Name, name) == 0)
            {
                return t;
            }
        }

        throw new ArgumentException("no template " + name);
    }

    public static int Id(PackPlan plan, ValueClass valueClass, string name) => plan.Schema.FindByName(valueClass, name)!.Id;

    public static string Hex(ulong value) => ScenarioRunner.Hex(value);

    public static HostOptions Options(int workers = 1, int parts = 64, StoragePolicy policy = StoragePolicy.Shared, int capacity = 1 << 16) =>
        new HostOptions { Workers = workers, Parts = parts, Policy = policy, EntityCapacity = capacity };
}
