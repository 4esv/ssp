using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Ssp.Web.Components;
using Ssp.Web.Editing;
using Ssp.Web.Schematic;
using Point = Ssp.Core.Layout.Point;

namespace Ssp.Web.Tests;

// NOTE: The gestures of a finger on the canvas (#176): a long press and a pinch. The clock is fake, so the tests do not wait.
public class TouchGestureTests : BunitContext
{
    static readonly TimeSpan JustUnder = LongPress.Hold - TimeSpan.FromMilliseconds(1);

    [Fact]
    public void A_press_held_for_the_hold_time_is_a_long_press()
    {
        var clock = new ManualTimeProvider();
        var held = 0;
        using var press = new LongPress(clock, () => held++);

        press.Down(1, 100, 100);
        clock.Advance(JustUnder);
        Assert.Equal(0, held);
        clock.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(1, held);
        Assert.True(press.Fired);
        Assert.True(press.Up(1));
    }

    [Fact]
    public void A_press_let_go_before_the_hold_time_is_a_tap()
    {
        var clock = new ManualTimeProvider();
        var held = 0;
        using var press = new LongPress(clock, () => held++);

        press.Down(1, 100, 100);
        clock.Advance(JustUnder);
        Assert.False(press.Up(1));
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(0, held);
        Assert.False(press.Fired);
    }

    [Fact]
    public void A_finger_that_drifts_within_the_slop_is_still_a_long_press()
    {
        var clock = new ManualTimeProvider();
        var held = 0;
        using var press = new LongPress(clock, () => held++);

        press.Down(1, 100, 100);
        press.Move(1, 100 + LongPress.Slop, 100 - LongPress.Slop);
        clock.Advance(LongPress.Hold);

        Assert.Equal(1, held);
    }

    [Fact]
    public void A_move_past_the_slop_is_a_drag_and_never_a_long_press()
    {
        var clock = new ManualTimeProvider();
        var held = 0;
        using var press = new LongPress(clock, () => held++);

        press.Down(1, 100, 100);
        press.Move(1, 100 + LongPress.Slop + 1, 100);
        press.Move(1, 100, 100);
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(0, held);
        Assert.False(press.Up(1));
    }

    [Fact]
    public void A_second_finger_makes_a_pinch_and_never_a_long_press()
    {
        var clock = new ManualTimeProvider();
        var held = 0;
        using var press = new LongPress(clock, () => held++);

        press.Down(1, 100, 100);
        press.Down(2, 200, 200);
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(0, held);
    }

    [Fact]
    public void A_new_press_forgets_the_last_long_press()
    {
        var clock = new ManualTimeProvider();
        using var press = new LongPress(clock, () => { });

        press.Down(1, 100, 100);
        clock.Advance(LongPress.Hold);
        press.Up(1);
        Assert.True(press.Fired);

        press.Down(2, 100, 100);
        Assert.False(press.Fired);
    }

    [Fact]
    public void A_pinch_scales_by_the_ratio_of_the_finger_distances_and_keeps_the_point_between_them()
    {
        var view = new ViewTransform(1, 20, -10);
        var (still, before, after) = (new Point(100, 200), new Point(200, 200), new Point(300, 220));
        var world = view.ScreenToWorld(Mid(still, before));

        var pinched = view.Pinch(before, after, still);

        var ratio = Distance(still, after) / Distance(still, before);
        Assert.Equal(ratio, pinched.Zoom, 9);
        var at = pinched.WorldToScreen(world);
        Assert.Equal(Mid(still, after).X, at.X, 9);
        Assert.Equal(Mid(still, after).Y, at.Y, 9);
    }

    [Fact]
    public void A_pinch_stops_at_the_zoom_limits()
    {
        var view = new ViewTransform(ViewTransform.MaxZoom, 0, 0);
        var pinched = view.Pinch(new Point(110, 100), new Point(500, 100), new Point(100, 100));
        Assert.Equal(ViewTransform.MaxZoom, pinched.Zoom);

        var zoomedOut = new ViewTransform(ViewTransform.MinZoom, 0, 0).Pinch(new Point(500, 100), new Point(101, 100), new Point(100, 100));
        Assert.Equal(ViewTransform.MinZoom, zoomedOut.Zoom);
    }

