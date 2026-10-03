using Ssp.Core.Netlist;

namespace Ssp.Core.Analysis;

/// <summary>Output noise density. The lists are empty if the analysis did not run.</summary>
/// <param name="Frequencies">Frequency of each point in hertz.</param>
/// <param name="Density">Output noise density at each frequency in V/√Hz. Same length as <paramref name="Frequencies"/>.</param>
/// <param name="Diagnostics">The reasons that the analysis did not run.</param>
public sealed record NoiseResult(
    IReadOnlyList<double> Frequencies,
    IReadOnlyList<double> Density,
    IReadOnlyList<Diagnostic> Diagnostics);
