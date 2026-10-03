using Ssp.Core.Netlist;
using Ssp.Core.Layout;
using LayoutDoc = Ssp.Core.Layout.Layout;

namespace Ssp.Core.Tests;

public class LayoutTests
{
    static LoadedCircuit Divider() => NetlistLoader.Load(Fixtures.Read("divider-basic.cir"));

    static string Temp() => Path.Combine(Path.GetTempPath(), $"ssp-{Guid.NewGuid():N}.layout.toml");

    static LayoutDoc Sample() => new(
        [new PartPlacement("V1", 0, 0, 90, false), new PartPlacement("R1", 12.5, -4, 0, true)],
        [new WireRoute("out", [new Point(1, 2), new Point(3.5, 2), new Point(3.5, 8)])]);

    [Fact]
    public void WrittenLayoutReadsBackEqual()
    {
        var path = Temp();
        try
        {
            var layout = Sample();
            LayoutDoc.Write(path, layout);
            Assert.Equal(layout, LayoutDoc.Read(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FixtureLayoutReadsAndMatchesItsNetlist()
    {
        var layout = LayoutDoc.Read(Path.Combine(Fixtures.Directory, "divider-basic.layout.toml"));

        Assert.NotEmpty(layout.Parts);
        Assert.Empty(layout.Validate(Divider()));
    }

    [Fact]
    public void ReferenceNotInNetlistGivesError()
    {
        var layout = new LayoutDoc([new PartPlacement("R9", 0, 0, 0, false)], []);

        var d = Assert.Single(layout.Validate(Divider()));
        Assert.Equal(Severity.Error, d.Severity);
        Assert.Contains("R9", d.Message);
    }

    [Fact]
    public void ReferenceMatchIgnoresCase()
    {
        var layout = new LayoutDoc([new PartPlacement("r1", 0, 0, 0, false)], []);

        Assert.Empty(layout.Validate(Divider()));
    }

    [Fact]
    public void WireOnUnknownNetGivesError()
    {
        var layout = new LayoutDoc([], [new WireRoute("nope", [new Point(0, 0), new Point(1, 0)])]);

        var d = Assert.Single(layout.Validate(Divider()));
        Assert.Equal(Severity.Error, d.Severity);
    }

    [Fact]
    public void RotationOtherThanQuarterTurnGivesError()
    {
        var layout = new LayoutDoc([new PartPlacement("R1", 0, 0, 45, false)], []);

        Assert.Equal(Severity.Error, Assert.Single(layout.Validate(Divider())).Severity);
    }

    [Fact]
    public void DuplicateReferenceGivesError()
    {
        var layout = new LayoutDoc([new PartPlacement("R1", 0, 0, 0, false), new PartPlacement("R1", 1, 1, 0, false)], []);

        Assert.Equal(Severity.Error, Assert.Single(layout.Validate(Divider())).Severity);
    }

    [Fact]
    public void InvalidTomlThrowsInvalidData()
    {
        var path = Temp();
        File.WriteAllText(path, "[[part]\nref =");
        try { Assert.Throws<InvalidDataException>(() => LayoutDoc.Read(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PartWithoutRefThrowsInvalidData()
    {
        var path = Temp();
        File.WriteAllText(path, "[[part]]\nx = 1\ny = 2\n");
        try { Assert.Throws<InvalidDataException>(() => LayoutDoc.Read(path)); }
        finally { File.Delete(path); }
    }
}
