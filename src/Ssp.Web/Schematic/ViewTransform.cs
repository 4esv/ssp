using Point = Ssp.Core.Layout.Point;

namespace Ssp.Web.Schematic;

/// <summary>The view of the schematic canvas: screen = world * Zoom + (X, Y). World is in view box units, screen is in pixels of the pane.</summary>
public sealed record ViewTransform(double Zoom, double X, double Y)
{
    public const double MinZoom = 0.1;
    public const double MaxZoom = 8;

    /// <summary>The most that Fit zooms in on a small circuit.</summary>
    public const double MaxFitZoom = 2;

    /// <summary>The zoom of one tick of a mouse wheel.</summary>
    public const double WheelTick = 1.15;

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

    /// <summary>
    /// The factor for one wheel event. A mouse wheel moves in ticks, 100 px or a few lines each: a tick is <see cref="WheelTick"/>, and a fast spin counts up to 3 ticks.
    /// A pinch on a trackpad is a wheel with Ctrl held and small deltas: it follows the fingers, smooth. A small delta without Ctrl is smooth too.
    /// </summary>
    public static double WheelFactor(double deltaY, long deltaMode, bool ctrl)
    {
        var pixels = deltaY * (deltaMode == 1 ? 16 : 1);
        if (ctrl) return Math.Exp(-pixels * 0.01);
        if (deltaMode != 1 && Math.Abs(pixels) < 50) return Math.Exp(-pixels * 0.0015);
        var ticks = deltaMode == 1 ? 1 : Math.Clamp(Math.Round(Math.Abs(pixels) / 100), 1, 3);
        return Math.Pow(WheelTick, -Math.Sign(pixels) * ticks);
    }

    /// <summary>Sets the zoom, within the limits, so that the world point under <paramref name="screen"/> stays under it.</summary>
    public ViewTransform ZoomTo(Point screen, double zoom) => ZoomAbout(screen, zoom / Zoom);

    /// <summary>The view as text: zoom, x and y.</summary>
    public string Format() => FormattableString.Invariant($"{Zoom:0.####} {X:0.##} {Y:0.##}");

    /// <summary>The view from <see cref="Format"/>, or null if the text is not one.</summary>
    public static ViewTransform? Parse(string? text)
    {
        var parts = text?.Split(' ');
        if (parts is not { Length: 3 }) return null;
        var n = new double[3];
        for (var i = 0; i < 3; i++)
        {
            if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out n[i]) || !double.IsFinite(n[i])) return null;
        }
        return n[0] is >= MinZoom and <= MaxZoom ? new ViewTransform(n[0], n[1], n[2]) : null;
    }

    /// <summary>
    /// One finger of a pinch moves from <paramref name="before"/> to <paramref name="after"/> while the other stays at <paramref name="other"/>.
    /// The zoom scales by the ratio of the finger distances, within the limits, and the world point between the fingers stays between them.
    /// </summary>
    public ViewTransform Pinch(Point before, Point after, Point other)
    {
        var world = ScreenToWorld(Mid(before, other));
        var zoom = Math.Clamp(Zoom * Distance(after, other) / Math.Max(Distance(before, other), 1), MinZoom, MaxZoom);
        var mid = Mid(after, other);
        return new ViewTransform(zoom, mid.X - world.X * zoom, mid.Y - world.Y * zoom);

        static Point Mid(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }

    /// <summary>Pans by the least that brings a world box inside the pane, with a margin. A box larger than the pane keeps its top left in. The zoom never changes.</summary>
    public ViewTransform Reveal(Point min, Point max, double paneWidth, double paneHeight, double margin)
    {
        var (a, b) = (WorldToScreen(min), WorldToScreen(max));
        return Pan(Shift(a.X, b.X, paneWidth, margin), Shift(a.Y, b.Y, paneHeight, margin));

        static double Shift(double lo, double hi, double size, double margin) =>
            lo < margin ? margin - lo : hi > size - margin ? Math.Max(size - margin - hi, margin - lo) : 0;
    }

    /// <summary>The view that centres a world box in a pane, as large as the margin allows, within the zoom limits and never past <paramref name="maxZoom"/> (200 percent unless given).</summary>
    public static ViewTransform Fit(double minX, double minY, double width, double height, double paneWidth, double paneHeight, double margin, double maxZoom = MaxFitZoom)
    {
        var fit = Math.Min((paneWidth - 2 * margin) / Math.Max(width, 1e-9), (paneHeight - 2 * margin) / Math.Max(height, 1e-9));
        var zoom = double.IsFinite(fit) ? Math.Clamp(fit, MinZoom, maxZoom) : 1;
        return new ViewTransform(zoom, (paneWidth - width * zoom) / 2 - minX * zoom, (paneHeight - height * zoom) / 2 - minY * zoom);
    }
}
