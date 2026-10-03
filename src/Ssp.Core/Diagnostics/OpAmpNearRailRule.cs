using Ssp.Core.Netlist;

namespace Ssp.Core.Diagnostics;

/// <summary>An op-amp output near a supply rail at the operating point has no room to swing, and the signal clips.</summary>
public sealed class OpAmpNearRailRule : IRule
{
    // An output nearer to a rail than this, in V, is near the rail. The models stop 1 V to 1.5 V short of each rail.
    const double Margin = 2.0;

    public IEnumerable<Diagnostic> Check(LoadedCircuit circuit)
    {
        var opAmps = BiasFacts.OpAmps(circuit);
        if (opAmps.Count == 0 || BiasFacts.OperatingPoint(circuit) is not { } op)
        {
            yield break;
        }

        foreach (var x in opAmps)
        {
            if (!op.NodeVoltages.TryGetValue(x.Pins[2], out var output))
            {
                continue;
            }

            foreach (var rail in new[] { x.Pins[3], x.Pins[4] })
            {
                if (!op.NodeVoltages.TryGetValue(rail, out var railVoltage))
                {
                    continue;
                }

                var distance = Math.Abs(railVoltage - output);
                if (distance < Margin)
                {
                    yield return new Diagnostic(Severity.Warning, $"{x.Name} output is {output:0.00} V, {distance:0.00} V from the rail on node {rail}. The signal clips. Bias the inputs between the rails.", null);
                }
            }
        }
    }
}
