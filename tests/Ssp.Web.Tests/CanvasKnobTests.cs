using Bunit;
using Ssp.Core.Netlist;
using Ssp.Web.Components;
using Ssp.Web.Schematic;
using LayoutDoc = Ssp.Core.Layout.Layout;

namespace Ssp.Web.Tests;

public class CanvasKnobTests : BunitContext
{
    const string Pot = "* pot\nV1 in 0 DC 0 AC 1\nRV1_1 in out 5k\nRV1_2 out 0 5k\n* ssp:knob RV1 linear 0.8\n.END\n";

    // NOTE: The values come from a script: total * position for linear, total * position^(log 0.1 / log 0.5) for audio,
    // and total * (1 - (1 - position)^(log 0.1 / log 0.5)) for reverse audio.
    public static TheoryData<string, double, double> Tapers() => new()
    {
        { "linear", 0, 0 }, { "linear", 0.25, 2500 }, { "linear", 0.5, 5000 }, { "linear", 0.75, 7500 }, { "linear", 1, 10000 },
        { "log", 0, 0 }, { "log", 0.25, 100 }, { "log", 0.5, 1000 }, { "log", 0.75, 3845.6 }, { "log", 1, 10000 },
        { "revlog", 0, 0 }, { "revlog", 0.25, 6154.4 }, { "revlog", 0.5, 9000 }, { "revlog", 0.75, 9900 }, { "revlog", 1, 10000 },
    };

    [Theory]
    [MemberData(nameof(Tapers))]
    public void Taper_gives_the_resistance_from_the_wiper_to_the_bottom(string taper, double position, double ohms) =>
        Assert.Equal(ohms, Knobs.Resistance(taper, position, 10_000), 0.1);

    [Theory]
    [InlineData("linear", "linear")]
    [InlineData("log", "audio")]
    [InlineData("revlog", "reverse audio")]
    public void Taper_has_a_plain_name(string taper, string name) => Assert.Equal(name, Knobs.TaperName(taper));

