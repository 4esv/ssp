using Ssp.Core.Layout;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Schematic;
using LayoutDoc = Ssp.Core.Layout.Layout;
using PartsApi = Ssp.Core.Parts.Parts;

namespace Ssp.Web.Tests;

public class SchematicRendererTests
{
    static readonly PartsTable Table = PartsApi.Load(Path.Combine(RepoPaths.Root, "models", "parts.toml"));

    static string Fixture(string name) => Path.Combine(RepoPaths.Root, "circuits", "fixtures", name);

    static string Render(string netlist, LayoutDoc layout)
    {
        var circuit = NetlistLoader.Load(netlist);
        return SchematicRenderer.ToSvg(circuit, layout, PartMap.Resolve(circuit, Table));
    }

    static string RenderDivider() => Render(
        File.ReadAllText(Fixture("divider-basic.cir")),
        LayoutDoc.Read(Fixture("divider-basic.layout.toml")));

    const string DiodeNetlist = "* ssp:part D1 1N4148\nV1 a 0 5\nD1 a b DGEN\nR1 b 0 1k\n.MODEL DGEN D\n.END\n";

    [Fact]
    public void FixtureSchematicEqualsGolden()
    {
        Golden.Assert("schematic-divider.svg", RenderDivider());
    }

    [Fact]
    public void OutputIsTheSameEachTime()
    {
        Assert.Equal(RenderDivider(), RenderDivider());
    }

    [Fact]
    public void RotationShowsInTheTransform()
    {
        var svg = Render(DiodeNetlist, new LayoutDoc([new PartPlacement("D1", 30, 40, 90, false)], []));

        Assert.Contains("<g data-ref=\"D1\" transform=\"translate(30 40) rotate(-90)\">", svg);
    }

    [Fact]
    public void FlipShowsInTheTransform()
    {
        var svg = Render(DiodeNetlist, new LayoutDoc([new PartPlacement("D1", 30, 40, 180, true)], []));

        Assert.Contains("<g data-ref=\"D1\" transform=\"translate(30 40) rotate(-180) scale(1 -1)\">", svg);
    }

    [Fact]
    public void MappedPartUsesItsSymbol()
    {
        var svg = Render(DiodeNetlist, new LayoutDoc([new PartPlacement("D1", 0, 0, 0, false)], []));

        Assert.Contains("M20 -10V10L40 0Z", svg);
    }

    [Fact]
    public void EachLayoutWireIsAPolyline()
    {
        var svg = Render(DiodeNetlist, new LayoutDoc([], [new WireRoute("b", [new Point(0, 0), new Point(20, 0), new Point(20, 30)])]));

        Assert.Contains("<polyline data-net=\"b\" points=\"0,0 20,0 20,30\"/>", svg);
    }

    [Fact]
    public void UnplacedComponentIsNotDrawn()
    {
        var svg = Render(DiodeNetlist, new LayoutDoc([new PartPlacement("R1", 0, 0, 0, false)], []));

        Assert.Contains("data-ref=\"R1\"", svg);
        Assert.DoesNotContain("data-ref=\"D1\"", svg);
    }

    public static TheoryData<string, int, bool> Orientations() => SymbolSetTests.Orientations();

    [Theory]
    [MemberData(nameof(Orientations))]
    public void SymbolEqualsGolden(string kind, int rotation, bool flip)
    {
        var svg = SchematicRenderer.SymbolSvg(kind, new PartPlacement("P1", 0, 0, rotation, flip));
        Golden.Assert($"symbol-{kind}-r{rotation}{(flip ? "-flip" : "")}.svg", svg);
    }

    public static TheoryData<string> VisualFixtures() =>
        new() { "rc-lowpass.cir", "bjt-ce-bias.cir", "opamp-buffer.cir", "pot-lowpass.cir", "clipper-bjt-si.cir" };

