using Ssp.Core.Netlist;
using DiagnosticsEngine = Ssp.Core.Diagnostics.Diagnostics;

namespace Ssp.Core.Tests;

public class DiagnosticsTests
{
    static IReadOnlyList<Diagnostic> Run(string fixture) =>
        DiagnosticsEngine.Run(NetlistLoader.Load(Fixtures.Read(fixture)));

    static void AssertOneNaming(string fixture, string name)
    {
        var diagnostic = Assert.Single(Run(fixture));
        Assert.Contains(name, diagnostic.Message);
        Assert.DoesNotContain("SpiceSharp", diagnostic.Message);
        Assert.DoesNotContain("Rule", diagnostic.Message);
    }

    [Fact]
    public void NoGroundNamesTheNode() => AssertOneNaming("diag-no-ground.cir", "node 0");

    [Fact]
    public void NoInputNamesTheDirective() => AssertOneNaming("diag-no-input.cir", "ssp:input");

    [Fact]
    public void NoOutputNamesTheDirective() => AssertOneNaming("diag-no-output.cir", "ssp:output");

    [Fact]
    public void DanglingPinNamesThePartAndNode()
    {
        var diagnostic = Assert.Single(Run("diag-dangling-pin.cir"));
        Assert.Contains("R3", diagnostic.Message);
        Assert.Contains("nowhere", diagnostic.Message);
    }

    [Fact]
    public void FloatingNodeNamesTheNode() => AssertOneNaming("diag-floating-node.cir", "mid");

    [Fact]
    public void VoltageLoopNamesAPart()
    {
        var diagnostic = Assert.Single(Run("diag-voltage-loop.cir"));
        Assert.Matches("V[12]", diagnostic.Message);
    }

    [Fact]
    public void RcLowpassGivesNoDiagnostics() => Assert.Empty(Run("rc-lowpass.cir"));
}
