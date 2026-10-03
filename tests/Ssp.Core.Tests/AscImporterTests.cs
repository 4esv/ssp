using Ssp.Core.Import.LtSpice;
using Ssp.Core.Netlist;
using Xunit;

namespace Ssp.Core.Tests;

public class AscImporterTests
{
    // NOTE: Derived by hand from the fixture geometry and PinTable.Builtin.
    // R1 (R90) and R2 (R90) meet on a wire with no flag, so that net is N001.
    // D1 cathode lands in the middle of the ground wire. D2 is rotated R180.
    const string Expected =
        "* Imported from LTspice\n" +
        "R1 out N001 10k\n" +
        "R2 N001 in 1k\n" +
        "C1 out 0 10n\n" +
        "D1 out 0 D1N4148\n" +
        "D2 0 out D1N4148\n" +
        ".model D1N4148 D(Is=2.52n Rs=0.568 N=1.752 Cjo=4p M=0.4 tt=20n)\n" +
        ".end\n";

    [Fact]
    public void FixtureNetlistEqualsExpected()
    {
        Assert.Equal(Expected, AscImporter.ToNetlist(Fixtures.Read("clipper-ltspice.asc")));
    }

    [Fact]
    public void FixtureNetlistLoadsWithNoErrorDiagnostic()
    {
        var loaded = NetlistLoader.Load(AscImporter.ToNetlist(Fixtures.Read("clipper-ltspice.asc")));

        Assert.DoesNotContain(loaded.Diagnostics, d => d.Severity == Severity.Error);
        Assert.Equal(["0", "in", "N001", "out"], loaded.NodeNames.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void FixtureImportHasNoDiagnostic()
    {
        Assert.Empty(AscImporter.Import(Fixtures.Read("clipper-ltspice.asc")).Diagnostics);
    }

    [Fact]
    public void UnknownSymbolGivesDiagnosticThatNamesIt()
    {
        var asc = "Version 4\nSHEET 1 880 680\nFLAG 16 96 0\nSYMBOL voltage 16 80 R0\nSYMATTR InstName V1\nSYMATTR Value 9\n";

        var result = AscImporter.Import(asc);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(Severity.Error, diagnostic.Severity);
        Assert.Contains("voltage", diagnostic.Message);
        Assert.Contains("V1", diagnostic.Message);
        Assert.Equal(4, diagnostic.Line);
        Assert.DoesNotContain("V1", result.Netlist);
    }

    [Fact]
    public void MirroredSymbolPinsAreMirrored()
    {
        // NOTE: npn M0 at (0,0): C (64,0) -> (-64,0), B (0,48) -> (0,48), E (64,96) -> (-64,96).
        var asc = "Version 4\nFLAG -64 0 c\nFLAG 0 48 b\nFLAG -64 96 0\nSYMBOL npn 0 0 M0\nSYMATTR InstName Q1\nSYMATTR Value 2N3904\n";

        Assert.Contains("Q1 c b 0 2N3904\n", AscImporter.ToNetlist(asc));
    }
}
