using Ssp.Core.Diagnostics;
using Ssp.Core.Netlist;
using DiagnosticsEngine = Ssp.Core.Diagnostics.Diagnostics;

namespace Ssp.Core.Tests;

public class BiasRulesTests
{
    static LoadedCircuit Load(string fixture)
    {
        // opamp-buffer.cir names its model OPAMP. Use the TL072.
        var netlist = Fixtures.Read(fixture).Replace("OPAMP", "TL072");
        // The op-amp fixtures use the TL072 model from models/.
        if (netlist.Contains("TL072", StringComparison.Ordinal))
        {
            netlist = File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "opamp-tl072.cir")) + "\n" + netlist;
        }

        var loaded = NetlistLoader.Load(netlist);
        Assert.Empty(loaded.Diagnostics);
        return loaded;
    }

    static void AssertOneNaming(IRule rule, string fixture, string part)
    {
        var diagnostic = Assert.Single(rule.Check(Load(fixture)));
        Assert.Contains(part, diagnostic.Message);
        Assert.DoesNotContain("SpiceSharp", diagnostic.Message);
        Assert.DoesNotContain("Rule", diagnostic.Message);
    }

    [Fact]
    public void BjtSaturatedNamesThePart() => AssertOneNaming(new BjtSaturatedRule(), "bias-bjt-saturated.cir", "Q1");

    [Fact]
    public void BjtCutOffNamesThePart() => AssertOneNaming(new BjtCutOffRule(), "bias-bjt-cutoff.cir", "Q1");

    [Fact]
    public void OpAmpNearRailNamesThePart() => AssertOneNaming(new OpAmpNearRailRule(), "bias-opamp-near-rail.cir", "X1");

    [Theory]
    [InlineData("bias-bjt-saturated.cir", "Q1")]
    [InlineData("bias-bjt-cutoff.cir", "Q1")]
    [InlineData("bias-opamp-near-rail.cir", "X1")]
    public void RunIncludesTheRule(string fixture, string part)
    {
        var diagnostic = Assert.Single(DiagnosticsEngine.Run(Load(fixture)));
        Assert.Contains(part, diagnostic.Message);
    }

    [Fact]
    public void BjtCeBiasGivesNoBiasDiagnostic() => Assert.Empty(DiagnosticsEngine.Run(Load("bjt-ce-bias.cir")));

    [Fact]
    public void BufferInsideTheRailsGivesNoBiasDiagnostic()
    {
        var loaded = Load("opamp-buffer.cir");
        Assert.Empty(new OpAmpNearRailRule().Check(loaded));
    }
}
