using System.Globalization;
using System.Text.RegularExpressions;
using Ssp.Core.Analysis;
using Ssp.Core.Diagnostics;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;

namespace Ssp.Core.Tests.Pedals;

// Tests for circuits/library/distortion-rat.cir (#421). Knob position 1 is the clockwise end.
public class DistortionRatTests
{
    static readonly string Text = File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "library", "distortion-rat.cir"));

    // Output level in dB at one frequency, with the three knobs set.
    static double Db(double hz, double distortion, double filter, double volume)
    {
        var text = Text;
        foreach (var (name, pos) in new[] { ("RV1", distortion), ("RV2", filter), ("RV3", volume) })
        {
            text = Regex.Replace(text, $@"(ssp:knob {name} \S+) \S+", $"$1 {pos.ToString(CultureInfo.InvariantCulture)}");
        }
        var circuit = NetlistLoader.Load(text);
        Assert.Empty(Pot.Apply(circuit));
        var result = Analyses.FrequencyResponse(circuit, new DecadeSweep(10, 100_000, 10));
        var i = Enumerable.Range(0, result.Frequencies.Count).MinBy(k => Math.Abs(Math.Log(result.Frequencies[k] / hz)));
        return result.MagnitudeDb["out"][i];
    }

    [Fact]
    public void GainAtOneKilohertzWithDistortionAtOneIsAtLeastSixtyDb() =>
        Assert.True(Db(1_000, 1, 0, 1) >= 60, $"gain {Db(1_000, 1, 0, 1):F1} dB");

    [Fact]
    public void FilterAtOneCutsFiveKilohertzByTenDbOrMore()
    {
        var open = Db(5_000, 1, 0, 1);
        var closed = Db(5_000, 1, 1, 1);
        Assert.True(open - closed >= 10, $"Filter 0 {open:F1} dB, Filter 1 {closed:F1} dB");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EachKnobSolvesAtThreePositionsAndChangesTheOutput(int knob)
    {
        double[] positions = [0, 0.5, 1];
        var levels = positions.Select(p => knob switch
        {
            0 => Db(1_000, p, 0.5, 0.5),
            1 => Db(5_000, 0.5, p, 0.5),
            _ => Db(1_000, 0.5, 0.5, p),
        }).ToArray();

        Assert.All(levels, l => Assert.False(double.IsNaN(l)));
        Assert.True(levels.Max() - levels.Min() > 1.0, string.Join(", ", levels.Select(l => l.ToString("F1"))));
    }

    // #443: the published RAT has a 2.2M input bias and a 2N5458 JFET source follower.
    [Fact]
    public void InputBiasIs2M2AndTheBufferIsA2N5458SourceFollower()
    {
        var circuit = NetlistLoader.Load(Text);
        Assert.True(circuit.Circuit.TryGetEntity("RB", out var rb));
        Assert.Equal(2.2e6, ((SpiceSharp.Components.Resistor)rb!).Parameters.Resistance, 1);
        Assert.True(circuit.Circuit.TryGetEntity("J1", out var j));
        Assert.IsType<SpiceSharp.Components.JFET>(j);
        Assert.False(circuit.Circuit.TryGetEntity("Q1", out _));
        Assert.Contains("2N5458", Text);
    }

    [Fact]
    public void TheSourceFollowerSitsInSaturationWithRoomForTheSignal()
    {
        var circuit = NetlistLoader.Load(Text);
        var op = SolverFailure.OperatingPoint(circuit);
        Assert.Null(op.Diagnostic?.Message);
        var v = op.Value!.NodeVoltages;
        var vds = 4.5 - v["e"];
        var vgs = v["b"] - v["e"];
        Assert.InRange(vgs, -3.5, -1.5);
        Assert.True(vds > (vgs + 4.0) + 0.3, $"Vds {vds:F2} Vgs {vgs:F2}");
    }
}
