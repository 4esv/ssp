using SpiceSharp.Components;
using SpiceSharp.Simulations;
using SpiceSharp.Simulations.IntegrationMethods;
using Ssp.Core.Netlist;

namespace Ssp.Core.Audio;

/// <summary>
/// Runs one long transient through a circuit and gives the output one chunk at a time.
/// The input is a <see cref="RingBufferWaveform"/> on the <c>ssp:input</c> node. <c>Transient.Run</c> is lazy,
/// so the solver state stays between the chunks: the output has no step at a chunk edge.
/// </summary>
public sealed class MonitorSession : IDisposable
{
    /// <summary>The input chunk size in samples. This is the live monitor block in docs/architecture.md.</summary>
    public const int ChunkSamples = 512;

    // NOTE: One hour. The fixed step transient needs a stop time, and the session ends before it.
    const double StopSeconds = 3600;

    // The Newton iterations for each step. The default is 10.
    const int MaxIterations = 100;

    readonly RingBufferWaveform input;
    readonly IEnumerator<int> steps;
    readonly Transient transient;
    readonly RealVoltageExport voltage;
    readonly VoltageSource source;
    readonly LoadedCircuit circuit;
    readonly bool addedSource;
    readonly int sampleRate;
    long produced;

    public MonitorSession(LoadedCircuit circuit, int sampleRate, int capacity = 1 << 16)
    {
        var inNode = circuit.Directives.Input
            ?? throw new InvalidOperationException("The monitor needs an ssp:input node.");
        var outNode = circuit.Directives.Output ?? "out";
        if (!circuit.NodeNames.Contains(outNode, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"The output node '{outNode}' is not in the circuit.");
        }

        this.circuit = circuit;
        this.sampleRate = sampleRate;
        input = new RingBufferWaveform(sampleRate, capacity);
        var existing = circuit.Circuit.OfType<VoltageSource>()
            .FirstOrDefault(v => string.Equals(v.Nodes[0], inNode, StringComparison.Ordinal)
                && string.Equals(v.Nodes[1], "0", StringComparison.Ordinal));
        source = existing ?? new VoltageSource("V_ssp_monitor", inNode, "0", 0.0);
        addedSource = existing is null;
        source.Parameters.Waveform = input;
        if (addedSource)
        {
            circuit.Circuit.Add(source);
        }

        // NOTE: The fixed step method cannot cut the step, so a step that does not converge throws "timestep too small" (#230).
        // A sharp input edge, as a dropped chunk gives, needs more than the default 10 Newton iterations. Full-scale white
        // noise into clipper-bjt-si needs more than 20 and converges in 50. A step that converges early costs the same.
        transient = new Transient("monitor", new FixedTrapezoidal
        {
            Step = 1.0 / sampleRate,
            StopTime = StopSeconds,
            TransientMaxIterations = MaxIterations,
        });
        voltage = new RealVoltageExport(transient, outNode);
        steps = transient.Run(circuit.Circuit, Transient.ExportTransient).GetEnumerator();
    }

    /// <summary>The number of output samples so far.</summary>
    public long Produced => produced;

    /// <summary>The number of input reads that had no sample yet.</summary>
    public long Starved => input.Starved;

    /// <summary>Pushes the input chunk and returns the output chunk of the same length.</summary>
    public double[] Process(ReadOnlySpan<double> chunk)
    {
        input.Push(chunk);
        var output = new double[chunk.Length];
        for (var i = 0; i < output.Length; i++)
        {
            // NOTE: The first step is the operating point at time 0. The loop skips steps until the next sample time.
            while (true)
            {
                if (!steps.MoveNext())
                {
                    throw new InvalidOperationException("The monitor transient ended.");
                }

                var k = (long)Math.Round(transient.Time * sampleRate);
                if (k == produced)
                {
                    output[i] = voltage.Value;
                    produced++;
                    break;
                }
            }
        }

        return output;
    }

    public void Dispose()
    {
        steps.Dispose();
        if (addedSource)
        {
            circuit.Circuit.Remove(source);
        }
    }
}
