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
}
