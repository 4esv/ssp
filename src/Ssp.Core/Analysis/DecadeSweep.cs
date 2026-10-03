namespace Ssp.Core.Analysis;

/// <summary>A logarithmic frequency sweep.</summary>
/// <param name="StartHz">First frequency in hertz. Must be greater than zero.</param>
/// <param name="StopHz">Last frequency in hertz. Must be greater than the start.</param>
/// <param name="PointsPerDecade">Number of points in each decade. Must be at least one.</param>
public sealed record DecadeSweep(double StartHz, double StopHz, int PointsPerDecade)
{
    /// <summary>Number of frequencies in the sweep, including both ends.</summary>
    public int PointCount => (int)Math.Round(Math.Log10(StopHz / StartHz) * PointsPerDecade) + 1;

    /// <summary>The frequencies in hertz, in ascending order.</summary>
    public IReadOnlyList<double> Frequencies()
    {
        if (StartHz <= 0 || StopHz <= StartHz || PointsPerDecade < 1)
        {
            throw new ArgumentException("A sweep needs 0 < start < stop and at least one point per decade.");
        }

        var count = PointCount;
        var step = 1.0 / PointsPerDecade;
        var result = new double[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = StartHz * Math.Pow(10, i * step);
        }

        return result;
    }
}
