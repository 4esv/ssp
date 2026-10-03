using System.Numerics;
using SpiceSharp.Components;
using SpiceSharp.Simulations;
using Ssp.Core.Netlist;

namespace Ssp.Core.Analysis;

public static class Analyses
{
    /// <summary>Computes the DC operating point of a loaded circuit.</summary>
    public static OpResult OperatingPoint(LoadedCircuit circuit, Action<BiasingParameters>? configure = null)
    {
        var op = new OP("op");
        if (configure is not null)
        {
            configure(op.BiasingParameters);
        }

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

    /// <summary>
    /// Computes input and output impedance from 10 Hz to 100 kHz, 10 points in each decade.
    /// </summary>
    public static ZResult Impedance(LoadedCircuit circuit) => Impedance(circuit, new DecadeSweep(10, 100_000, 10));

    /// <summary>
    /// Computes input and output impedance over a sweep.
    /// Input impedance is V/I at the input source. The input is the source on the <c>ssp:input</c> node, or the first voltage source.
    /// Output impedance is the voltage at the output node for 1 A of AC current, with the input source set to zero.
    /// The output node is the <c>ssp:output</c> node, or <c>out</c>. The circuit is the same afterwards.
    /// </summary>
    public static ZResult Impedance(LoadedCircuit circuit, DecadeSweep sweep)
    {
        var frequencies = sweep.Frequencies();
        var sources = circuit.Circuit.OfType<VoltageSource>().ToList();
        var input = (circuit.Directives.Input is { } inNode
                ? sources.FirstOrDefault(v => string.Equals(v.Nodes[0], inNode, StringComparison.Ordinal))
                : null)
            ?? sources.FirstOrDefault()
            ?? throw new InvalidOperationException("Impedance needs a voltage source as the input.");
        var outNode = circuit.Directives.Output ?? "out";
        if (!circuit.NodeNames.Contains(outNode, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"The output node '{outNode}' is not in the circuit.");
        }

        var saved = sources.ToDictionary(v => v, v => v.Parameters.AcMagnitude);
        var zin = new List<Complex>(frequencies.Count);
        var zout = new List<Complex>(frequencies.Count);
        try
        {
            foreach (var v in sources)
            {
                v.Parameters.AcMagnitude = 0.0;
            }

            input.Parameters.AcMagnitude = 1.0;
            var acIn = new AC("zin", frequencies);
            var current = new ComplexPropertyExport(acIn, input.Name, "i");
            var inVoltage = new ComplexVoltageExport(acIn, input.Nodes[0]);
            foreach (var _ in acIn.Run(circuit.Circuit, AC.ExportSmallSignal))
            {
                // SPICE sign: the source current is positive into the positive pin, so the load current is its negative.
                zin.Add(inVoltage.Value / -current.Value);
            }

            input.Parameters.AcMagnitude = 0.0;
            var probe = new CurrentSource("I_ssp_zout");
            probe.Connect("0", outNode);
            probe.Parameters.DcValue = 0.0;
            probe.Parameters.AcMagnitude = 1.0;
            circuit.Circuit.Add(probe);
            try
            {
                var acOut = new AC("zout", frequencies);
                var outVoltage = new ComplexVoltageExport(acOut, outNode);
                foreach (var _ in acOut.Run(circuit.Circuit, AC.ExportSmallSignal))
                {
                    zout.Add(outVoltage.Value);
                }
            }
            finally
            {
                circuit.Circuit.Remove(probe);
            }
        }
        finally
        {
            foreach (var (v, magnitude) in saved)
            {
                v.Parameters.AcMagnitude = magnitude;
            }
        }

        return new ZResult(frequencies, zin, zout);
    }

    /// <summary>
    /// Computes the output noise density from 10 Hz to 100 kHz, 10 points in each decade.
    /// </summary>
    public static NoiseResult Noise(LoadedCircuit circuit) => Noise(circuit, new DecadeSweep(10, 100_000, 10));

    /// <summary>
    /// Computes the output noise density over a sweep.
    /// The input is the independent source on the <c>ssp:input</c> node. The output is the <c>ssp:output</c> node, or <c>out</c>, relative to ground.
    /// If the input or the output is not in the circuit, the result has a diagnostic and no points.
    /// </summary>
    public static NoiseResult Noise(LoadedCircuit circuit, DecadeSweep sweep)
    {
        if (circuit.Directives.Input is not { } inNode)
        {
            return Skip("Noise needs an ssp:input directive.");
        }

        var input = circuit.Circuit
            .Where(e => e is VoltageSource or CurrentSource)
            .Cast<IComponent>()
            .FirstOrDefault(c => c.Nodes.Contains(inNode, StringComparer.Ordinal));
        if (input is null)
        {
            return Skip($"Noise needs a voltage or current source on the ssp:input node '{inNode}'.");
        }

        var outNode = circuit.Directives.Output ?? "out";
        if (!circuit.NodeNames.Contains(outNode, StringComparer.Ordinal))
        {
            return Skip($"Noise needs the output node '{outNode}' in the circuit.");
        }

        var frequencies = sweep.Frequencies();
        var noise = new Noise("noise", input.Name, outNode, "0", frequencies);
        var export = new OutputNoiseDensityExport(noise);
        var density = new List<double>(frequencies.Count);

        // Exports are valid only while the simulation yields, so copy the values inside the loop.
        foreach (var _ in noise.Run(circuit.Circuit, SpiceSharp.Simulations.Noise.ExportNoise))
        {
            // NOTE: The engine gives the density in V²/Hz.
            density.Add(Math.Sqrt(export.Value));
        }

        return new NoiseResult(frequencies, density, []);

        static NoiseResult Skip(string message) => new([], [], [new Diagnostic(Severity.Warning, message, null)]);
    }
}
