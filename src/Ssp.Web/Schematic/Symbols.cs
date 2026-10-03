namespace Ssp.Web.Schematic;

/// <summary>The symbol for each kind in <c>models/parts.toml</c>.</summary>
public static class Symbols
{
    /// <summary>Grid spacing in symbol units.</summary>
    public const int Grid = 10;

    const string Style = "fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"";

    static readonly Dictionary<string, Symbol> ByKind = new(StringComparer.Ordinal)
    {
        // Anode at left, cathode at right.
        ["diode"] = new Symbol(
            "diode",
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 -20 60 40\" {Style}>" +
            "<path d=\"M0 0H20M40 0H60M20 -10V10L40 0Z M40 -10V10\"/></svg>",
            [new SymbolPin("A", 0, 0), new SymbolPin("K", 60, 0)]),

        // Inverting input above the non-inverting input, output at right.
        ["opamp"] = new Symbol(
            "opamp",
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 -30 80 60\" {Style}>" +
            "<path d=\"M0 -10H20M0 10H20M60 0H80M20 -30V30L60 0Z\"/></svg>",
            [new SymbolPin("in-", 0, -10), new SymbolPin("in+", 0, 10), new SymbolPin("out", 80, 0)]),
    };

    /// <summary>Returns the symbol for a part kind. Throws <see cref="KeyNotFoundException"/> if the kind has none.</summary>
    public static Symbol For(string kind) =>
        ByKind.TryGetValue(kind, out var symbol)
            ? symbol
            : throw new KeyNotFoundException($"No symbol for kind \"{kind}\".");
}
