using Ssp.Core.Analysis;
using Ssp.Core.Netlist;

namespace Ssp.Core.Tests;

public class OperatingPointTests
{
    [Fact]
    public void DividerOutputIsTwoThirdsVolt()
    {
        var loaded = NetlistLoader.Load(Fixtures.Read("divider-basic.cir"));

        var result = Analyses.OperatingPoint(loaded);

        Assert.Equal(2.0 / 3.0, result.NodeVoltages["out"], 6);
        Assert.Equal(1.0, result.NodeVoltages["in"], 6);
    }

    [Fact]
    public void DividerHasOneEntryForEachNodeSourceAndDevice()
    {
        var loaded = NetlistLoader.Load(Fixtures.Read("divider-basic.cir"));

        var result = Analyses.OperatingPoint(loaded);

        Assert.Equal(new[] { "0", "in", "out" }, result.NodeVoltages.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(new[] { "V1" }, result.SourceCurrents.Keys);
        Assert.Equal(new[] { "R1", "R2", "V1" }, result.DevicePowers.Keys.OrderBy(k => k, StringComparer.Ordinal));
        // Source V1 delivers 1 V / 3k = 333.33 uA. SPICE sign: current into the positive pin is negative.
        Assert.Equal(-1.0 / 3000.0, result.SourceCurrents["V1"], 9);
        Assert.Equal(1.0 / 3000.0 * 2.0 / 3.0, result.DevicePowers["R2"], 9);
    }

    [Fact]
    public void BjtVceIsWithinFivePercentOfHandCalculation()
    {
        const double handVce = 3.454; // from the header of bjt-ce-bias.cir
        var loaded = NetlistLoader.Load(Fixtures.Read("bjt-ce-bias.cir"));

        var result = Analyses.OperatingPoint(loaded);

        var vce = result.NodeVoltages["c"] - result.NodeVoltages["e"];
        Assert.InRange(vce, handVce * 0.95, handVce * 1.05);
        Assert.Contains("Q1", result.DevicePowers.Keys);
    }
}
