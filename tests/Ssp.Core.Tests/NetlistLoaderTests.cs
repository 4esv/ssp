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

    // NOTE: #384: text after .END gives a plain message, not the parser's token words.
    [Fact]
    public void TextAfterEndGivesAPlainMessage()
    {
        const string netlist = "* t\nV1 in 0 DC 1\nR1 in out 1k\n.END\nstray text\n";

        var loaded = NetlistLoader.Load(netlist);

        var diagnostic = Assert.Single(loaded.Diagnostics);
        Assert.Equal(Severity.Error, diagnostic.Severity);
        Assert.Equal(5, diagnostic.Line);
        Assert.Equal("The text after .END is not read. Remove it.", diagnostic.Message);
        Assert.Contains(".END", diagnostic.Message);
        Assert.DoesNotContain("token", diagnostic.Message);
        Assert.DoesNotContain("expected", diagnostic.Message);
        Assert.DoesNotContain("column", diagnostic.Message);
    }

    [Fact]
    public void UnknownDirectiveGivesOneWarningNamingTheDirectiveAndLine()
    {
        const string netlist = "* ssp:frobnicate 3\nV1 in 0 1\nR1 in out 1k\n.END\n";

        var loaded = NetlistLoader.Load(netlist);

        var diagnostic = Assert.Single(loaded.Diagnostics);
        Assert.Equal(Severity.Warning, diagnostic.Severity);
        Assert.Contains("frobnicate", diagnostic.Message);
        Assert.Equal(1, diagnostic.Line);
    }

    [Fact]
    public void KnobWithNoArgumentsGivesOneWarning()
    {
        const string netlist = "* ssp:knob\nV1 in 0 1\nR1 in out 1k\n.END\n";

        var loaded = NetlistLoader.Load(netlist);

        var diagnostic = Assert.Single(loaded.Diagnostics);
        Assert.Equal(Severity.Warning, diagnostic.Severity);
        Assert.Contains("ssp:knob", diagnostic.Message);
        Assert.Equal(1, diagnostic.Line);
    }
}
