namespace Ssp.Core.Analysis;

/// <summary>The work of one <see cref="Analyses.Render"/>. The counts include a fixed-step start that does not converge.</summary>
public sealed class RenderStatistics
{
    /// <summary>The accepted solver time points.</summary>
    public int Steps { get; internal set; }

    /// <summary>The Newton iterations of the transient.</summary>
    public int Iterations { get; internal set; }

    /// <summary>True if the render uses variable steps after a fixed step did not converge.</summary>
    public bool VariableSteps { get; internal set; }
}
