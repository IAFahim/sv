using System;
using System.IO;
using System.Xml.Linq;
using Xunit;

public sealed class CoreDependencyTests
{
    [Fact]
    public void SvCore_ReferencesNothing()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "sv.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        var project = XDocument.Load(Path.Combine(dir, "src", "Sv", "Sv.csproj"));
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Empty(project.Descendants("ProjectReference"));
    }
}
