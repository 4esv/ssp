using Bunit;
using Ssp.Web.Components;
using Ssp.Web.Schematic;

namespace Ssp.Web.Tests;

public class SchematicEditorHintTests : BunitContext
{
    const string TwoResistors = "* two resistors\nV1 in 0 1\nR1 in a 1k\nR2 b 0 1k\n.END\n";

    IRenderedComponent<SchematicEditor> Editor(string netlist) => Render<SchematicEditor>(p => p
        .Add(c => c.Netlist, netlist)
        .Add(c => c.Changed, (SchematicChange _) => { }));

    static string Hint(IRenderedComponent<SchematicEditor> editor) => editor.Find("p.schematic-hint").TextContent.Trim();

    [Fact]
    public void IdleHintAsksForATool()
    {
        var editor = Editor(TwoResistors);
        editor.Find("button.tool[data-tool=wire]").Click();
        Assert.Equal("Choose a tool, or place a part.", Hint(editor));
    }

    [Fact]
    public void WireHintAsksForAPin()
    {
        var editor = Editor(TwoResistors);
        Assert.Equal("Wire: click a pin", Hint(editor));
    }

    [Fact]
    public void WireWithPendingPinNamesThePin()
    {
        var editor = Editor(TwoResistors);
        editor.Find("circle.pin[data-ref=\"R1\"][data-pin=\"0\"]").Click();
        Assert.Equal("Wire: click a second pin to connect it to R1 pin 1", Hint(editor));
    }

    [Fact]
    public void GroundHintAsksForAPin()
    {
        var editor = Editor(TwoResistors);
        editor.Find("button.tool[data-tool=ground]").Click();
        Assert.Equal("Ground: click a pin", Hint(editor));
    }

    [Fact]
    public void SelectedPartShowsKeys()
    {
        var editor = Editor(TwoResistors);
        editor.Find("rect.part[data-ref=\"R1\"]").Click();
        Assert.Equal("R1 selected: R rotate, H/V flip, arrows move, Delete remove", Hint(editor));
    }

    [Fact]
    public void PlacedPartIsSelectedAtOnce()
    {
        var editor = Editor(TwoResistors);
        editor.Find("button.place[data-kind=resistor]").Click();
        Assert.Equal("R3 selected: R rotate, H/V flip, arrows move, Delete remove", Hint(editor));
        Assert.Single(editor.FindAll("rect.part.selected"));
    }

    [Fact]
    public void PinHitTargetIsAtLeast24()
    {
        var editor = Editor(TwoResistors);
        var hits = editor.FindAll("circle.pin-hit");
        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.True(double.Parse(h.GetAttribute("r")!, System.Globalization.CultureInfo.InvariantCulture) * 2 >= 24));
        Assert.Equal(hits.Count, editor.FindAll("circle.pin").Count);
    }

    [Fact]
    public void PartButtonsAreGroupedApartFromTools()
    {
        var editor = Editor(TwoResistors);
        var parts = editor.Find("[role=group][aria-label=Parts]");
        var tools = editor.Find("[role=group][aria-label=Tools]");
        Assert.Equal(2, parts.QuerySelectorAll("button.place").Length);
        Assert.Equal(2, tools.QuerySelectorAll("button.tool").Length);
        Assert.Contains("Add a part", parts.TextContent);
    }

    [Fact]
    public void EmptySchematicSaysWhatToDoFirst()
    {
        var editor = Editor("");
        Assert.Contains("Add a part", editor.Find("p.schematic-empty").TextContent);
    }
}
