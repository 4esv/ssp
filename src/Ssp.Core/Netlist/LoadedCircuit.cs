using SpiceSharp;

namespace Ssp.Core.Netlist;

/// <summary>
/// A loaded netlist. <paramref name="SubcircuitPins"/> is the declared pin names of each <c>.subckt</c> definition,
/// keyed by the definition name, in definition order. <paramref name="Subcircuits"/> holds the X lines instead.
/// </summary>
public sealed record LoadedCircuit(
    Circuit Circuit,
    IReadOnlyCollection<string> NodeNames,
    IReadOnlyList<Diagnostic> Diagnostics,
    Directives Directives,
    IReadOnlyList<SubcircuitInstance> Subcircuits,
    IReadOnlyDictionary<string, IReadOnlyList<string>> SubcircuitPins);

/// <summary>An X line of the netlist. The engine circuit has only the parts inside it, named <c>Name.Part</c>.</summary>
public sealed record SubcircuitInstance(string Name, string Model, IReadOnlyList<string> Pins);
