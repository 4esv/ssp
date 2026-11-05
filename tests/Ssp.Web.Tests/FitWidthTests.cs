using System.Globalization;
using System.Text.RegularExpressions;
using Bunit;
using Ssp.Web.Components;
using Ssp.Web.Schematic;

namespace Ssp.Web.Tests;

// NOTE: #441: on a phone Fit fits the width of the circuit, so a tall circuit opens at a readable size and the canvas pans down.
public class FitWidthTests : BunitContext
{
    [Fact]
    public void A_tall_box_is_width_bound_and_starts_at_the_top_margin()
    {
        // NOTE: 500 x 1200 units in a 375 x 700 pane: Fit is height-bound (0.55 x 24 margin), FitWidth is width-bound.
        var fit = ViewTransform.Fit(0, 0, 500, 1200, 375, 700, 24);
        var width = ViewTransform.FitWidth(0, 0, 500, 1200, 375, 700, 24);
        Assert.Equal((375 - 48) / 500.0, width.Zoom, 6);
        Assert.True(width.Zoom > fit.Zoom);
        Assert.Equal(24, width.Y, 6);
        Assert.Equal(24, width.X, 6);
    }

    [Fact]
    public void A_box_that_fits_is_centred_like_Fit()
    {
        var fit = ViewTransform.Fit(10, 20, 500, 200, 375, 700, 24);
        var width = ViewTransform.FitWidth(10, 20, 500, 200, 375, 700, 24);
        Assert.Equal(fit, width);
    }

    [Fact]
    public void The_zoom_of_a_narrow_box_stops_at_the_fit_limit()
    {
        Assert.Equal(ViewTransform.MaxFitZoom, ViewTransform.FitWidth(0, 0, 10, 10, 375, 700, 24).Zoom);
    }

    const string Netlist = "* t\nV1 in 0 DC 1\nR1 in a 1k\nR2 a b 1k\nR3 b c 1k\nR4 c d 1k\nR5 d e 1k\nR6 e 0 1k\n.end\n";

    static double ZoomOf(IRenderedComponent<SchematicEditor> e) =>
        double.Parse(Regex.Match(e.Find("figure.schematic").GetAttribute("style")!, @"--z:\s*([\d.]+)").Groups[1].Value, CultureInfo.InvariantCulture);

    double Open(double[] pane)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./js/schematic-canvas.js").Setup<double[]>("paneBox", _ => true).SetResult(pane);
        return ZoomOf(Render<SchematicEditor>(p => p.Add(c => c.Netlist, Netlist).Add(c => c.Changed, (SchematicChange _) => { })));
    }

    [Fact]
    public void The_editor_fits_the_width_when_the_pane_says_phone_and_the_whole_drawing_when_it_does_not()
    {
        var desktop = Open([0, 0, 1200, 260, 0]);
        var phone = Open([0, 0, 1200, 260, 1]);
        Assert.True(phone > desktop, $"phone {phone} is not more than desktop {desktop}");
    }
}
