using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Ssp.Web.Schematic;

/// <summary>
/// A one-page PDF of a schematic: the drawing as vectors, the parts list with a link on each line, and a title block.
/// The PDF is written by hand, with no package. The text is not compressed, and uses the standard Helvetica font.
/// </summary>
public static partial class SchematicPdf
{
    const double PageW = 842, PageH = 595, Edge = 20;
    const double TitleX = 572, TitleY = 500, ListTop = 492, ListRowHeight = 12, ListColumnWidth = 180;
    const int ListColumns = 3, ListRowsPerColumn = 6;

    // NOTE: Helvetica has no fixed width. This is the mean advance, used to place anchored text.
    const double GlyphWidth = 0.556;

    /// <summary>The PDF bytes for a schematic SVG, as <see cref="SchematicRenderer.ToSvg"/> writes it, and its parts list.</summary>
    public static byte[] Build(string svg, PartsList list)
    {
        var root = XDocument.Parse(svg).Root!;
        var view = root.Attribute("viewBox")!.Value.Split(' ').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();

        var content = new StringBuilder();
        content.Append("1 0 0 -1 0 ").Append(N(PageH)).Append(" cm\n");
        Frame(content, list);

        // NOTE: The drawing fits the area above the list and the title block, and is not enlarged past twice its size.
        var (areaX, areaY, areaW, areaH) = (Edge + 10, Edge + 10, PageW - 2 * Edge - 20, ListTop - 10 - Edge - 10);
        var scale = view[2] > 0 && view[3] > 0 ? Math.Min(2, Math.Min(areaW / view[2], areaH / view[3])) : 1;
        var (ox, oy) = (areaX + (areaW - view[2] * scale) / 2 - view[0] * scale, areaY + (areaH - view[3] * scale) / 2 - view[1] * scale);
        content.Append("q\n").Append(Matrix(scale, 0, 0, scale, ox, oy)).Append(" cm\n0 g 0 G\n");
        Draw(content, root, new Style(false, true));
        content.Append("Q\n");

        var links = Parts(content, list);

        return Document(content.ToString(), links);
    }

    static void Frame(StringBuilder c, PartsList list)
    {
        c.Append("0 g 0 G 0.75 w\n");
        c.Append(N(Edge)).Append(' ').Append(N(Edge)).Append(' ').Append(N(PageW - 2 * Edge)).Append(' ').Append(N(PageH - 2 * Edge)).Append(" re S\n");
        var (w, h) = (PageW - Edge - TitleX, PageH - Edge - TitleY);
        c.Append(N(TitleX)).Append(' ').Append(N(TitleY)).Append(' ').Append(N(w)).Append(' ').Append(N(h)).Append(" re S\n");
        c.Append(N(TitleX)).Append(' ').Append(N(TitleY + 40)).Append(" m ").Append(N(TitleX + w)).Append(' ').Append(N(TitleY + 40)).Append(" l S\n");
        c.Append(N(TitleX + w / 2)).Append(' ').Append(N(TitleY + 40)).Append(" m ").Append(N(TitleX + w / 2)).Append(' ').Append(N(TitleY + h)).Append(" l S\n");

        Text(c, "F1", 7, TitleX + 6, TitleY + 10, "Title");
        Text(c, "F2", 11, TitleX + 6, TitleY + 31, Fit(list.Title.Length > 0 ? list.Title : "Untitled", 11, w - 12));
        Text(c, "F1", 9, TitleX + 6, TitleY + 56, $"Parts: {list.Total}");
        Text(c, "F1", 9, TitleX + w / 2 + 6, TitleY + 56, "Drawn with ssp");
    }

