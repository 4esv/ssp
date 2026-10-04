using Ssp.Core.Layout;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Schematic;
using LayoutDoc = Ssp.Core.Layout.Layout;
using Point = Ssp.Core.Layout.Point;

namespace Ssp.Web.Tests;

public class SchematicPlaceTests
{
    const string Empty = "* schematic\n.END\n";
    static readonly LayoutDoc NoLayout = new([], []);

    static PartMap Parts(string netlist) => PartMap.Resolve(NetlistLoader.Load(netlist), Table);

    static readonly PartsTable Table = Ssp.Core.Parts.Parts.Parse(
        File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "parts.toml")), "parts.toml");

    [Theory]
    [InlineData(123.4, 57.6, 120, 60)]
    [InlineData(-14, 4, -10, 0)]
    [InlineData(0, 0, 0, 0)]
    public void PlaceAt_snaps_the_part_to_the_grid(double x, double y, double ex, double ey)
    {
        var change = SchematicEdits.PlaceAt(Empty, NoLayout, "resistor", new Point(x, y));

        var part = Assert.Single(change.Layout.Parts);
        Assert.Equal((ex, ey), (part.X, part.Y));
        Assert.Equal(0, part.X % Symbols.Grid);
        Assert.Equal(0, part.Y % Symbols.Grid);
    }

    [Theory]
    [InlineData(14, 4, 20, 10)]
    [InlineData(-4, -6, 10, 0)]
    [InlineData(0, 0, 10, 10)]
    public void Drag_snaps_to_the_nearest_grid_step(double dx, double dy, double ex, double ey)
    {
        var placed = SchematicEdits.PlaceAt(Empty, NoLayout, "resistor", new Point(10, 10));

        var moved = SchematicEdits.Drag(placed.Netlist, placed.Layout, Parts(placed.Netlist), "R1", dx, dy);

        var part = Assert.Single(moved.Layout.Parts);
        Assert.Equal((ex, ey), (part.X, part.Y));
        Assert.Equal(placed.Netlist, moved.Netlist);
    }

    [Fact]
    public void Drop_of_a_pin_on_another_pin_merges_the_nets()
    {
        var a = SchematicEdits.PlaceAt(Empty, NoLayout, "resistor", new Point(0, 0));
        var b = SchematicEdits.PlaceAt(a.Netlist, a.Layout, "resistor", new Point(200, 100));
        Assert.NotEqual(Node(b.Netlist, "R1", 1), Node(b.Netlist, "R2", 0));

        // R2 pin 1 is at (200, 100). Drag R2 so that it sits on the second pin of R1 at (60, 0).
        var moved = SchematicEdits.Drag(b.Netlist, b.Layout, Parts(b.Netlist), "R2", -140, -100);

        Assert.Equal(Node(moved.Netlist, "R1", 1), Node(moved.Netlist, "R2", 0));
        Assert.Empty(moved.Layout.Validate(NetlistLoader.Load(moved.Netlist)));
    }

    [Fact]
    public void Drop_of_a_pin_on_a_wire_joins_its_net()
    {
        var a = SchematicEdits.PlaceAt(Empty, NoLayout, "resistor", new Point(0, 0));
        var b = SchematicEdits.PlaceAt(a.Netlist, a.Layout, "resistor", new Point(0, 100));
        var wired = SchematicEdits.Wire(b.Netlist, b.Layout, Parts(b.Netlist), new PinRef("R1", 1), new PinRef("R2", 1));
        var c = SchematicEdits.PlaceAt(wired.Netlist, wired.Layout, "resistor", new Point(200, 200));

        // The wire runs from (60, 0) down to (60, 100). Put R3 pin 1 at (60, 50).
        var moved = SchematicEdits.Drag(c.Netlist, c.Layout, Parts(c.Netlist), "R3", -140, -150);

        Assert.Equal(Node(moved.Netlist, "R1", 1), Node(moved.Netlist, "R3", 0));
    }

    [Fact]
    public void Drag_moves_the_ends_of_wires_on_its_pins()
    {
        var a = SchematicEdits.PlaceAt(Empty, NoLayout, "resistor", new Point(0, 0));
        var b = SchematicEdits.PlaceAt(a.Netlist, a.Layout, "resistor", new Point(0, 100));
        var wired = SchematicEdits.Wire(b.Netlist, b.Layout, Parts(b.Netlist), new PinRef("R1", 1), new PinRef("R2", 0));

        var moved = SchematicEdits.Drag(wired.Netlist, wired.Layout, Parts(wired.Netlist), "R2", 0, 50);

        var points = Assert.Single(moved.Layout.Wires).Points;
        Assert.Equal(new Point(60, 0), points[0]);
        Assert.Equal(new Point(0, 150), points[^1]);
    }

    static string Node(string netlist, string reference, int pin)
    {
        var line = netlist.Split('\n').First(l => l.StartsWith(reference + " ", StringComparison.Ordinal));
        return line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1 + pin];
    }
}
