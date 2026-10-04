using Ssp.Core.Diagnostics;
using Ssp.Core.Netlist;

namespace Ssp.Core.Tests;

public class ProblemsTests
{
    static string OpAmp(string netlist) =>
        File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "opamp-tl072.cir")) + "\n" + netlist;

    static IReadOnlyList<Problem> Find(string netlist) => Problems.Find(NetlistLoader.Load(netlist));

    [Fact]
    public void FloatingPinNamesThePartAndPin()
    {
        var problem = Assert.Single(Find(Fixtures.Read("diag-dangling-pin.cir")));
        Assert.Equal("R3", problem.Reference);
        Assert.Equal(1, problem.Pin);
        Assert.Contains("pin 2", problem.Message);
    }

    [Fact]
    public void FixedFloatingPinGivesNothing() =>
        Assert.Empty(Find(Fixtures.Read("diag-dangling-pin.cir").Replace("R3 out nowhere 10k", "R3 out 0 10k")));

    [Fact]
    public void MissingGroundNamesAPart()
    {
        var problem = Assert.Single(Find(Fixtures.Read("diag-no-ground.cir")));
        Assert.Equal("V1", problem.Reference);
        Assert.Contains("ground", problem.Message);
    }

    [Fact]
    public void FixedGroundGivesNothing() =>
        Assert.Empty(Find(Fixtures.Read("diag-no-ground.cir").Replace("V1 in a 1", "V1 in 0 1").Replace("R2 out a 2k", "R2 out 0 2k")));

    [Fact]
    public void OpAmpWithNoBiasNamesTheOpAmpAndInput()
    {
        var problem = Assert.Single(Find(OpAmp(Fixtures.Read("diag-opamp-no-bias.cir"))));
        Assert.Equal("X1", problem.Reference);
        Assert.Equal(0, problem.Pin);
        Assert.Contains("bias", problem.Message);
    }

    [Fact]
    public void FixedOpAmpBiasGivesNothing() =>
        Assert.Empty(Find(OpAmp(Fixtures.Read("diag-opamp-no-bias.cir").Replace("Rl out 0 10k", "Rl out 0 10k\nRb nobias 0 1Meg"))));
}