    // The list is in columns under the drawing. Each line links to its supplier search. Returns the link rectangles.
    static List<(double X, double Y, double W, double H, string Url)> Parts(StringBuilder c, PartsList list)
    {
        var links = new List<(double, double, double, double, string)>();
        var slots = ListColumns * ListRowsPerColumn;
        var shown = list.Rows.Count > slots ? slots - 1 : list.Rows.Count;
        for (var i = 0; i < shown; i++)
        {
            var r = list.Rows[i];
            var (x, y) = (Edge + 10 + i / ListRowsPerColumn * ListColumnWidth, ListTop + 8 + i % ListRowsPerColumn * ListRowHeight);
            var line = Fit($"{r.Quantity}x {r.References}  {r.Value} {r.Kind}", 8, ListColumnWidth - 8);
            Text(c, "F1", 8, x, y + 8, line);
            links.Add((x, y, ListColumnWidth - 8, ListRowHeight, r.BuyUrl));
        }
        if (shown < list.Rows.Count)
        {
            var i = slots - 1;
            Text(c, "F1", 8, Edge + 10 + i / ListRowsPerColumn * ListColumnWidth, ListTop + 16 + i % ListRowsPerColumn * ListRowHeight, $"+{list.Rows.Count - shown} more rows in the CSV");
        }
        return links;
    }

    readonly record struct Style(bool Fill, bool Stroke);

    static void Draw(StringBuilder c, XElement e, Style inherited)
    {
        var style = new Style(Paint(e, "fill", inherited.Fill), Paint(e, "stroke", inherited.Stroke));
        var open = false;
        if (e.Attribute("transform") is { } t)
        {
            var local = Transform(t.Value);
            c.Append("q\n").Append(Matrix(local)).Append(" cm\n");
            open = true;
        }

        switch (e.Name.LocalName)
        {
            case "path":
                Paint(c, Path(e.Attribute("d")?.Value ?? ""), style);
                break;
            case "polyline":
                var pts = Numbers(e.Attribute("points")?.Value ?? "");
                var line = new StringBuilder();
                for (var i = 0; i + 1 < pts.Count; i += 2) line.Append(N(pts[i])).Append(' ').Append(N(pts[i + 1])).Append(i == 0 ? " m\n" : " l\n");
                Paint(c, line.ToString(), style with { Fill = false });
                break;
            case "circle":
                Paint(c, Circle(Num(e, "cx"), Num(e, "cy"), Num(e, "r")), style);
                break;
            case "text":
                Label(c, e, style);
                break;
        }

        foreach (var child in e.Elements()) Draw(c, child, style);
        if (open) c.Append("Q\n");
    }

    // NOTE: The schematic uses currentColor and none. Anything not none draws in black.
    static bool Paint(XElement e, string name, bool inherited) =>
        e.Attribute(name) is { } a ? a.Value != "none" : inherited;

    static void Paint(StringBuilder c, string path, Style s)
    {
        if (path.Length == 0 || (!s.Fill && !s.Stroke)) return;
        c.Append(path).Append(s.Fill && s.Stroke ? "B\n" : s.Fill ? "f\n" : "S\n");
    }

    // Text is upright: the page is flipped, so the text matrix flips again.
    static void Label(StringBuilder c, XElement e, Style s)
    {
        if (!s.Fill) return;
        var text = e.Value;
        var size = 10.0;
        var width = text.Length * size * GlyphWidth;
        var anchor = e.Attribute("text-anchor")?.Value ?? e.Parent?.Attribute("text-anchor")?.Value ?? "middle";
        var x = Num(e, "x") - (anchor == "end" ? width : anchor == "start" ? 0 : width / 2);
        c.Append("BT /F1 ").Append(N(size)).Append(" Tf 1 0 0 -1 ").Append(N(x)).Append(' ').Append(N(Num(e, "y"))).Append(" Tm (")
            .Append(Escape(text)).Append(") Tj ET\n");
    }

    static void Text(StringBuilder c, string font, double size, double x, double y, string text) =>
        c.Append("BT /").Append(font).Append(' ').Append(N(size)).Append(" Tf 1 0 0 -1 ").Append(N(x)).Append(' ').Append(N(y)).Append(" Tm (")
            .Append(Escape(text)).Append(") Tj ET\n");

    static string Fit(string text, double size, double width)
    {
        var max = Math.Max(1, (int)(width / (size * GlyphWidth)));
        return text.Length <= max ? text : text[..Math.Max(0, max - 1)] + "~";
    }

