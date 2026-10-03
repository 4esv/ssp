using System.Globalization;
using System.Security;
using System.Text;
using Ssp.Core.Layout;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using SpiceSharp.Components;
using LayoutDoc = Ssp.Core.Layout.Layout;
using Point = Ssp.Core.Layout.Point;

namespace Ssp.Web.Schematic;

/// <summary>Draws a circuit as an SVG schematic from its layout.</summary>
public static class SchematicRenderer
{
    const double Margin = 20;
    const double LabelGap = 4;

    /// <summary>
    /// An SVG document. Each placed component is its symbol at the layout position, with the layout rotation and flip.
    /// A component with no part or no symbol for its kind is a box. Each layout wire is a polyline.
    /// A component that the layout does not place is not drawn. The output is the same for the same input.
    /// </summary>
    public static string ToSvg(LoadedCircuit circuit, LayoutDoc layout, PartMap parts)
    {
        var placements = layout.Parts.ToDictionary(p => p.Reference, StringComparer.OrdinalIgnoreCase);
        var bounds = new Bounds();
        var body = new StringBuilder();
        var labels = new StringBuilder();

        foreach (var c in circuit.Circuit.OfType<IComponent>().OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!placements.TryGetValue(c.Name, out var p)) continue;
            var (inner, box) = Drawing(parts.Parts.GetValueOrDefault(c.Name), c.Nodes.Count);

            var transform = $"translate({N(p.X)} {N(p.Y)})";
            if (Norm(p.Rotation) != 0) transform += $" rotate({N(-Norm(p.Rotation))})";
            if (p.Flip) transform += " scale(1 -1)";
            body.Append("<g data-ref=\"").Append(Escape(c.Name)).Append("\" transform=\"").Append(transform).Append("\">")
                .Append(inner).Append("</g>\n");

            var part = new Bounds();
            foreach (var (x, y) in new[] { (box.X, box.Y), (box.X + box.W, box.Y), (box.X, box.Y + box.H), (box.X + box.W, box.Y + box.H) })
            {
                part.Add(Place(p, x, y));
            }
            bounds.Add(part);
            var labelX = (part.MinX + part.MaxX) / 2;
            var labelY = part.MinY - LabelGap;
            bounds.Add((labelX, labelY - 10));
            labels.Append("<text x=\"").Append(N(labelX)).Append("\" y=\"").Append(N(labelY)).Append("\">")
                  .Append(Escape(c.Name)).Append("</text>\n");
        }

        foreach (var w in layout.Wires)
        {
            foreach (var pt in w.Points) bounds.Add((pt.X, pt.Y));
            body.Append("<polyline data-net=\"").Append(Escape(w.Net)).Append("\" points=\"")
                .Append(string.Join(' ', w.Points.Select(pt => $"{N(pt.X)},{N(pt.Y)}"))).Append("\"/>\n");
        }

        var (minX, minY, width, height) = bounds.Empty
            ? (0.0, 0.0, 0.0, 0.0)
            : (bounds.MinX - Margin, bounds.MinY - Margin, bounds.MaxX - bounds.MinX + 2 * Margin, bounds.MaxY - bounds.MinY + 2 * Margin);

