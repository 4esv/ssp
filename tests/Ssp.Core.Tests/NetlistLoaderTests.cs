using Ssp.Core.Netlist;

namespace Ssp.Core.Tests;

public class NetlistLoaderTests
{
    [Fact]
    public void LoadsRcLowpassWithThreeEntitiesAndThreeNodes()
    {
        var loaded = NetlistLoader.Load(Fixtures.Read("rc-lowpass.cir"));

        Assert.Empty(loaded.Diagnostics);
        Assert.Equal(3, loaded.Circuit.Count);
        Assert.Equal(new[] { "0", "in", "out" }, loaded.NodeNames.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void SyntaxErrorGivesOneErrorDiagnosticWithLineAndDoesNotThrow()
    {
        const string netlist = "bad netlist\nV1 in 0 1\nR1 in out 1k\nR2 out 0 (\n.END\n";

        var loaded = NetlistLoader.Load(netlist);

        var diagnostic = Assert.Single(loaded.Diagnostics);
        Assert.Equal(Severity.Error, diagnostic.Severity);
        Assert.Equal(4, diagnostic.Line);
    }
}
