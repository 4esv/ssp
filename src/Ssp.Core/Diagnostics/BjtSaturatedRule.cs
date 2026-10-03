using Ssp.Core.Netlist;

namespace Ssp.Core.Diagnostics;

/// <summary>A BJT that conducts with a very small collector-emitter voltage is saturated.</summary>
public sealed class BjtSaturatedRule : IRule
{
    // A BJT that conducts with a smaller Vce, in V, is saturated.
    const double SaturatedVce = 0.3;

    public IEnumerable<Diagnostic> Check(LoadedCircuit circuit)
    {
        var bjts = BiasFacts.Bjts(circuit);
        if (bjts.Count == 0 || BiasFacts.OperatingPoint(circuit) is not { } op)
        {
            yield break;
        }

        foreach (var q in bjts.Where(q => q.ForwardCurrent(op) >= BiasFacts.CutOffCurrent && q.Vce(op) < SaturatedVce))
        {
            yield return new Diagnostic(Severity.Warning, $"{q.Name} is saturated. Vce is {q.Vce(op):0.00} V. Increase the base resistor or decrease the collector resistor.", null);
        }
    }
}
