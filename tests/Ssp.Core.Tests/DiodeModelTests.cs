using System.Globalization;
using Ssp.Core.Analysis;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using PartsApi = Ssp.Core.Parts.Parts;

namespace Ssp.Core.Tests;

/// <summary>
/// The 1N4148 in models/diodes-si.lib. The 1N914 part row uses the same model.
/// Sheet: Vishay 1N4148, document 81857, Rev. 1.6: VF 1 V max at IF = 10 mA; IR 25 nA max at VR = 20 V
/// and 5 uA max at VR = 75 V; CD 4 pF max at VR = 0.
/// </summary>
public class DiodeModelTests
{
    static readonly string Lib = File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "diodes-si.lib"));

    static PartsTable Table => PartsApi.Load(Path.Combine(RepoPaths.Root, "models", "parts.toml"));

    // A current source drives the diode forward; the node voltage is VF.
    static LoadedCircuit Forward(string part, double current) => Load(string.Create(
        CultureInfo.InvariantCulture,
        $"* ssp:part D1 {part}\nI1 0 a {current}\nD1 a 0 1N4148\n.END\n"));

    static LoadedCircuit Reverse(string part, double volts) => Load(string.Create(
        CultureInfo.InvariantCulture,
        $"* ssp:part D1 {part}\nV1 0 a {volts}\nD1 a 0 1N4148\n.END\n"));

    static LoadedCircuit Load(string body)
    {
        var loaded = NetlistLoader.Load(Lib + "\n" + body);
        Assert.Empty(loaded.Diagnostics);
        return loaded;
    }

    [Theory]
    [InlineData("1N914")]
    [InlineData("1N4148")]
    public void PartResolvesToAModelThatLoads(string part)
    {
        var row = Table.Get(part);

        Assert.NotNull(row);
        Assert.Matches($@"(?im)^\.model\s+{row.Model}\s+D\b", Lib);
    }

    [Theory]
    [InlineData("1N914")]
    [InlineData("1N4148")]
    public void ForwardVoltageAtTenMilliampsIsBelowTheSheetMaximum(string part)
    {
        var op = Analyses.OperatingPoint(Forward(part, 10e-3));

        var vf = op.NodeVoltages["a"];
        Assert.InRange(vf, 0.6, 1.0);
    }

    [Fact]
    public void ReverseCurrentIsWithinTheSheetLimits()
    {
        var at20 = -Analyses.OperatingPoint(Reverse("1N914", 20)).SourceCurrents["V1"];
        var at75 = -Analyses.OperatingPoint(Reverse("1N914", 75)).SourceCurrents["V1"];

        Assert.InRange(Math.Abs(at20), 0, 25e-9);
        Assert.InRange(Math.Abs(at75), 0, 5e-6);
    }

    [Fact]
    public void LibraryHasAProvenanceHeaderNamingTheSheet()
    {
        foreach (var tag in new[] { "* provenance:", "* fit:", "* author:", "* license:" })
        {
            Assert.Contains(tag, Lib);
        }

        Assert.Contains("https://", Lib);
        Assert.Contains("CJO=4p", Lib.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
    }
}
