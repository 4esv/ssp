using System.Text.RegularExpressions;
using Bunit;
using Ssp.Web.Components;
using Ssp.Web.Schematic;

namespace Ssp.Web.Tests;

// NOTE: #382: on a phone a bottom panel changes the part of the canvas that the user sees. The editor fits the circuit again when the panel opens or closes.
public class SchematicSheetRefitTests : BunitContext
{
    static string Zoom(IRenderedComponent<SchematicEditor> e) => Regex.Match(e.Find("figure.schematic").GetAttribute("style")!, @"--z:\s*([\d.]+)").Groups[1].Value;

    [Fact]
    public void Opening_and_closing_a_panel_fits_the_circuit_again()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./js/schematic-canvas.js").Setup<double[]>("paneBox", _ => true).SetResult([0, 0, 375, 400]);
        var editor = Render<SchematicEditor>(p => p.Add(c => c.Netlist, "* r\nV1 in 0 DC 1\nR1 in 0 1k\n.END\n").Add(c => c.Changed, (SchematicChange _) => { }));
        var fitted = Zoom(editor);

        editor.Find("button[data-zoom=in]").Click();
        editor.Find("button[data-zoom=in]").Click();
        Assert.NotEqual(fitted, Zoom(editor));

        editor.Render(p => p.Add(c => c.Covered, true));
        Assert.Equal(fitted, Zoom(editor));

        editor.Find("button[data-zoom=in]").Click();
        editor.Render(p => p.Add(c => c.Covered, false));
        Assert.Equal(fitted, Zoom(editor));
    }
}
