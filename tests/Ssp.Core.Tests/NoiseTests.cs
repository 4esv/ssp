using Ssp.Core.Analysis;
using Ssp.Core.Netlist;

namespace Ssp.Core.Tests;

public class NoiseTests
{
    const double Boltzmann = 1.380649e-23;

    // NOTE: The engine default temperature is 27 °C.
    const double Kelvin = 300.15;

    [Fact]
    public void ResistorDensityIsFourKTR()
    {
        var result = Analyses.Noise(NetlistLoader.Load(Fixtures.Read("noise-resistor.cir")));

        var expected = Math.Sqrt(4 * Boltzmann * Kelvin * 1_000);
        Assert.Empty(result.Diagnostics);
        Assert.NotEmpty(result.Density);
        Assert.All(result.Density, d => Assert.InRange(d, expected * 0.98, expected * 1.02));
    }

    [Fact]
    public void HasOneDensityForEachFrequency()
    {
        var result = Analyses.Noise(NetlistLoader.Load(Fixtures.Read("noise-resistor.cir")));

        Assert.Equal(41, result.Frequencies.Count);
        Assert.Equal(result.Frequencies.Count, result.Density.Count);
    }

    [Fact]
    public void NoInputDirectiveGivesDiagnostic()
    {
        var result = Analyses.Noise(NetlistLoader.Load(Fixtures.Read("rc-lowpass.cir")));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Contains("ssp:input", diagnostic.Message);
        Assert.Empty(result.Density);
    }

    [Fact]
    public void CircuitIsUnchangedAfterwards()
    {
        var loaded = NetlistLoader.Load(Fixtures.Read("pot-lowpass.cir"));
        var before = Analyses.FrequencyResponse(loaded, RunOptions.DefaultSweep);

        Analyses.Noise(loaded);
        var after = Analyses.FrequencyResponse(loaded, RunOptions.DefaultSweep);

        Assert.Equal(before.MagnitudeDb, after.MagnitudeDb);
    }
}
