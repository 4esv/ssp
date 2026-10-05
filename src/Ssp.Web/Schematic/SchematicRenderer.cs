using System.Globalization;
using System.Security;
using System.Text;
using Ssp.Core.Layout;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using SpiceSharp;
using SpiceSharp.Components;
using LayoutDoc = Ssp.Core.Layout.Layout;
using Point = Ssp.Core.Layout.Point;

namespace Ssp.Web.Schematic;

/// <summary>
/// One symbol of a schematic. A subcircuit instance is one element, a pot resistor pair is one element, and each other
/// component is one element. <paramref name="Members"/> are the netlist components the element draws.
/// A null <paramref name="Kind"/> draws as a box.
/// </summary>
public sealed record SchematicElement(string Reference, string? Kind, IReadOnlyList<string> Nodes, string Value, IReadOnlyList<string> Members);

/// <summary>Draws a circuit as an SVG schematic from its layout.</summary>
public static partial class SchematicRenderer
{
    const double Margin = 20;
    const double LabelGap = 4;
    const double FontSize = 10;
    const double SymbolLength = 20;

    static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    // NOTE: The renderer cannot see the .subckt pin names. These models have the pins inp inn out vcc vee, like models/opamp-*.cir.
    static readonly HashSet<string> OpampModels = new(Names)
    {
        "OPAMP", "TL071", "TL072", "TL074", "JRC4558", "NJM4558", "RC4558", "LM308", "LM358", "LM741", "UA741", "NE5532", "OPA2134",
    };

    static readonly string[] RailPrefixes = ["vcc", "vee", "vdd", "vss", "vbat", "v+", "v-"];

    /// <summary>
    /// An SVG document. Each placed element is its symbol at the layout position, with the layout rotation and flip.
    /// Its reference is above it and its value is below it, upright. A pin on ground has a ground symbol.
    /// An element with no symbol is a box. Each layout wire is a polyline. An element is placed by its reference, or
    /// else by the first placed member. An element that the layout does not place is not drawn. The output is the same
    /// for the same input.
    /// </summary>
    public static string ToSvg(LoadedCircuit circuit, LayoutDoc layout, PartMap parts)
    {
        var placements = layout.Parts.ToDictionary(p => p.Reference, Names);
        var bounds = new Bounds();
        var body = new StringBuilder();
        var labels = new StringBuilder();
        var elements = Elements(circuit, parts);
        var rails = elements.Where(e => e.Kind == "rail").ToDictionary(e => e.Nodes[0], e => e.Value, Names);
        var railPins = elements.Where(e => e.Kind == "rail").Select(e => placements.GetValueOrDefault(e.Reference)).OfType<PartPlacement>()
            .Select(p => (p.X, p.Y)).ToHashSet();

        foreach (var e in elements)
        {
            var p = placements.GetValueOrDefault(e.Reference)
                    ?? e.Members.Order(Names).Select(placements.GetValueOrDefault).FirstOrDefault(m => m is not null);
            if (p is null) continue;
            var symbol = Fits(Symbols.Find(e.Kind), e.Nodes.Count);
            var (inner, box) = symbol is null ? Fallback(e.Nodes.Count) : Drawing(symbol);

            body.Append("<g data-ref=\"").Append(Escape(e.Reference)).Append("\" transform=\"").Append(Transform(p)).Append("\">")
                .Append(inner).Append(Blades(e)).Append("</g>\n");

            var part = Outline(p, box);
            if (e.Kind != "rail")
            {
                var pins = LocalPins(symbol, e.Nodes.Count);
                for (var i = 0; i < e.Nodes.Count; i++)
                {
                    if (symbol?.Pins[i].Hidden == true) continue;
                    var (x, y) = Place(p, pins[i].X, pins[i].Y);
                    // NOTE: A ground net or a supply net has a symbol at each pin, not a wire. A rail element on the pin is that symbol.
                    if (IsGround(e.Nodes[i]))
                    {
                        PinSymbol(body, bounds, "ground", false, x, y);
                    }
                    else if (rails.TryGetValue(e.Nodes[i], out var volts) && !railPins.Contains((Math.Round(x, 2) + 0.0, Math.Round(y, 2) + 0.0)))
                    {
                        var down = volts.StartsWith('-');
                        PinSymbol(body, bounds, "rail", down, x, y);
                        Label(labels, bounds, x, down ? y + SymbolLength + LabelGap + FontSize : y - SymbolLength - LabelGap, volts);
                    }
                }
            }

            bounds.Add(part);
            foreach (var label in Labels(e, p)) Label(labels, bounds, label.X, label.Y, label.Text, label.Start, label.End);
        }

        foreach (var w in layout.Wires)
        {
            foreach (var pt in w.Points) bounds.Add((pt.X, pt.Y));
            body.Append("<polyline data-net=\"").Append(Escape(w.Net)).Append("\" points=\"")
                .Append(string.Join(' ', w.Points.Select(pt => $"{N(pt.X)},{N(pt.Y)}"))).Append("\"/>\n");
        }

        return Document(bounds, body, labels);
    }

