using SpiceSharp;
using SpiceSharp.Algebra;
using SpiceSharp.Components;
using SpiceSharp.Simulations;
using SpiceSharp.Validation;
using Ssp.Core.Analysis;
using Ssp.Core.Netlist;

namespace Ssp.Core.Diagnostics;

/// <summary>The value of a solver run, or the diagnostic that explains why it failed. One of the two is null.</summary>
public sealed record SolverRun<T>(T? Value, Diagnostic? Diagnostic) where T : class;

/// <summary>Turns an engine failure into a diagnostic with a next step for the user.</summary>
public static class SolverFailure
{
    const int RaisedGminSteps = 100;
    const int RaisedSourceSteps = 100;
    const int RaisedDcMaxIterations = 500;

    /// <summary>
    /// Runs the operating point. The engine already tries gmin stepping, diagonal gmin and source stepping.
    /// If it still fails, raise the step and iteration limits and run one more time, then return a diagnostic.
    /// Only a validation failure or a convergence failure is caught.
    /// </summary>
    public static SolverRun<OpResult> OperatingPoint(LoadedCircuit circuit) => OperatingPoint(circuit, Analyses.OperatingPoint);

    /// <summary>Same as <see cref="OperatingPoint(LoadedCircuit)"/> with the run supplied by the caller.</summary>
    public static SolverRun<OpResult> OperatingPoint(LoadedCircuit circuit, Func<LoadedCircuit, Action<BiasingParameters>?, OpResult> run)
    {
        try
        {
            return new SolverRun<OpResult>(run(circuit, null), null);
        }
        catch (Exception first) when (IsSolverFailure(first))
        {
            try
            {
                return new SolverRun<OpResult>(run(circuit, Raise), null);
            }
            catch (Exception second) when (IsSolverFailure(second))
            {
                return new SolverRun<OpResult>(null, ToDiagnostic(second, circuit));
            }
        }
    }

    /// <summary>Maps an engine exception to a diagnostic. Every message ends with a next step.</summary>
    public static Diagnostic ToDiagnostic(Exception exception, LoadedCircuit circuit)
    {
        if (exception is ValidationFailedException validation)
        {
            return new Diagnostic(Severity.Error, $"The solver rejects the circuit. {Describe(validation)}", null);
        }

        if (exception is SingularException)
        {
            return new Diagnostic(Severity.Error, "The solver cannot find a unique solution. A node has no DC path to ground. Connect each node to ground through a resistor or a source.", null);
        }

        return new Diagnostic(Severity.Error, "The solver did not converge. Check the circuit for a part with an extreme value. Lower the supply voltage or add a series resistor to a diode or a transistor, then run again.", null);
    }

    static void Raise(BiasingParameters parameters)
    {
        parameters.GminSteps = RaisedGminSteps;
        parameters.SourceSteps = RaisedSourceSteps;
        parameters.DcMaxIterations = RaisedDcMaxIterations;
    }

    static bool IsSolverFailure(Exception exception) =>
        exception is ValidationFailedException or SingularException || IsConvergenceFailure(exception);

    // The engine reports "Source stepping failed" as a plain SpiceSharpException after its own retries.
    static bool IsConvergenceFailure(Exception exception) => exception.GetType() == typeof(SpiceSharpException);

    static string Describe(ValidationFailedException exception)
    {
        var parts = new List<string>();
        var rules = exception.Rules;
        foreach (var rule in rules is null ? [] : rules.AsEnumerable())
        {
            foreach (var violation in rule.Violations)
            {
                var name = (violation.Subject as IComponent)?.Name;
                if (name is not null && !parts.Contains(name))
                {
                    parts.Add(name);
                }
            }
        }

        return parts.Count > 0
            ? $"Check {string.Join(", ", parts)}: the way that the part is connected is not valid. Change its connections, then run again."
            : "A part or a node is connected in a way that the solver cannot accept. Check the connections of each part, then run again.";
    }
}
