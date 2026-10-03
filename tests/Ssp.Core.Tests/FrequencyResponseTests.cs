using Ssp.Core.Analysis;
using Ssp.Core.Netlist;

namespace Ssp.Core.Tests;

public class FrequencyResponseTests
{
    private const double R = 1_000.0;
    private const double C = 100e-9;
    private static readonly double Corner = 1.0 / (2.0 * Math.PI * R * C);

    private static AcResult Run(DecadeSweep sweep) =>
        Analyses.FrequencyResponse(NetlistLoader.Load(Fixtures.Read("rc-lowpass.cir")), sweep);

    [Fact]
    public void RcLowPassIsMinusThreeDbAtCorner()
    {
        var result = Run(new DecadeSweep(10, 100_000, 200));

        var i = Enumerable.Range(0, result.Frequencies.Count)
            .MinBy(k => Math.Abs(Math.Log(result.Frequencies[k] / Corner)));
        Assert.InRange(result.Frequencies[i], Corner * 0.98, Corner * 1.02);
        var db = result.MagnitudeDb["out"][i];
        Assert.InRange(db, -3.0103 * 1.02, -3.0103 * 0.98);
        Assert.InRange(result.PhaseDegrees["out"][i], -46.0, -44.0);
    }

    [Fact]
    public void PointCountMatchesSweepAndEachNodeHasOneSeries()
    {
        var sweep = new DecadeSweep(10, 100_000, 10);

        var result = Run(sweep);

        Assert.Equal(sweep.PointCount, result.Frequencies.Count);
        Assert.Equal(4 * 10 + 1, result.Frequencies.Count);
        Assert.Equal(new[] { "0", "in", "out" }, result.MagnitudeDb.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(new[] { "0", "in", "out" }, result.PhaseDegrees.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.All(result.MagnitudeDb.Values, s => Assert.Equal(sweep.PointCount, s.Count));
        Assert.All(result.PhaseDegrees.Values, s => Assert.Equal(sweep.PointCount, s.Count));
    }

    [Fact]
    public void InputNodeIsZeroDbAndZeroDegrees()
    {
        var result = Run(new DecadeSweep(10, 1_000, 5));

        Assert.All(result.MagnitudeDb["in"], v => Assert.Equal(0.0, v, 6));
        Assert.All(result.PhaseDegrees["in"], v => Assert.Equal(0.0, v, 6));
    }
}