    /// <summary>One symbol at a placement, as an SVG document. Shows the symbol of each kind in each orientation.</summary>
    public static string SymbolSvg(string kind, PartPlacement placement)
    {
        var (inner, box) = Drawing(Symbols.For(kind));
        var bounds = Outline(placement, box);
        var body = new StringBuilder()
            .Append("<g data-kind=\"").Append(Escape(kind)).Append("\" transform=\"").Append(Transform(placement)).Append("\">")
            .Append(inner).Append("</g>\n");
        return Document(bounds, body, new StringBuilder());
    }

    /// <summary>The schematic elements of a circuit, in reference order.</summary>
    public static IReadOnlyList<SchematicElement> Elements(LoadedCircuit circuit, PartMap? parts)
    {
        var components = circuit.Circuit.OfType<IComponent>().ToDictionary(c => c.Name, Names);
        var instances = circuit.Subcircuits.ToDictionary(x => x.Name, Names);
        var elements = new List<SchematicElement>();
        var done = new HashSet<string>(Names);

        foreach (var x in circuit.Subcircuits)
        {
            var members = components.Keys.Where(n => Names.Equals(n.Split('.')[0], x.Name)).ToList();
            done.UnionWith(members);
            elements.Add(new SchematicElement(x.Name, SubcircuitKind(x), x.Pins, x.Model, members));
        }

        // NOTE: A switch SW<n> is the resistors RSW<n>_<pole>A and RSW<n>_<pole>B for poles 1 to 3. A closed throw is a small resistor.
        foreach (var group in components.Values.OfType<Resistor>().Select(r => (r, m: SwitchPart().Match(r.Name))).Where(x => x.m.Success)
                     .GroupBy(x => x.m.Groups["ref"].Value, Names).OrderBy(g => g.Key, Names))
        {
            if (instances.ContainsKey(group.Key)) continue;
            var poles = new List<(Resistor A, Resistor B)>();
            for (var k = 1; k <= 3; k++)
            {
                var a = group.FirstOrDefault(x => x.m.Groups["pole"].Value == $"{k}" && x.m.Groups["side"].Value.Equals("A", StringComparison.OrdinalIgnoreCase)).r;
                var b = group.FirstOrDefault(x => x.m.Groups["pole"].Value == $"{k}" && x.m.Groups["side"].Value.Equals("B", StringComparison.OrdinalIgnoreCase)).r;
                if (a is null || b is null || !Names.Equals(a.Nodes[0], b.Nodes[0])) break;
                poles.Add((a, b));
            }
            if (poles.Count == 0 || poles.Count != group.Count() / 2 || group.Count() % 2 != 0) continue;
            var members = poles.SelectMany(p => new[] { p.A.Name, p.B.Name }).ToList();
            done.UnionWith(members);
            var throwA = poles[0].A.Parameters.Resistance.Value <= poles[0].B.Parameters.Resistance.Value;
            elements.Add(new SchematicElement(group.Key, $"switch{poles.Count}",
                poles.SelectMany(p => new[] { p.A.Nodes[0], p.A.Nodes[1], p.B.Nodes[1] }).ToList(), throwA ? "A" : "B", members));
        }

        // NOTE: Pot.cs names the two halves of pot P as P_1 (top to wiper) and P_2 (wiper to bottom).
        foreach (var upper in components.Values.OfType<Resistor>().Where(r => r.Name.EndsWith("_1", StringComparison.Ordinal)))
        {
            var name = upper.Name[..^2];
            if (done.Contains(upper.Name) || instances.ContainsKey(name)) continue;
            if (components.GetValueOrDefault(name + "_2") is not Resistor lower || !Names.Equals(upper.Nodes[1], lower.Nodes[0])) continue;
            done.Add(upper.Name);
            done.Add(lower.Name);
            var total = upper.Parameters.Resistance.Value + lower.Parameters.Resistance.Value;
            elements.Add(new SchematicElement(name, "pot", [upper.Nodes[0], upper.Nodes[1], lower.Nodes[1]], Plain(total), [upper.Name, lower.Name]));
        }

        foreach (var c in components.Values)
        {
            if (done.Contains(c.Name)) continue;
            var row = parts?.Parts.GetValueOrDefault(c.Name);
            if (Rail(c) is { } rail)
            {
                elements.Add(new SchematicElement(c.Name, "rail", [rail.Node], rail.Volts, [c.Name]));
                continue;
            }
            elements.Add(new SchematicElement(c.Name, Kind(c, row, circuit.Circuit), c.Nodes.ToList(), Value(c, row), [c.Name]));
        }

        return elements.OrderBy(e => e.Reference, Names).ThenBy(e => e.Reference, StringComparer.Ordinal).ToList();
    }

