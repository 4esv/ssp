using Bunit;
using Ssp.Web.Components;
using Ssp.Web.Schematic;

namespace Ssp.Web.Tests;

public class SchematicNetlistErrorTests : BunitContext
{
    IRenderedComponent<SchematicEditor> Editor(string netlist) => Render<SchematicEditor>(p => p
        .Add(c => c.Netlist, netlist)
        .Add(c => c.Changed, (SchematicChange _) => { }));

    [Fact]
    public void ErrorLineIsShownOnTheCanvas()
    {
        var editor = Editor("V1 in 0 1\nR1 in out 1k\nQ1 out in 0 NOPE\n.END\n");

        var items = editor.FindAll("ul.netlist-errors li");
        Assert.Single(items);
        Assert.Contains("The netlist has an error on line 3. Fix it in the Netlist panel.", items[0].TextContent);
        Assert.DoesNotContain("Error:", items[0].TextContent);
    }

    [Fact]
    public void EveryErrorIsListed()
    {
        var editor = Editor("V1 in 0 1\nQ1 out in 0 NOPE\nQ2 out in 0 NADA\n.END\n");

        var items = editor.FindAll("ul.netlist-errors li");
        Assert.Equal(2, items.Count);
        Assert.Contains("line 2", items[0].TextContent);
        Assert.Contains("line 3", items[1].TextContent);
    }

    [Fact]
    public void NoErrorShowsNoList()
    {
        var editor = Editor("V1 in 0 1\nR1 in 0 1k\n.END\n");

        Assert.Empty(editor.FindAll("ul.netlist-errors"));
    }
}
