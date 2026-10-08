using Ssp.Core.Layout;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Schematic;
using LayoutDoc = Ssp.Core.Layout.Layout;

namespace Ssp.Web.Tests;

// NOTE: #305: an instance of an empty .subckt draws no components, so its element had no members. The element lookups
// fall back to `Members[0]`, so the empty list made every edit on the schematic throw ArgumentOutOfRangeException.
public class EmptySubcircuitTests
{
    const string Netlist = "* x\n.subckt EMPTY a b\n.ends\nX1 in out EMPTY\nV1 in 0 1\nR1 out 0 1k\n.END\n";
    static readonly PartsTable Table = Ssp.Core.Parts.Parts.Parse(
        File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "parts.toml")), "parts.toml");

    static (LayoutDoc Layout, PartMap Parts) Build()
    {
        var circuit = NetlistLoader.Load(Netlist);
        return (AutoPlacer.Place(circuit, circuit.Directives), PartMap.Resolve(circuit, Table));
    }

    [Fact]
    public void The_empty_instance_is_an_element_of_its_own()
    {
        var circuit = NetlistLoader.Load(Netlist);
        var element = SchematicRenderer.Elements(circuit, null).Single(e => e.Reference == "X1");
        Assert.NotEmpty(element.Members);
    }

    [Fact]
    public void An_edit_on_any_part_of_a_netlist_with_an_empty_instance_does_not_throw()
    {
        var (layout, parts) = Build();
        foreach (var reference in new[] { "X1", "EMPTY", "R1", "V1" })
        {
            SchematicEdits.Drag(Netlist, layout, parts, reference, 10, 10);
            SchematicEdits.Rotate(Netlist, layout, reference);
        }
    }
}
