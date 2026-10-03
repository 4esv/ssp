using Ssp.Core;

namespace Ssp.Web.Hosting;

/// <summary>
/// The one boundary between the UI and <c>Ssp.Core</c>. UI code calls the host only.
/// </summary>
public interface ISimulationHost
{
    /// <summary>Runs the pipeline on the netlist text. See <see cref="Runner.Run"/>.</summary>
    Task<RunResult> Run(string netlist, RunOptions options);

    /// <summary>Renders the input samples through the circuit and returns the output samples, one for each input sample.</summary>
    Task<double[]> Render(string netlist, double[] input, int sampleRate, int oversample);

    /// <summary>Runs the pipeline once for each value of the part <paramref name="reference"/>.</summary>
    Task<SweepResult> Sweep(string netlist, string reference, IReadOnlyList<double> values, RunOptions options);

    /// <summary>Returns the version text of ssp and of each engine package.</summary>
    Task<IReadOnlyList<string>> Versions();
}
