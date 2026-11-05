using System.Globalization;
using Ssp.Core.Analysis;
using Ssp.Core.Diagnostics;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;

namespace Ssp.Core.Tests.Pedals;

// #422: the silicon Fuzz Face in circuits/library/fuzz-face-si.cir.
public class FuzzFaceSiTests
{
    static string Text => File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "library", "fuzz-face-si.cir"));

    static LoadedCircuit Load(double fuzz = 0.5, double volume = 1, string? output = null)
    {
        var text = Text
            .Replace("ssp:knob RV1 linear 0.5", "ssp:knob RV1 linear " + fuzz.ToString(CultureInfo.InvariantCulture))
            .Replace("ssp:knob RV2 log 1", "ssp:knob RV2 log " + volume.ToString(CultureInfo.InvariantCulture));
        if (output is not null) text = text.Replace("* ssp:output out", "* ssp:output " + output);
        var loaded = NetlistLoader.Load(text);
        Assert.Empty(loaded.Diagnostics);
        Assert.Empty(Pot.Apply(loaded));
        return loaded;
    }

    static double[] Render(LoadedCircuit loaded, double amplitude, int fs = 32000)
    {
        var count = fs / 4;
        var input = Enumerable.Range(0, count).Select(i => amplitude * Math.Sin(2 * Math.PI * 220 * i / fs)).ToArray();
        return Analyses.Render(loaded, input, fs, 2).Skip(count / 2).ToArray();
    }

    // RMS of harmonics 2 to 5 of 220 Hz, in volts.
    static double Harmonics(double[] y, int fs = 32000)
    {
        var mean = y.Average();
        double Power(double f)
        {
            double re = 0, im = 0;
            for (var i = 0; i < y.Length; i++)
            {
                var w = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (y.Length - 1));
                re += w * (y[i] - mean) * Math.Cos(2 * Math.PI * f * i / fs);
                im += w * (y[i] - mean) * Math.Sin(2 * Math.PI * f * i / fs);
            }
            return re * re + im * im;
        }
        return Math.Sqrt(Enumerable.Range(2, 4).Sum(h => Power(h * 220))) / y.Length;
    }

    static double Collector2(LoadedCircuit loaded)
    {
        var op = SolverFailure.OperatingPoint(loaded);
        Assert.Null(op.Diagnostic?.Message);
        return op.Value!.NodeVoltages["c2"];
    }

    [Fact]
    public void Q2CollectorIsAtTheStandardBiasPoint()
    {
        var v = Collector2(Load());
        Assert.InRange(v, 4.0, 5.5);
    }

    [Fact]
    public void TheOperatingPointIsTheSameStateInFiveRuns()
    {
        var first = Collector2(Load());
        for (var run = 0; run < 5; run++)
        {
            var loaded = Load();
            var op = SolverFailure.OperatingPoint(loaded).Value!;
            Assert.InRange(op.NodeVoltages["c2"], 4.0, 5.5);
            Assert.Equal(first, op.NodeVoltages["c2"], 3);
            // NOTE: The other state has Q1 saturated or cut off: its collector is then near 0 V or 9 V.
            Assert.InRange(op.NodeVoltages["c1"], 0.9, 2.0);
        }
    }

    [Fact]
    public void FuzzAtOneGivesMoreHarmonicContentThanAtZeroWhenDrivenAt100Millivolts()
    {
        var low = Harmonics(Render(Load(fuzz: 0), 0.1));
        var high = Harmonics(Render(Load(fuzz: 1), 0.1));
        Assert.True(high > low, $"Fuzz 1: {high:0.0000} V, Fuzz 0: {low:0.0000} V");
    }

    [Fact]
    public void TheOutputIsClippedAt100Millivolts()
    {
        // NOTE: Ten times the input gives well under ten times the output.
        var small = Render(Load(fuzz: 1), 0.01).Max(Math.Abs);
        var big = Render(Load(fuzz: 1), 0.1).Max(Math.Abs);
        Assert.True(big < 2 * small, $"10 mV: {small:0.000} V, 100 mV: {big:0.000} V");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void TheCircuitSolvesAtEachKnobPosition(double knob)
    {
        foreach (var loaded in new[] { Load(fuzz: knob), Load(volume: knob) })
        {
            Assert.InRange(Collector2(loaded), 3.5, 6.0);
            var y = Render(loaded, 0.05);
            Assert.All(y, v => Assert.True(double.IsFinite(v)));
        }
    }

    [Fact]
    public void FuzzChangesTheOutput()
    {
        var gain = new[] { 0.0, 0.5, 1.0 }.Select(k => Analyses.FrequencyResponse(Load(fuzz: k), new DecadeSweep(220, 2200, 10)).MagnitudeDb["out"][0]).ToArray();
        Assert.True(gain[1] - gain[0] > 1 && gain[2] - gain[1] > 1, string.Join(", ", gain));
    }

    [Fact]
    public void VolumeChangesTheOutput()
    {
        var level = new[] { 0.0, 0.5, 1.0 }.Select(k => Render(Load(volume: k), 0.1).Max(Math.Abs)).ToArray();
        Assert.True(level[0] < level[1] && level[1] < level[2], string.Join(", ", level));
    }
}
