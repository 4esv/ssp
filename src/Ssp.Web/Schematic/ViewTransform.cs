using Point = Ssp.Core.Layout.Point;

namespace Ssp.Web.Schematic;

/// <summary>The view of the schematic canvas: screen = world * Zoom + (X, Y). World is in view box units, screen is in pixels of the pane.</summary>
public sealed record ViewTransform(double Zoom, double X, double Y)
{
    public const double MinZoom = 0.25;
    public const double MaxZoom = 4;

    public static ViewTransform Identity { get; } = new(1, 0, 0);

    public Point ScreenToWorld(Point screen) => new((screen.X - X) / Zoom, (screen.Y - Y) / Zoom);

    public Point WorldToScreen(Point world) => new(world.X * Zoom + X, world.Y * Zoom + Y);

    public ViewTransform Pan(double dx, double dy) => this with { X = X + dx, Y = Y + dy };

    /// <summary>Scales by a factor, within the limits, so that the world point under <paramref name="screen"/> stays under it.</summary>
    public ViewTransform ZoomAbout(Point screen, double factor)
    {
        var zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        var world = ScreenToWorld(screen);
        return new ViewTransform(zoom, screen.X - world.X * zoom, screen.Y - world.Y * zoom);
    }

    /// <summary>The view that centres a world box in a pane, as large as the margin allows, within the zoom limits and never past 100 percent.</summary>
    public static ViewTransform Fit(double minX, double minY, double width, double height, double paneWidth, double paneHeight, double margin)
    {
        var fit = Math.Min((paneWidth - 2 * margin) / Math.Max(width, 1e-9), (paneHeight - 2 * margin) / Math.Max(height, 1e-9));
        var zoom = double.IsFinite(fit) ? Math.Clamp(fit, MinZoom, 1) : 1;
        return new ViewTransform(zoom, (paneWidth - width * zoom) / 2 - minX * zoom, (paneHeight - height * zoom) / 2 - minY * zoom);
    }
}
