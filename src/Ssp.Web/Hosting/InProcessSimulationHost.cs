using Ssp.Core;
using Ssp.Core.Analysis;
using Ssp.Core.Diagnostics;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Diagnostic = Ssp.Core.Netlist.Diagnostic;

namespace Ssp.Web.Hosting;

/// <summary>
/// Runs <c>Ssp.Core</c> on the calling thread. In the browser, a long run blocks the page.
/// </summary>
public sealed class InProcessSimulationHost : ISimulationHost
{
    public Task<RunResult> Run(string netlist, RunOptions options) =>
        Task.FromResult(Runner.Run(netlist, options));

    public Task<RunResult> Live(string netlist, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(LiveResult(netlist));
    }

    /// <summary>
    /// The first half of <see cref="Runner.Run"/>: the load, the knobs, the rule checks and the operating point.
    /// The live view shows only these, so the frequency response, the impedance and the noise do not run.
    /// </summary>
    public static RunResult LiveResult(string netlist)
    {
        var diagnostics = new List<Diagnostic>();
        var circuit = NetlistLoader.Load(netlist);
        diagnostics.AddRange(circuit.Diagnostics);
        if (!HasError())
        {
            diagnostics.AddRange(Pot.Apply(circuit));
            diagnostics.AddRange(Diagnostics.Run(circuit));
        }

        OpResult? op = null;
        if (!HasError())
        {
            var run = SolverFailure.OperatingPoint(circuit);
            op = run.Value;
            if (run.Diagnostic is { } failure)
            {
                diagnostics.Add(failure);
            }
        }

        return new RunResult(op, null, null, null, diagnostics, new Dictionary<string, double>());

        bool HasError() => diagnostics.Any(d => d.Severity == Severity.Error);
    }

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
