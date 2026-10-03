using SpiceSharp.Components;
using SpiceSharp.Simulations;
using Ssp.Core.Netlist;

namespace Ssp.Core.Analysis;

public static class Analyses
{
    /// <summary>Computes the DC operating point of a loaded circuit.</summary>
    public static OpResult OperatingPoint(LoadedCircuit circuit)
    {
        var op = new OP("op");
        var nodeExports = circuit.NodeNames.ToDictionary(n => n, n => new RealVoltageExport(op, n), StringComparer.Ordinal);
        var sourceExports = new Dictionary<string, RealPropertyExport>(StringComparer.Ordinal);
        var powerExports = new Dictionary<string, RealPropertyExport>(StringComparer.Ordinal);
        foreach (var entity in circuit.Circuit)
        {
            if (entity is not IComponent)
            {
                continue;
            }

            powerExports[entity.Name] = new RealPropertyExport(op, entity.Name, "p");
            if (entity is VoltageSource)
            {
                sourceExports[entity.Name] = new RealPropertyExport(op, entity.Name, "i");
            }
        }

        var voltages = new Dictionary<string, double>(StringComparer.Ordinal);
        var currents = new Dictionary<string, double>(StringComparer.Ordinal);
        var powers = new Dictionary<string, double>(StringComparer.Ordinal);

        // Exports are valid only while the simulation yields, so copy the values inside the loop.
        foreach (var _ in op.Run(circuit.Circuit))
        {
            foreach (var (name, export) in nodeExports)
            {
                voltages[name] = export.Value;
            }

            foreach (var (name, export) in sourceExports)
            {
                currents[name] = export.Value;
            }

            foreach (var (name, export) in powerExports)
            {
                powers[name] = export.Value;
            }
        }

        return new OpResult(voltages, currents, powers);
    }
}
