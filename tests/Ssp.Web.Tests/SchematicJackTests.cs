using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Ssp.Core.Layout;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Components;
using Ssp.Web.Schematic;
using LayoutDoc = Ssp.Core.Layout.Layout;

namespace Ssp.Web.Tests;

public class SchematicJackTests : BunitContext
{
    const string TwoResistors = "* two resistors\nV1 in 0 1\nR1 in a 1k\nR2 b 0 1k\n.END\n";
    static readonly PartsTable Table = Ssp.Core.Parts.Parts.Parse(
        File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "parts.toml")), "parts.toml");

    static string Hint(IRenderedComponent<SchematicEditor> editor) => editor.Find("p.schematic-hint").TextContent.Trim();

    static int Components(string netlist) => NetlistLoader.Load(netlist).Circuit.Count();

    IRenderedComponent<SchematicEditor> Editor(List<SchematicChange> seen)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Render<SchematicEditor>(p => p
            .Add(c => c.Netlist, TwoResistors)
            .Add(c => c.Changed, (SchematicChange c) => seen.Add(c)));
    }

    [Theory]
    [InlineData("jack-in", "input")]
    [InlineData("jack-out", "output")]
    public void A_jack_on_a_pin_writes_a_directive_and_no_component(string kind, string directive)
    {
        var circuit = NetlistLoader.Load(TwoResistors);
        var layout = AutoPlacer.Place(circuit, circuit.Directives);
        var parts = PartMap.Resolve(circuit, Table);
        var pin = new PinRef("R1", 1);
        var spot = new Spot(SchematicEdits.Pin(circuit, layout, parts, pin).Position, pin);

        var change = SchematicEdits.PlaceJack(TwoResistors, layout, parts, kind, spot);

        Assert.Contains($"* ssp:{directive} a", change.Netlist);
        Assert.Equal(Components(TwoResistors), Components(change.Netlist));
        Assert.Equal(layout, change.Layout);
    }

    [Fact]
    public void A_second_jack_in_moves_the_directive()
    {
        var circuit = NetlistLoader.Load(TwoResistors);
        var layout = AutoPlacer.Place(circuit, circuit.Directives);
        var parts = PartMap.Resolve(circuit, Table);
        var first = SchematicEdits.PlaceJack(TwoResistors, layout, parts, "jack-in", new Spot(default, new PinRef("R1", 0)));
        var second = SchematicEdits.PlaceJack(first.Netlist, layout, parts, "jack-in", new Spot(default, new PinRef("R1", 1)));

        Assert.Single(second.Netlist.Split('\n'), l => l.Contains("ssp:input"));
        Assert.Contains("* ssp:input a", second.Netlist);
    }

    [Fact]
    public void A_blocked_direction_leaves_the_netlist_and_says_so()
    {
        var seen = new List<SchematicChange>();
        var editor = Editor(seen);
        var circuit = NetlistLoader.Load(TwoResistors);
        var layout = AutoPlacer.Place(circuit, circuit.Directives);
        var parts = PartMap.Resolve(circuit, Table);
        var pin = new PinRef("R1", 1);
        var spot = new Spot(SchematicEdits.Pin(circuit, layout, parts, pin).Position, pin);
        var free = SchematicEdits.Directions(TwoResistors, layout, parts, spot);
        var blocked = Enum.GetValues<Direction>().First(d => !free.Contains(d));

        editor.Find("circle.pin[data-ref=\"R1\"][data-pin=\"1\"]").Click();
        editor.Find("figure.schematic").KeyDown(new KeyboardEventArgs { Key = "Arrow" + blocked });

        Assert.Empty(seen);
        var others = free.Select(d => d.ToString().ToLowerInvariant()).ToList();
        var list = others.Count == 1 ? others[0] : string.Join(", ", others[..^1]) + " or " + others[^1];
        Assert.Equal($"{blocked} is blocked here. Try {list}.", Hint(editor));
    }

    [Fact]
    public void A_letter_with_no_direction_changes_nothing_and_says_so()
    {
        var seen = new List<SchematicChange>();
        var editor = Editor(seen);
        editor.Find("circle.pin[data-ref=\"R1\"][data-pin=\"1\"]").Click();
        editor.Find("figure.schematic").KeyDown(new KeyboardEventArgs { Key = "r" });

        Assert.Empty(seen);
        Assert.Equal("Pick a direction first, then a letter.", Hint(editor));
    }

    [Fact]
    public void G_grounds_the_active_pin_and_says_so()
    {
        var seen = new List<SchematicChange>();
        var editor = Editor(seen);
        editor.Find("circle.pin[data-ref=\"R1\"][data-pin=\"1\"]").Click();
        editor.Find("figure.schematic").KeyDown(new KeyboardEventArgs { Key = "g" });

        Assert.Single(seen);
        Assert.Equal("Grounded R1 pin 2.", Hint(editor));
    }
}
