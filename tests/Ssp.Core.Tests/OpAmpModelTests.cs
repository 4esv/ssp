using System.Globalization;
using SpiceSharp;
using SpiceSharp.Simulations;
using Ssp.Core.Analysis;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;

namespace Ssp.Core.Tests;

public class OpAmpModelTests
{
    const double Rail = 15.0;

    public static TheoryData<string, string> Sets => new()
    {
        { "TL072", "opamp-tl072.cir" },
        { "LM308", "opamp-lm308.cir" },
        { "JRC4558", "opamp-jrc4558.cir" },
    };

    static OpAmpModel Set(string name) => name switch
    {
        "TL072" => OpAmpModel.Tl072,
        "LM308" => OpAmpModel.Lm308,
        "JRC4558" => OpAmpModel.Jrc4558,
        _ => throw new ArgumentException(name),
    };

    static string ModelText(string file) => File.ReadAllText(Path.Combine(RepoPaths.Root, "models", file));

    // The buffer fixture drives the input with `DC 1`. `dc` replaces that value.
    static LoadedCircuit Buffer(string name, string file, double dc)
    {
        var netlist = ModelText(file) + "\n" + Fixtures.Read("opamp-buffer.cir")
            .Replace("OPAMP", name)
            .Replace("DC 1 ", $"DC {dc.ToString("G", CultureInfo.InvariantCulture)} ");
        var loaded = NetlistLoader.Load(netlist);
        Assert.Empty(loaded.Diagnostics);
        return loaded;
    }

    [Theory]
    [MemberData(nameof(Sets))]
    public void BufferHasUnityGainAt1kHz(string name, string file)
    {
        var loaded = Buffer(name, file, 0);

        var result = Analyses.FrequencyResponse(loaded, new DecadeSweep(1000, 1000, 1));

        Assert.InRange(result.MagnitudeDb["out"][0], -0.1, 0.1);
    }

    [Theory]
    [MemberData(nameof(Sets))]
    public void OutputClampsAtRailMinusDrop(string name, string file)
    {
        var model = Set(name);

        var high = Analyses.OperatingPoint(Buffer(name, file, Rail + 5));
        var low = Analyses.OperatingPoint(Buffer(name, file, -Rail - 5));

        Assert.Equal(Rail - model.RailDrop, high.NodeVoltages["out"], 2);
        Assert.Equal(-Rail + model.RailDrop, low.NodeVoltages["out"], 2);
    }

    [Fact]
    public void Tl072ClampsAt13V5()
    {
        Assert.Equal(1.5, OpAmpModel.Tl072.RailDrop);
        var op = Analyses.OperatingPoint(Buffer("TL072", "opamp-tl072.cir", 20));

        Assert.Equal(13.5, op.NodeVoltages["out"], 2);
    }

    [Fact]
    public void BufferFollowsAnInputInsideTheRails()
    {
        var op = Analyses.OperatingPoint(Buffer("TL072", "opamp-tl072.cir", 3));

        Assert.Equal(3.0, op.NodeVoltages["out"], 3);
    }

    // Drives a 0 to 5 V, 10 kHz square wave into the buffer. Returns (time, output) samples.
    static List<(double T, double V)> Square(string name, string file)
    {
        var netlist = ModelText(file) + "\n" + Fixtures.Read("opamp-buffer.cir")
            .Replace("OPAMP", name)
            .Replace("DC 1 AC 1", "PULSE(0 5 10u 1n 1n 50u 100u)");
        var loaded = NetlistLoader.Load(netlist);
        Assert.Empty(loaded.Diagnostics);

        var tran = new Transient("tran", 5e-9, 210e-6);
        var export = new RealVoltageExport(tran, "out");
        var samples = new List<(double, double)>();
        foreach (var _ in tran.Run(loaded.Circuit, Transient.ExportTransient))
        {
            samples.Add((tran.Time, export.Value));
        }
        return samples;
    }

    [Theory]
    [MemberData(nameof(Sets))]
    public void SlewRateLimitsA10kHzSquareWave(string name, string file)
    {
        var slew = Set(name).SlewRate;

        var samples = Square(name, file);

        // The largest slope of the output is the slew rate. A 3 MHz amplifier without a limit would reach 94 V/us.
        var maxSlope = 0.0;
        for (var i = 1; i < samples.Count; i++)
        {
            var dt = samples[i].T - samples[i - 1].T;
            if (dt > 0)
            {
                maxSlope = Math.Max(maxSlope, Math.Abs(samples[i].V - samples[i - 1].V) / dt);
            }
        }
        Assert.InRange(maxSlope, 0.9 * slew, 1.1 * slew);

        // The 10% to 90% rise takes 0.8 * 5 V / SR.
        double Cross(double level) => samples.First(s => s.T > 10e-6 && s.V >= level).T;
        var rise = 0.8 * 5.0 / slew;
        Assert.InRange(Cross(4.5) - Cross(0.5), 0.9 * rise, 1.1 * rise);
    }

    [Theory]
    [MemberData(nameof(Sets))]
    public void ModelFileHasProvenanceHeaderAndMatchesTheRecord(string name, string file)
    {
        var lines = ModelText(file).Split('\n');

        Assert.StartsWith("* provenance:", lines[0]);
        Assert.StartsWith("* fit:", lines[1]);
        Assert.StartsWith("* author:", lines[2]);
        Assert.StartsWith("* license:", lines[3]);
        Assert.EndsWith(Set(name).ToSubcircuit(name) + "\n", ModelText(file).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ParameterSetsAreDistinctAndPositive()
    {
        var sets = new[] { OpAmpModel.Tl072, OpAmpModel.Lm308, OpAmpModel.Jrc4558 };

        Assert.Equal(3, sets.Distinct().Count());
        Assert.All(sets, s => Assert.True(s.Gain > 0 && s.Gbw > 0 && s.SlewRate > 0 && s.RailDrop > 0));
    }
}
