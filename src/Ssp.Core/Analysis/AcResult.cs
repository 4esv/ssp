namespace Ssp.Core.Analysis;

/// <summary>
/// Small-signal frequency response. Magnitude is in dB relative to 1 V of AC drive. Phase is in degrees.
/// </summary>
/// <param name="Frequencies">Frequency of each point in hertz.</param>
/// <param name="MagnitudeDb">Magnitude series of each node, keyed by node name. Same length as <paramref name="Frequencies"/>.</param>
/// <param name="PhaseDegrees">Phase series of each node, keyed by node name. Same length as <paramref name="Frequencies"/>.</param>
public sealed record AcResult(
    IReadOnlyList<double> Frequencies,
    IReadOnlyDictionary<string, IReadOnlyList<double>> MagnitudeDb,
    IReadOnlyDictionary<string, IReadOnlyList<double>> PhaseDegrees);
