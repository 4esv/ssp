using Bunit;
using Ssp.Core.Layout;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Components;
using Ssp.Web.Schematic;
using LayoutDoc = Ssp.Core.Layout.Layout;

namespace Ssp.Web.Tests;

public class SchematicAddByClickTests : BunitContext
{
    const string TwoResistors = "* two resistors\nV1 in 0 1\nR1 in a 1k\nR2 b 0 1k\n.END\n";
    static readonly PartsTable Table = Ssp.Core.Parts.Parts.Parse(
        File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "parts.toml")), "parts.toml");
    static readonly LayoutDoc NoLayout = new([], []);

    IRenderedComponent<SchematicEditor> Editor(string netlist = TwoResistors)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Render<SchematicEditor>(p => p
            .Add(c => c.Netlist, netlist)
            .Add(c => c.Changed, (SchematicChange _) => { }));
    }

    static string Hint(IRenderedComponent<SchematicEditor> editor) => editor.Find("p.schematic-hint").TextContent.Trim();

    [Fact]
    public void Placing_at_a_view_centre_snaps_to_the_grid()
    {
        var change = SchematicEdits.PlaceAt("* schematic\n.END\n", NoLayout, "resistor", new Point(203.3, 97.6));

        var placed = Assert.Single(change.Layout.Parts);
        Assert.Equal(SchematicEdits.Snap(203.3), placed.X);
        Assert.Equal(SchematicEdits.Snap(97.6), placed.Y);
        Assert.Equal(0, placed.X % Symbols.Grid);
        Assert.Equal(0, placed.Y % Symbols.Grid);
    }

    [Fact]
    public void The_palette_markup_has_no_pointer_handlers()
    {
        var editor = Editor();
        var palette = editor.Find(".palette");
        var markup = palette.OuterHtml;

        Assert.DoesNotContain("onpointer", markup);
        Assert.DoesNotContain("draggable", markup);
        Assert.NotEmpty(palette.QuerySelectorAll("button.palette-item"));
    }

    [Fact]
    public void Clicking_a_palette_kind_places_and_selects_a_part()
    {
        var editor = Editor();
        editor.Find("button.palette-item[data-kind=capacitor]").Click();

        Assert.Equal("C1 selected: R rotate, H/V flip, arrows move, Delete remove", Hint(editor));
        Assert.Single(editor.FindAll("rect.part.selected"));
    }

    [Fact]
    public void A_selected_pin_shows_plus_dots_and_a_dot_opens_the_picker()
    {
        var editor = Editor();
        Assert.Empty(editor.FindAll("g.pin-add"));

        editor.Find("circle.pin[data-ref=\"R1\"][data-pin=\"1\"]").Click();
        Assert.NotEmpty(editor.FindAll("g.pin-add"));
        Assert.Empty(editor.FindAll(".pin-menu"));

        editor.Find("g.pin-add").Click();
        var items = editor.FindAll(".pin-menu button");
        Assert.Equal(SchematicEdits.Kinds.Count + 3, items.Count);

        items.Single(b => b.GetAttribute("data-kind") == "capacitor").Click();
        Assert.Empty(editor.FindAll(".pin-menu"));
        Assert.Contains("C1 pin 2", Hint(editor));
    }

    [Fact]
    public void Connecting_two_pins_goes_through_the_picker_and_the_status_says_what_to_tap()
    {
        var editor = Editor();
        editor.Find("circle.pin[data-ref=\"R1\"][data-pin=\"1\"]").Click();
        editor.Find("g.pin-add").Click();
        editor.Find(".pin-menu button[data-action=connect]").Click();
        Assert.Equal("Tap the pin to join. Esc cancels.", Hint(editor));

        editor.Find("circle.pin[data-ref=\"R2\"][data-pin=\"0\"]").Click();
        Assert.DoesNotContain("Tap the pin", Hint(editor));
        Assert.Empty(editor.FindAll("g.pin-add"));
    }

    [Fact]
    public void Escape_and_a_tap_on_the_empty_canvas_cancel_the_connection()
    {
        var editor = Editor();
        editor.Find("circle.pin[data-ref=\"R1\"][data-pin=\"1\"]").Click();
        editor.Find("figure.schematic").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.DoesNotContain("pin 2", Hint(editor));
        Assert.Empty(editor.FindAll("g.pin-add"));

        editor.Find("circle.pin[data-ref=\"R1\"][data-pin=\"1\"]").Click();
        editor.Find("svg.schematic-pins").Click();
        Assert.DoesNotContain("pin 2", Hint(editor));
    }
}
