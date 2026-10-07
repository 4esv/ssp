using Ssp.Core.Analysis;
using Ssp.Core.Netlist;

namespace Ssp.Core.Tests;

public class MosfetModelTests
{
    [Fact]
    public void OperatingPointOfAMosfetRunsWithoutADiagnostic()
    {
        var loaded = NetlistLoader.Load(Fixtures.Read("mosfet-op.cir"));

        Assert.Empty(loaded.Diagnostics);

        var result = Analyses.OperatingPoint(loaded);

        Assert.Equal(5.0, result.NodeVoltages["d"], 2);
    }
}