    /// <summary>A value in plain units with an SI prefix: 100k, 10n, 1u, 4.7k. Three significant digits at most.</summary>
    public static string Plain(double value)
    {
        if (value == 0 || !double.IsFinite(value)) return value.ToString(CultureInfo.InvariantCulture);
        const string Prefixes = "pnum kMGT";
        var a = Math.Abs(value);
        var e = Math.Clamp((int)Math.Floor(Math.Log10(a) / 3) * 3, -12, 12);
        var m = a / Math.Pow(10, e);
        m = Math.Round(m, Math.Max(0, 2 - (int)Math.Floor(Math.Log10(m))));
        if (m >= 1000 && e < 12)
        {
            m /= 1000;
            e += 3;
        }
        var prefix = Prefixes[(e + 12) / 3];
        return (value < 0 ? "-" : "") + m.ToString("0.##", CultureInfo.InvariantCulture) + (prefix == ' ' ? "" : prefix.ToString());
    }

    /// <summary>
    /// The schematic position of each pin of a placed component, in node order. A component with a symbol uses the
    /// symbol pins. Other components use the pins of the fallback box.
    /// </summary>
    public static IReadOnlyList<Point> Pins(IComponent component, PartPlacement placement, PartRow? row)
    {
        var count = component.Nodes.Count;
        return LocalPins(Fits(Symbols.Find(Kind(component, row, null)), count), count)
            .Select(p => Place(placement, p.X, p.Y)).Select(p => new Point(Math.Round(p.X, 2) + 0.0, Math.Round(p.Y, 2) + 0.0)).ToList();
    }

    /// <summary>A pin of a placed element: its net, its schematic position, and whether it is hidden (no lead, no ground symbol).</summary>
    public readonly record struct ElementPin(string Net, Point At, bool Hidden);

    /// <summary>The pins of a placed element, in node order. They are the symbol pins, or the pins of the fallback box.</summary>
    public static IReadOnlyList<ElementPin> Pins(SchematicElement element, PartPlacement placement)
    {
        var symbol = Fits(Symbols.Find(element.Kind), element.Nodes.Count);
        var local = LocalPins(symbol, element.Nodes.Count);
        return local.Select((p, i) =>
        {
            var (x, y) = Place(placement, p.X, p.Y);
            return new ElementPin(element.Nodes[i], new Point(Math.Round(x, 2) + 0.0, Math.Round(y, 2) + 0.0), symbol?.Pins[i].Hidden == true);
        }).ToList();
    }

