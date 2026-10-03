using SpiceSharp.Validation;
using Ssp.Core.Netlist;
using Engine = SpiceSharp.Validation;

namespace Ssp.Core.Diagnostics;

/// <summary>Wraps the engine rule for nodes with no DC path to ground.</summary>
public sealed class FloatingNodeRule : IRule
{
    public IEnumerable<Diagnostic> Check(LoadedCircuit circuit)
    {
        // Without ground every node floats. NoGroundRule already reports that.
        if (!circuit.NodeNames.Contains(CircuitFacts.Ground))
        {
            yield break;
        }

        foreach (var violation in EngineRules.Collect<Engine.FloatingNodeRule>(circuit))
        {
            var v = (FloatingNodeRuleViolation)violation;
            yield return new Diagnostic(Severity.Error, $"Node {v.FloatingVariable.Name} has no DC path to ground.", null);
        }
    }
}
