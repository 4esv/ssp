using Ssp.Core.Layout;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Schematic;
using LayoutDoc = Ssp.Core.Layout.Layout;

namespace Ssp.Web.Tests;

// NOTE: #309: two rail-classified supplies on one node share a node key, so the rail lookup must keep one of them.
public class SchematicRailTests
{
    static readonly PartsTable Table = Ssp.Core.Parts.Parts.Parse(
        File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "parts.toml")), "parts.toml");

    static PartMap Map(LoadedCircuit circuit) => PartMap.Resolve(circuit, Table);

    // NOTE: VCC1 and VEE1 are both rails on n1.
    const string Static = "* schematic\nVCC1 n1 0 DC 9\nVEE1 n1 0 DC -9\nR1 n1 0 1k\n.END\n";

    [Fact]
    public void Two_rails_on_one_node_render()
    {
        var circuit = NetlistLoader.Load(Static);
        var layout = new LayoutDoc(
            [new PartPlacement("VCC1", 0, 0, 0, false), new PartPlacement("VEE1", 0, 60, 0, false), new PartPlacement("R1", 60, 0, 0, false)],
            []);

        var svg = SchematicRenderer.ToSvg(circuit, layout, Map(circuit));

        Assert.Contains("data-ref=\"VCC1\"", svg);
        Assert.Contains("data-ref=\"VEE1\"", svg);
    }

    // NOTE: Monkey seed 105: a drag that puts the n2 pin of R2 on the n1 pin of R1 merges n2 into n1, so both rails share n1.
    [Fact]
    public void A_drag_that_puts_two_rails_on_one_node_renders()
    {
        const string Netlist = "* schematic\nVCC1 n1 0 DC 9\nVEE1 n2 0 DC -9\nR1 n1 0 1k\nR2 n2 0 1k\n.END\n";
        var circuit = NetlistLoader.Load(Netlist);
        var parts = Map(circuit);
        var layout = AutoPlacer.Place(circuit, circuit.Directives);
        var elements = SchematicRenderer.Elements(circuit, parts).ToDictionary(e => e.Reference, StringComparer.OrdinalIgnoreCase);

        Point PinAt(string reference, string net) =>
            SchematicRenderer.Pins(elements[reference], layout.Parts.Single(p => p.Reference == reference)).Single(p => p.Net == net).At;
        var to = PinAt("R1", "n1");
        var from = PinAt("R2", "n2");

        var change = SchematicEdits.Drag(Netlist, layout, parts, "R2", to.X - from.X, to.Y - from.Y);

        var after = NetlistLoader.Load(change.Netlist);
        Assert.Contains(after.Circuit.OfType<SpiceSharp.Components.IComponent>(), c => c.Name == "VEE1" && c.Nodes.Contains("n1"));
        var svg = SchematicRenderer.ToSvg(after, change.Layout, Map(after));
        Assert.Contains("data-ref=\"VEE1\"", svg);
    }
}
