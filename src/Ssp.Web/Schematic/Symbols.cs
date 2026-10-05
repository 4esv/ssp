using System.Globalization;

namespace Ssp.Web.Schematic;

/// <summary>
/// The schematic symbol for each part kind. The kinds are the kinds in <c>models/parts.toml</c> and the kinds that
/// <see cref="SchematicRenderer"/> gives to netlist components. A symbol with two pins has them at (0, 0) and (60, 0).
/// </summary>
public static class Symbols
{
    /// <summary>Grid spacing in symbol units.</summary>
    public const int Grid = 10;

    const string Style = "fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"";

    // US zigzag from x 15 to x 45.
    const string Zigzag = "M0 0H15L17.5 -6L22.5 6L27.5 -6L32.5 6L37.5 -6L42.5 6L45 0H60";

    // Triangle and bar. Anode at left, cathode at right.
    const string DiodeBody = "M0 0H20M40 0H60M20 -10V10L40 0Z M40 -10V10";

    // Bar at x 20, base at left, collector up, emitter down.
    const string BjtBody = "M0 0H20M20 -15V15M20 -7L40 -20V-30M20 7L40 20V30";

    // Channel at x 20, gate at left, drain up, source down.
    const string JfetBody = "M0 0H20M20 -15V15M20 -10H40V-30M20 10H40V30";

    // Triangle with the inverting input above the non-inverting input and the output at the apex.
    const string OpampBody = "M0 -10H20M0 10H20M60 0H80M20 -30V30L60 0Z M24 -10H30 M24 10H30M27 7V13";

    // Tip spring from the pin at (0, 0), the sleeve contact below it, and a ground symbol under the sleeve.
    const string JackBody = "M0 0H-22L-26 -5H-46M-26 10H-46M-36 10V18M-44 18H-28M-41 22H-31M-38 26H-34";

    static string JackLabel(string text) =>
        $"<text x=\"-36\" y=\"-12\" fill=\"currentColor\" stroke=\"none\" font-family=\"sans-serif\" font-size=\"10\" font-weight=\"700\" text-anchor=\"middle\">{text}</text>";

    static readonly SymbolPin[] TwoPin = [new("1", 0, 0), new("2", 60, 0)];

