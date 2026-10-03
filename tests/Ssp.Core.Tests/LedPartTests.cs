using System.Globalization;
using System.Text.RegularExpressions;
using SpiceSharp;
using SpiceSharp.Components;
using SpiceSharp.Simulations;
using Ssp.Core.Parts;
using PartsApi = Ssp.Core.Parts.Parts;

namespace Ssp.Core.Tests;

public class LedPartTests
{
    // Typical Vf in volts at 10 mA. These values are the fit points in models/led.lib.
    public static TheoryData<string, double> Colours => new()
    {
        { "LED-RED", 1.95 },
        { "LED-YELLOW", 2.10 },
        { "LED-GREEN", 2.15 },
        { "LED-BLUE", 3.00 },
        { "LED-WHITE", 3.10 },
    };

    static readonly string Lib = File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "led.lib"));

    static PartRow Row(string id) =>
        PartsApi.Load(Path.Combine(RepoPaths.Root, "models", "parts.toml")).Get(id)!;

    [Theory]
    [MemberData(nameof(Colours))]
    public void ForwardVoltageAtTenMilliampsMatchesDatasheet(string id, double vf)
    {
        var row = Row(id);
        Assert.Equal("led", row.Kind);
        var m = Regex.Match(Lib, $@"^\.model\s+{row.Model}\s+D\(Is=(\S+)\s+N=(\S+)\)", RegexOptions.Multiline);
        Assert.True(m.Success, $"no .model for {row.Model} in led.lib");
        var model = new DiodeModel(row.Model);
        model.Parameters.SaturationCurrent = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        model.Parameters.EmissionCoefficient = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var circuit = new Circuit(
            new CurrentSource("I1", "0", "a", 10e-3),
            model,
            new Diode("D1", "a", "0", row.Model));

        var op = new OP("op");
        var va = new RealVoltageExport(op, "a");
        var result = double.NaN;
        foreach (var _ in op.Run(circuit))
        {
            result = va.Value;
        }

        Assert.InRange(result, vf - 0.05, vf + 0.05);
    }

    [Fact]
    public void EveryColourHasARowWithASymbolAndAHeader()
    {
        foreach (var (id, _) in Colours.Select(d => ((string)d[0], (double)d[1])))
        {
            var row = Row(id);
            Assert.Equal("led", row.Symbol);
            Assert.False(string.IsNullOrWhiteSpace(row.Provenance));
        }

        foreach (var tag in new[] { "* provenance:", "* fit:", "* author:", "* license:" })
        {
            Assert.Contains(tag, Lib);
        }
    }
}
