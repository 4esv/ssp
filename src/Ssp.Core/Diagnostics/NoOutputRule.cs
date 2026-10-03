using Ssp.Core.Netlist;

namespace Ssp.Core.Diagnostics;

public sealed class NoOutputRule : IRule
{
    public IEnumerable<Diagnostic> Check(LoadedCircuit circuit)
    {
        if (circuit.NodeNames.Count == 0)
        {
            yield break;
        }

        var output = CircuitFacts.OutputNode(circuit);
        if (output is null)
        {
            yield return new Diagnostic(Severity.Error, "The circuit has no output. Add a node other than ground and the input, or an ssp:output directive.", null);
        }
        else if (!circuit.NodeNames.Contains(output))
        {
            yield return new Diagnostic(Severity.Error, $"The output node {output} is not in the circuit.", null);
        }
    }
}
