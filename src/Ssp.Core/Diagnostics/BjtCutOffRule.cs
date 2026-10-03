using Ssp.Core.Netlist;

namespace Ssp.Core.Diagnostics;

/// <summary>A BJT whose base-emitter junction passes almost no current at the operating point is cut off.</summary>
public sealed class BjtCutOffRule : IRule
{
    public IEnumerable<Diagnostic> Check(LoadedCircuit circuit)
    {
        var bjts = BiasFacts.Bjts(circuit);
        if (bjts.Count == 0 || BiasFacts.OperatingPoint(circuit) is not { } op)
        {
            yield break;
        }

        foreach (var q in bjts.Where(q => q.ForwardCurrent(op) < BiasFacts.CutOffCurrent))
        {
            yield return new Diagnostic(Severity.Warning, $"{q.Name} is cut off. Vbe is {q.Vbe(op):0.00} V, so almost no collector current flows. Check the base bias.", null);
        }
    }
}
