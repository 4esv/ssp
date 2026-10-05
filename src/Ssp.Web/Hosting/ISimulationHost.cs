using Ssp.Core;

namespace Ssp.Web.Hosting;

/// <summary>
/// The one boundary between the UI and <c>Ssp.Core</c>. UI code calls the host only.
/// </summary>
public interface ISimulationHost
{
    /// <summary>Runs the pipeline on the netlist text. See <see cref="Runner.Run"/>.</summary>
    Task<RunResult> Run(string netlist, RunOptions options);

    /// <summary>
    /// Runs what the live view shows after an edit: the load, the knobs, the rule checks and the operating point.
    /// A host that can solve off the page thread does so. A cancelled run ends with an <see cref="OperationCanceledException"/>
    /// or a result that the caller drops.
    /// </summary>
    Task<RunResult> Live(string netlist, CancellationToken token) => Run(netlist, new RunOptions());

    /// <summary>Renders the input samples through the circuit and returns the output samples, one for each input sample.</summary>
    Task<double[]> Render(string netlist, double[] input, int sampleRate, int oversample);

    /// <summary>
    /// Renders as <see cref="Render(string, double[], int, int)"/> does and reports the seconds of audio done.
    /// A host that cannot report progress ignores <paramref name="onProgress"/>.
    /// </summary>
    Task<double[]> Render(string netlist, double[] input, int sampleRate, int oversample, Action<double>? onProgress) =>
        Render(netlist, input, sampleRate, oversample);

    /// <summary>Stops the render that runs. The task of that render ends with an <see cref="OperationCanceledException"/>.</summary>
    void CancelRender()
    {
    }

    /// <summary>Convolves the samples with the impulse response, where the host can do it off the page thread.</summary>
    Task<double[]> Convolve(double[] samples, double[] ir) => Task.FromResult(Ssp.Core.Audio.Convolution.Convolve(samples, ir));

    /// <summary>Runs the pipeline once for each value of the part <paramref name="reference"/>.</summary>
    Task<SweepResult> Sweep(string netlist, string reference, IReadOnlyList<double> values, RunOptions options);

    /// <summary>Returns the version text of ssp and of each engine package.</summary>
    Task<IReadOnlyList<string>> Versions();
}
