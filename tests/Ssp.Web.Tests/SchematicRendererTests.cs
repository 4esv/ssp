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
}
