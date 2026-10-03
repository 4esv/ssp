using Ssp.Core.Netlist;

namespace Ssp.Core.Diagnostics;

public sealed class NoInputRule : IRule
{
    public IEnumerable<Diagnostic> Check(LoadedCircuit circuit)
    {
        if (circuit.NodeNames.Count == 0)
        {
            yield break;
        }

        var input = CircuitFacts.InputNode(circuit);
        if (input is null)
        {
            yield return new Diagnostic(Severity.Error, "The circuit has no input. Add a voltage source or an ssp:input directive.", null);
        }
        else if (!circuit.NodeNames.Contains(input))
        {
            yield return new Diagnostic(Severity.Error, $"The input node {input} is not in the circuit.", null);
        }
    }
}
