using System.Globalization;
using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Ssp.Web.Components;
using Ssp.Web.Schematic;

namespace Ssp.Web.Tests;

public class SchematicBuildFlowTests : BunitContext
{
    const string TwoResistors = "* two resistors\nV1 in 0 1\nR1 in a 1k\nR2 b 0 1k\n.END\n";
    const double PaneWidth = 1280, PaneHeight = 700, Button = 44;

    IRenderedComponent<SchematicEditor> Editor(string netlist, List<SchematicChange> seen)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./js/schematic-canvas.js").Setup<double[]>("paneBox", _ => true).SetResult([0, 0, PaneWidth, PaneHeight]);
        return Render<SchematicEditor>(p => p
            .Add(c => c.Netlist, netlist)
            .Add(c => c.Changed, (SchematicChange c) => seen.Add(c)));
    }

    static double Num(string text, string name) =>
        double.Parse(Regex.Match(text, name + @"\s*[:=]\s*""?(-?[\d.]+)").Groups[1].Value, CultureInfo.InvariantCulture);

    [Fact]
    public void Action_buttons_of_a_horizontal_part_do_not_touch_its_pin_circles()
    {
        var editor = Editor("", []);
        editor.Find("button.palette-item[data-kind=resistor]").Click();
        editor.Find("button[data-zoom=fit]").Click();
        Assert.NotEmpty(editor.FindAll("rect.part.selected"));

        // NOTE: Screen position of a world point is world * zoom + view offset. The stage style holds the offset plus the box origin times the zoom.
        var style = editor.Find("figure.schematic").GetAttribute("style")!;
        var (zoom, sx, sy) = (Num(style, "--z"), Num(style, "--x"), Num(style, "--y"));
        var box = Regex.Match(editor.Find("svg.schematic-pins").GetAttribute("viewBox")!, @"(-?[\d.]+) (-?[\d.]+)");
        var (ox, oy) = (sx - double.Parse(box.Groups[1].Value, CultureInfo.InvariantCulture) * zoom, sy - double.Parse(box.Groups[2].Value, CultureInfo.InvariantCulture) * zoom);

        var buttons = editor.FindAll(".part-actions button");
        Assert.Equal(4, buttons.Count);
        var circles = editor.FindAll("circle.pin-hit").Select(c => (X: Num(c.OuterHtml, "cx") * zoom + ox, Y: Num(c.OuterHtml, "cy") * zoom + oy, R: Num(c.OuterHtml, "r") * zoom)).ToList();
        Assert.True(circles.Count >= 2);
        var rects = buttons.Select(b => (L: Num(b.GetAttribute("style")!, "left"), T: Num(b.GetAttribute("style")!, "top"))).ToList();
        foreach (var (l, t) in rects)
        {
            foreach (var c in circles)
            {
                var (dx, dy) = (Math.Max(Math.Abs(c.X - l) - Button / 2, 0), Math.Max(Math.Abs(c.Y - t) - Button / 2, 0));
                Assert.True(Math.Sqrt(dx * dx + dy * dy) > c.R, $"button at {l},{t} overlaps pin circle at {c.X},{c.Y} r {c.R}");
            }
        }
        // One row above the part: the same top for all four, and above every pin.
        Assert.Single(rects.Select(r => r.T).Distinct());
        Assert.All(rects, r => Assert.True(r.T < circles.Min(c => c.Y)));
    }

    [Fact]
    public void Fit_on_one_part_is_at_most_100_percent_and_centres()
    {
        var view = ViewTransform.Fit(0, 0, 40, 20, 1280, 700, 24);
        Assert.True(view.Zoom <= 1);
        Assert.Equal(1280 / 2.0, 40 * view.Zoom / 2 + view.X, 9);
        Assert.Equal(700 / 2.0, 20 * view.Zoom / 2 + view.Y, 9);
    }

    [Fact]
    public void A_click_on_empty_canvas_clears_selection_and_the_active_pin()
    {
        var editor = Editor(TwoResistors, []);
        editor.Find("rect.part[data-ref=\"R1\"]").Click();
        Assert.NotEmpty(editor.FindAll("rect.part.selected"));
        editor.Find("svg.schematic-pins").Click();
        Assert.Empty(editor.FindAll("rect.part.selected"));
        Assert.Empty(editor.FindAll(".part-actions button"));

        editor.FindAll("polyline.wire-hit")[0].Click();
        Assert.NotEmpty(editor.FindAll("polyline.wire-hit.selected"));
        editor.Find("svg.schematic-pins").Click();
        Assert.Empty(editor.FindAll("polyline.wire-hit.selected"));

        editor.Find("circle.pin[data-ref=\"R1\"][data-pin=\"0\"]").Click();
        var dir = editor.Find("g.pin-add").GetAttribute("data-dir")!;
        editor.Find($"g.pin-add[data-dir={dir}]").Click();
        Assert.NotEmpty(editor.FindAll(".pin-menu"));
        editor.Find("svg.schematic-pins").Click();
        Assert.Empty(editor.FindAll(".pin-menu"));
        Assert.Empty(editor.FindAll("g.pin-add"));
        Assert.Empty(editor.FindAll("circle.pin.active"));
    }

    [Fact]
    public void A_click_on_a_pin_of_a_selected_part_makes_the_pin_active_and_the_part_unselected()
    {
        var editor = Editor(TwoResistors, []);
        editor.Find("rect.part[data-ref=\"R1\"]").Click();
        editor.Find("circle.pin[data-ref=\"R1\"][data-pin=\"1\"]").Click();
        Assert.Empty(editor.FindAll("rect.part.selected"));
        Assert.Empty(editor.FindAll(".part-actions button"));
        Assert.Equal("1", editor.Find("circle.pin.active").GetAttribute("data-pin"));
    }

    [Fact]
    public void The_empty_canvas_text_says_how_to_start()
    {
        var editor = Editor("", []);
        Assert.Equal("Tap a part in the palette to start. Then tap a pin to see where to go next.", editor.Find("p.schematic-empty").TextContent.Trim());
    }

    [Fact]
    public void New_gives_an_empty_netlist_without_asking_and_undo_restores()
    {
        var seen = new List<SchematicChange>();
        var editor = Editor(TwoResistors, seen);
        editor.Find("button[data-action=new]").Click();
        Assert.Equal("", seen[^1].Netlist.Trim());
        Assert.Empty(seen[^1].Layout.Parts);
        Assert.Empty(editor.FindAll("svg.schematic-pins"));

        editor.Find(".delete-status button").Click();
        Assert.Equal(TwoResistors, seen[^1].Netlist);
        Assert.NotEmpty(seen[^1].Layout.Parts);
    }

    [Theory]
    [InlineData("s", "V2 ")]
    [InlineData("i", "ssp:input")]
    [InlineData("o", "ssp:output")]
    [InlineData("l", "LED_RED")]
    [InlineData("n", "QPNP")]
    public void Each_new_letter_places_its_kind(string key, string expected)
    {
        var seen = new List<SchematicChange>();
        var editor = Editor(TwoResistors, seen);
        editor.Find("circle.pin[data-ref=\"R1\"][data-pin=\"0\"]").Click();
        var dir = editor.Find("g.pin-add").GetAttribute("data-dir")!;
        var arrow = dir switch { "up" => "ArrowUp", "down" => "ArrowDown", "left" => "ArrowLeft", _ => "ArrowRight" };
        editor.Find("figure.schematic").KeyDown(new KeyboardEventArgs { Key = arrow });
        editor.Find("figure.schematic").KeyDown(new KeyboardEventArgs { Key = key });
        Assert.Contains(expected, seen[^1].Netlist);
    }
}
