using Ssp.Core.Analysis;
using Ssp.Core.Netlist;

namespace Ssp.Core.Diagnostics;

/// <summary>An input impedance below 100 kΩ at any frequency loads the source, such as a guitar pickup. Reads a <see cref="ZResult"/>; runs no simulation.</summary>
public sealed class LowInputImpedanceRule(ZResult impedance) : IRule
{
    /// <summary>The lowest input impedance without a diagnostic, in Ω.</summary>
    public const double Limit = 100_000.0;

    public IEnumerable<Diagnostic> Check(LoadedCircuit circuit)
    {
        if (impedance.Input.Count == 0)
        {
            yield break;
        }

        var i = Enumerable.Range(0, impedance.Input.Count).MinBy(k => impedance.Input[k].Magnitude);
        var z = impedance.Input[i].Magnitude;
        if (z < Limit)
        {
            yield return new Diagnostic(Severity.Warning, $"The input impedance is {z / 1_000.0:0.0} kΩ at {impedance.Frequencies[i]:0} Hz, below {Limit / 1_000.0:0} kΩ. The input loads the source. Increase the resistance at the input.", null);
        }
    }
}