    IRenderedComponent<SchematicEditor> Editor(string netlist, List<SchematicChange> seen)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Render<SchematicEditor>(p => p
            .Add(c => c.Netlist, netlist)
            .Add(c => c.Changed, (SchematicChange c) => seen.Add(c)));
    }

    static double Position(string netlist) => DirectiveParser.Parse(netlist).Directives.Knobs.Single().Position;

    [Fact]
    public void A_pot_has_a_knob_on_the_canvas_with_its_taper_and_position()
    {
        var editor = Editor(Pot, []);

        var knob = editor.Find(".knob[data-ref=RV1]");
        Assert.Equal("slider", knob.GetAttribute("role"));
        Assert.Equal("80", knob.GetAttribute("aria-valuenow"));
        Assert.Contains("linear", knob.GetAttribute("aria-valuetext"));
    }

    [Fact]
    public void Double_click_returns_the_knob_to_noon_and_keeps_the_layout()
    {
        var seen = new List<SchematicChange>();
        var editor = Editor(Pot, seen);
        var before = editor.Instance.Drawn;

        editor.Find(".knob[data-ref=RV1]").DoubleClick();

        var change = Assert.Single(seen);
        Assert.Contains("* ssp:knob RV1 linear 0.5\n", change.Netlist);
        Assert.Equal(0.5, Position(change.Netlist));
        Assert.Same(before, change.Layout);
    }

    [Fact]
    public void Arrow_keys_and_the_wheel_turn_the_knob()
    {
        var seen = new List<SchematicChange>();
        var editor = Editor(Pot, seen);

        editor.Find(".knob[data-ref=RV1]").KeyDown("ArrowUp");
        Assert.Equal(0.81, Position(seen[^1].Netlist), 6);
        editor.Find(".knob[data-ref=RV1]").KeyDown("ArrowDown");
        editor.Find(".knob[data-ref=RV1]").KeyDown("ArrowDown");
        Assert.Equal(0.79, Position(seen[^1].Netlist), 6);
        editor.Find(".knob[data-ref=RV1]").Wheel(new Microsoft.AspNetCore.Components.Web.WheelEventArgs { DeltaY = 100 });
        Assert.Equal(0.74, Position(seen[^1].Netlist), 6);
        editor.Find(".knob[data-ref=RV1]").KeyDown("End");
        Assert.Equal(1, Position(seen[^1].Netlist));
    }

    [Fact]
    public void Dragging_the_knob_up_turns_it_up_once_on_release()
    {
        var seen = new List<SchematicChange>();
        var editor = Editor(Pot, seen);

        editor.Find(".knob[data-ref=RV1]").PointerDown(new Microsoft.AspNetCore.Components.Web.PointerEventArgs { ClientY = 200, PointerId = 1 });
        editor.Find(".knob[data-ref=RV1]").PointerMove(new Microsoft.AspNetCore.Components.Web.PointerEventArgs { ClientY = 185, PointerId = 1 });
        Assert.Equal("90", editor.Find(".knob[data-ref=RV1]").GetAttribute("aria-valuenow"));
        Assert.Empty(seen);
        editor.Find(".knob[data-ref=RV1]").PointerUp(new Microsoft.AspNetCore.Components.Web.PointerEventArgs { ClientY = 185, PointerId = 1 });

        Assert.Equal(0.9, Position(Assert.Single(seen).Netlist), 6);
    }

    static int PinCount(string netlist, string reference) =>
        SchematicRenderer.Elements(NetlistLoader.Load(netlist), null).Single(e => e.Reference == reference).Nodes.Count;

    [Fact]
    public void Pole_count_changes_the_pin_count()
    {
        var placed = SchematicEdits.Place("* s\n.END\n", new LayoutDoc([], []), "switch");
        Assert.Equal("switch1", SchematicRenderer.Elements(NetlistLoader.Load(placed.Netlist), null).Single().Kind);
        Assert.Equal(3, PinCount(placed.Netlist, "SW1"));

        var two = SchematicEdits.SetPoles(placed.Netlist, placed.Layout, "SW1", 2);
        Assert.Equal(6, PinCount(two.Netlist, "SW1"));
        var three = SchematicEdits.SetPoles(two.Netlist, two.Layout, "SW1", 3);
        Assert.Equal(9, PinCount(three.Netlist, "SW1"));
        Assert.Equal(3, PinCount(SchematicEdits.SetPoles(three.Netlist, three.Layout, "SW1", 1).Netlist, "SW1"));
        Assert.Throws<ArgumentOutOfRangeException>(() => SchematicEdits.SetPoles(two.Netlist, two.Layout, "SW1", 4));
    }

    [Fact]
    public void A_switch_toggle_on_the_canvas_moves_every_pole_to_the_other_throw()
    {
        var placed = SchematicEdits.Place("* s\n.END\n", new LayoutDoc([], []), "switch");
        var two = SchematicEdits.SetPoles(placed.Netlist, placed.Layout, "SW1", 2);
        var seen = new List<SchematicChange>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        var editor = Render<SchematicEditor>(p => p
            .Add(c => c.Netlist, two.Netlist)
            .Add(c => c.Layout, two.Layout)
            .Add(c => c.Changed, (SchematicChange c) => seen.Add(c)));
        Assert.Equal("A", SchematicRenderer.Elements(NetlistLoader.Load(two.Netlist), null).Single().Value);

        editor.Find(".knob[data-ref=SW1]").Click();

        var change = Assert.Single(seen);
        Assert.Equal("B", SchematicRenderer.Elements(NetlistLoader.Load(change.Netlist), null).Single().Value);
        Assert.Equal(6, PinCount(change.Netlist, "SW1"));
        Assert.Same(two.Layout, change.Layout);
    }
}
