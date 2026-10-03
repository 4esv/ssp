using SpiceSharp.Components;
using SpiceSharp.Validation;
using Ssp.Core.Netlist;
using Engine = SpiceSharp.Validation;

namespace Ssp.Core.Diagnostics;

/// <summary>Wraps the engine rule for loops made only of voltage sources and inductors.</summary>
public sealed class VoltageLoopRule : IRule
{
    public IEnumerable<Diagnostic> Check(LoadedCircuit circuit)
    {
        foreach (var violation in EngineRules.Collect<Engine.VoltageLoopRule>(circuit))
        {
            var part = (violation.Subject as IComponent)?.Name ?? "a source";
            yield return new Diagnostic(Severity.Error, $"{part} forms a loop of voltage sources. Add a resistor in the loop.", null);
        }
    }
}
