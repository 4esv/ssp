using Ssp.Core.Analysis;
using Ssp.Core.Netlist;

namespace Ssp.Core;

/// <summary>The result of one run. An analysis is null if the pipeline stopped before it.</summary>
/// <param name="OperatingPoint">The DC operating point.</param>
/// <param name="FrequencyResponse">The small-signal frequency response.</param>
/// <param name="Impedance">The input and output impedance.</param>
/// <param name="Diagnostics">The diagnostics from the loader, the overrides, the rules and the solver.</param>
/// <param name="TimingsMs">The time of each section in milliseconds, keyed by section name, in run order.</param>
public sealed record RunResult(
    OpResult? OperatingPoint,
    AcResult? FrequencyResponse,
    ZResult? Impedance,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyDictionary<string, double> TimingsMs);
