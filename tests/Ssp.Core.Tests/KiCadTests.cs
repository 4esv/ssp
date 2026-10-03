using System.Text.RegularExpressions;
using Ssp.Core.Export;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using LayoutDoc = Ssp.Core.Layout.Layout;
using PartsApi = Ssp.Core.Parts.Parts;

namespace Ssp.Core.Tests;

public class KiCadTests
{
    static readonly PartsTable Table = PartsApi.Load(Path.Combine(RepoPaths.Root, "models", "parts.toml"));

    static string Export(string netlist, LayoutDoc layout)
    {
        var circuit = NetlistLoader.Load(netlist);
        return KiCad.ToSchematic(circuit, layout, PartMap.Resolve(circuit, Table));
    }

    static string ExportDivider() => Export(
        Fixtures.Read("divider-basic.cir"),
        LayoutDoc.Read(Path.Combine(Fixtures.Directory, "divider-basic.layout.toml")));

    [Fact]
    public void FixtureSchematicEqualsGolden()
    {
        Golden.Assert("kicad-divider", ExportDivider());
    }

    [Fact]
    public void EverySymbolHasAFootprintReference()
    {
        var text = ExportDivider();
        var instances = Regex.Matches(text, @"\(symbol\s+\(lib_id").Count;
        var footprints = Regex.Matches(text, @"\(property ""Footprint"" ""[^""]+""\s+\(at [^)]*\)\s+\(effects \(font \(size 1\.27 1\.27\)\) \(hide yes\)\)\)").Count;

        Assert.Equal(3, instances);
        Assert.Equal(instances, footprints);
    }

    [Fact]
    public void MappedPartUsesItsFootprint()
    {
        var text = Export("* ssp:part D1 1N4148\nV1 a 0 5\nD1 a b DGEN\nR1 b 0 1k\n.MODEL DGEN D\n.END\n",
            new LayoutDoc([], []));

        Assert.Contains("(property \"Footprint\" \"DO-35\"", text);
    }

    [Fact]
    public void OutputIsTheSameEachTime()
    {
        Assert.Equal(ExportDivider(), ExportDivider());
    }
}