        return new StringBuilder()
            .Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"")
            .Append(N(minX)).Append(' ').Append(N(minY)).Append(' ').Append(N(width)).Append(' ').Append(N(height))
            .Append("\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">\n")
            .Append(body)
            .Append("<g fill=\"currentColor\" stroke=\"none\" font-family=\"sans-serif\" font-size=\"10\" text-anchor=\"middle\">\n")
            .Append(labels)
            .Append("</g>\n</svg>\n")
            .ToString();
    }

    /// <summary>
    /// The schematic position of each pin of a placed component, in node order. A component with a symbol uses the
    /// symbol pins. Other components use the pins of the fallback box.
    /// </summary>
    public static IReadOnlyList<Point> Pins(IComponent component, PartPlacement placement, PartRow? row)
    {
        var count = component.Nodes.Count;
        IEnumerable<(double X, double Y)> local = Symbol(row) is { } symbol && symbol.Pins.Count == count
            ? symbol.Pins.Select(p => ((double)p.X, (double)p.Y))
            : Enumerable.Range(0, count).Select(i => (i % 2 == 0 ? 0.0 : 60.0, 2.0 * Symbols.Grid * (i / 2)));
        return local.Select(p => Place(placement, p.X, p.Y)).Select(p => new Point(Math.Round(p.X, 2) + 0.0, Math.Round(p.Y, 2) + 0.0)).ToList();
    }

    readonly record struct Box(double X, double Y, double W, double H);

    // NOTE: The symbol pins are in node order. Pins uses the fallback pins when the symbol has a different pin count.
    static Symbol? Symbol(PartRow? row)
    {
        if (row is null) return null;
        try
        {
            return Symbols.For(row.Kind);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    // The symbol content without its outer svg element, and its view box.
    static (string Inner, Box Box) Drawing(PartRow? row, int pins)
    {
        var symbol = Symbol(row);
        if (symbol is null) return Fallback(pins);

        var svg = symbol.Svg;
        var open = svg.IndexOf('>', StringComparison.Ordinal);
        var inner = svg[(open + 1)..svg.LastIndexOf("</svg>", StringComparison.Ordinal)];
        var viewBoxAt = svg.IndexOf("viewBox=\"", StringComparison.Ordinal) + "viewBox=\"".Length;
        var v = svg[viewBoxAt..svg.IndexOf('"', viewBoxAt)].Split(' ').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        return (inner, new Box(v[0], v[1], v[2], v[3]));
    }

    // A box with leads. Pins alternate left and right, two to a row, on the symbol grid.
    static (string Inner, Box Box) Fallback(int pins)
    {
        const int Step = 2 * Symbols.Grid;
        var rows = Math.Max(1, (pins + 1) / 2);
        var bottom = Step * (rows - 1) + Symbols.Grid;
        var d = new StringBuilder();
        for (var i = 0; i < pins; i++)
        {
            d.Append(i % 2 == 0 ? "M0 " : "M40 ").Append(Step * (i / 2)).Append("H").Append(i % 2 == 0 ? 20 : 60);
        }
        d.Append("M20 ").Append(-Symbols.Grid).Append("H40V").Append(bottom).Append("H20Z");
        return ($"<path d=\"{d}\"/>", new Box(0, -Symbols.Grid, 60, bottom + Symbols.Grid));
    }

    // NOTE: Same order as the transform attribute: flip about the x axis, rotate counter-clockwise on screen, then translate.
    static (double X, double Y) Place(PartPlacement p, double x, double y)
    {
        if (p.Flip) y = -y;
        var r = -Norm(p.Rotation) * Math.PI / 180;
        return (p.X + x * Math.Cos(r) - y * Math.Sin(r), p.Y + x * Math.Sin(r) + y * Math.Cos(r));
    }

    sealed class Bounds
    {
        public double MinX { get; private set; } = double.PositiveInfinity;
        public double MinY { get; private set; } = double.PositiveInfinity;
        public double MaxX { get; private set; } = double.NegativeInfinity;
        public double MaxY { get; private set; } = double.NegativeInfinity;
        public bool Empty => double.IsPositiveInfinity(MinX);

        public void Add((double X, double Y) pt)
        {
            MinX = Math.Min(MinX, pt.X);
            MinY = Math.Min(MinY, pt.Y);
            MaxX = Math.Max(MaxX, pt.X);
            MaxY = Math.Max(MaxY, pt.Y);
        }

        public void Add(Bounds other)
        {
            if (other.Empty) return;
            Add((other.MinX, other.MinY));
            Add((other.MaxX, other.MaxY));
        }
    }

    static int Norm(int degrees) => (degrees % 360 + 360) % 360;

    static string N(double v) => (Math.Round(v, 2) + 0.0).ToString("0.##", CultureInfo.InvariantCulture);

    static string Escape(string s) => SecurityElement.Escape(s);
}
