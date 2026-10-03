using Ssp.Core.Analysis;
using Ssp.Core.Netlist;

namespace Ssp.Core.Diagnostics;

/// <summary>An output impedance above 10 kΩ at any frequency loses level and treble into the next input or the cable. Reads a <see cref="ZResult"/>; runs no simulation.</summary>
public sealed class HighOutputImpedanceRule(ZResult impedance) : IRule
{
    /// <summary>The highest output impedance without a diagnostic, in Ω.</summary>
    public const double Limit = 10_000.0;

    public IEnumerable<Diagnostic> Check(LoadedCircuit circuit)
    {
        if (impedance.Output.Count == 0)
        {
            yield break;
        }

        var i = Enumerable.Range(0, impedance.Output.Count).MaxBy(k => impedance.Output[k].Magnitude);
        var z = impedance.Output[i].Magnitude;
        if (z > Limit)
        {
            yield return new Diagnostic(Severity.Warning, $"The output impedance is {z / 1_000.0:0.0} kΩ at {impedance.Frequencies[i]:0} Hz, above {Limit / 1_000.0:0} kΩ. The next input or the cable loads the output. Add a buffer at the output.", null);
        }
    }
}
