using System.Xml.Linq;

namespace Tankstat.Reader.Core.UnitTests;

/// <summary>
/// The photo reader is a separate service: the main app's projects never reference it or its native packages (the main image stays
/// small, Alpine and multi-arch), and the reader never references the main app.
/// </summary>
public class ArchitectureTests
{
    private static readonly string[] ReaderOnlyPackages = ["SkiaSharp", "TorchSharp", "libtorch", "Tesseract"];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tankstat.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Tankstat.slnx not found above the test output.");
    }

    private static IEnumerable<(string Name, List<string> Packages, List<string> Projects)> Projects()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot(), "src", "backend"), "*.csproj", SearchOption.AllDirectories))
        {
            var xml = XDocument.Load(file);
            yield return (
                Path.GetFileNameWithoutExtension(file),
                xml.Descendants("PackageReference").Select(p => (string?)p.Attribute("Include") ?? "").ToList(),
                xml.Descendants("ProjectReference").Select(p => Path.GetFileNameWithoutExtension(((string?)p.Attribute("Include") ?? "").Replace('\\', '/'))).ToList());
        }
    }

    [Fact]
    public void TheMainProjects_NeverReferenceTheReaderOrItsNativePackages()
    {
        foreach (var (name, packages, projects) in Projects().Where(p => !p.Name.StartsWith("Tankstat.Reader", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain(packages, p => ReaderOnlyPackages.Any(r => p.StartsWith(r, StringComparison.OrdinalIgnoreCase)));
            Assert.DoesNotContain(projects, p => p.StartsWith("Tankstat.Reader", StringComparison.Ordinal));
            _ = name;
        }
    }

    [Fact]
    public void TheReader_NeverReferencesTheMainApp_AndItsCoreStaysFreeOfPackages()
    {
        var reader = Projects().Where(p => p.Name.StartsWith("Tankstat.Reader", StringComparison.Ordinal)).ToList();

        Assert.Contains(reader, p => p.Name == "Tankstat.Reader.Core");
        Assert.All(reader, p => Assert.All(p.Projects, r => Assert.StartsWith("Tankstat.Reader", r)));
        Assert.Empty(reader.Single(p => p.Name == "Tankstat.Reader.Core").Packages);
    }
}
