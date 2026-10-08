using Ssp.Core.Layout;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Schematic;
using LayoutDoc = Ssp.Core.Layout.Layout;
using Point = Ssp.Core.Layout.Point;

namespace Ssp.Web.Tests;

// NOTE: #313: a PinRef can name a reference that the netlist no longer has (the text changed from outside the schematic).
// An edit on a stale pin is a no-op that returns the input unchanged, not an exception. The mutation check: replace the
// TryPin guard in Ground or Wire with the throwing Pin and every test here fails.
public class SchematicEditsStalePinTests
{
    const string Netlist = "* amp\nV1 in 0 1\nR1 in a 1k\nR2 a 0 1k\n.END\n";
    static readonly PartsTable Table = Ssp.Core.Parts.Parts.Parse(
        File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "parts.toml")), "parts.toml");

    static LayoutDoc Placed()
    {
        var circuit = NetlistLoader.Load(Netlist);
        return AutoPlacer.Place(circuit, circuit.Directives);
    }

    static PartMap Parts => PartMap.Resolve(NetlistLoader.Load(Netlist), Table);

    [Fact]
    public void Ground_on_a_stale_pin_reference_is_a_no_op()
    {
        var layout = Placed();

        var change = SchematicEdits.Ground(Netlist, layout, Parts, new PinRef("RV1", 0));

        Assert.Equal(Netlist, change.Netlist);
        Assert.Same(layout, change.Layout);
    }

    [Fact]
    public void Wire_from_a_stale_pin_reference_is_a_no_op()
    {
        var layout = Placed();

        var change = SchematicEdits.Wire(Netlist, layout, Parts, new PinRef("RV1", 0), new PinRef("R1", 1));

        Assert.Equal(Netlist, change.Netlist);
        Assert.Same(layout, change.Layout);
    }

    [Fact]
    public void Wire_to_a_stale_pin_reference_is_a_no_op()
    {
        var layout = Placed();

        var change = SchematicEdits.Wire(Netlist, layout, Parts, new PinRef("R1", 1), new PinRef("RV1", 0));

        Assert.Equal(Netlist, change.Netlist);
        Assert.Same(layout, change.Layout);
    }

    // NOTE: A spot that is on no pin and no wire stays an argument error: that is argument validation, not a stale reference.
    [Fact]
    public void Ground_on_a_spot_that_is_on_nothing_is_an_argument_error()
    {
        var layout = Placed();

        Assert.Throws<ArgumentException>(() => SchematicEdits.Ground(Netlist, layout, Parts, new Spot(new Point(-500, -500))));
    }
}
