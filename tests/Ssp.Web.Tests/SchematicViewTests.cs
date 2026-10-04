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
    public void Zoom_stops_at_25_and_400_percent()
    {
        var at = new Point(10, 10);

        Assert.Equal(0.25, new ViewTransform(1, 0, 0).ZoomAbout(at, 0.001).Zoom);
        Assert.Equal(4, new ViewTransform(1, 0, 0).ZoomAbout(at, 1000).Zoom);
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
        var view = ViewTransform.Fit(100, 50, 800, 400, 1000, 500, margin);

        var min = view.WorldToScreen(new Point(100, 50));
        var max = view.WorldToScreen(new Point(900, 450));
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
        Assert.Equal(4, ViewTransform.Fit(0, 0, 10, 10, 1000, 1000, 0).Zoom);
        Assert.Equal(0.25, ViewTransform.Fit(0, 0, 100000, 100000, 1000, 1000, 0).Zoom);
    }
}
