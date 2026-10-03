using System.Numerics;

namespace Ssp.Core.Analysis;

/// <summary>Input and output impedance. Values are in ohms.</summary>
/// <param name="Frequencies">Frequency of each point in hertz.</param>
/// <param name="Input">Input impedance at each frequency. Same length as <paramref name="Frequencies"/>.</param>
/// <param name="Output">Output impedance at each frequency, with the input source set to zero. Same length as <paramref name="Frequencies"/>.</param>
public sealed record ZResult(
    IReadOnlyList<double> Frequencies,
    IReadOnlyList<Complex> Input,
    IReadOnlyList<Complex> Output);
