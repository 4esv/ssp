using Ssp.Core.Analysis;
using Ssp.Core.Netlist;

namespace Ssp.Core.Tests;

public class RunnerTests
{
    static RunResult Run(string fixture) => Runner.Run(Fixtures.Read(fixture), new RunOptions());

    [Fact]
    public void RcLowpassHasAllSections()
    {
        var result = Run("rc-lowpass.cir");

        Assert.NotNull(result.OperatingPoint);
        Assert.NotNull(result.FrequencyResponse);
        Assert.NotNull(result.Impedance);
        Assert.NotNull(result.Diagnostics);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Severity.Error);
        foreach (var section in new[] { "load", "diagnostics", "operatingPoint", "frequencyResponse", "impedance" })
        {
            Assert.True(result.TimingsMs.TryGetValue(section, out var ms), section);
            Assert.True(ms >= 0, section);
        }
    }

    [Fact]
    public void StructuralErrorStopsBeforeTheAnalyses()
    {
        var result = Run("diag-no-ground.cir");

        Assert.Null(result.OperatingPoint);
        Assert.Null(result.FrequencyResponse);
        Assert.Null(result.Impedance);
        Assert.Contains(result.Diagnostics, d => d.Severity == Severity.Error && d.Message.Contains("node 0"));
        Assert.False(result.TimingsMs.ContainsKey("operatingPoint"));
    }

    [Fact]
    public void OptionsSetTheSweepAndTheOverrides()
    {
        var options = new RunOptions(new DecadeSweep(100, 10_000, 5), [Overrides.Parse("R1=2k")]);
        var result = Runner.Run(Fixtures.Read("rc-lowpass.cir"), options);

        Assert.Equal(11, result.FrequencyResponse!.Frequencies.Count);
        Assert.Equal(11, result.Impedance!.Frequencies.Count);
        // NOTE: R1 = 2k with the 100 nF capacitor gives a corner at 796 Hz, so 1 kHz is more than 3 dB down.
        var at1k = result.FrequencyResponse.Frequencies.ToList().FindIndex(f => Math.Abs(f - 1000) < 1e-6);
        Assert.True(result.FrequencyResponse.MagnitudeDb["out"][at1k] < -3.0);
    }

    [Fact]
    public void UnknownOverrideStopsBeforeTheAnalyses()
    {
        var options = new RunOptions(overrides: [Overrides.Parse("R9=1k")]);
        var result = Runner.Run(Fixtures.Read("rc-lowpass.cir"), options);

        Assert.Null(result.OperatingPoint);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("R9"));
    }
}
