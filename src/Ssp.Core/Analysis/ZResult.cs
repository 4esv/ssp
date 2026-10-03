using System.Numerics;
using Ssp.Core.Netlist;

namespace Ssp.Core.Analysis;

/// <summary>Input and output impedance. Values are in ohms. The lists are empty if the analysis did not run.</summary>
/// <param name="Frequencies">Frequency of each point in hertz.</param>
/// <param name="Input">Input impedance at each frequency. Same length as <paramref name="Frequencies"/>.</param>
/// <param name="Output">Output impedance at each frequency, with the input source set to zero. Same length as <paramref name="Frequencies"/>.</param>
/// <param name="Diagnostics">The reasons that the analysis did not run.</param>
public sealed record ZResult(
    IReadOnlyList<double> Frequencies,
    IReadOnlyList<Complex> Input,
    IReadOnlyList<Complex> Output,
    IReadOnlyList<Diagnostic> Diagnostics);
