using Ssp.Core.Export;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using PartsApi = Ssp.Core.Parts.Parts;

namespace Ssp.Core.Tests;

public class BomTests
{
    static readonly PartsTable Table = PartsApi.Load(Path.Combine(RepoPaths.Root, "models", "parts.toml"));

    const string Netlist = "* ssp:part D1 1N4148\nV1 a 0 5\nD1 a b DGEN\nR1 b 0 1k\n.MODEL DGEN D\n.END\n";

    [Fact]
    public void FixtureBomEqualsGoldenCsv()
    {
        var csv = Bom.ToCsv(PartMap.Resolve(NetlistLoader.Load(Netlist), Table));

        Golden.Assert("bom-mixed", csv);
    }

    [Fact]
    public void UnmappedReferenceHasEmptyPartColumn()
    {
        var csv = Bom.ToCsv(PartMap.Resolve(NetlistLoader.Load(Netlist), Table));

        Assert.Contains("\nR1,,", csv);
    }

    [Fact]
    public void ValueWithCommaIsQuoted()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ssp-{Guid.NewGuid():N}.toml");
        try
        {
            File.WriteAllText(path, """
                [[part]]
                id = "X1"
                kind = "diode"
                model = "DX"
                symbol = "diode"
                footprint = "DO-35, axial"
                buy_url = "https://example.com/?q={id}"
                provenance = "test"
                """);
            var map = PartMap.Resolve(NetlistLoader.Load("V1 a 0 5\nD1 a b DX\nR1 b 0 1k\n.MODEL DX D\n.END\n"), PartsApi.Load(path));

            Assert.Contains("\"DO-35, axial\"", Bom.ToCsv(map));
        }
        finally { File.Delete(path); }
    }
}
