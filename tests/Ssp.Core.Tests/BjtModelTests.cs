using System.Globalization;
using System.Text.RegularExpressions;
using Ssp.Core.Analysis;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using PartsApi = Ssp.Core.Parts.Parts;

namespace Ssp.Core.Tests;

/// <summary>
/// The small-signal silicon NPNs in models/bjts.lib. Each test biases the model at the datasheet point
/// Ic = 1 mA, Vce = 5 V and checks the current gain and the base-emitter voltage against the sheet.
/// </summary>
public class BjtModelTests
{
    const double Ic = 1e-3;
    const double Vce = 5.0;

    // Ic = 1 mA, Vce = 5 V, 25 C.
    //
    // An NPN junction moves about 60 mV for each decade of current, so a datasheet figure at 10 mA
    // sits 60 mV above the same figure at 1 mA.
    //
    // 2N3904, onsemi 2N3904-D: hFE 70 min at IC = 1.0 mA; 300 max at IC = 10 mA.
    //   VBE(sat) 0.65 min and 0.85 max at IC = 10 mA, IB = 1 mA, so 0.59..0.79 at 1 mA.
    // 2N5088, onsemi 2N5088: hFE 350 min at IC = 1.0 mA; 900 max at IC = 0.1 mA (the sheet gives no
    //   max at 1 mA). VBE(on) 0.8 V max at IC = 10 mA, so 0.74 V max at 1 mA. The sheet gives no
    //   VBE(on) minimum, so the floor is the 0.59 V that the 2N3904 sheet allows at 1 mA.
    public static TheoryData<string, double, double, double, double> Models => new()
    {
        { "Q2N3904", 70, 300, 0.59, 0.79 },
        { "Q2N5088", 350, 900, 0.59, 0.74 },
    };

    static readonly string Lib = File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "bjts.lib"));

    static PartsTable Table => PartsApi.Load(Path.Combine(RepoPaths.Root, "models", "parts.toml"));

    /// <summary>A common-emitter fixture: Vce holds the collector 5 V above ground, Vbe drives the base.</summary>
    static LoadedCircuit CommonEmitter(string model, double vbe)
    {
        var netlist = Lib + "\n" + string.Create(
            CultureInfo.InvariantCulture,
            $"* Vce = {Vce} V, Vbe is the search variable.\nVce c 0 {Vce}\nVbe b 0 {vbe}\nQ1 c b 0 {model}\n.END\n");
        var loaded = NetlistLoader.Load(netlist);
        Assert.Empty(loaded.Diagnostics);
        return loaded;
    }

    // The SPICE sign of a source current is positive into the positive pin, so a source that feeds the
    // collector or the base reads negative.
    static double CollectorCurrent(string model, double vbe)
    {
        var op = Analyses.OperatingPoint(CommonEmitter(model, vbe));
        return -op.SourceCurrents["Vce"];
    }

    static (double Vbe, double Ic, double Ib) AtOneMilliamp(string model)
    {
        // Bisect Vbe until the collector current is Ic. The current rises with Vbe, so the search is a
        // plain bisection: the low guess has too little current, the high guess has too much.
        var low = 0.4;
        var high = 0.9;
        for (var i = 0; i < 60; i++)
        {
            var middle = (low + high) / 2;
            if (CollectorCurrent(model, middle) < Ic)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        var vbe = (low + high) / 2;
        var op = Analyses.OperatingPoint(CommonEmitter(model, vbe));
        return (vbe, -op.SourceCurrents["Vce"], -op.SourceCurrents["Vbe"]);
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void ModelMatchesTheDatasheetAtOneMilliamp(string model, double hfeMin, double hfeMax, double vbeMin, double vbeMax)
    {
        var (vbe, collector, baseCurrent) = AtOneMilliamp(model);

        Assert.Equal(Ic, collector, 9);
        Assert.InRange(collector / baseCurrent, hfeMin, hfeMax);
        Assert.InRange(vbe, vbeMin, vbeMax);
    }

    [Theory]
    [InlineData("2N3904", "npn", "Q2N3904")]
    [InlineData("2N5088", "npn", "Q2N5088")]
    [InlineData("1N914", "diode", "1N4148")]
    public void PartRowMapsThePartToItsModel(string id, string kind, string model)
    {
        var row = Table.Get(id);

        Assert.NotNull(row);
        Assert.Equal(kind, row.Kind);
        Assert.Equal(model, row.Model);
        Assert.False(string.IsNullOrWhiteSpace(row.Provenance));
        Assert.False(string.IsNullOrWhiteSpace(row.Footprint));
    }

    [Fact]
    public void LibraryHasAProvenanceHeaderAndBothModels()
    {
        foreach (var tag in new[] { "* provenance:", "* fit:", "* author:", "* license:" })
        {
            Assert.Contains(tag, Lib);
        }

        Assert.Matches(new Regex(@"^\.model\s+Q2N3904\s+NPN\b", RegexOptions.Multiline), Lib);
        Assert.Matches(new Regex(@"^\.model\s+Q2N5088\s+NPN\b", RegexOptions.Multiline), Lib);
    }

    [Fact]
    public void ProvenanceHeaderNamesTheDatasheetForEachPart()
    {
        var header = Lib.Split('\n').TakeWhile(line => line.StartsWith('*') || line.Length == 0).ToList();
        var provenance = header.First(line => line.StartsWith("* provenance:"));

        Assert.Contains("2N3904", provenance);
        Assert.Contains("2N5088", provenance);
        Assert.Contains("https://", provenance);
    }

    // onsemi 2N3904-D: Cobo 4.0 pF max (VCB = 5 V), Cibo 8.0 pF max (VEB = 0.5 V).
    // onsemi 2N5088: Ccb 4.0 pF max (VCB = 5 V), Ceb 10 pF max (VEB = 0.5 V).
    [Theory]
    [InlineData("Q2N3904", "4p", "8p")]
    [InlineData("Q2N5088", "4p", "10p")]
    public void ModelCarriesTheSheetCapacitances(string model, string cjc, string cje)
    {
        var line = Lib.Split('\n').First(l => l.StartsWith($".model {model} ", StringComparison.OrdinalIgnoreCase));

        Assert.Contains($"CJC={cjc}", line);
        Assert.Contains($"CJE={cje}", line);
    }
}
