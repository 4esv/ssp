using System.Numerics;
using SpiceSharp;
using SpiceSharp.Components;
using SpiceSharp.Simulations;
using SpiceSharp.Simulations.IntegrationMethods;
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
    /// If the circuit has no voltage source, the result has a diagnostic and no points.
    /// </summary>
    public static ZResult Impedance(LoadedCircuit circuit, DecadeSweep sweep)
    {
        var frequencies = sweep.Frequencies();
        var sources = circuit.Circuit.OfType<VoltageSource>().ToList();
        var input = (circuit.Directives.Input is { } inNode
                ? sources.FirstOrDefault(v => string.Equals(v.Nodes[0], inNode, StringComparison.Ordinal))
                : null)
            ?? sources.FirstOrDefault();
        if (input is null)
        {
            return new ZResult([], [], [], [new Diagnostic(Severity.Warning, "Impedance needs a voltage source as the input.", null)]);
        }
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

        return new ZResult(frequencies, zin, zout, []);
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

    /// <summary>
    /// Renders an input signal through a circuit and returns the output samples, one for each input sample.
    /// The input drives the <c>ssp:input</c> node through a piecewise-linear source.
    /// The output is the voltage at the <c>ssp:output</c> node, or <c>out</c>.
    /// The solver uses fixed trapezoidal steps of 1 / (fs * oversample). If a fixed step does not converge, the render starts
    /// again with variable trapezoidal steps of at most that size, and the output is interpolated at the sample times.
    /// The circuit is the same afterwards.
    /// <paramref name="progress"/> gets the number of output samples done, about ten times for each second of audio.
    /// A render with variable steps starts again from 0.
    /// </summary>
    /// <exception cref="InvalidOperationException">The variable steps also do not converge. The message gives the time
    /// and the node that changes most in the last iteration.</exception>
    public static double[] Render(LoadedCircuit circuit, double[] input, int fs, int oversample, Action<int>? progress = null)
    {
        if (fs <= 0 || oversample < 1)
        {
            throw new ArgumentException("Render needs fs > 0 and oversample >= 1.");
        }

        if (input.Length == 0)
        {
            return [];
        }

        var inNode = circuit.Directives.Input
            ?? throw new InvalidOperationException("Render needs an ssp:input node.");
        var outNode = circuit.Directives.Output ?? "out";
        if (!circuit.NodeNames.Contains(outNode, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"The output node '{outNode}' is not in the circuit.");
        }

        var points = new double[2 * Math.Max(input.Length, 2)];
        for (var i = 0; i < points.Length / 2; i++)
        {
            points[2 * i] = (double)i / fs;
            points[(2 * i) + 1] = input[Math.Min(i, input.Length - 1)];
        }

        var sources = circuit.Circuit.OfType<VoltageSource>().ToList();
        var existing = sources.FirstOrDefault(v => string.Equals(v.Nodes[0], inNode, StringComparison.Ordinal)
                && string.Equals(v.Nodes[1], "0", StringComparison.Ordinal));
        // NOTE: A source drawn with its plus on ground has Nodes[0] = 0. It drives the input node with the input negated (#216).
        var reversed = existing is null
            ? sources.FirstOrDefault(v => string.Equals(v.Nodes[0], "0", StringComparison.Ordinal)
                && string.Equals(v.Nodes[1], inNode, StringComparison.Ordinal))
            : null;
        existing ??= reversed;
        if (reversed is not null)
        {
            for (var i = 1; i < points.Length; i += 2)
            {
                points[i] = -points[i];
            }
        }

        var pwl = new Pwl();
        pwl.SetPoints(points);

        var source = existing ?? new VoltageSource("V_ssp_render", inNode, "0", 0.0);
        var saved = source.Parameters.Waveform;
        var step = 1.0 / ((double)fs * oversample);
        var stop = (input.Length - 1) * oversample * step;
        try
        {
            source.Parameters.Waveform = pwl;
            if (existing is null)
            {
                circuit.Circuit.Add(source);
            }

            try
            {
                return Sample(circuit, new FixedTrapezoidal { Step = step, StopTime = stop }, outNode, input.Length, fs, progress);
            }
            catch (TimestepTooSmallException)
            {
                // NOTE: A loud transient into a saturating op-amp model needs steps near 1e-10 s for one sample (#210).
                // The fixed method cannot make the step smaller, so the variable method renders the full input again.
                return Sample(circuit, new Trapezoidal { StopTime = stop, MaxStep = step, InitialStep = step }, outNode, input.Length, fs, progress);
            }
        }
        finally
        {
            source.Parameters.Waveform = saved;
            if (existing is null)
            {
                circuit.Circuit.Remove(source);
            }
        }
    }

    // Streams the accepted time points and keeps the output at each sample time i / fs, interpolated between the two
    // time points around it. A time point within 1e-6 of a sample period from a sample time is that sample.
    private static double[] Sample(LoadedCircuit circuit, TimeParameters method, string outNode, int count, int fs, Action<int>? progress)
    {
        var tran = new Transient("render", method);
        var voltage = new RealVoltageExport(tran, outNode);
        var output = new double[count];
        var written = 0;
        var reported = 0;
        var tenth = Math.Max(1, fs / 10);
        double lastTime = 0, lastValue = 0;
        IBiasingSimulationState? state = null;
        try
        {
            foreach (var _ in tran.Run(circuit.Circuit, Transient.ExportTransient))
            {
                state ??= tran.GetState<IBiasingSimulationState>();
                var time = tran.Time;
                var value = voltage.Value;
                while (written < count)
                {
                    var target = (double)written / fs;
                    if (Math.Abs(time - target) * fs < 1e-6)
                    {
                        output[written++] = value;
                    }
                    else if (target < time && written > 0)
                    {
                        output[written++] = lastValue + ((value - lastValue) * (target - lastTime) / (time - lastTime));
                    }
                    else
                    {
                        break;
                    }
                }

                if (progress is not null && written / tenth != reported / tenth)
                {
                    progress(written);
                }

                reported = written;
                lastTime = time;
                lastValue = value;
            }
        }
        catch (TimestepTooSmallException e) when (method is not FixedTrapezoidal)
        {
            throw new InvalidOperationException(
                $"The render stops at t = {e.Time:0.000000} s: the solver does not converge with a step of {e.Timestep:G3} s. "
                + $"The node that changes most in the last iteration is {LargestChange(state)}.", e);
        }

        if (written != count)
        {
            throw new InvalidOperationException($"The solver gave {written} of {count} output samples.");
        }

        return output;
    }

    private static string LargestChange(IBiasingSimulationState? state)
    {
        if (state is null)
        {
            return "not known";
        }

        var name = "not known";
        var largest = -1.0;
        foreach (var (variable, index) in state.Map)
        {
            var change = Math.Abs(state.Solution[index] - state.OldSolution[index]);
            if (index > 0 && change > largest)
            {
                largest = change;
                name = $"{variable.Name}, by {change:G3}";
            }
        }

        return name;
    }
}
