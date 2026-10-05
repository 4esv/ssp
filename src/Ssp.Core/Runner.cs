using System.Diagnostics;
using Ssp.Core.Analysis;
using Ssp.Core.Diagnostics;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Diagnostic = Ssp.Core.Netlist.Diagnostic;
using DiagnosticsEngine = Ssp.Core.Diagnostics.Diagnostics;
using Switch = Ssp.Core.Parts.Switch;

namespace Ssp.Core;

public static class Runner
{
    /// <summary>
    /// Loads the netlist, sets the overrides, checks the rules, then runs the operating point, the frequency response, the impedance and, if the netlist has an <c>ssp:input</c> directive, the noise.
    /// An error from the loader, the overrides or the rules stops the pipeline before the analyses.
    /// A solver failure in the operating point stops the pipeline before the other analyses.
    /// Without an output node the impedance and the noise are skipped, with an info diagnostic.
    /// Run does not throw: an exception in a step becomes an error diagnostic with the step name, and the other analyses still run.
    /// </summary>
    public static RunResult Run(string netlist, RunOptions options)
    {
        var diagnostics = new List<Diagnostic>();
        var timings = new Dictionary<string, double>(StringComparer.Ordinal);

        var circuit = Time("load", "Load", timings, diagnostics, () => NetlistLoader.Load(netlist));
        if (circuit is null)
        {
            return Stop();
        }
        diagnostics.AddRange(circuit.Diagnostics);
        if (HasError(diagnostics))
        {
            return Stop();
        }

        // NOTE: Knob and switch positions are set first, so an explicit override of a pot or switch resistor wins.
        diagnostics.AddRange(Time("overrides", "Overrides", timings, diagnostics, () => Pot.Apply(circuit).Concat(Switch.Apply(circuit)).Concat(Overrides.Apply(circuit, options.Overrides)).ToList()) ?? []);
        diagnostics.AddRange(Time("diagnostics", "Checks", timings, diagnostics, () => DiagnosticsEngine.Run(circuit).ToList()) ?? []);
        if (HasError(diagnostics))
        {
            return Stop();
        }

        var op = Time("operatingPoint", "Operating point", timings, diagnostics, () => SolverFailure.OperatingPoint(circuit));
        if (op?.Value is null)
        {
            if (op?.Diagnostic is { } failure)
            {
                diagnostics.Add(failure);
            }
            return Stop();
        }

        var ac = Time("frequencyResponse", "Frequency response", timings, diagnostics, () => Analyses.FrequencyResponse(circuit, options.Sweep));
        if (!circuit.NodeNames.Contains(circuit.Directives.Output ?? "out", StringComparer.Ordinal))
        {
            diagnostics.Add(new Diagnostic(Severity.Info, "No output node. Name a node out or add * ssp:output <node> to see output impedance and noise.", null));
            return new RunResult(op.Value, ac, null, null, diagnostics, timings);
        }

        var impedance = Time("impedance", "Impedance", timings, diagnostics, () => Analyses.Impedance(circuit, options.Sweep));
        diagnostics.AddRange(impedance?.Diagnostics ?? []);
        var z = impedance?.Frequencies.Count > 0 ? impedance : null;
        NoiseResult? noise = null;
        if (circuit.Directives.Input is not null)
        {
            var result = Time("noise", "Noise", timings, diagnostics, () => Analyses.Noise(circuit, options.Sweep));
            diagnostics.AddRange(result?.Diagnostics ?? []);
            noise = result?.Density.Count > 0 ? result : null;
        }

        return new RunResult(op.Value, ac, z, noise, diagnostics, timings);

        RunResult Stop() => new(null, null, null, null, diagnostics, timings);
    }

    static bool HasError(IEnumerable<Diagnostic> diagnostics) => diagnostics.Any(d => d.Severity == Severity.Error);

    // NOTE: A step that throws gives null and an error diagnostic with its name, so one failure does not take down the run.
    static T? Time<T>(string section, string name, Dictionary<string, double> timings, List<Diagnostic> diagnostics, Func<T> step)
        where T : class
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            return step();
        }
        catch (Exception e)
        {
            diagnostics.Add(new Diagnostic(Severity.Error, $"{name} failed: {e.Message}", null));
            return null;
        }
        finally
        {
            timings[section] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
    }
}
