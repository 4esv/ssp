using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Ssp.Core.Layout;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Components;
using Ssp.Web.Schematic;
using LayoutDoc = Ssp.Core.Layout.Layout;
using Point = Ssp.Core.Layout.Point;

namespace Ssp.Web.Tests;

public class SchematicMovesTests : BunitContext
{
    const string One = "* one resistor\nR1 a 0 1k\n.END\n";
    static readonly PartsTable Table = Ssp.Core.Parts.Parts.Parse(
        File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "parts.toml")), "parts.toml");
    static readonly LayoutDoc Placed = new([new PartPlacement("R1", 100, 100, 0, false)], []);

    static PartMap Map(string netlist) => PartMap.Resolve(NetlistLoader.Load(netlist), Table);

    static (string Node, Point At) PinOf(SchematicChange change, PinRef pin) =>
        SchematicEdits.Pin(NetlistLoader.Load(change.Netlist), change.Layout, Map(change.Netlist), pin);

    [Fact]
    public void A_free_pin_offers_four_directions_and_a_wire_to_the_right_takes_one_away()
    {
        var spot = new Spot(new Point(500, 500));
        var free = SchematicEdits.Directions(One, Placed, Map(One), spot);
        Assert.Equal([Direction.Up, Direction.Down, Direction.Left, Direction.Right], free);

        var wired = new LayoutDoc(Placed.Parts, [new WireRoute("a", [new Point(500, 500), new Point(540, 500)])]);
        Assert.Equal([Direction.Up, Direction.Down, Direction.Left], SchematicEdits.Directions(One, wired, Map(One), spot));
    }

    [Fact]
    public void A_part_blocks_the_direction_of_its_own_body()
    {
        // NOTE: R1 pin 1 is at the right end of the resistor. Left is the body.
        var free = SchematicEdits.Directions(One, Placed, Map(One), new Spot(new Point(160, 100), new PinRef("R1", 1)));
        Assert.Equal([Direction.Up, Direction.Down, Direction.Right], free);
    }

    [Fact]
    public void Picking_a_resistor_to_the_right_places_it_one_step_away_on_the_net_of_the_pin()
    {
        var pin = new PinRef("R1", 1);
        var (node, at) = PinOf(new SchematicChange(One, Placed), pin);

        var (change, far) = SchematicEdits.PlaceNext(One, Placed, Map(One), "resistor", new Spot(at, pin), Direction.Right);

        var after = NetlistLoader.Load(change.Netlist);
        Assert.DoesNotContain(after.Diagnostics, d => d.Severity == Severity.Error);
        Assert.Empty(change.Layout.Validate(after));
        var added = change.Layout.Parts[^1];
        Assert.Equal("R2", added.Reference);
        var near = PinOf(change, new PinRef("R2", 0));
        Assert.Equal(new Point(at.X + SchematicEdits.Step, at.Y), near.At);
        Assert.Equal(PinOf(change, pin).Node, near.Node);
        Assert.Equal(node, near.Node);

        // The far pin is the new active pin, away from the pin.
        Assert.Equal(new PinRef("R2", 1), far.Pin);
        Assert.Equal(PinOf(change, new PinRef("R2", 1)).At, far.At);
        Assert.True(far.At.X > near.At.X);
        Assert.Equal(near.At.Y, far.At.Y);
    }

    [Theory]
    [InlineData(Direction.Up)]
    [InlineData(Direction.Down)]
    [InlineData(Direction.Left)]
    [InlineData(Direction.Right)]
    [InlineData(Direction.Right, "npn")]
    [InlineData(Direction.Up, "npn")]
    [InlineData(Direction.Down, "pot")]
    [InlineData(Direction.Left, "battery")]
    public void The_far_pin_is_away_from_the_pin_in_each_direction(Direction dir, string kind = "capacitor")
    {
        // NOTE: The body of R1 is to the right of pin 0 and to the left of pin 1, so use the pin that keeps dir free.
        var pin = new PinRef("R1", dir == Direction.Right ? 1 : 0);
        var (_, at) = PinOf(new SchematicChange(One, Placed), pin);

        var (change, far) = SchematicEdits.PlaceNext(One, Placed, Map(One), kind, new Spot(at, pin), dir);

        Assert.Empty(change.Layout.Validate(NetlistLoader.Load(change.Netlist)));
        var (dx, dy) = dir switch { Direction.Up => (0, -1), Direction.Down => (0, 1), Direction.Left => (-1, 0), _ => (1, 0) };
        Assert.True((far.At.X - at.X) * dx + (far.At.Y - at.Y) * dy > SchematicEdits.Step);
    }

    [Fact]
    public void An_open_end_chains_a_part_on_the_net_of_the_wire()
    {
        var pin = new PinRef("R1", 1);
        var (_, at) = PinOf(new SchematicChange(One, Placed), pin);
        var (wired, open) = SchematicEdits.ExtendWire(One, Placed, Map(One), new Spot(at, pin), Direction.Right);
        Assert.Null(open.Pin);
        Assert.Equal(new Point(at.X + SchematicEdits.Step, at.Y), open.At);
        Assert.Equal(One, wired.Netlist);

        var (change, far) = SchematicEdits.PlaceNext(wired.Netlist, wired.Layout, Map(wired.Netlist), "capacitor", open, Direction.Right);
        Assert.Equal(PinOf(change, pin).Node, PinOf(change, new PinRef("C1", 0)).Node);
        Assert.Equal(new PinRef("C1", 1), far.Pin);
    }

    [Fact]
    public void Ground_puts_the_pin_on_node_zero_and_draws_the_symbol()
    {
        var pin = new PinRef("R1", 0);
        var (_, at) = PinOf(new SchematicChange(One, Placed), pin);

        var change = SchematicEdits.Ground(One, Placed, Map(One), new Spot(at, pin));

        Assert.Equal("0", PinOf(change, pin).Node);
        Assert.Contains(change.Layout.Wires, w => w.Net == "0" && w.Points[0] == at);
    }

    IRenderedComponent<SchematicEditor> Editor(List<SchematicChange> changes)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Render<SchematicEditor>(p => p
            .Add(c => c.Netlist, One)
            .Add(c => c.Layout, Placed)
            .Add(c => c.Changed, (SchematicChange c) => changes.Add(c)));
    }

    [Fact]
    public void The_keyboard_path_gives_the_same_result_as_the_taps()
    {
        var tapped = new List<SchematicChange>();
        var byTap = Editor(tapped);
        byTap.Find("circle.pin[data-ref=\"R1\"][data-pin=\"1\"]").Click();
        Assert.Equal(3, byTap.FindAll("g.pin-add").Count);
        byTap.Find("g.pin-add[data-dir=right]").Click();
        byTap.Find(".pin-menu button[data-kind=resistor]").Click();

        var typed = new List<SchematicChange>();
        var byKey = Editor(typed);
        byKey.Find("circle.pin[data-ref=\"R1\"][data-pin=\"1\"]").Click();
        byKey.Find("figure.schematic").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        byKey.Find("figure.schematic").KeyDown(new KeyboardEventArgs { Key = "r" });

        Assert.Single(tapped);
        Assert.Single(typed);
        Assert.Equal(tapped[0].Netlist, typed[0].Netlist);
        Assert.Equal(tapped[0].Layout, typed[0].Layout);

        // The far pin is the active pin: its dots show, and Escape clears them.
        Assert.NotEmpty(byTap.FindAll("g.pin-add"));
        Assert.NotEmpty(byKey.FindAll("g.pin-add"));
        byKey.Find("figure.schematic").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(byKey.FindAll("g.pin-add"));
    }

    [Fact]
    public void Ground_from_the_picker_and_the_g_key_join_node_zero()
    {
        var changes = new List<SchematicChange>();
        var editor = Editor(changes);
        editor.Find("circle.pin[data-ref=\"R1\"][data-pin=\"0\"]").Click();
        editor.Find("figure.schematic").KeyDown(new KeyboardEventArgs { Key = "g" });

        var change = Assert.Single(changes);
        Assert.Equal("0", PinOf(change, new PinRef("R1", 0)).Node);
    }

    [Fact]
    public void Connect_to_a_pin_lights_every_other_pin_and_a_tap_joins_the_nets()
    {
        var netlist = "* two\nR1 a 0 1k\nR2 b 0 1k\n.END\n";
        var layout = new LayoutDoc([new PartPlacement("R1", 100, 100, 0, false), new PartPlacement("R2", 100, 200, 0, false)], []);
        JSInterop.Mode = JSRuntimeMode.Loose;
        var changes = new List<SchematicChange>();
        var editor = Render<SchematicEditor>(p => p.Add(c => c.Netlist, netlist).Add(c => c.Layout, layout)
            .Add(c => c.Changed, (SchematicChange c) => changes.Add(c)));

        editor.Find("circle.pin[data-ref=\"R1\"][data-pin=\"0\"]").Click();
        editor.Find("g.pin-add[data-dir=up]").Click();
        editor.Find(".pin-menu button[data-action=connect]").Click();
        Assert.Equal(3, editor.FindAll("circle.pin.target").Count);
        editor.Find("circle.pin[data-ref=\"R2\"][data-pin=\"0\"]").Click();

        var change = Assert.Single(changes);
        Assert.Equal(PinOf(change, new PinRef("R1", 0)).Node, PinOf(change, new PinRef("R2", 0)).Node);
    }

    [Fact]
    public void The_chain_goes_on_when_the_parent_passes_the_change_back()
    {
        var changes = new List<SchematicChange>();
        var editor = Editor(changes);
        editor.Find("circle.pin[data-ref=\"R1\"][data-pin=\"1\"]").Click();
        editor.Find("g.pin-add[data-dir=right]").Click();
        editor.Find(".pin-menu button[data-kind=resistor]").Click();

        editor.Render(p => p.Add(c => c.Netlist, changes[0].Netlist).Add(c => c.Layout, changes[0].Layout));

        Assert.NotEmpty(editor.FindAll("g.pin-add"));
        Assert.Contains("R2 pin 2", editor.Find("p.schematic-hint").TextContent);
    }

    [Fact]
    public void Ground_from_the_picker_keeps_the_pin_active_and_the_ground_stem_takes_down_away()
    {
        var changes = new List<SchematicChange>();
        var editor = Editor(changes);
        editor.Find("circle.pin[data-ref=\"R1\"][data-pin=\"1\"]").Click();
        Assert.Equal(3, editor.FindAll("g.pin-add").Count);
        editor.Find("g.pin-add[data-dir=right]").Click();
        editor.Find(".pin-menu button[data-action=ground]").Click();
        editor.Render(p => p.Add(c => c.Netlist, changes[0].Netlist).Add(c => c.Layout, changes[0].Layout));

        Assert.Equal("0", PinOf(changes[0], new PinRef("R1", 1)).Node);
        Assert.Equal(2, editor.FindAll("g.pin-add").Count);
    }
}
