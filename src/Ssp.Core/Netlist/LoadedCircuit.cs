using SpiceSharp;

namespace Ssp.Core.Netlist;

public sealed record LoadedCircuit(
    Circuit Circuit,
    IReadOnlyCollection<string> NodeNames,
    IReadOnlyList<Diagnostic> Diagnostics,
    Directives Directives,
    IReadOnlyList<SubcircuitInstance> Subcircuits);

/// <summary>An X line of the netlist. The engine circuit has only the parts inside it, named <c>Name.Part</c>.</summary>
public sealed record SubcircuitInstance(string Name, string Model, IReadOnlyList<string> Pins);
