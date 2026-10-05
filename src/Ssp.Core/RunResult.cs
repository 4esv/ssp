using Ssp.Core.Analysis;
using Ssp.Core.Netlist;

namespace Ssp.Core;

/// <summary>The result of one run. An analysis is null if the pipeline stopped before it.</summary>
/// <param name="OperatingPoint">The DC operating point.</param>
/// <param name="FrequencyResponse">The small-signal frequency response.</param>
/// <param name="Impedance">The input and output impedance.</param>
/// <param name="Noise">The output noise density. Null if the netlist has no <c>ssp:input</c> directive.</param>
/// <param name="Diagnostics">The diagnostics from the loader, the overrides, the rules and the solver.</param>
/// <param name="TimingsMs">The time of each section in milliseconds, keyed by section name, in run order.</param>
public sealed record RunResult(
    OpResult? OperatingPoint,
    AcResult? FrequencyResponse,
    ZResult? Impedance,
    NoiseResult? Noise,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyDictionary<string, double> TimingsMs)
{
    /// <summary>The version of docs/schema/run-result.schema.json that <see cref="ToJson"/> writes.</summary>
    public const string SchemaVersion = "1.1.0";

    /// <summary>The <c>$id</c> of the schema. It ends with <see cref="SchemaVersion"/>.</summary>
    public const string SchemaId = "urn:ssp:schema:run-result:" + SchemaVersion;

    /// <summary>Writes the result as JSON that validates against docs/schema/run-result.schema.json.</summary>
    public string ToJson() => RunResultJson.Write(this);
}
