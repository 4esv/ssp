using System.Diagnostics;
using Ssp.Core.Analysis;
using Ssp.Core.Diagnostics;
using Ssp.Core.Netlist;
using Diagnostic = Ssp.Core.Netlist.Diagnostic;
using DiagnosticsEngine = Ssp.Core.Diagnostics.Diagnostics;

namespace Ssp.Core;

public static class Runner
{
    /// <summary>
    /// Loads the netlist, sets the overrides, checks the rules, then runs the operating point, the frequency response and the impedance.
    /// An error from the loader, the overrides or the rules stops the pipeline before the analyses.
    /// A solver failure in the operating point stops the pipeline before the other analyses.
    /// </summary>
    public static RunResult Run(string netlist, RunOptions options)
    {
        var diagnostics = new List<Diagnostic>();
        var timings = new Dictionary<string, double>(StringComparer.Ordinal);

        var circuit = Time("load", timings, () => NetlistLoader.Load(netlist));
        diagnostics.AddRange(circuit.Diagnostics);
        if (HasError(diagnostics))
        {
            return Stop();
        }

        diagnostics.AddRange(Time("overrides", timings, () => Overrides.Apply(circuit, options.Overrides)));
        diagnostics.AddRange(Time("diagnostics", timings, () => DiagnosticsEngine.Run(circuit)));
        if (HasError(diagnostics))
        {
            return Stop();
        }

        var op = Time("operatingPoint", timings, () => SolverFailure.OperatingPoint(circuit));
        if (op.Value is null)
        {
            diagnostics.Add(op.Diagnostic!);
            return Stop();
        }

        var ac = Time("frequencyResponse", timings, () => Analyses.FrequencyResponse(circuit, options.Sweep));
        var z = Time("impedance", timings, () => Analyses.Impedance(circuit, options.Sweep));
        return new RunResult(op.Value, ac, z, diagnostics, timings);

        RunResult Stop() => new(null, null, null, diagnostics, timings);
    }

    static bool HasError(IEnumerable<Diagnostic> diagnostics) => diagnostics.Any(d => d.Severity == Severity.Error);

    static T Time<T>(string section, Dictionary<string, double> timings, Func<T> step)
    {
        var start = Stopwatch.GetTimestamp();
        var value = step();
        timings[section] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return value;
    }
}