    static string Escape(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (ch is '(' or ')' or '\\') sb.Append('\\').Append(ch);
            else sb.Append(ch is >= ' ' and <= '~' ? ch : '?');
        }
        return sb.ToString();
    }

    // The path commands of the symbols: M H V L Q A Z, absolute. An arc is a circular arc, drawn as short lines.
    static string Path(string d)
    {
        var sb = new StringBuilder();
        double x = 0, y = 0, startX = 0, startY = 0;
        foreach (Match command in PathCommand().Matches(d))
        {
            var v = Numbers(command.Groups[2].Value);
            switch (command.Groups[1].Value[0])
            {
                case 'M':
                    (x, y) = (v[0], v[1]);
                    (startX, startY) = (x, y);
                    sb.Append(N(x)).Append(' ').Append(N(y)).Append(" m\n");
                    for (var i = 2; i + 1 < v.Count; i += 2) Line(sb, ref x, ref y, v[i], v[i + 1]);
                    break;
                case 'H': foreach (var n in v) Line(sb, ref x, ref y, n, y); break;
                case 'V': foreach (var n in v) Line(sb, ref x, ref y, x, n); break;
                case 'L': for (var i = 0; i + 1 < v.Count; i += 2) Line(sb, ref x, ref y, v[i], v[i + 1]); break;
                case 'Q':
                    for (var i = 0; i + 3 < v.Count; i += 4)
                    {
                        sb.Append(N(x + 2.0 / 3 * (v[i] - x))).Append(' ').Append(N(y + 2.0 / 3 * (v[i + 1] - y))).Append(' ')
                            .Append(N(v[i + 2] + 2.0 / 3 * (v[i] - v[i + 2]))).Append(' ').Append(N(v[i + 3] + 2.0 / 3 * (v[i + 1] - v[i + 3]))).Append(' ')
                            .Append(N(v[i + 2])).Append(' ').Append(N(v[i + 3])).Append(" c\n");
                        (x, y) = (v[i + 2], v[i + 3]);
                    }
                    break;
                case 'A':
                    for (var i = 0; i + 6 < v.Count; i += 7) Arc(sb, ref x, ref y, v[i], v[i + 3] != 0, v[i + 4] != 0, v[i + 5], v[i + 6]);
                    break;
                case 'Z':
                    sb.Append("h\n");
                    (x, y) = (startX, startY);
                    break;
            }
        }
        return sb.ToString();
    }

    static void Line(StringBuilder sb, ref double x, ref double y, double nx, double ny)
    {
        sb.Append(N(nx)).Append(' ').Append(N(ny)).Append(" l\n");
        (x, y) = (nx, ny);
    }

    // A circular arc of radius r from the current point to (nx, ny), with the SVG large-arc and sweep flags.
    static void Arc(StringBuilder sb, ref double x, ref double y, double r, bool large, bool sweep, double nx, double ny)
    {
        var (dx, dy) = (nx - x, ny - y);
        var chord = Math.Sqrt(dx * dx + dy * dy);
        if (chord == 0 || r <= 0)
        {
            Line(sb, ref x, ref y, nx, ny);
            return;
        }
        r = Math.Max(r, chord / 2);
        var h = Math.Sqrt(Math.Max(0, r * r - chord * chord / 4));
        var sign = large == sweep ? -1 : 1;
        var (cx, cy) = ((x + nx) / 2 + sign * h * dy / chord, (y + ny) / 2 - sign * h * dx / chord);
        var a0 = Math.Atan2(y - cy, x - cx);
        var delta = Math.Atan2(ny - cy, nx - cx) - a0;
        if (sweep && delta < 0) delta += 2 * Math.PI;
        if (!sweep && delta > 0) delta -= 2 * Math.PI;
        const int Steps = 12;
        for (var i = 1; i <= Steps; i++)
        {
            var a = a0 + delta * i / Steps;
            sb.Append(N(cx + r * Math.Cos(a))).Append(' ').Append(N(cy + r * Math.Sin(a))).Append(" l\n");
        }
        (x, y) = (nx, ny);
    }

    static string Circle(double cx, double cy, double r)
    {
        const double K = 0.5523;
        var k = K * r;
        return $"{N(cx + r)} {N(cy)} m\n" +
               $"{N(cx + r)} {N(cy + k)} {N(cx + k)} {N(cy + r)} {N(cx)} {N(cy + r)} c\n" +
               $"{N(cx - k)} {N(cy + r)} {N(cx - r)} {N(cy + k)} {N(cx - r)} {N(cy)} c\n" +
               $"{N(cx - r)} {N(cy - k)} {N(cx - k)} {N(cy - r)} {N(cx)} {N(cy - r)} c\n" +
               $"{N(cx + k)} {N(cy - r)} {N(cx + r)} {N(cy - k)} {N(cx + r)} {N(cy)} c\nh\n";
    }

    // The matrix of an SVG transform list: translate, rotate and scale, applied in order.
    static (double A, double B, double C, double D, double E, double F) Transform(string list)
    {
        var m = (A: 1.0, B: 0.0, C: 0.0, D: 1.0, E: 0.0, F: 0.0);
        foreach (Match t in TransformCall().Matches(list))
        {
            var v = Numbers(t.Groups[2].Value);
            var n = t.Groups[1].Value switch
            {
                "translate" => (1.0, 0.0, 0.0, 1.0, v[0], v.Count > 1 ? v[1] : 0),
                "scale" => (v[0], 0.0, 0.0, v.Count > 1 ? v[1] : v[0], 0.0, 0.0),
                "rotate" => (Math.Cos(v[0] * Math.PI / 180), Math.Sin(v[0] * Math.PI / 180), -Math.Sin(v[0] * Math.PI / 180), Math.Cos(v[0] * Math.PI / 180), 0.0, 0.0),
                _ => (1.0, 0.0, 0.0, 1.0, 0.0, 0.0),
            };
            m = (m.A * n.Item1 + m.C * n.Item2, m.B * n.Item1 + m.D * n.Item2, m.A * n.Item3 + m.C * n.Item4, m.B * n.Item3 + m.D * n.Item4,
                m.A * n.Item5 + m.C * n.Item6 + m.E, m.B * n.Item5 + m.D * n.Item6 + m.F);
        }
        return m;
    }

    static string Matrix((double A, double B, double C, double D, double E, double F) m) => Matrix(m.A, m.B, m.C, m.D, m.E, m.F);

    static string Matrix(double a, double b, double c, double d, double e, double f) =>
        string.Join(' ', new[] { a, b, c, d, e, f }.Select(N));

    static List<double> Numbers(string s) =>
        NumberToken().Matches(s).Select(x => double.Parse(x.Value, CultureInfo.InvariantCulture)).ToList();

    static double Num(XElement e, string name) =>
        e.Attribute(name) is { } a ? double.Parse(a.Value, CultureInfo.InvariantCulture) : 0;

    static string N(double v) => (Math.Round(v, 3) + 0.0).ToString("0.###", CultureInfo.InvariantCulture);

    static byte[] Document(string content, List<(double X, double Y, double W, double H, string Url)> links)
    {
        // Objects: 1 catalog, 2 pages, 3 page, 4 content, 5 and 6 fonts, then one annotation for each link.
        var annots = string.Join(' ', links.Select((_, i) => $"{7 + i} 0 R"));
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(PageW)} {N(PageH)}] /Contents 4 0 R /Resources << /Font << /F1 5 0 R /F2 6 0 R >> >> /Annots [{annots}] >>",
            $"<< /Length {content.Length} >>\nstream\n{content}endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>",
        };
        // NOTE: The page is flipped for drawing, so a rectangle in page space has its y from the bottom.
        objects.AddRange(links.Select(l =>
            $"<< /Type /Annot /Subtype /Link /Border [0 0 0] /Rect [{N(l.X)} {N(PageH - l.Y - l.H)} {N(l.X + l.W)} {N(PageH - l.Y)}] /A << /S /URI /URI ({Escape(l.Url)}) >> >>"));

        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }
        var xref = sb.Length;
        sb.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append(o.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        sb.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    [GeneratedRegex(@"([MHVLQAZ])([^MHVLQAZ]*)")]
    private static partial Regex PathCommand();

    [GeneratedRegex(@"(translate|rotate|scale)\(([^)]*)\)")]
    private static partial Regex TransformCall();

    [GeneratedRegex(@"-?\d*\.?\d+(?:[eE][-+]?\d+)?")]
    private static partial Regex NumberToken();
}