    /// <summary>A label of a placed element: text at a point, anchored in the middle, at the start or at the end.</summary>
    public readonly record struct ElementLabel(string Text, double X, double Y, bool Start = false, bool End = false)
    {
        /// <summary>The rectangle of the text: left, top, right, bottom.</summary>
        public (double X0, double Y0, double X1, double Y1) Extent
        {
            get
            {
                var width = Text.Length * FontSize * 0.6;
                var x0 = Start ? X : End ? X - width : X - width / 2;
                return (x0, Y - FontSize, x0 + width, Y);
            }
        }
    }

    /// <summary>
    /// The labels of a placed element. A rail has its voltage at the end of the stem. A vertical part has its reference and
    /// value to its right. Other parts have the reference above and the value below, beside a pin at the top or bottom centre.
    /// </summary>
    public static IReadOnlyList<ElementLabel> Labels(SchematicElement e, PartPlacement p)
    {
        var (min, max) = Outline(e, p);
        if (e.Kind == "rail") return [new(e.Value, (min.X + max.X) / 2, p.Flip ? max.Y + LabelGap + FontSize : min.Y - LabelGap)];

        var labels = new List<ElementLabel>();
        if (Norm(p.Rotation) is 90 or 270)
        {
            var middle = (min.Y + max.Y) / 2;
            labels.Add(new(e.Reference, max.X + LabelGap, middle - LabelGap / 2, Start: true));
            if (e.Value.Length > 0) labels.Add(new(e.Value, max.X + LabelGap, middle + FontSize, Start: true));
            return labels;
        }

        // NOTE: A pin at the top or bottom centre has a lead and a symbol. The label goes beside them.
        var pins = Pins(e, p).Where(x => !x.Hidden).Select(x => x.At).ToList();
        var center = (min.X + max.X) / 2;
        double Beside(double edge) => pins.Where(x => Math.Abs(x.Y - edge) < 0.5 && Math.Abs(x.X - center) < 15).Select(x => x.X - LabelGap).DefaultIfEmpty(double.NaN).First();
        var (top, bottom) = (Beside(min.Y), Beside(max.Y));
        labels.Add(new(e.Reference, double.IsNaN(top) ? center : top, min.Y - LabelGap, End: !double.IsNaN(top)));
        if (e.Value.Length > 0) labels.Add(new(e.Value, double.IsNaN(bottom) ? center : bottom, max.Y + LabelGap + FontSize, End: !double.IsNaN(bottom)));
        return labels;
    }

    /// <summary>The top-left and bottom-right corners of the drawing of a placed element, in schematic units.</summary>
    public static (Point Min, Point Max) Outline(SchematicElement element, PartPlacement placement)
    {
        var symbol = Fits(Symbols.Find(element.Kind), element.Nodes.Count);
        var outline = Outline(placement, symbol is null ? Fallback(element.Nodes.Count).Box : Drawing(symbol).Box);
        return (new Point(outline.MinX, outline.MinY), new Point(outline.MaxX, outline.MaxY));
    }

    /// <summary>The schematic position of each pin of a placed pot: top, wiper, bottom.</summary>
    public static IReadOnlyList<Point> PotPins(PartPlacement placement) =>
        Symbols.For("pot").Pins.Select(p => Place(placement, p.X, p.Y)).Select(p => new Point(Math.Round(p.X, 2) + 0.0, Math.Round(p.Y, 2) + 0.0)).ToList();

    /// <summary>The top-left and bottom-right corners of the drawing of a placed pot, in schematic units.</summary>
    public static (Point Min, Point Max) PotOutline(PartPlacement placement)
    {
        var outline = Outline(placement, Drawing(Symbols.For("pot")).Box);
        return (new Point(outline.MinX, outline.MinY), new Point(outline.MaxX, outline.MaxY));
    }

    /// <summary>The top-left and bottom-right corners of the drawing of a placed component, in schematic units.</summary>
    public static (Point Min, Point Max) Outline(IComponent component, PartPlacement placement, PartRow? row)
    {
        var symbol = Fits(Symbols.Find(Kind(component, row, null)), component.Nodes.Count);
        var outline = Outline(placement, symbol is null ? Fallback(component.Nodes.Count).Box : Drawing(symbol).Box);
        return (new Point(outline.MinX, outline.MinY), new Point(outline.MaxX, outline.MaxY));
    }

