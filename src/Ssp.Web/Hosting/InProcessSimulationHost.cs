using Ssp.Core;
using Ssp.Core.Netlist;

namespace Ssp.Web.Hosting;

/// <summary>
/// Runs <c>Ssp.Core</c> on the calling thread. In the browser, a long run blocks the page.
/// </summary>
public sealed class InProcessSimulationHost : ISimulationHost
{
    public Task<RunResult> Run(string netlist, RunOptions options) =>
        Task.FromResult(Runner.Run(netlist, options));

    // TODO: call Analyses.Render when Core has it (#7).
    public Task<double[]> Render(string netlist, double[] input, int sampleRate, int oversample) =>
        Task.FromException<double[]>(new NotSupportedException("Core has no transient render yet (#7)."));

    public Task<SweepResult> Sweep(string netlist, string reference, IReadOnlyList<double> values, RunOptions options)
    {
        var series = values
            .Select(value => new SweepSeries(
                value,
                Runner.Run(netlist, options with { Overrides = [.. options.Overrides, new Override(reference, value)] })))
            .ToList();
        return Task.FromResult(new SweepResult(reference, series));
    }

    public Task<IReadOnlyList<string>> Versions() => Task.FromResult(EngineInfo.VersionLines());
}
