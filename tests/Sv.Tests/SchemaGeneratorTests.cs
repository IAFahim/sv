using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sv;
using Sv.Generators;
using Xunit;

public class SchemaGeneratorTests
{
    static CSharpCompilation Compile(string source)
    {
        var sv = typeof(SchemaBuilder).Assembly.Location;
        var core = typeof(object).Assembly.Location;
        var runtime = Path.Combine(Path.GetDirectoryName(core)!, "System.Runtime.dll");
        return CSharpCompilation.Create(
            "SchemaHarness",
            new[] { CSharpSyntaxTree.ParseText(source) },
            new[]
            {
                MetadataReference.CreateFromFile(core),
                MetadataReference.CreateFromFile(runtime),
                MetadataReference.CreateFromFile(sv)
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    static Compilation Generate(string source, out Diagnostic[] diagnostics)
    {
        var compilation = Compile(source);
        CSharpGeneratorDriver.Create(new SvSchemaGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var generatorDiagnostics);
        diagnostics = generatorDiagnostics.ToArray();
        return updated;
    }

    static string GeneratedText(Compilation updated)
    {
        var tree = updated.SyntaxTrees.Single(tree => tree.FilePath.EndsWith("GameSchema.g.cs", StringComparison.Ordinal));
        return tree.ToString();
    }

    [Fact]
    public void HappyPath_CompilesAndEmits()
    {
        var updated = Generate(
            """
            using Sv;
            [SvSchema]
            [SvStat("power", 10f)]
            [SvStat("vigor", 3.5f)]
            [SvDerived("damage", 2f, new[] { "power", "vigor" }, new[] { 1f, 0.5f })]
            [SvIntrinsic("hp")]
            internal static partial class GameSchema { }
            """,
            out var diagnostics);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(updated.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var text = GeneratedText(updated);
        Assert.Contains("internal static partial class GameSchema", text);
        Assert.Contains("public static global::Sv.StatId power { get; }", text);
        Assert.Contains("public static global::Sv.IntrinsicId hp { get; }", text);
        Assert.Contains("builder.Derived(\"damage\", 2f, new (string, float)[] { (\"power\", 1f), (\"vigor\", 0.5f) });", text);
        Assert.Contains("public static float Getdamage(global::Sv.Board board, int slot) => board.Output(damage)[slot];", text);
    }

    [Fact]
    public void GeneratedText_IsDeterministic()
    {
        var first = GeneratedText(Generate(
            """
            using Sv;
            [SvSchema]
            [SvStat("power", 10f)]
            [SvDerived("damage", 2f, new[] { "power" }, new[] { 1f })]
            internal static partial class GameSchema { }
            """, out _));
        var second = GeneratedText(Generate(
            """
            using Sv;
            [SvSchema]
            [SvStat("power", 10f)]
            [SvDerived("damage", 2f, new[] { "power" }, new[] { 1f })]
            internal static partial class GameSchema { }
            """, out _));
        Assert.Equal(first, second);
    }

    [Fact]
    public void UnknownSource_FailsToCompile()
    {
        var updated = Generate(
            """
            using Sv;
            [SvSchema]
            [SvStat("power", 10f)]
            [SvDerived("damage", 2f, new[] { "strength" }, new[] { 1f })]
            internal static partial class GameSchema { }
            """,
            out var diagnostics);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SVSGEN02" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ForwardReference_FailsToCompile()
    {
        var updated = Generate(
            """
            using Sv;
            [SvSchema]
            [SvDerived("damage", 2f, new[] { "power" }, new[] { 1f })]
            [SvStat("power", 10f)]
            internal static partial class GameSchema { }
            """,
            out var diagnostics);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SVSGEN01" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SelfReference_FailsToCompile()
    {
        var updated = Generate(
            """
            using Sv;
            [SvSchema]
            [SvStat("power", 10f)]
            [SvDerived("damage", 2f, new[] { "damage" }, new[] { 1f })]
            internal static partial class GameSchema { }
            """,
            out var diagnostics);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SVSGEN01" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void DuplicateStat_FailsToCompile()
    {
        var updated = Generate(
            """
            using Sv;
            [SvSchema]
            [SvStat("power", 10f)]
            [SvStat("power", 11f)]
            internal static partial class GameSchema { }
            """,
            out var diagnostics);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SVSGEN03" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void StatAndIntrinsicClash_FailsToCompile()
    {
        var updated = Generate(
            """
            using Sv;
            [SvSchema]
            [SvStat("power", 10f)]
            [SvIntrinsic("power")]
            internal static partial class GameSchema { }
            """,
            out var diagnostics);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SVSGEN03" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void NonPartialClass_FailsToCompile()
    {
        var updated = Generate(
            """
            using Sv;
            [SvSchema]
            [SvStat("power", 10f)]
            internal static class GameSchema { }
            """,
            out var diagnostics);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SVSGEN04" && diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(updated.SyntaxTrees, tree => tree.FilePath.EndsWith("GameSchema.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void NonStaticClass_FailsToCompile()
    {
        var updated = Generate(
            """
            using Sv;
            [SvSchema]
            [SvStat("power", 10f)]
            internal partial class GameSchema { }
            """,
            out var diagnostics);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SVSGEN04" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void NestedClass_FailsToCompile()
    {
        var updated = Generate(
            """
            using Sv;
            internal static partial class Outer
            {
                [SvSchema]
                [SvStat("power", 10f)]
                internal static partial class GameSchema { }
            }
            """,
            out var diagnostics);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SVSGEN04" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void InvalidIdentifier_FailsToCompile()
    {
        var updated = Generate(
            """
            using Sv;
            [SvSchema]
            [SvStat("max power", 10f)]
            internal static partial class GameSchema { }
            """,
            out var diagnostics);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SVSGEN05" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void EmptyName_FailsToCompile()
    {
        var updated = Generate(
            """
            using Sv;
            [SvSchema]
            [SvStat("", 10f)]
            internal static partial class GameSchema { }
            """,
            out var diagnostics);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SVSGEN05" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void LengthMismatch_FailsToCompile()
    {
        var updated = Generate(
            """
            using Sv;
            [SvSchema]
            [SvStat("power", 10f)]
            [SvStat("vigor", 3f)]
            [SvDerived("damage", 2f, new[] { "power", "vigor" }, new[] { 1f })]
            internal static partial class GameSchema { }
            """,
            out var diagnostics);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SVSGEN06" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ZeroSources_FailsToCompile()
    {
        var updated = Generate(
            """
            using Sv;
            [SvSchema]
            [SvStat("power", 10f)]
            [SvDerived("damage", 2f, new string[0], new float[0])]
            internal static partial class GameSchema { }
            """,
            out var diagnostics);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SVSGEN06" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void NonFiniteCoefficient_FailsToCompile()
    {
        var updated = Generate(
            """
            using Sv;
            [SvSchema]
            [SvStat("power", 10f)]
            [SvDerived("damage", 2f, new[] { "power" }, new[] { float.NaN })]
            internal static partial class GameSchema { }
            """,
            out var diagnostics);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SVSGEN07" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void EmptySchema_FailsToCompile()
    {
        var updated = Generate(
            """
            using Sv;
            [SvSchema]
            internal static partial class GameSchema { }
            """,
            out var diagnostics);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SVSGEN08" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ReservedName_FailsToCompile()
    {
        var updated = Generate(
            """
            using Sv;
            [SvSchema]
            [SvStat("Schema", 10f)]
            internal static partial class GameSchema { }
            """,
            out var diagnostics);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "SVSGEN09" && diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
