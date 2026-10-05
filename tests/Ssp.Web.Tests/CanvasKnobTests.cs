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

    // NOTE: #273. A drawn pot is placed as RV1_1. Its halves had an outline and pins of their own, and the lower pin of RV1_1
    // (the wiper net) lay on the bottom pin of RV1 (ground).
    [Fact]
    public void A_drawn_pot_has_its_three_pins_and_one_outline_only()
    {
        var pot = SchematicEdits.Place("* schematic\n.END\n", new LayoutDoc([], []), "pot");
        JSInterop.Mode = JSRuntimeMode.Loose;
        var editor = Render<SchematicEditor>(p => p.Add(c => c.Netlist, pot.Netlist).Add(c => c.Layout, pot.Layout));

        Assert.Equal(["RV1", "RV1", "RV1"], editor.FindAll("svg.schematic-pins circle.pin[data-ref]").Select(e => e.GetAttribute("data-ref")));
        Assert.Equal(["RV1"], editor.FindAll("svg.schematic-pins rect.part").Select(e => e.GetAttribute("data-ref")));
    }

    // NOTE: #273. The hit circle of the knob is 22 px in radius and lies about 21 units from the wiper, so at a zoom near 1 it
    // covered the wiper pin and a click on the wiper turned the knob. The pin dots lie above the knob, the hit circles of the pins under it.
    [Fact]
    public void The_pin_dots_lie_above_the_knob_and_the_pin_hit_circles_under_it()
    {
        var pot = SchematicEdits.Place("* schematic\n.END\n", new LayoutDoc([], []), "pot");
        JSInterop.Mode = JSRuntimeMode.Loose;
        var editor = Render<SchematicEditor>(p => p.Add(c => c.Netlist, pot.Netlist).Add(c => c.Layout, pot.Layout));

        var order = editor.FindAll("svg.schematic-pins > *").Select(e => e.GetAttribute("class")).ToList();
        var knob = order.IndexOf("knob");
        Assert.True(knob >= 0);
        Assert.All(order.Select((c, i) => (c, i)).Where(x => x.c == "pin-hit"), x => Assert.True(x.i < knob, $"pin-hit at {x.i}, knob at {knob}"));
        Assert.All(order.Select((c, i) => (c, i)).Where(x => x.c?.StartsWith("pin ", StringComparison.Ordinal) == true || x.c == "pin"), x => Assert.True(x.i > knob, $"{x.c} at {x.i}, knob at {knob}"));
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
}
