using Ssp.Core.Netlist;

namespace Ssp.Core.Diagnostics;

public static class Diagnostics
{
    static readonly IRule[] Rules =
    [
        new NoGroundRule(),
        new NoInputRule(),
        new NoOutputRule(),
        new DanglingPinRule(),
        new FloatingNodeRule(),
        new VoltageLoopRule(),
    ];

    /// <summary>Checks the circuit with every rule. No simulation runs.</summary>
    public static IReadOnlyList<Diagnostic> Run(LoadedCircuit circuit) =>
        Rules.SelectMany(rule => rule.Check(circuit)).ToList();
}