    static readonly Dictionary<string, Symbol> ByKind = new Symbol[]
    {
        Make("resistor", "0 -8 60 16", Zigzag, TwoPin),

        // Top, wiper, bottom. The wiper arrow points at the track.
        Make("pot", "0 -30 60 38", Zigzag + "M30 -30V-12", [new("1", 0, 0), new("W", 30, -30), new("2", 60, 0)],
            new SymbolArrow(30, -20, 30, -7)),

        Make("capacitor", "0 -12 60 24", "M0 0H27M33 0H60M27 -12V12M33 -12V12", TwoPin),

        // Positive plate and + mark at the first pin, curved negative plate at the second pin.
        Make("electrolytic", "0 -14 60 28", "M0 0H27M27 -12V12M37 -12Q31 0 37 12M33 0H60M17 -10H23M20 -13V-7",
            [new("+", 0, 0), new("-", 60, 0)]),

        Make("inductor", "0 -7 60 9", "M0 0H10A5 5 0 0 1 20 0A5 5 0 0 1 30 0A5 5 0 0 1 40 0A5 5 0 0 1 50 0H60", TwoPin),

        // The triangle is the arrow. The symbol does not draw a second head.
        Make("diode", "0 -12 60 24", DiodeBody, [new("A", 0, 0), new("K", 60, 0)], new SymbolArrow(20, 0, 40, 0), draw: false),

        // A diode with two light arrows.
        Make("led", "0 -26 60 38", DiodeBody + "M28 -13L36 -21M36 -13L44 -21", [new("A", 0, 0), new("K", 60, 0)],
            new SymbolArrow(20, 0, 40, 0), draw: false, extra: "<path fill=\"currentColor\" stroke=\"none\" d=\"M38 -23L33 -21.5L36.5 -18Z M46 -23L41 -21.5L44.5 -18Z\"/>"),

        // Collector, base, emitter, substrate: the SPICE node order. The substrate pin is hidden under the bar. The emitter arrow points out.
        Make("npn", "0 -30 40 60", BjtBody, Bjt, new SymbolArrow(26, 10.9, 36, 17.4)),

        // The emitter arrow points in.
        Make("pnp", "0 -30 40 60", BjtBody, Bjt, new SymbolArrow(36, 17.4, 26, 10.9)),

        // Drain, gate, source: the SPICE node order. The gate arrow points in for N-channel.
        Make("njf", "0 -30 40 60", JfetBody, Jfet, new SymbolArrow(4, 0, 19, 0)),

        // The gate arrow points out for P-channel.
        Make("pjf", "0 -30 40 60", JfetBody, Jfet, new SymbolArrow(19, 0, 4, 0)),

        Make("opamp", "0 -30 80 60", OpampBody, [new("in-", 0, -10), new("in+", 0, 10), new("out", 80, 0)]),

        // The pin order of the op-amp subcircuits in models/: inp inn out vcc vee.
        Make("opamp5", "0 -30 80 60", OpampBody + "M40 -15V-30M40 15V30",
            [new("in+", 0, 10), new("in-", 0, -10), new("out", 80, 0), new("V+", 40, -30), new("V-", 40, 30)]),

        // A cell: the long plate is the positive pin, first. The + mark sits at the long plate.
        Make("battery", "0 -14 60 28", "M0 0H27M33 0H60M27 -12V12M33 -6V6M16 -10H22M19 -13V-7", TwoPin),

        // An AC source: a circle with one sine period. Positive node first.
        Make("source", "0 -15 60 30", "M0 0H16M44 0H60M22 0Q26 -10 30 0T38 0<circle cx=\"30\" cy=\"0\" r=\"14\"/>", TwoPin),

        // A 1/4 inch mono jack, drawn to the left of its pin. The pin is the tip, a spring contact. The sleeve contact below
        // goes to ground. The label is beside the symbol.
        Make("jack-in", "-52 -26 58 56", JackBody, [new("T", 0, 0)], extra: JackLabel("IN")),
        Make("jack-out", "-52 -26 58 56", JackBody, [new("T", 0, 0)], extra: JackLabel("OUT")),

        // The arrow is the direction of the current through the source, from the first node to the second.
        Make("isource", "0 -15 60 30", "M0 0H16M44 0H60<circle cx=\"30\" cy=\"0\" r=\"14\"/>", TwoPin,
            new SymbolArrow(20, 0, 41, 0)),

        Make("ground", "-10 0 20 20", "M0 0V10M-10 10H10M-6 14H6M-2 18H2", [new("1", 0, 0)]),

        // A supply rail. The label is its voltage.
        Make("rail", "-10 -20 20 20", "M0 0V-20M-10 -20H10", [new("1", 0, 0)]),

        // Primary on the left, secondary on the right. Top pins are p1 and s1.
        Make("transformer", "0 -10 80 80", "M0 0H20M0 60H20M60 0H80M60 60H80M20 0V60M60 0V60M38 0V60M42 0V60",
            [new("p1", 0, 0), new("p2", 0, 60), new("s1", 80, 0), new("s2", 80, 60)]),

        // A switch: the common at left, the throws at right, throw 1 at the top. The lever is drawn by Lever at the position.
        Make("switch-1", "0 -18 60 22", "M0 0H15M45 0H60" + Contacts(1), [new("1", 0, 0), new("2", 60, 0)]),
        Make("switch-2", "0 -24 60 48", "M0 0H15M45 -20H60M45 20H60" + Contacts(2), [new("C", 0, 0), new("1", 60, -20), new("2", 60, 20)]),
        Make("switch-3", "0 -24 60 48", "M0 0H15M45 -20H60M45 0H60M45 20H60" + Contacts(3),
            [new("C", 0, 0), new("1", 60, -20), new("2", 60, 0), new("3", 60, 20)]),

        // LED above, resistor below. LED anode a at top left, cathode k at top right. LDR pins p1 left and p2 right at the bottom.
        Make("vactrol", "0 -30 60 100", DiodeBody + "M30 -14L40 -24M38 -14L48 -24 M0 60H15L20 50L30 70L40 50L45 60H60",
            [new("a", 0, 0), new("k", 60, 0), new("p1", 0, 60), new("p2", 60, 60)]),
    }.ToDictionary(s => s.Kind, StringComparer.Ordinal);