    [Fact]
    public void Two_fingers_on_the_canvas_pinch_the_view()
    {
        var editor = Editor(new ManualTimeProvider());
        var pins = editor.Find("svg.schematic-pins");
        var zoom = editor.Find(".zoom-level").GetAttribute("value")!;

        pins.PointerDown(Pointer(1, 100, 200));
        pins.PointerDown(Pointer(2, 200, 200));
        pins.PointerMove(Pointer(2, 300, 200));
        pins.PointerUp(Pointer(2, 300, 200));
        pins.PointerUp(Pointer(1, 100, 200));

        Assert.NotEqual(zoom, editor.Find(".zoom-level").GetAttribute("value")!);
    }

    [Fact]
    public void A_long_press_on_a_part_opens_its_actions_and_a_drift_after_it_moves_nothing()
    {
        var clock = new ManualTimeProvider();
        var seen = new List<SchematicChange>();
        var editor = Editor(clock, seen);
        var part = editor.Find("rect.part[data-ref=\"R1\"]");

        part.PointerDown(Pointer(1, 200, 200));
        editor.Find("rect.part[data-ref=\"R1\"]").PointerMove(Pointer(1, 208, 206));
        clock.Advance(LongPress.Hold);
        editor.Find("rect.part[data-ref=\"R1\"]").PointerMove(Pointer(1, 240, 240));
        editor.Find("rect.part[data-ref=\"R1\"]").PointerUp(Pointer(1, 240, 240));

        editor.WaitForAssertion(() => Assert.Equal(["delete", "rotate", "flip", "duplicate"], editor.FindAll(".part-action").Select(b => b.GetAttribute("data-action"))));
        Assert.Empty(seen);
        Assert.Empty(editor.FindAll(".part.ghost"));
    }

    [Fact]
    public void A_finger_drift_under_the_slop_moves_no_part()
    {
        var seen = new List<SchematicChange>();
        var editor = Editor(new ManualTimeProvider(), seen);

        editor.Find("rect.part[data-ref=\"R1\"]").PointerDown(Pointer(1, 200, 200));
        editor.Find("rect.part[data-ref=\"R1\"]").PointerMove(Pointer(1, 208, 208));
        editor.Find("rect.part[data-ref=\"R1\"]").PointerUp(Pointer(1, 208, 208));

        Assert.Empty(seen);
    }

    [Fact]
    public void A_long_press_on_empty_canvas_keeps_the_selection_and_the_view()
    {
        var clock = new ManualTimeProvider();
        var seen = new List<SchematicChange>();
        var editor = Editor(clock, seen);
        editor.Find("rect.part[data-ref=\"R1\"]").Click();
        var style = editor.Find("figure.schematic").GetAttribute("style");

        var pins = editor.Find("svg.schematic-pins");
        pins.PointerDown(Pointer(1, 20, 480));
        pins.PointerMove(Pointer(1, 28, 486));
        clock.Advance(LongPress.Hold);
        pins.PointerUp(Pointer(1, 28, 486));
        editor.Find("figure.schematic").Click();

        Assert.Empty(seen);
        Assert.Equal(style, editor.Find("figure.schematic").GetAttribute("style"));
        Assert.NotEmpty(editor.FindAll(".part-action"));
    }

    [Fact]
    public void A_pin_hit_is_44_px_across_at_any_zoom()
    {
        var editor = Editor(new ManualTimeProvider());
        foreach (var step in new[] { "out", "out", "out", "out", "out", "out", "out" })
        {
            var zoom = double.Parse(editor.Find(".zoom-level").GetAttribute("value")!.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture) / 100;
            foreach (var hit in editor.FindAll("circle.pin-hit"))
            {
                var r = double.Parse(hit.GetAttribute("r")!, System.Globalization.CultureInfo.InvariantCulture);
                Assert.True(2 * r * zoom >= 44 - 0.5, $"A pin hit is {2 * r * zoom:0.#} px across at {zoom:0%}.");
            }
            editor.Find($"button[data-zoom={step}]").Click();
        }
    }

    IRenderedComponent<SchematicEditor> Editor(TimeProvider clock, List<SchematicChange>? seen = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./js/schematic-canvas.js").Setup<double[]>("paneBox", _ => true).SetResult([0, 0, 390, 500]);
        var editor = Render<SchematicEditor>(p => p
            .Add(c => c.Netlist, "* one\nV1 in 0 1\nR1 in 0 1k\n.END\n")
            .Add(c => c.Clock, clock)
            .Add(c => c.Changed, (SchematicChange c) => seen?.Add(c)));
        editor.Find("button[data-zoom=fit]").Click();
        return editor;
    }

    static PointerEventArgs Pointer(long id, double x, double y) =>
        new() { PointerId = id, ClientX = x, ClientY = y, PointerType = "touch", IsPrimary = id == 1 };

    static Point Mid(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
