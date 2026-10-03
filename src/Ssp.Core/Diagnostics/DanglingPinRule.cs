using Ssp.Core.Netlist;

namespace Ssp.Core.Diagnostics;

/// <summary>A node that only one pin uses, other than ground, input and output, goes nowhere.</summary>
public sealed class DanglingPinRule : IRule
{
    public IEnumerable<Diagnostic> Check(LoadedCircuit circuit)
    {
        var exempt = new HashSet<string?> { CircuitFacts.Ground, circuit.Directives.Input, circuit.Directives.Output };
        var pins = CircuitFacts.Components(circuit)
            .SelectMany(c => c.Nodes.Select(n => (Node: n, Part: c.Name)))
            .GroupBy(p => p.Node, StringComparer.Ordinal);
        foreach (var node in pins)
        {
            if (node.Count() == 1 && !exempt.Contains(node.Key))
            {
                yield return new Diagnostic(Severity.Warning, $"{node.First().Part} has a pin on node {node.Key} that connects to nothing else.", null);
            }
        }
    }
}
