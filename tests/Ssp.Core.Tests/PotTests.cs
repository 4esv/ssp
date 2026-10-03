using SpiceSharp.Components;
using Ssp.Core.Analysis;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;

namespace Ssp.Core.Tests;

public class PotTests
{
    private static LoadedCircuit Load(string taper, double pos) =>
        NetlistLoader.Load(Fixtures.Read("pot-lowpass.cir")
            .Replace("ssp:knob RV1 log 0.5", $"ssp:knob RV1 {taper} {pos.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));

    // Ratio of the wiper-to-ground resistor to the total resistance.
    private static double Ratio(LoadedCircuit c)
    {
        var upper = ((Resistor)c.Circuit["RV1_1"]).Parameters.Resistance;
        var lower = ((Resistor)c.Circuit["RV1_2"]).Parameters.Resistance;
        return lower / (upper + lower);
    }

    [Theory]
    [InlineData("linear", 0.5, 0.50)]
    [InlineData("log", 0.5, 0.10)]
    [InlineData("revlog", 0.5, 0.90)]
    [InlineData("linear", 0.2, 0.20)]
    [InlineData("log", 0.8, 0.476)]
    [InlineData("revlog", 0.2, 0.524)]
    public void TaperGivesTheResistanceRatio(string taper, double pos, double expected)
    {
        var circuit = Load(taper, pos);

        var diagnostics = Pot.Apply(circuit);

        Assert.Empty(diagnostics);
        Assert.InRange(Ratio(circuit), expected - 0.01, expected + 0.01);
    }

    [Fact]
    public void TotalResistanceIsKept()
    {
        var circuit = Load("log", 0.3);

        Pot.Apply(circuit);

        var total = ((Resistor)circuit.Circuit["RV1_1"]).Parameters.Resistance
            + ((Resistor)circuit.Circuit["RV1_2"]).Parameters.Resistance;
        Assert.Equal(10_000.0, total, 6);
    }

    [Fact]
    public void ChangingTheKnobChangesTheFrequencyResponse()
    {
        var sweep = new DecadeSweep(10, 100_000, 20);
        AcResult Run(double pos)
        {
            var c = Load("log", pos);
            Assert.Empty(Pot.Apply(c));
            return Analyses.FrequencyResponse(c, sweep);
        }

        var low = Run(0.3).MagnitudeDb["out"];
        var high = Run(0.7).MagnitudeDb["out"];

        Assert.Contains(low.Zip(high), p => Math.Abs(p.First - p.Second) > 1.0);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void PositionOutsideRangeGivesADiagnostic(double pos)
    {
        var circuit = Load("linear", pos);

        var diagnostics = Pot.Apply(circuit);

        var d = Assert.Single(diagnostics);
        Assert.Equal(Severity.Error, d.Severity);
        Assert.Contains("position", d.Message);
    }

    [Fact]
    public void UnknownTaperGivesADiagnostic()
    {
        var diagnostics = Pot.Apply(Load("audio", 0.5));

        Assert.Contains("taper", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void KnobWithoutResistorsGivesADiagnostic()
    {
        var circuit = NetlistLoader.Load("* ssp:knob RV9 linear 0.5\nR1 a 0 1k\n.END\n");

        Assert.Contains("RV9", Assert.Single(Pot.Apply(circuit)).Message);
    }
}
