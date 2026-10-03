namespace Ssp.Web.Schematic;

/// <summary>A pin position in symbol units. Positions are multiples of <see cref="Symbols.Grid"/>.</summary>
public sealed record SymbolPin(string Name, int X, int Y);

/// <summary>An SVG drawing of a part kind and its pin positions.</summary>
public sealed record Symbol(string Kind, string Svg, IReadOnlyList<SymbolPin> Pins);
