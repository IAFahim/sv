using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace Sv.Tests;

// Roadmap issues/0058, atom 0.2: drives every public window API against guard-page
// columns in the Sv.Fuzz child process. A case that reads or writes outside the
// memory it was offered dies on the PROT_NONE page (nonzero exit); the table in
// Sv.Fuzz declares what TODAY does — the crash expectations ARE the known defects
// (F1 Frent window offset/wordCount, F2 multi-word tail, F3 span-length trust, F23
// range overflow). Atom 1.1 flips the table to ok/threw and this fact proves it.

public class GuardPageFuzzTests
{
    [Fact]
    public void GuardPageFuzz_PinsTodaysDefectSet()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var dll = Path.Combine(AppContext.BaseDirectory, "Sv.Fuzz.dll");
        Assert.True(File.Exists(dll), $"sv: Sv.Fuzz.dll not found next to the tests at {dll}");

        var cases = new List<(string Id, string Expected)>();
        foreach (var line in RunFuzz(dll, "--list").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|');
            cases.Add((parts[0], parts[1]));
        }

        Assert.True(cases.Count >= 20, $"sv: fuzz table too small ({cases.Count} cases)");

        var mismatches = new List<string>();
        foreach (var (id, expected) in cases)
        {
            var (exit, output) = RunFuzzWithOutput(dll, $"--case {id}");
            var line = LastLine(output);
            string got;
            if (exit != 0)
            {
                got = "crash";
            }
            else if (line.StartsWith("threw", StringComparison.Ordinal))
            {
                got = "threw";
            }
            else if (line == "ok")
            {
                got = "ok";
            }
            else
            {
                got = $"corrupt-output:{line}";
            }

            if (got != expected)
            {
                mismatches.Add($"{id}: expected {expected}, got {got} (exit {exit}, stdout '{line}')");
            }
        }

        Assert.True(mismatches.Count == 0, "sv: guard-page fuzz disagrees with the pinned defect set:\n" + string.Join('\n', mismatches));
    }

    static string LastLine(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.Length == 0 ? "" : lines[^1].Trim();
    }

    static string RunFuzz(string dll, string args)
    {
        return RunFuzzWithOutput(dll, args).Output;
    }

    static (int Exit, string Output) RunFuzzWithOutput(string dll, string args)
    {
        var start = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(dll);
        foreach (var arg in args.Split(' '))
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output.Trim());
    }
}
