using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Ssp.Core.Tests;

public class EngineInfoTests
{
    [Fact]
    public void PackagesListSpiceSharp323()
    {
        Assert.Contains(("SpiceSharp", "3.2.3"), EngineInfo.Packages());
    }

    [Fact]
    public void PackagesMatchGoldenFile()
    {
        var text = string.Join("\n", EngineInfo.Packages().Select(p => $"{p.Package} {p.Version}")) + "\n";
        Golden.Assert("engine-packages", text);
    }

    // Semantic Versioning 2.0.0, the official regular expression from semver.org.
    private static readonly Regex SemVer = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$",
        RegexOptions.CultureInvariant);

    private static string SourceVersion() =>
        XDocument.Load(Path.Combine(RepoPaths.Root, "Directory.Build.props"))
            .Descendants("Version").Single().Value.Trim();

    [Fact]
    public void SourceVersionIsValidSemVer()
    {
        var version = SourceVersion();
        Assert.True(SemVer.IsMatch(version), $"'{version}' is not valid Semantic Versioning 2.0.0.");
    }

    [Fact]
    public void ProductVersionEqualsSourceVersion()
    {
        var version = SourceVersion();
        var plus = version.IndexOf('+');
        Assert.Equal(plus < 0 ? version : version[..plus], EngineInfo.ProductVersion);
    }
}
