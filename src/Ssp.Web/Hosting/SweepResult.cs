using Ssp.Core;

namespace Ssp.Web.Hosting;

/// <summary>The result of a sweep over the values of one part.</summary>
/// <param name="Reference">The part that the sweep changed.</param>
/// <param name="Series">One run for each value, in the order of the values.</param>
public sealed record SweepResult(string Reference, IReadOnlyList<SweepSeries> Series);

/// <summary>One run of a sweep.</summary>
/// <param name="Value">The part value for this run.</param>
/// <param name="Result">The result of the run.</param>
public sealed record SweepSeries(double Value, RunResult Result);
