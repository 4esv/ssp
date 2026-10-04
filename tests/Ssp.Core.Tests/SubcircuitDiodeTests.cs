using Ssp.Core.Analysis;
using Ssp.Core.Diagnostics;
using Ssp.Core.Chains;
using Ssp.Core.Netlist;

namespace Ssp.Core.Tests;

public class SubcircuitDiodeTests
{
    const string Netlist = """
        * diode inside a subcircuit
        * ssp:input in
        * ssp:output out
        .subckt clip a b
        .model DCLIP D(Is=2.52n N=1.752)
        R1 a b 1k
        D1 b 0 DCLIP
        .ends clip
        VIN in 0 DC 0 AC 1
        X1 in out clip
        RL out 0 100k
        .END
        """;

    const string TopModel = """
        * diode inside a subcircuit, model at the top level
        * ssp:input in
        * ssp:output out
        .model DCLIP D(Is=2.52n N=1.752)
        .subckt clip a b
        R1 a b 1k
        D1 b 0 DCLIP
        .ends clip
        VIN in 0 DC 0 AC 1
        X1 in out clip
        RL out 0 100k
        .END
        """;

    // NOTE: 0.1 V peak is a guitar level. At 1 V peak clip-feedback-led-tl072.cir fails with a too small timestep, also without a chain.
    static double[] Sine(int fs) =>
        Enumerable.Range(0, fs / 100).Select(i => 0.1 * Math.Sin(2 * Math.PI * 1000 * i / fs)).ToArray();

    [Theory]
    [InlineData(Netlist)]
    [InlineData(TopModel)]
    public void RunDoesNotThrow(string netlist)
    {
        var result = Runner.Run(netlist, new RunOptions());

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Severity.Error);
        Assert.NotNull(result.OperatingPoint);
        Assert.NotNull(result.FrequencyResponse);
        Assert.All(result.FrequencyResponse!.MagnitudeDb["out"], v => Assert.True(double.IsFinite(v)));
    }

    [Theory]
    [InlineData(Netlist)]
    [InlineData(TopModel)]
    public void RenderDoesNotThrow(string netlist)
    {
        var output = Analyses.Render(NetlistLoader.Load(netlist), Sine(48_000), 48_000, 1);

        Assert.All(output, v => Assert.True(double.IsFinite(v)));
    }

    public static TheoryData<string> Blocks() =>
    [
        "power-9v.cir",
        .. Directory.GetFiles(Path.Combine(RepoPaths.Root, "circuits", "blocks"), "clip-*.cir").Select(f => Path.GetFileName(f)).Order(),
    ];

    [Theory]
    [MemberData(nameof(Blocks))]
    public void ChainWithTheBlockRenders(string block)
    {
        var netlist = File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "blocks", block));
        var chain = Chain.Compose([new ChainStage("stage", netlist)]);
        var output = Analyses.Render(NetlistLoader.Load(chain), Sine(48_000), 48_000, 1);

        Assert.All(output, v => Assert.True(double.IsFinite(v)));
    }
}
