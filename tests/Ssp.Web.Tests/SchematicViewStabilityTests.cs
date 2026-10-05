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
        Assert.Matches(@"^\d+%$", editor.Find("input.zoom-level").GetAttribute("value")!);
        Assert.Equal(Zoom(Transform(editor)), double.Parse(editor.Find("input.zoom-level").GetAttribute("value")!.TrimEnd('%'), CultureInfo.InvariantCulture) / 100, 2);
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
    public void Fit_on_a_lone_part_is_at_most_200_percent_and_centres()
    {
        var view = ViewTransform.Fit(0, 0, 20, 10, PaneWidth, PaneHeight, 24);

        Assert.True(view.Zoom <= 2);
        var c = view.WorldToScreen(new Point(10, 5));
        Assert.Equal((PaneWidth / 2, PaneHeight / 2), (c.X, c.Y));
    }

    [Fact]
    public void Reveal_keeps_a_box_below_a_band_at_the_top_that_an_overlay_covers()
    {
        var view = new ViewTransform(1, 0, 0);

        var below = view.Reveal(new Point(100, 60), new Point(160, 80), PaneWidth, PaneHeight, 24, top: 100);

        Assert.Equal((1d, 0d, 64d), (below.Zoom, below.X, below.Y));
        Assert.Equal(view, view.Reveal(new Point(100, 200), new Point(160, 240), PaneWidth, PaneHeight, 24, top: 100));
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

    const string Chain = "* chain\nV1 in 0 1\nR1 in a 1k\nR2 a b 1k\nR3 b 0 1k\n.END\n";

    static string Level(IRenderedComponent<SchematicEditor> e) => e.Find("input.zoom-level").GetAttribute("value")!;

    [Fact]
    public void A_number_typed_in_the_zoom_field_sets_the_zoom()
    {
        var editor = Editor(Chain);

        editor.Find("input.zoom-level").Change("150");
        Assert.Equal(1.5, Transform(editor).Zoom, 9);
        Assert.Equal("150%", Level(editor));

        editor.Find("input.zoom-level").Change("5000%");
        Assert.Equal(ViewTransform.MaxZoom, Transform(editor).Zoom);

        editor.Find("input.zoom-level").Change("nonsense");
        Assert.Equal(ViewTransform.MaxZoom, Transform(editor).Zoom);
    }

    [Fact]
    public void The_zoom_menu_has_fit_100_percent_and_fit_selection()
    {
        var editor = Editor(Chain);
        editor.Find("button[data-zoom=in]").Click();
        editor.Find("button[data-zoom=menu]").Click();
        Assert.Equal(["Fit", "100%", "Fit selection"], editor.FindAll(".zoom-menu button").Select(b => b.TextContent.Trim()));

        editor.Find("[data-zoom-item=\"100\"]").Click();
        Assert.Equal(1, Transform(editor).Zoom, 9);
        Assert.Empty(editor.FindAll(".zoom-menu"));

        editor.Find(".part[data-ref=R1]").Click();
        editor.Find(".part[data-ref=R2]").Click(new MouseEventArgs { ShiftKey = true });
        editor.Find("button[data-zoom=menu]").Click();
        editor.Find("[data-zoom-item=\"selection\"]").Click();
        Assert.True(Transform(editor).Zoom > 1, "Two parts fill the pane, so the zoom goes up.");

        editor.Find("button[data-zoom=menu]").Click();
        editor.Find("[data-zoom-item=\"fit\"]").Click();
        Assert.True(Transform(editor).Zoom <= ViewTransform.MaxFitZoom);
    }

    [Fact]
    public void The_f_key_fits_a_selection_of_two_or_more_parts()
    {
        var editor = Editor(Chain);
        var all = Transform(editor);
        editor.Find(".part[data-ref=R2]").Click();
        editor.Find(".part[data-ref=R3]").Click(new MouseEventArgs { ShiftKey = true });

        editor.Find("figure.schematic").KeyDown(new KeyboardEventArgs { Key = "f" });

        Assert.True(Transform(editor).Zoom > all.Zoom);
    }

    [Fact]
    public void A_double_click_on_empty_canvas_zooms_in_at_that_point_and_on_a_part_it_does_not()
    {
        var editor = Editor(Chain);
        var before = Transform(editor);
        var at = new Point(PaneWidth - 100, 100);
        var world = new ViewTransform(before.Zoom, before.X, before.Y).ScreenToWorld(at);

        editor.Find("figure.schematic").DoubleClick(new MouseEventArgs { ClientX = at.X, ClientY = at.Y });

        var after = Transform(editor);
        Assert.True(after.Zoom > before.Zoom);
        var back = new ViewTransform(after.Zoom, after.X, after.Y).WorldToScreen(world);
        Assert.True(Math.Abs(back.X - at.X) < 1 && Math.Abs(back.Y - at.Y) < 1);

        var part = editor.Find(".part[data-ref=R2]");
        var (x, y) = (double.Parse(part.GetAttribute("x")!, CultureInfo.InvariantCulture) + 1, double.Parse(part.GetAttribute("y")!, CultureInfo.InvariantCulture) + 1);
        var onPart = new ViewTransform(after.Zoom, after.X, after.Y).WorldToScreen(new Point(x, y));
        editor.Find("figure.schematic").DoubleClick(new MouseEventArgs { ClientX = onPart.X, ClientY = onPart.Y });
        Assert.Equal(after.Zoom, Transform(editor).Zoom, 9);
    }

    [Fact]
    public void A_wheel_tick_is_a_step_and_the_pointer_point_stays()
    {
        var editor = Editor(Chain);
        var before = Transform(editor);
        var at = new Point(400, 300);

        editor.Find("figure.schematic").TriggerEvent("onwheel", new WheelEventArgs { DeltaY = -100, ClientX = at.X, ClientY = at.Y });

        var after = Transform(editor);
        Assert.InRange(after.Zoom / before.Zoom, 1.1, 1.25);
        var world = new ViewTransform(before.Zoom, before.X, before.Y).ScreenToWorld(at);
        var back = new ViewTransform(after.Zoom, after.X, after.Y).WorldToScreen(world);
        Assert.True(Math.Abs(back.X - at.X) < 1 && Math.Abs(back.Y - at.Y) < 1);
    }

    [Fact]
    public void The_view_goes_to_the_project_and_comes_back_with_it()
    {
        var seen = new List<string>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./js/schematic-canvas.js").Setup<double[]>("paneBox", _ => true).SetResult([0, 0, PaneWidth, PaneHeight]);
        var editor = Render<SchematicEditor>(p => p.Add(c => c.Netlist, Chain).Add(c => c.ViewChanged, (string v) => seen.Add(v)).Add(c => c.Changed, (SchematicChange _) => { }));
        Assert.Empty(seen);

        editor.Find("button[data-zoom=in]").Click();
        Assert.Single(seen);
        var kept = ViewTransform.Parse(seen[0])!;
        Assert.Equal(Transform(editor).Zoom, kept.Zoom, 3);

        var reopened = Render<SchematicEditor>(p => p.Add(c => c.Netlist, Chain).Add(c => c.View, seen[0]).Add(c => c.Changed, (SchematicChange _) => { }));
        Assert.Equal(kept.Zoom, Transform(reopened).Zoom, 3);
        Assert.Equal(kept.X, Transform(reopened).X, 1);
    }
}
