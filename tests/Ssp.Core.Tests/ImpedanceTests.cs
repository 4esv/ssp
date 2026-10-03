using Ssp.Core.Analysis;
using Ssp.Core.Netlist;

namespace Ssp.Core.Tests;

public class ImpedanceTests
{
    private static int IndexOf(ZResult result, double hz) =>
        Enumerable.Range(0, result.Frequencies.Count).MinBy(k => Math.Abs(Math.Log(result.Frequencies[k] / hz)));

    [Fact]
    public void InputImpedanceIsOneMegohmAtOneKilohertz()
    {
        var result = Analyses.Impedance(NetlistLoader.Load(Fixtures.Read("zin-1meg.cir")));

        var i = IndexOf(result, 1_000);
        Assert.InRange(result.Frequencies[i], 990, 1010);
        Assert.InRange(result.Input[i].Magnitude, 1e6 * 0.99, 1e6 * 1.01);
    }

    [Fact]
    public void OutputImpedanceIsParallelOfR1AndR2()
    {
        var result = Analyses.Impedance(NetlistLoader.Load(Fixtures.Read("divider-basic.cir")));

        var expected = 1_000.0 * 2_000.0 / (1_000.0 + 2_000.0);
        var i = IndexOf(result, 1_000);
        Assert.InRange(result.Output[i].Magnitude, expected * 0.99, expected * 1.01);
    }

    [Fact]
    public void HasOneInputAndOutputValueForEachFrequency()
    {
        var result = Analyses.Impedance(NetlistLoader.Load(Fixtures.Read("divider-basic.cir")));

        Assert.Equal(result.Frequencies.Count, result.Input.Count);
        Assert.Equal(result.Frequencies.Count, result.Output.Count);
    }

    [Fact]
    public void OperatingPointIsTheSameBeforeAndAfter()
    {
        var loaded = NetlistLoader.Load(Fixtures.Read("divider-basic.cir"));
        var before = Analyses.OperatingPoint(loaded);

        Analyses.Impedance(loaded);
        var after = Analyses.OperatingPoint(loaded);

        Assert.Equal(before.NodeVoltages, after.NodeVoltages);
        Assert.Equal(before.SourceCurrents, after.SourceCurrents);
        Assert.Equal(before.DevicePowers, after.DevicePowers);
    }

    [Fact]
    public void CircuitIsUnchangedAfterwards()
    {
        var loaded = NetlistLoader.Load(Fixtures.Read("divider-basic.cir"));
        var names = loaded.Circuit.Select(e => e.Name).ToList();

        Analyses.Impedance(loaded);
        Analyses.Impedance(loaded);

        Assert.Equal(names, loaded.Circuit.Select(e => e.Name).ToList());
    }

    [Fact]
    public void CircuitWithNoVoltageSourceGivesADiagnostic()
    {
        var result = Analyses.Impedance(NetlistLoader.Load(Fixtures.Read("noise-resistor.cir")));

        Assert.Empty(result.Frequencies);
        Assert.Contains(result.Diagnostics, d => d.Severity == Severity.Warning && d.Message.Contains("voltage source", StringComparison.Ordinal));
    }

    [Fact]
    public void RunnerReturnsAResultForACircuitWithNoVoltageSource()
    {
        var result = Runner.Run(Fixtures.Read("noise-resistor.cir"), new RunOptions());

        Assert.NotNull(result.OperatingPoint);
        Assert.True(result.Impedance is not null || result.Diagnostics.Any(d => d.Message.Contains("Impedance", StringComparison.Ordinal)));
    }
}