    static Bounds Outline(PartPlacement p, Box box)
    {
        var part = new Bounds();
        foreach (var (x, y) in new[] { (box.X, box.Y), (box.X + box.W, box.Y), (box.X, box.Y + box.H), (box.X + box.W, box.Y + box.H) })
        {
            part.Add(Place(p, x, y));
        }
        return part;
    }

    readonly record struct Box(double X, double Y, double W, double H);

    static Symbol? Fits(Symbol? symbol, int pins) => symbol is not null && symbol.Pins.Count == pins ? symbol : null;

    // The pins of a symbol, or of the fallback box when there is no symbol.
    static IReadOnlyList<(double X, double Y)> LocalPins(Symbol? symbol, int count) =>
        symbol is not null
            ? symbol.Pins.Select(p => ((double)p.X, (double)p.Y)).ToList()
            : Enumerable.Range(0, count).Select(i => (i % 2 == 0 ? 0.0 : 60.0, 2.0 * Symbols.Grid * (i / 2))).ToList();

    // The kind of a component. The part row kind comes first. Without the circuit, a BJT is npn and a JFET is njf.
    static string? Kind(IComponent c, PartRow? row, Circuit? circuit)
    {
        if (row is not null && Fits(Symbols.Find(row.Kind), c.Nodes.Count) is not null) return row.Kind;
        return c switch
        {
            Resistor => "resistor",
            // NOTE: A netlist has no polarity flag. Pedal circuits use electrolytics from 1u up.
            Capacitor cap => cap.Parameters.Capacitance.Value >= 1e-6 ? "electrolytic" : "capacitor",
            Inductor => "inductor",
            Diode => "diode",
            BipolarJunctionTransistor q =>
                circuit is not null && q.Model is not null && circuit.TryGetEntity(q.Model, out var m) && m is BipolarJunctionTransistorModel bm && bm.Parameters.BipolarType < 0 ? "pnp" : "npn",
            JFET j =>
                circuit is not null && j.Model is not null && circuit.TryGetEntity(j.Model, out var m) && m is JFETModel jm && jm.Parameters.JFETType < 0 ? "pjf" : "njf",
            VoltageSource => "vsource",
            CurrentSource => "isource",
            _ => null,
        };
    }

    /// <summary>The number of poles of a switch kind, or 0 for another kind.</summary>
    public static int Poles(string? kind) => kind is "switch1" ? 1 : kind is "switch2" ? 2 : kind is "switch3" ? 3 : 0;

