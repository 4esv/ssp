using System.Globalization;
using System.Text.RegularExpressions;
using Ssp.Core.Analysis;
using Ssp.Core.Netlist;

namespace Ssp.Core.Tests;

/// <summary>
/// The MOSFETs in models/mosfets.lib in a common-source stage on 9 V. The tests check the operating point and the
/// small-signal gain against a hand calculation from the Level 1 square law with the model parameters, and check
/// each model against its datasheet.
/// </summary>
public class MosfetStageTests
{
    static readonly string Lib = File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "mosfets.lib"));

    // The model parameters, the same as the .model lines in models/mosfets.lib. W = L = 100u (the SpiceSharp
    // defaults), so beta = KP.
    const double Vto2N7000 = 1.96;
    const double Kp2N7000 = 98e-3;
    const double Lambda2N7000 = 0.0105;
    const double VtoBs250 = -2.06;
    const double KpBs250 = 56e-3;

    const double Vdd = 9.0;
    const double Rd = 4.7e3;

    // NMOS: a 1M / 300k divider holds the gate at 9 * 300k / 1.3M = 2.077 V. The source and the bulk are at ground.
    // PMOS: a 330k / 1M divider holds the gate at 9 * 1M / 1.33M = 6.767 V. The source and the bulk are at 9 V.
    // C1 couples the 1 V AC input to the gate. The corner is about 0.1 Hz, so 1 kHz is mid-band.
    const string NmosStage = """
        * ssp:title 2N7000 common-source stage
        * ssp:input in
        * ssp:output d
        VDD vdd 0 9
        VIN in 0 DC 0 AC 1
        C1 in g 10u
        R1 vdd g 1Meg
        R2 g 0 300k
        RD vdd d 4.7k
        M1 d g 0 0 M2N7000
        .END
        """;

    const string PmosStage = """
        * ssp:title BS250P common-source stage
        * ssp:input in
        * ssp:output d
        VDD vdd 0 9
        VIN in 0 DC 0 AC 1
        C1 in g 10u
        R1 vdd g 330k
        R2 g 0 1Meg
        RD d 0 4.7k
        M1 d g vdd vdd MBS250
        .END
        """;

    static LoadedCircuit Load(string stage)
    {
        var loaded = NetlistLoader.Load(Lib + "\n" + stage);
        Assert.Empty(loaded.Diagnostics);
        return loaded;
    }

    /// <summary>
    /// The hand calculation in saturation: Id = K * Vov^2 * (1 + lambda * |Vds|) with K = KP / 2. With |Vds| = Vdd - Id * Rd
    /// this is linear in Id: Id = A * (1 + lambda * Vdd) / (1 + A * lambda * Rd), A = K * Vov^2.
    /// The gain is gm * (Rd || ro), gm = 2 * Id / Vov, 1 / ro = lambda * A.
    /// </summary>
    static (double Id, double Vds, double Gain) Hand(double vsg, double vto, double kp, double lambda)
    {
        var vov = vsg - Math.Abs(vto);
        var a = kp / 2 * vov * vov;
        var id = a * (1 + lambda * Vdd) / (1 + a * lambda * Rd);
        var vds = Vdd - id * Rd;
        var gm = 2 * id / vov;
        var gds = lambda * a;
        var gain = gm * Rd / (1 + gds * Rd);
        Assert.True(vds > vov, "The hand calculation assumes saturation.");
        return (id, vds, gain);
    }

    static (double Id, double Vds, double Gain) HandNmos() => Hand(Vdd * 300e3 / 1.3e6, Vto2N7000, Kp2N7000, Lambda2N7000);

    static (double Id, double Vds, double Gain) HandPmos() => Hand(Vdd - (Vdd * 1e6 / 1.33e6), VtoBs250, KpBs250, 0);

    // Tolerance: 1 % on the drain voltage and on the gain. The hand calculation leaves out the bulk junction leakage
    // and the divider loading by the gate (both below 1e-9 of the drain current).
    [Fact]
    public void NmosStageSolvesToTheHandCalculation()
    {
        var circuit = Load(NmosStage);
        var (id, vds, gain) = HandNmos();

        var op = Analyses.OperatingPoint(circuit);
        Assert.Equal(vds, op.NodeVoltages["d"], vds * 0.01);
        Assert.Equal(id, (Vdd - op.NodeVoltages["d"]) / Rd, id * 0.01);

        var ac = Analyses.FrequencyResponse(circuit, new DecadeSweep(1000, 10000, 1));
        Assert.Equal(20 * Math.Log10(gain), ac.MagnitudeDb["d"][0], 0.09);
        Assert.Equal(180, Math.Abs(ac.PhaseDegrees["d"][0]), 1.0);
    }

    [Fact]
    public void PmosStageSolvesToTheHandCalculation()
    {
        var circuit = Load(PmosStage);
        var (id, vsd, gain) = HandPmos();

        var op = Analyses.OperatingPoint(circuit);
        var vd = Vdd - vsd;
        Assert.Equal(vd, op.NodeVoltages["d"], vd * 0.01);
        Assert.Equal(id, op.NodeVoltages["d"] / Rd, id * 0.01);

        var ac = Analyses.FrequencyResponse(circuit, new DecadeSweep(1000, 10000, 1));
        Assert.Equal(20 * Math.Log10(gain), ac.MagnitudeDb["d"][0], 0.09);
        Assert.Equal(180, Math.Abs(ac.PhaseDegrees["d"][0]), 1.0);
    }

    // A MOSFET dissipates |Vds| * Id (no gate current). SpiceSharp gives a MOSFET no "p" property, so the export
    // read 0 W before Analyses computed the power itself.
    [Theory]
    [InlineData("n")]
    [InlineData("p")]
    public void DevicePowerOfAMosfetIsVdsTimesId(string channel)
    {
        var op = Analyses.OperatingPoint(Load(channel == "n" ? NmosStage : PmosStage));
        var vd = op.NodeVoltages["d"];
        var (id, vds) = channel == "n" ? ((Vdd - vd) / Rd, vd) : (vd / Rd, Vdd - vd);

        Assert.Equal(vds * id, op.DevicePowers["M1"], vds * id * 0.01);
    }

    /// <summary>The drain current with Vgs and Vds held by sources. A P-channel takes negative voltages.</summary>
    static double DrainCurrent(string model, double vgs, double vds)
    {
        var netlist = Lib + "\n" + string.Create(
            CultureInfo.InvariantCulture,
            $"VDS d 0 {vds}\nVGS g 0 {vgs}\nM1 d g 0 0 {model}\n.END\n");
        var op = Analyses.OperatingPoint(NetlistLoader.Load(netlist));
        return Math.Abs(op.SourceCurrents["VDS"]);
    }

    /// <summary>The threshold as the sheet measures it: Vds = Vgs, Id = 1 mA. Bisect on |Vgs|.</summary>
    static double Threshold(string model, double sign)
    {
        var low = 0.0;
        var high = 5.0;
        for (var i = 0; i < 60; i++)
        {
            var middle = (low + high) / 2;
            if (DrainCurrent(model, sign * middle, sign * middle) < 1e-3)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return (low + high) / 2;
    }

    // Vishay 70226, 2N7000: VGS(th) 0.8 min, 2.1 typ, 3 max at VDS = VGS, ID = 1 mA.
    // Diodes DS33014, BS250P: VGS(th) -1 min, -3.5 max at VDS = VGS, ID = -1 mA (no typical).
    [Theory]
    [InlineData("M2N7000", 1.0, 0.8, 3.0)]
    [InlineData("MBS250", -1.0, 1.0, 3.5)]
    public void ThresholdIsInTheSheetWindow(string model, double sign, double min, double max)
    {
        Assert.InRange(Threshold(model, sign), min, max);
    }

    // Vishay 70226, 2N7000: ID(on) 0.35 A typ, 0.075 A min at VDS = 10 V, VGS = 4.5 V; gfs 100 mS at VDS = 10 V,
    // ID = 0.2 A (the Diodes/Zetex 2N7000 sheet gives the same 100 mS as a minimum).
    [Fact]
    public void Model2N7000MatchesTheSheetOnCurrentAndTransconductance()
    {
        Assert.Equal(0.35, DrainCurrent("M2N7000", 4.5, 10), 0.35 * 0.02);
        Assert.True(Transconductance("M2N7000", 1.0, 0.2) >= 0.1);
    }

    // Diodes DS33014, BS250P: gfs 150 mS typ at VDS = -10 V, ID = -200 mA.
    [Fact]
    public void ModelBs250MatchesTheSheetTransconductance()
    {
        Assert.Equal(0.15, Transconductance("MBS250", -1.0, 0.2), 0.15 * 0.05);
    }

    /// <summary>gfs at |Vds| = 10 V and the drain current: bisect Vgs to the current, then a central difference.</summary>
    static double Transconductance(string model, double sign, double current)
    {
        var low = 0.0;
        var high = 10.0;
        for (var i = 0; i < 60; i++)
        {
            var middle = (low + high) / 2;
            if (DrainCurrent(model, sign * middle, sign * 10) < current)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        var vgs = (low + high) / 2;
        const double step = 1e-3;
        return (DrainCurrent(model, sign * (vgs + step), sign * 10) - DrainCurrent(model, sign * (vgs - step), sign * 10)) / (2 * step);
    }

    [Fact]
    public void LibraryHasAProvenanceHeaderAndBothModels()
    {
        foreach (var tag in new[] { "* provenance:", "* fit:", "* author:", "* license:" })
        {
            Assert.Contains(tag, Lib);
        }

        var provenance = Lib.Split('\n').First(line => line.StartsWith("* provenance:", StringComparison.Ordinal));
        Assert.Contains("2N7000", provenance);
        Assert.Contains("BS250P", provenance);
        Assert.Contains("https://", provenance);

        Assert.Matches(new Regex(@"^\.model\s+M2N7000\s+NMOS\b", RegexOptions.Multiline), Lib);
        Assert.Matches(new Regex(@"^\.model\s+MBS250\s+PMOS\b", RegexOptions.Multiline), Lib);
    }

    // The test constants above are the model lines. A change to one without the other fails here.
    [Theory]
    [InlineData("M2N7000", "VTO=1.96", "KP=98m", "LAMBDA=0.0105")]
    [InlineData("MBS250", "VTO=-2.06", "KP=56m", null)]
    public void ModelLineCarriesTheTestedParameters(string model, string vto, string kp, string? lambda)
    {
        var line = Lib.Split('\n').First(l => l.StartsWith($".model {model} ", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(vto, line);
        Assert.Contains(kp, line);
        if (lambda is null)
        {
            Assert.DoesNotContain("LAMBDA", line);
        }
        else
        {
            Assert.Contains(lambda, line);
        }
    }
}
