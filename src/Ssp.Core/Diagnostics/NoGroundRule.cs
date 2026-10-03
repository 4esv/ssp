using Ssp.Core.Netlist;

namespace Ssp.Core.Diagnostics;

public sealed class NoGroundRule : IRule
{
    public IEnumerable<Diagnostic> Check(LoadedCircuit circuit)
    {
        if (circuit.NodeNames.Count > 0 && !circuit.NodeNames.Contains(CircuitFacts.Ground))
        {
            yield return new Diagnostic(Severity.Error, "No part is connected to ground (node 0). Connect one node to ground.", null);
        }
    }
}
