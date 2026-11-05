using System.Globalization;
using System.Text.RegularExpressions;
using Ssp.Core.Analysis;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;

namespace Ssp.Core.Tests.Pedals;

/// <summary>
/// #419: the clean booster of the library. It is the one-transistor stage that builders know, so it gives about +20 dB at
/// full volume, its collector sits between 4 V and 6 V, and the volume knob works from silence to full.
/// </summary>
public class CleanBoostTests
{
    static readonly string Netlist = File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "library", "clean-boost.cir"));

    static readonly string Output = NetlistLoader.Load(Netlist).Directives.Output!;

    // NOTE: The library circuit ships with the knob at its middle position. A test sets the volume by rewriting that line,
    // which is how the editor and the clip player set a knob.
    static LoadedCircuit At(double volume)
    {
        var text = Regex.Replace(Netlist, @"(?m)^\* ssp:knob RV1 \S+ .*$",
            $"* ssp:knob RV1 linear {volume.ToString("R", CultureInfo.InvariantCulture)}");
        var circuit = NetlistLoader.Load(text);
        Assert.Empty(circuit.Diagnostics);
        Assert.Empty(Pot.Apply(circuit));
        return circuit;
    }

    // A ten-points-a-decade sweep from 10 Hz puts 1 kHz at index 20.
    static readonly DecadeSweep Sweep = new(10, 100_000, 10);

    const int OneKilohertz = 20;

    static IReadOnlyList<double> Response(LoadedCircuit circuit) =>
        Analyses.FrequencyResponse(circuit, Sweep).MagnitudeDb[Output];

    [Fact]
    public void TheSweepHasOneKilohertzInIt()
    {
        Assert.Equal(1_000.0, Sweep.Frequencies()[OneKilohertz], 6);
    }

    [Fact]
    public void GainAtOneKilohertzWithTheVolumeAtOneIsAboutTwentyDecibels()
    {
        var db = Response(At(1));

        Assert.InRange(db[OneKilohertz], 15.0, 25.0);
    }

    [Fact]
    public void TheCollectorSitsMidRail()
    {
        var op = Analyses.OperatingPoint(At(1));

        Assert.InRange(op.NodeVoltages["c"], 4.0, 6.0);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void TheCircuitSolvesAtEachKnobPosition(double volume)
    {
        var circuit = At(volume);

        var op = Analyses.OperatingPoint(circuit);
        Assert.All(op.NodeVoltages.Values, v => Assert.True(double.IsFinite(v), $"{volume}: a node voltage is not finite."));

        Assert.All(Response(circuit), db => Assert.True(double.IsFinite(db), $"{volume}: a gain is not finite."));
    }

    [Fact]
    public void EachKnobPositionChangesTheOutput()
    {
        var (off, half, full) = (Response(At(0))[OneKilohertz], Response(At(0.5))[OneKilohertz], Response(At(1))[OneKilohertz]);

        Assert.True(off < half - 3, $"The volume at 0 is {off} dB and at 0.5 is {half} dB.");
        Assert.True(half < full - 3, $"The volume at 0.5 is {half} dB and at 1 is {full} dB.");
    }
}
