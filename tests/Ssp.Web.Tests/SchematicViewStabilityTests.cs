using System.Globalization;
using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Ssp.Web.Components;
using Ssp.Web.Schematic;
using Point = Ssp.Core.Layout.Point;

namespace Ssp.Web.Tests;

public class SchematicViewStabilityTests : BunitContext
{
    const double PaneWidth = 1280, PaneHeight = 700;

    IRenderedComponent<SchematicEditor> Editor(string netlist)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./js/schematic-canvas.js").Setup<double[]>("paneBox", _ => true).SetResult([0, 0, PaneWidth, PaneHeight]);
        return Render<SchematicEditor>(p => p.Add(c => c.Netlist, netlist).Add(c => c.Changed, (SchematicChange _) => { }));
    }

    static double Num(string text, string name) =>
        double.Parse(Regex.Match(text, name + @"\s*[:=]\s*""?(-?[\d.]+)").Groups[1].Value, CultureInfo.InvariantCulture);

    // NOTE: The view is where the world origin lands on screen, and the zoom. The stage size and offset follow the view box, which grows with the circuit.
    static (double Zoom, double X, double Y) Transform(IRenderedComponent<SchematicEditor> e)
    {
        var style = e.Find("figure.schematic").GetAttribute("style")!;
        var box = Regex.Match(e.Find("svg.schematic-pins").GetAttribute("viewBox")!, @"(-?[\d.]+) (-?[\d.]+)");
        var z = Num(style, "--z");
        return (z, Num(style, "--x") - double.Parse(box.Groups[1].Value, CultureInfo.InvariantCulture) * z, Num(style, "--y") - double.Parse(box.Groups[2].Value, CultureInfo.InvariantCulture) * z);
    }

    static double Zoom((double Zoom, double X, double Y) t) => t.Zoom;

    [Fact]
    public void The_view_is_a_transform_from_the_first_render_and_the_label_is_a_number()
    {
        var editor = Editor("");
        editor.Find("button.palette-item[data-kind=source]").Click();

        Assert.NotNull(editor.Find("figure.schematic").GetAttribute("data-view"));
        Assert.Matches(@"^\d+%$", editor.Find("output.zoom-level").TextContent);
        Assert.Equal(Zoom(Transform(editor)), double.Parse(editor.Find("output.zoom-level").TextContent.TrimEnd('%'), CultureInfo.InvariantCulture) / 100, 2);
    }

    [Fact]
    public void A_pin_tap_a_part_tap_and_a_place_leave_the_transform_alone()
    {
        var editor = Editor("");
        editor.Find("button.palette-item[data-kind=source]").Click();
        var before = Transform(editor);

        editor.Find("[data-pin]").Click();
        Assert.Equal(before, Transform(editor));

        editor.Find("rect.part").PointerDown(new PointerEventArgs { Button = 0, ClientX = 10, ClientY = 10 });
        editor.Find("rect.part").PointerUp(new PointerEventArgs { Button = 0, ClientX = 10, ClientY = 10 });
        Assert.Equal(before, Transform(editor));

        editor.Find("button.palette-item[data-kind=resistor]").Click();
        Assert.Equal(before, Transform(editor));
    }

    [Fact]
    public void The_parent_passing_an_edit_back_leaves_the_transform_alone()
    {
        var seen = new List<SchematicChange>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./js/schematic-canvas.js").Setup<double[]>("paneBox", _ => true).SetResult([0, 0, PaneWidth, PaneHeight]);
        var editor = Render<SchematicEditor>(p => p.Add(c => c.Netlist, "").Add(c => c.Changed, (SchematicChange c) => seen.Add(c)));
        editor.Find("button.palette-item[data-kind=source]").Click();
        editor.Find("button[data-zoom=in]").Click();
        var before = Transform(editor);

        editor.Render(p => p.Add(c => c.Netlist, seen[^1].Netlist).Add(c => c.Layout, seen[^1].Layout).Add(c => c.Changed, (SchematicChange c) => seen.Add(c)));

        Assert.Equal(before, Transform(editor));
    }

    [Fact]
    public void Fit_on_a_lone_part_is_at_most_100_percent_and_centres()
    {
        var view = ViewTransform.Fit(0, 0, 20, 10, PaneWidth, PaneHeight, 24);

        Assert.True(view.Zoom <= 1);
        var c = view.WorldToScreen(new Point(10, 5));
        Assert.Equal((PaneWidth / 2, PaneHeight / 2), (c.X, c.Y));
    }

    [Fact]
    public void Reveal_pans_by_the_least_and_never_zooms()
    {
        var view = new ViewTransform(1, 0, 0);

        Assert.Equal(view, view.Reveal(new Point(100, 100), new Point(160, 140), PaneWidth, PaneHeight, 24));

        var right = view.Reveal(new Point(1300, 100), new Point(1360, 140), PaneWidth, PaneHeight, 24);
        Assert.Equal((1d, PaneWidth - 24 - 1360, 0d), (right.Zoom, right.X, right.Y));

        var topLeft = view.Reveal(new Point(-50, -30), new Point(10, 10), PaneWidth, PaneHeight, 24);
        Assert.Equal((74d, 54d), (topLeft.X, topLeft.Y));
    }
}
