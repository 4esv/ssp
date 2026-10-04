using Ssp.Core.Analysis;
using Ssp.Core.Audio;
using Ssp.Core.Chains;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;

namespace Ssp.Core.Tests;

/// <summary>A fuzz drawn in the editor, through the virtual amp, stopped the render with a step that was too small (#210).</summary>
public class RenderRobustnessTests
{
    static string Block(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "blocks", name));

    static LoadedCircuit Chain()
    {
        var netlist = Chains.Chain.Compose(
        [
            new ChainStage("pedal", Fixtures.Read("fuzz-drawn.cir")),
            new ChainStage("gain", Block("gain-variable-tl072.cir")),
            new ChainStage("tone", Block("tone-baxandall-passive.cir")),
            new ChainStage("power", Block("power-9v.cir")),
        ]);
        var circuit = NetlistLoader.Load(netlist);
        Pot.Apply(circuit);
        return circuit;
    }

    // The first second of the web player clip. It peaks near full scale, so it is the loud input of the report.
    static (double[] Samples, int Fs) Clip()
    {
        using var stream = File.OpenRead(Path.Combine(RepoPaths.Root, "src", "Ssp.Web", "Audio", "clip.wav"));
        var wav = Wav.Read(stream);
        return (wav.Channels[0][..wav.SampleRate], wav.SampleRate);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void DrawnFuzzThroughTheAmpRendersALoudSecond(int oversample)
    {
        var (input, fs) = Clip();

        var output = Analyses.Render(Chain(), input, fs, oversample);

        Assert.Equal(input.Length, output.Length);
        Assert.All(output, v => Assert.True(double.IsFinite(v)));
        var rms = Math.Sqrt(output[(fs / 2)..].Average(v => v * v));
        Assert.True(rms > 0.01, $"The output RMS is {rms} V.");
    }

    // Above 0.1 V in, B1 sinks 2 mA out of 'out' at 0 V and above, and sources 2 mA below 0 V. The 1k load cannot balance
    // a jump across zero, so no voltage at 'out' solves the circuit and no step size helps.
    const string NoSolution = """
        * no solution
        * ssp:input in
        * ssp:output out
        R1 in 0 1k
        R2 out 0 1k
        B1 out 0 I={V(in) > 0.1 ? (V(out) >= 0 ? 0.002 : -0.002) : 0}
        .END
        """;

    [Fact]
    public void RenderThatCannotConvergeSaysWhereAndWhy()
    {
        var input = Enumerable.Range(0, 100).Select(i => i / 100.0).ToArray();

        var e = Assert.Throws<InvalidOperationException>(() => Analyses.Render(NetlistLoader.Load(NoSolution), input, 1000, 1));

        Assert.Contains("does not converge", e.Message);
        Assert.Contains("out", e.Message);
        Assert.Matches(@"t = 0\.01\d+ s", e.Message);
    }

    const string Divider = """
        * divider
        * ssp:input in
        * ssp:output out
        {0}
        R1 in out 1k
        R2 out 0 1k
        .END
        """;

    [Fact]
    public void ASourceWithItsPlusOnGroundRendersLikeTheOtherWayRound()
    {
        var input = Enumerable.Range(0, 100).Select(i => i / 100.0).ToArray();
        var normal = Analyses.Render(NetlistLoader.Load(string.Format(Divider, "V1 in 0 DC 0 SINE(0 1 1k)")), input, 1000, 1);

        var reversed = Analyses.Render(NetlistLoader.Load(string.Format(Divider, "V1 0 in DC 0 SINE(0 1 1k)")), input, 1000, 1);

        Assert.Equal(0.5, normal[^1], 2);
        Assert.Equal(normal.Length, reversed.Length);
        for (var i = 0; i < normal.Length; i++)
        {
            Assert.Equal(normal[i], reversed[i], 6);
        }
    }

    const string ReversedPedal = """
        * schematic
        * ssp:input n2
        V1 0 n2 DC 0 AC 1 SINE(0 1 1k)
        C1 n2 n4 100n
        .model QNPN NPN (IS=1e-14 BF=200)
        Q1 n3 n4 n6 0 QNPN
        R2 n3 n10 2.2k
        V2 n10 0 DC 9
        C2 n3 n8 100n
        .model DGEN D (IS=1e-14 N=1.9)
        D1 n8 0 DGEN
        RV1_1 n8 n9 5k
        RV1_2 n9 0 5k
        * ssp:knob RV1 linear 0.5
        R3 n9 out 1m
        * ssp:output out
        R4 n6 0 1k
        C3 n6 0 22u
        R5 n4 0 47k
        R6 n4 n10 100k
        """;

    [Fact]
    public void ADrawnPedalWithTheSourcePlusOnGroundRendersTheClip()
    {
        var netlist = Chains.Chain.Compose(
        [
            new ChainStage("pedal", ReversedPedal),
            new ChainStage("gain", Block("gain-variable-tl072.cir")),
            new ChainStage("tone", Block("tone-baxandall-passive.cir")),
            new ChainStage("power", Block("power-9v.cir")),
        ]);
        var circuit = NetlistLoader.Load(netlist);
        Pot.Apply(circuit);
        var (input, fs) = Clip();

        var output = Analyses.Render(circuit, input, fs, 1);

        Assert.Equal(input.Length, output.Length);
        Assert.True(Math.Sqrt(output[(fs / 2)..].Average(v => v * v)) > 0.01);
    }
}