    // opamp-buffer.cir names its model OPAMP. Use the TL072.
    static LoadedCircuit LoadFixture(string name)
    {
        var netlist = File.ReadAllText(Fixture(name));
        if (netlist.Contains("OPAMP", StringComparison.Ordinal))
        {
            netlist = File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "opamp-tl072.cir")) + "\n" + netlist.Replace("OPAMP", "TL072");
        }
        return NetlistLoader.Load(netlist);
    }

    [Theory]
    [MemberData(nameof(VisualFixtures))]
    public void FixtureWithAutoLayoutEqualsGolden(string name)
    {
        var circuit = LoadFixture(name);
        var svg = SchematicRenderer.ToSvg(circuit, AutoPlacer.Place(circuit, circuit.Directives), PartMap.Resolve(circuit, Table));
        Golden.Assert($"schematic-{Path.GetFileNameWithoutExtension(name)}.svg", svg);
    }

    static Dictionary<string, SchematicElement> Elements(LoadedCircuit circuit) =>
        SchematicRenderer.Elements(circuit, PartMap.Resolve(circuit, Table)).ToDictionary(e => e.Reference, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void KindsComeFromTheComponentType()
    {
        var e = Elements(NetlistLoader.Load(
            "* ssp:part D2 LED-RED\nV1 a 0 DC 1 AC 1\nR1 a b 4.7k\nC1 b 0 10n\nC2 b c 22u\nL1 c 0 100m\nD1 c 0 DGEN\nD2 c 0 LED_RED\n" +
            "I1 0 c 1m\nQ1 c b 0 0 QN\nQ2 c b 0 0 QP\nJ1 c b 0 JN\nJ2 c b 0 JP\n" +
            ".MODEL DGEN D\n.MODEL LED_RED D\n.MODEL QN NPN\n.MODEL QP PNP\n.MODEL JN NJF\n.MODEL JP PJF\n.END\n"));

        Assert.Equal("vsource", e["V1"].Kind);
        Assert.Equal("resistor", e["R1"].Kind);
        Assert.Equal("capacitor", e["C1"].Kind);
        Assert.Equal("electrolytic", e["C2"].Kind);
        Assert.Equal("inductor", e["L1"].Kind);
        Assert.Equal("diode", e["D1"].Kind);
        Assert.Equal("led", e["D2"].Kind);
        Assert.Equal("isource", e["I1"].Kind);
        Assert.Equal("npn", e["Q1"].Kind);
        Assert.Equal("pnp", e["Q2"].Kind);
        Assert.Equal("njf", e["J1"].Kind);
        Assert.Equal("pjf", e["J2"].Kind);
    }

    [Fact]
    public void OpampSubcircuitIsOneOpampSymbol()
    {
        var circuit = LoadFixture("opamp-buffer.cir");
        var e = Elements(circuit);

        Assert.Equal("opamp5", e["X1"].Kind);
        Assert.Equal(["in", "out", "out", "vcc", "vee"], e["X1"].Nodes);
        Assert.Equal("TL072", e["X1"].Value);
        Assert.DoesNotContain(e.Keys, k => k.StartsWith("X1.", StringComparison.OrdinalIgnoreCase));

        var svg = SchematicRenderer.ToSvg(circuit, AutoPlacer.Place(circuit, circuit.Directives), PartMap.Resolve(circuit, Table));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(svg, "data-ref=\"X1"));
    }

    [Fact]
    public void PotPairIsOnePot()
    {
        var e = Elements(LoadFixture("pot-lowpass.cir"));

        Assert.Equal("pot", e["RV1"].Kind);
        Assert.Equal(["in", "out", "0"], e["RV1"].Nodes);
        Assert.Equal("10k", e["RV1"].Value);
        Assert.False(e.ContainsKey("RV1_1"));
        Assert.False(e.ContainsKey("RV1_2"));
    }

    [Fact]
    public void GroundedSupplySourceIsARailWithItsVoltage()
    {
        var e = Elements(LoadFixture("opamp-buffer.cir"));

        Assert.Equal("rail", e["Vcc"].Kind);
        Assert.Equal("+15V", e["Vcc"].Value);
        Assert.Equal(["vcc"], e["Vcc"].Nodes);
        Assert.Equal("rail", e["Vee"].Kind);
        Assert.Equal("-15V", e["Vee"].Value);
        Assert.Equal("vsource", e["V1"].Kind);
    }

    [Theory]
    [InlineData(100e3, "100k")]
    [InlineData(4.7e3, "4.7k")]
    [InlineData(10e-9, "10n")]
    [InlineData(1e-6, "1u")]
    [InlineData(100e-9, "100n")]
    [InlineData(22e-6, "22u")]
    [InlineData(820, "820")]
    [InlineData(1e6, "1M")]
    [InlineData(1e12, "1T")]
    [InlineData(0.1, "100m")]
    [InlineData(47e-12, "47p")]
    [InlineData(0, "0")]
    [InlineData(-15, "-15")]
    public void ValuesAreInPlainUnits(double value, string text) => Assert.Equal(text, SchematicRenderer.Plain(value));

    [Fact]
    public void LabelsShowReferenceAndValueAndStayUpright()
    {
        var svg = Render(File.ReadAllText(Fixture("rc-lowpass.cir")), new LayoutDoc([new PartPlacement("R1", 0, 0, 90, true), new PartPlacement("C1", 100, 0, 270, false)], []));

        Assert.Contains(">R1</text>", svg);
        Assert.Contains(">1k</text>", svg);
        Assert.Contains(">100n</text>", svg);
        Assert.DoesNotContain("E+", svg);
        // Labels are outside the rotated group.
        Assert.DoesNotMatch("<g data-ref[^>]*>[^\\n]*<text", svg);
    }

    [Fact]
    public void GroundedPinGetsAGroundSymbolButTheHiddenSubstrateDoesNot()
    {
        var bjt = Render(File.ReadAllText(Fixture("bjt-ce-bias.cir")), new LayoutDoc([new PartPlacement("Q1", 0, 0, 0, false)], []));
        var resistor = Render(File.ReadAllText(Fixture("bjt-ce-bias.cir")), new LayoutDoc([new PartPlacement("RE", 0, 0, 0, false)], []));

        Assert.DoesNotContain("<g transform=\"translate(", bjt);
        Assert.Contains("<g transform=\"translate(60 0)\">", resistor);
    }
}
