using Ssp.Core.Import.LtSpice;
using Ssp.Core.Layout;
using Ssp.Core.Netlist;
using SpiceSharp.Components;
using Xunit;
using LayoutDoc = Ssp.Core.Layout.Layout;

namespace Ssp.Core.Tests;

public class AscLayoutTests
{
    static readonly string Asc = Fixtures.Read("clipper-ltspice.asc");

    static LayoutDoc RoundTrip(LayoutDoc layout)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ssp-{Guid.NewGuid():N}.layout.toml");
        try
        {
            LayoutDoc.Write(path, layout);
            return LayoutDoc.Read(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    static PartPlacement Place(string asc) => Assert.Single(AscImporter.ToLayout(asc).Parts);

    [Fact]
    public void FixtureLayoutReadsBackEqual()
    {
        var layout = AscImporter.ToLayout(Asc);

        Assert.Equal(layout, RoundTrip(layout));
    }

    [Fact]
    public void EachNetlistPartHasOneLayoutEntry()
    {
        var circuit = NetlistLoader.Load(AscImporter.ToNetlist(Asc));
        var layout = RoundTrip(AscImporter.ToLayout(Asc));

        Assert.Equal(
            circuit.Circuit.OfType<IComponent>().Select(c => c.Name).Order(StringComparer.OrdinalIgnoreCase),
            layout.Parts.Select(p => p.Reference).Order(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void LayoutValidatesAgainstNetlist()
    {
        var circuit = NetlistLoader.Load(AscImporter.ToNetlist(Asc));
        var layout = RoundTrip(AscImporter.ToLayout(Asc));

        Assert.Empty(layout.Validate(circuit));
    }

    [Fact]
    public void FixturePlacementsEqualExpected()
    {
        // NOTE: Derived by hand. Each two-pin part is placed at its first LTspice pin.
        // R1 res (208,80) R90: pin A (16,16) -> (-16,16) -> (192,96). D2 diode (432,176) R180: pin + (16,0) -> (416,176).
        PartPlacement[] expected =
        [
            new("R1", 192, 96, 180, false),
            new("R2", 80, 96, 180, false),
            new("C1", 240, 112, 270, false),
            new("D1", 336, 112, 270, false),
            new("D2", 416, 176, 90, false),
        ];

        Assert.Equal(expected, AscImporter.ToLayout(Asc).Parts);
    }

    // NOTE: An LTspice diode at R0 points down (anode at top). The ssp diode at rotation 0 points right.
    // LTspice turns clockwise and mirrors x; the layout turns counter-clockwise and flips y before it turns.
    [Theory]
    [InlineData("R0", 270, false)]
    [InlineData("R90", 180, false)]
    [InlineData("R180", 90, false)]
    [InlineData("R270", 0, false)]
    [InlineData("M0", 270, true)]
    [InlineData("M90", 180, true)]
    [InlineData("M180", 90, true)]
    [InlineData("M270", 0, true)]
    public void DiodeOrientationMapsToLayoutRotation(string orientation, int rotation, bool flip)
    {
        var p = Place($"Version 4\nSYMBOL diode 0 0 {orientation}\nSYMATTR InstName D1\n");

        Assert.Equal((rotation, flip), (p.Rotation, p.Flip));
    }

    // NOTE: The LTspice and ssp op-amps both have the inputs at left and the output at right.
    [Theory]
    [InlineData("R0", 0, false)]
    [InlineData("R90", 270, false)]
    [InlineData("R180", 180, false)]
    [InlineData("R270", 90, false)]
    [InlineData("M0", 180, true)]
    [InlineData("M90", 90, true)]
    [InlineData("M180", 0, true)]
    [InlineData("M270", 270, true)]
    public void OpAmpOrientationMapsToLayoutRotation(string orientation, int rotation, bool flip)
    {
        var p = Place($"Version 4\nSYMBOL opamp 0 0 {orientation}\nSYMATTR InstName U1\n");

        Assert.Equal((rotation, flip), (p.Rotation, p.Flip));
    }

    [Fact]
    public void OpAmpIsPlacedBetweenItsInputs()
    {
        // NOTE: opamp pins invin (-32,48) and noninvin (-32,80). The ssp op-amp origin is between its inputs.
        var p = Place("Version 4\nSYMBOL opamp 100 200 R0\nSYMATTR InstName U1\n");

        Assert.Equal(new PartPlacement("XU1", 68, 264, 0, false), p);
    }

    [Fact]
    public void SymbolThatIsNotImportedHasNoLayoutEntry()
    {
        var asc = "Version 4\nSYMBOL voltage 16 80 R0\nSYMATTR InstName V1\nSYMBOL res 0 0 R7\nSYMATTR InstName R1\nSYMBOL cap 0 0 R0\n";

        Assert.Empty(AscImporter.ToLayout(asc).Parts);
    }
}
