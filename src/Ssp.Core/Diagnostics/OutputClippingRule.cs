using Ssp.Core.Netlist;

namespace Ssp.Core.Diagnostics;

/// <summary>A render output at or past full scale, 1 V, clips when it is written to a WAV file. Reads the output of <see cref="Analysis.Analyses.Render"/>; runs no simulation.</summary>
public sealed class OutputClippingRule(IReadOnlyList<double> output) : IRule
{
    /// <summary>Full scale, in V. <see cref="Audio.Wav.Write"/> clamps samples to this.</summary>
    public const double FullScale = 1.0;

    public IEnumerable<Diagnostic> Check(LoadedCircuit circuit)
    {
        var clipped = output.Count(v => Math.Abs(v) >= FullScale);
        if (clipped > 0)
        {
            var peak = output.Max(Math.Abs);
            yield return new Diagnostic(Severity.Warning, $"The render output reaches {peak:0.00} V, past the full scale of {FullScale:0} V, in {clipped} of {output.Count} samples. The output clips. Decrease the gain or the input level.", null);
        }
    }
}
