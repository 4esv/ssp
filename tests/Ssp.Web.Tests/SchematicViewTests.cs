using Ssp.Web.Schematic;
using Point = Ssp.Core.Layout.Point;

namespace Ssp.Web.Tests;

public class SchematicViewTests
{
    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1)]
    [InlineData(2.5)]
    [InlineData(4)]
    public void Screen_to_world_and_back_round_trips(double zoom)
    {
        var view = new ViewTransform(zoom, 37.5, -120);
        var screen = new Point(311, 94);

        var back = view.WorldToScreen(view.ScreenToWorld(screen));

        Assert.Equal(screen.X, back.X, 9);
        Assert.Equal(screen.Y, back.Y, 9);
    }

    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 0.4)]
    public void Zoom_about_a_point_keeps_the_point_fixed(double zoom, double factor)
    {
        var view = new ViewTransform(zoom, 40, 25);
        var at = new Point(200, 150);
        var before = view.ScreenToWorld(at);

        var zoomed = view.ZoomAbout(at, factor);

        var after = zoomed.ScreenToWorld(at);
        Assert.Equal(before.X, after.X, 9);
        Assert.Equal(before.Y, after.Y, 9);
        Assert.Equal(Math.Clamp(zoom * factor, ViewTransform.MinZoom, ViewTransform.MaxZoom), zoomed.Zoom, 9);
    }

    [Fact]
    public void Zoom_stops_at_10_and_800_percent()
    {
        var at = new Point(10, 10);

        Assert.Equal(0.1, new ViewTransform(1, 0, 0).ZoomAbout(at, 0.001).Zoom);
        Assert.Equal(8, new ViewTransform(1, 0, 0).ZoomAbout(at, 1000).Zoom);
    }

    [Fact]
    public void Pan_moves_the_view_by_the_pixels()
    {
        var view = new ViewTransform(2, 10, 20).Pan(5, -8);

        Assert.Equal((2d, 15d, 12d), (view.Zoom, view.X, view.Y));
    }

    [Fact]
    public void Fit_frames_the_bounding_box_with_a_margin()
    {
        const double margin = 24;
        var view = ViewTransform.Fit(100, 50, 1600, 800, 1000, 500, margin);

        var min = view.WorldToScreen(new Point(100, 50));
        var max = view.WorldToScreen(new Point(1700, 850));
        Assert.True(min.X >= margin - 1e-9 && min.Y >= margin - 1e-9);
        Assert.True(max.X <= 1000 - margin + 1e-9 && max.Y <= 500 - margin + 1e-9);
        // NOTE: The box is centred and as large as the margin allows on the tight axis.
        Assert.Equal(1000 - max.X, min.X, 9);
        Assert.Equal(500 - max.Y, min.Y, 9);
        Assert.Equal(margin, Math.Min(min.X, min.Y), 9);
    }

    [Fact]
    public void Fit_stays_inside_the_zoom_limits()
    {
        Assert.Equal(2, ViewTransform.Fit(0, 0, 10, 10, 1000, 1000, 0).Zoom);
        Assert.Equal(4, ViewTransform.Fit(0, 0, 10, 10, 1000, 1000, 0, maxZoom: 4).Zoom);
        Assert.Equal(0.1, ViewTransform.Fit(0, 0, 100000, 100000, 1000, 1000, 0).Zoom);
    }

    [Theory]
    [InlineData(100, 0, false)]
    [InlineData(-100, 0, false)]
    [InlineData(3, 1, false)]
    [InlineData(-3, 1, false)]
    public void A_mouse_wheel_tick_is_between_1_1_and_1_25(double deltaY, long mode, bool ctrl)
    {
        var factor = ViewTransform.WheelFactor(deltaY, mode, ctrl);

        Assert.InRange(Math.Max(factor, 1 / factor), 1.1, 1.25);
        Assert.Equal(deltaY < 0, factor > 1);
    }

    [Fact]
    public void A_pinch_is_smooth_and_a_fast_spin_is_capped()
    {
        Assert.InRange(ViewTransform.WheelFactor(-2, 0, true), 1.01, 1.05);
        Assert.InRange(ViewTransform.WheelFactor(-10, 0, false), 1.01, 1.05);
        Assert.Equal(Math.Pow(ViewTransform.WheelTick, 3), ViewTransform.WheelFactor(-900, 0, false), 9);
    }

    [Theory]
    [InlineData(200, 200, true)]
    [InlineData(700, 400, true)]
    [InlineData(200, 200, false)]
    [InlineData(700, 400, false)]
    public void Ten_wheel_ticks_keep_the_point_under_the_pointer_within_a_pixel(double x, double y, bool pinch)
    {
        var view = new ViewTransform(1.3, -40, 25);
        var at = new Point(x, y);
        var world = view.ScreenToWorld(at);

        for (var i = 0; i < 10; i++)
        {
            view = view.ZoomAbout(at, ViewTransform.WheelFactor(pinch ? -10 : -100, 0, pinch));
            var back = view.WorldToScreen(world);
            Assert.True(Math.Abs(back.X - x) < 1 && Math.Abs(back.Y - y) < 1);
        }
    }

    [Fact]
    public void Zoom_to_a_level_keeps_the_point_and_the_view_round_trips_as_text()
    {
        var view = new ViewTransform(0.8, 12.5, -7.25).ZoomTo(new Point(300, 200), 1.5);

        Assert.Equal(1.5, view.Zoom, 9);
        Assert.Equal(new Point(300, 200).X, view.WorldToScreen(new ViewTransform(0.8, 12.5, -7.25).ScreenToWorld(new Point(300, 200))).X, 9);
        Assert.Equal(view.Format(), ViewTransform.Parse(view.Format())!.Format());
        Assert.Null(ViewTransform.Parse("nonsense"));
        Assert.Null(ViewTransform.Parse("99 0 0"));
    }
}
