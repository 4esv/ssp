using Ssp.Core;
using Ssp.Web.Hosting;

namespace Ssp.Web.Tests;

public class InProcessSimulationHostTests
{
    static readonly ISimulationHost Host = new InProcessSimulationHost();

    static string Fixture(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", name));

    [Fact]
    public async Task RunGivesOperatingPoint()
    {
        var result = await Host.Run(Fixture("rc-lowpass.cir"), new RunOptions());

        Assert.NotNull(result.OperatingPoint);
        Assert.Equal(1.0, result.OperatingPoint.NodeVoltages["in"], 6);
    }

    [Fact]
    public async Task SweepGivesOneSeriesForEachValue()
    {
        var result = await Host.Sweep(Fixture("rc-lowpass.cir"), "R1", [4_700, 10_000, 22_000], new RunOptions());

        Assert.Equal("R1", result.Reference);
        Assert.Equal([4_700, 10_000, 22_000], result.Series.Select(s => s.Value));
        Assert.All(result.Series, s => Assert.NotNull(s.Result.FrequencyResponse));
        // NOTE: a larger R1 moves the corner down, so each value gives a different response.
        Assert.Equal(3, result.Series.Select(s => s.Result.FrequencyResponse!.MagnitudeDb["out"][^1]).Distinct().Count());
    }

    [Fact]
    public async Task RenderIsNotSupportedYet()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => Host.Render(Fixture("rc-lowpass.cir"), [0.0], 48_000, 1));
    }
}
