using Bunit;
using Ssp.Web.Components;
using Ssp.Web.Schematic;

namespace Ssp.Web.Tests;

// #408: a .subckt with no .ends makes a palette add a silent no-op. The editor names the .subckt.
public class SchematicPaletteNoEndsTests : BunitContext
{
    const string NoEnds = "* open subcircuit\n* the body is a resistor\n.subckt X a b\nRin a b 1k\n.END\n";

    IRenderedComponent<SchematicEditor> Editor(string netlist, List<SchematicChange> seen) => Render<SchematicEditor>(p => p
        .Add(c => c.Netlist, netlist)
        .Add(c => c.Changed, (SchematicChange c) => seen.Add(c)));

    [Fact]
    public void A_palette_add_with_no_ends_names_the_subcircuit_and_keeps_the_netlist()
    {
        var seen = new List<SchematicChange>();
        var editor = Editor(NoEnds, seen);

        editor.Find("button.palette-item[data-kind=resistor]").Click();

        Assert.Empty(seen);
        var item = Assert.Single(editor.FindAll("ul.netlist-errors li"));
        Assert.Contains("The netlist has an error on line 3. Fix it in the Netlist panel. .subckt X has no .ends. Add .ends after its last line.", item.TextContent);
    }
}