    // The blade of each pole of a switch, from the common pin to the closed throw, and a dashed link between the blades.
    static string Blades(SchematicElement e)
    {
        var poles = Poles(e.Kind);
        if (poles == 0) return "";
        var up = e.Value == "A" ? -1 : 1;
        var d = new StringBuilder();
        for (var k = 0; k < poles; k++) d.Append(CultureInfo.InvariantCulture, $"M20 {k * Symbols.PoleStep}L40 {k * Symbols.PoleStep + up * 10}");
        var link = poles > 1 ? $"<path stroke-dasharray=\"3 3\" d=\"M30 {N(up * 5)}V{N((poles - 1) * Symbols.PoleStep + up * 5)}\"/>" : "";
        return $"<path class=\"blade\" d=\"{d}\"/>{link}";
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^R(?<ref>SW\d+)_(?<pole>[1-3])(?<side>[AB])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex SwitchPart();

    /// <summary>The switch that a netlist component is part of, or null.</summary>
    public static string? SwitchOf(string name) => SwitchPart().Match(name) is { Success: true } m ? m.Groups["ref"].Value : null;

    static string? SubcircuitKind(SubcircuitInstance x)
    {
        if (x.Pins.Count == 5 && OpampModels.Contains(x.Model)) return "opamp5";
        return Fits(Symbols.Find(x.Model.ToLowerInvariant()), x.Pins.Count)?.Kind;
    }

    static string Value(IComponent c, PartRow? row) => c switch
    {
        Resistor r => Plain(r.Parameters.Resistance.Value),
        Capacitor cap => Plain(cap.Parameters.Capacitance.Value),
        Inductor l => Plain(l.Parameters.Inductance),
        VoltageSource v => Source(v.Parameters, "V"),
        CurrentSource i => Source(i.Parameters, "A"),
        _ => row?.Id ?? (c is Component k ? k.Model ?? "" : ""),
    };

    static string Source(SpiceSharp.Components.CommonBehaviors.IndependentSourceParameters p, string unit)
    {
        var parts = new List<string>();
        if (p.Waveform is not null) parts.Add(p.Waveform.GetType().Name.ToLowerInvariant());
        else if (p.DcValue.Value != 0 || p.AcMagnitude == 0) parts.Add(Plain(p.DcValue.Value) + unit);
        if (p.AcMagnitude != 0) parts.Add("AC " + Plain(p.AcMagnitude) + unit);
        return string.Join(' ', parts);
    }

    // A DC voltage source from ground to a supply node, such as VCC vcc 0 9. The value is the supply voltage, with its sign.
    static (string Node, string Volts)? Rail(IComponent c)
    {
        if (c is not VoltageSource v || v.Parameters.Waveform is not null || v.Parameters.AcMagnitude != 0) return null;
        var (plus, minus) = (c.Nodes[0], c.Nodes[1]);
        if (IsGround(plus) == IsGround(minus)) return null;
        var node = IsGround(minus) ? plus : minus;
        if (!RailPrefixes.Any(r => node.StartsWith(r, StringComparison.OrdinalIgnoreCase) || c.Name.StartsWith(r, StringComparison.OrdinalIgnoreCase))) return null;
        var volts = IsGround(minus) ? v.Parameters.DcValue.Value : -v.Parameters.DcValue.Value;
        return (node, (volts > 0 ? "+" : "") + Plain(volts) + "V");
    }

    static bool IsGround(string node) => node == "0" || Names.Equals(node, "gnd");

    static string Transform(PartPlacement p)
    {
        var transform = $"translate({N(p.X)} {N(p.Y)})";
        if (Norm(p.Rotation) != 0) transform += $" rotate({N(-Norm(p.Rotation))})";
        if (p.Flip) transform += " scale(1 -1)";
        return transform;
    }

    static void Label(StringBuilder labels, Bounds bounds, double x, double y, string text, bool start = false, bool end = false)
    {
        bounds.Add((x - (end ? text.Length * FontSize * 0.6 : 0), y - FontSize));
        bounds.Add((x + (start ? text.Length * FontSize * 0.6 : 0), y));
        labels.Append("<text x=\"").Append(N(x)).Append("\" y=\"").Append(N(y)).Append('"').Append(start ? " text-anchor=\"start\"" : end ? " text-anchor=\"end\"" : "").Append('>').Append(Escape(text)).Append("</text>\n");
    }

    // A ground symbol hangs below a pin. A rail symbol stands above it, or hangs below it when down.
    static void PinSymbol(StringBuilder body, Bounds bounds, string kind, bool down, double x, double y)
    {
        var (inner, box) = Drawing(Symbols.For(kind));
        body.Append("<g transform=\"translate(").Append(N(x)).Append(' ').Append(N(y)).Append(')').Append(down ? " scale(1 -1)" : "").Append("\">").Append(inner).Append("</g>\n");
        var (y0, y1) = (down ? y - box.Y - box.H : y + box.Y, down ? y - box.Y : y + box.Y + box.H);
        bounds.Add((x + box.X, y0));
        bounds.Add((x + box.X + box.W, y1));
    }

    static string Document(Bounds bounds, StringBuilder body, StringBuilder labels)
    {
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

    // The symbol content without its outer svg element, and its view box.
    static (string Inner, Box Box) Drawing(Symbol symbol)
    {
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

    /// <summary>
    /// The schematic position of a point of a symbol. Same order as the transform attribute: flip about the x axis,
    /// rotate counter-clockwise on screen, then translate.
    /// </summary>
    public static (double X, double Y) Place(PartPlacement p, double x, double y)
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
