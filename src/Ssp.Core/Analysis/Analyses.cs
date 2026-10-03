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

    /// <summary>
    /// Computes the small-signal frequency response of a loaded circuit.
    /// If no voltage source has an AC magnitude, the first voltage source is driven with 1 V AC.
    /// </summary>
    public static AcResult FrequencyResponse(LoadedCircuit circuit, DecadeSweep sweep)
    {
        var frequencies = sweep.Frequencies();
        var sources = circuit.Circuit.OfType<VoltageSource>().ToList();
        if (sources.Count > 0 && sources.All(v => v.Parameters.AcMagnitude == 0))
        {
            sources[0].Parameters.AcMagnitude = 1.0;
        }

        var ac = new AC("ac", frequencies);
        var exports = circuit.NodeNames.ToDictionary(n => n, n => new ComplexVoltageExport(ac, n), StringComparer.Ordinal);
        var magnitude = circuit.NodeNames.ToDictionary(n => n, _ => new List<double>(frequencies.Count), StringComparer.Ordinal);
        var phase = circuit.NodeNames.ToDictionary(n => n, _ => new List<double>(frequencies.Count), StringComparer.Ordinal);

        // Exports are valid only while the simulation yields, so copy the values inside the loop.
        foreach (var _ in ac.Run(circuit.Circuit, AC.ExportSmallSignal))
        {
            foreach (var (name, export) in exports)
            {
                var value = export.Value;
                magnitude[name].Add(20.0 * Math.Log10(value.Magnitude));
                phase[name].Add(value.Phase * 180.0 / Math.PI);
            }
        }

        return new AcResult(
            frequencies,
            magnitude.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<double>)kv.Value, StringComparer.Ordinal),
            phase.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<double>)kv.Value, StringComparer.Ordinal));
    }
}