    // The contact circles of a switch: the common, then each throw.
    static string Contacts(int throws) =>
        string.Concat(new (int X, int Y)[] { (15, 0) }.Concat(Throws(throws)).Select(c => $"<circle cx=\"{c.X}\" cy=\"{c.Y}\" r=\"2.5\"/>"));

    static (int X, int Y)[] Throws(int throws) => throws switch
    {
        1 => [(45, 0)],
        2 => [(45, -20), (45, 20)],
        _ => [(45, -20), (45, 0), (45, 20)],
    };

    /// <summary>
    /// The path of the lever of a switch kind at a position, in symbol units. The lever goes from the common to the
    /// closed throw. At position 0 it points up, between the throws, and touches none.
    /// </summary>
    public static string Lever(string kind, int position)
    {
        var throws = Throws(kind[^1] - '0');
        var (x, y) = position >= 1 && position <= throws.Length ? throws[position - 1] : (42, -14);
        return $"M15 0L{x} {y}";
    }

    static SymbolPin[] Bjt => [new("C", 40, -30), new("B", 0, 0), new("E", 40, 30), new("S", 20, 30, Hidden: true)];

    static SymbolPin[] Jfet => [new("D", 40, -30), new("G", 0, 0), new("S", 40, 30)];

    /// <summary>Every kind that has a symbol.</summary>
    public static IReadOnlyCollection<string> Kinds => ByKind.Keys;

    /// <summary>Returns the symbol for a part kind. Throws <see cref="KeyNotFoundException"/> if the kind has none.</summary>
    public static Symbol For(string kind) =>
        ByKind.TryGetValue(kind, out var symbol)
            ? symbol
            : throw new KeyNotFoundException($"No symbol for kind \"{kind}\".");

    /// <summary>The palette icon of a kind: its symbol, and for a switch also its lever at the first position.</summary>
    public static string Icon(string kind) =>
        kind.StartsWith("switch-", StringComparison.Ordinal)
            ? For(kind).Svg.Replace("</svg>", $"<path d=\"{Lever(kind, Ssp.Core.Parts.Switch.Positions(kind[^1] - '0')[0])}\"/></svg>", StringComparison.Ordinal)
            : For(kind).Svg;

    /// <summary>The symbol for a part kind, or null if the kind has none.</summary>
    public static Symbol? Find(string? kind) => kind is not null && ByKind.TryGetValue(kind, out var symbol) ? symbol : null;

    // NOTE: A path in d ends at the first '<'. The rest of d is more SVG elements.
    static Symbol Make(string kind, string viewBox, string d, SymbolPin[] pins, SymbolArrow? arrow = null, string extra = "", bool draw = true)
    {
        var at = d.IndexOf('<', StringComparison.Ordinal);
        var (path, more) = at < 0 ? (d, "") : (d[..at], d[at..]);
        var head = arrow is null || !draw ? "" : Head(arrow);
        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{viewBox}\" {Style}><path d=\"{path}\"/>{more}{extra}{head}</svg>";
        return new Symbol(kind, svg, pins, arrow);
    }

    // A filled head with its tip at the arrow head, and the shaft from the tail.
    static string Head(SymbolArrow a)
    {
        const double Length = 7, HalfWidth = 3.5;
        var (dx, dy) = (a.HeadX - a.TailX, a.HeadY - a.TailY);
        var n = Math.Sqrt(dx * dx + dy * dy);
        (dx, dy) = (dx / n, dy / n);
        var (bx, by) = (a.HeadX - Length * dx, a.HeadY - Length * dy);
        return $"<path d=\"M{F(a.TailX)} {F(a.TailY)}L{F(bx)} {F(by)}\"/>" +
               $"<path fill=\"currentColor\" stroke=\"none\" d=\"M{F(a.HeadX)} {F(a.HeadY)}L{F(bx - HalfWidth * dy)} {F(by + HalfWidth * dx)}L{F(bx + HalfWidth * dy)} {F(by - HalfWidth * dx)}Z\"/>";
    }

    static string F(double v) => (Math.Round(v, 2) + 0.0).ToString("0.##", CultureInfo.InvariantCulture);
}
