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

    // NOTE: #316: an X line carries the connected nodes (a, b, 0), not the names the .subckt declares. The renderer
    // needs the declared names, in definition order, to label an instance that has no symbol.
    [Fact]
    public void SubcircuitPinsAreTheDeclaredPinNamesInOrder()
    {
        const string Netlist = """
            * ssp:title Fuzz box
            .subckt FUZZ inp outp gnd params: gain=2
            R1 inp outp 1k
            .ends
            V1 a 0 1
            X1 a b 0 FUZZ
            R3 b 0 1k
            .END
            """;

        var loaded = NetlistLoader.Load(Netlist);

        Assert.Empty(loaded.Diagnostics);
        Assert.Equal(["inp", "outp", "gnd"], loaded.SubcircuitPins["fuzz"]);
        Assert.Equal(["a", "b", "0"], loaded.Subcircuits.Single(x => x.Name == "X1").Pins);
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
