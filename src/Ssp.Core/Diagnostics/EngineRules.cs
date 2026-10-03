using SpiceSharp.Simulations.Biasing;
using SpiceSharp.Simulations.Variables;
using SpiceSharp.Validation;
using Ssp.Core.Netlist;

namespace Ssp.Core.Diagnostics;

static class EngineRules
{
    /// <summary>Applies every part to the engine rules, as the engine does before a solve, and returns the violations of one rule.</summary>
    public static IEnumerable<IRuleViolation> Collect<TRule>(LoadedCircuit circuit) where TRule : SpiceSharp.Validation.IRule
    {
        var rules = new Rules(new VariableFactory(), StringComparer.Ordinal);
        foreach (var entity in circuit.Circuit)
        {
            (entity as IRuleSubject)?.Apply(rules);
        }

        return rules.GetRules<TRule>().SelectMany(r => r.Violations);
    }
}
