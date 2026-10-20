using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

public class AllocationTests
{
    [Fact]
    public void NativeMemoryAlloc_CallsUseByteCountOverloads()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "sv.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(directory.FullName, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                if (Regex.IsMatch(line, @"NativeMemory\.(Alloc|AllocZeroed)\([^)]*,\s*(\(nuint\))?sizeof\(|NativeMemory\.(Alloc|AllocZeroed)\([^)]*,\s*16\)"))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{lineNumber}: {line.Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "two-argument NativeMemory.Alloc/AllocZeroed compute count × size, not alignment:\n" + string.Join("\n", offenders));
    }
}
