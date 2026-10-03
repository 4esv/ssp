using SpiceSharp;

namespace Ssp.Core.Netlist;

public sealed record LoadedCircuit(
    Circuit Circuit,
    IReadOnlyCollection<string> NodeNames,
    IReadOnlyList<Diagnostic> Diagnostics);
