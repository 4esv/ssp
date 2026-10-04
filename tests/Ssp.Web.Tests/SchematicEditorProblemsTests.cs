using Bunit;
using Ssp.Web.Components;
using Ssp.Web.Schematic;

namespace Ssp.Web.Tests;

public class SchematicEditorProblemsTests : BunitContext
{
    const string Floating = "* floating\nV1 in 0 1\nR1 in out 1k\nR2 out 0 2k\nR3 out nowhere 10k\n.END\n";
    const string NoGround = "* no ground\nV1 in a 1\nR1 in out 1k\nR2 out a 2k\n.END\n";
    const string Fixed = "* fixed\nV1 in 0 1\nR1 in out 1k\nR2 out 0 2k\nR3 out 0 10k\n.END\n";

    IRenderedComponent<SchematicEditor> Editor(string netlist) => Render<SchematicEditor>(p => p
        .Add(c => c.Netlist, netlist)
        .Add(c => c.Changed, (SchematicChange _) => { }));

    [Fact]
    public void FloatingPinGivesOneMessageAndOneHighlightedPin()
    {
        var editor = Editor(Floating);
        var message = Assert.Single(editor.FindAll("button.problem-item"));
        Assert.Contains("R3", message.TextContent);
        var pin = Assert.Single(editor.FindAll("circle.pin.problem"));
        Assert.Equal("R3", pin.GetAttribute("data-ref"));
        Assert.Equal("1", pin.GetAttribute("data-pin"));
    }

    [Fact]
    public void MissingGroundGivesOneMessageAndOneHighlightedPart()
    {
        var editor = Editor(NoGround);
        Assert.Single(editor.FindAll("button.problem-item"));
        var part = Assert.Single(editor.FindAll("rect.part.problem"));
        Assert.Equal("V1", part.GetAttribute("data-ref"));
    }

    [Fact]
    public void FixedCircuitClearsTheProblems()
    {
        var editor = Editor(Floating);
        editor.Render(p => p.Add(c => c.Netlist, Fixed));
        Assert.Empty(editor.FindAll("button.problem-item"));
        Assert.Empty(editor.FindAll(".problem"));
    }

    [Fact]
    public void ClickingTheMessageSelectsThePart()
    {
        var editor = Editor(Floating);
        editor.Find("button.problem-item").Click();
        Assert.Equal("R3", editor.Find("rect.part.selected").GetAttribute("data-ref"));
    }
}
