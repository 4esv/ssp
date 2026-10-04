namespace Ssp.Web.Schematic;

/// <summary>A pin position in symbol units. Positions are multiples of <see cref="Symbols.Grid"/>.</summary>
public sealed record SymbolPin(string Name, int X, int Y);

/// <summary>The direction arrow of a symbol, from its tail to its head, in symbol units. The symbol draws its head.</summary>
public sealed record SymbolArrow(double TailX, double TailY, double HeadX, double HeadY);

/// <summary>An SVG drawing of a part kind and its pin positions. Pins are in node order.</summary>
public sealed record Symbol(string Kind, string Svg, IReadOnlyList<SymbolPin> Pins, SymbolArrow? Arrow = null);
