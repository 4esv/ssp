using System.Globalization;
using Ssp.Core.Layout;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Schematic;
using SpiceSharp.Components;
using LayoutDoc = Ssp.Core.Layout.Layout;
using Point = Ssp.Core.Layout.Point;
using PartsApi = Ssp.Core.Parts.Parts;

namespace Ssp.Web.Tests;

public class AutoPlacerTests
{
    static readonly PartsTable Table = PartsApi.Load(Path.Combine(RepoPaths.Root, "models", "parts.toml"));

    static string Fixture(string name) => Path.Combine(RepoPaths.Root, "circuits", "fixtures", name);

    // NOTE: The .asc fixture is an LTspice file, not a netlist. LTspice import is not in the scope of #35.
    public static TheoryData<string> Netlists()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.GetFiles(Path.Combine(RepoPaths.Root, "circuits", "fixtures"), "*.cir").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(path));
        }
        return data;
    }

    static (LoadedCircuit Circuit, LayoutDoc Layout) Place(string fixture)
    {
        var circuit = NetlistLoader.Load(File.ReadAllText(Fixture(fixture)));
        return (circuit, AutoPlacer.Place(circuit, circuit.Directives));
    }

    readonly record struct Box(double X0, double Y0, double X1, double Y1)
    {
        public bool Overlaps(Box o) => X0 < o.X1 && o.X0 < X1 && Y0 < o.Y1 && o.Y0 < Y1;
    }

    // The box that the renderer draws for the part: the symbol view box, or the fallback box with its leads.
    static Box SymbolBox(PartRow? row, int pins, PartPlacement p)
    {
        Assert.Equal(0, p.Rotation);
        Assert.False(p.Flip);
        Symbol? symbol = null;
        if (row is not null)
        {
            try { symbol = Symbols.For(row.Kind); }
            catch (KeyNotFoundException) { }
        }
        if (symbol is null)
        {
            var bottom = 2 * Symbols.Grid * (Math.Max(1, (pins + 1) / 2) - 1) + Symbols.Grid;
            return new Box(p.X, p.Y - Symbols.Grid, p.X + 60, p.Y + bottom);
        }
        var at = symbol.Svg.IndexOf("viewBox=\"", StringComparison.Ordinal) + "viewBox=\"".Length;
        var v = symbol.Svg[at..symbol.Svg.IndexOf('"', at)].Split(' ').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        return new Box(p.X + v[0], p.Y + v[1], p.X + v[0] + v[2], p.Y + v[1] + v[3]);
    }

    [Theory]
    [MemberData(nameof(Netlists))]
    public void EveryPartIsPlacedOnce(string fixture)
    {
        var (circuit, layout) = Place(fixture);

        var names = circuit.Circuit.OfType<IComponent>().Select(c => c.Name).Order(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(names, layout.Parts.Select(p => p.Reference).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Empty(layout.Validate(circuit));
    }

    [Theory]
    [MemberData(nameof(Netlists))]
    public void SymbolsDoNotOverlap(string fixture)
    {
        var (circuit, layout) = Place(fixture);
        var parts = PartMap.Resolve(circuit, Table);
        var pins = circuit.Circuit.OfType<IComponent>().ToDictionary(c => c.Name, c => c.Nodes.Count, StringComparer.OrdinalIgnoreCase);

        var boxes = layout.Parts.Select(p => (p.Reference, Box: SymbolBox(parts.Parts.GetValueOrDefault(p.Reference), pins[p.Reference], p))).ToList();
        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                Assert.False(boxes[i].Box.Overlaps(boxes[j].Box), $"{boxes[i].Reference} overlaps {boxes[j].Reference}.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Netlists))]
    public void EveryWireSegmentIsOrthogonal(string fixture)
    {
        var (_, layout) = Place(fixture);

        foreach (var w in layout.Wires)
        {
            Assert.True(w.Points.Count >= 2, $"A wire on net '{w.Net}' has fewer than two points.");
            foreach (var (a, b) in w.Points.Zip(w.Points.Skip(1)))
            {
                Assert.True(a.X == b.X ^ a.Y == b.Y, $"Net '{w.Net}' has segment {a} to {b}, which is not orthogonal.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Netlists))]
    public void PlacementIsTheSameEachRun(string fixture)
    {
        Assert.Equal(Place(fixture).Layout, Place(fixture).Layout);
    }

    [Fact]
    public void EveryNetWithTwoPinsHasAWire()
    {
        var (_, layout) = Place("divider-basic.cir");

        Assert.Equal(["0", "in", "out"], layout.Wires.Select(w => w.Net).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void PartsGoFromInputToOutput()
    {
        var circuit = NetlistLoader.Load("* ssp:input in\n* ssp:output out\nR3 b out 1k\nR1 in a 1k\nR2 a b 1k\nV1 in 0 1\nR4 out 0 1k\n.END\n");
        var x = AutoPlacer.Place(circuit, circuit.Directives).Parts.ToDictionary(p => p.Reference, p => p.X);

        Assert.True(x["R1"] < x["R2"], "R1 is not left of R2.");
        Assert.True(x["R2"] < x["R3"], "R2 is not left of R3.");
        Assert.True(x["R3"] < x["R4"], "R3 is not left of R4.");
    }

    [Fact]
    public void ExistingLayoutIsNotChanged()
    {
        var path = Fixture("divider-basic.layout.toml");
        var before = File.ReadAllBytes(path);
        var layout = LayoutDoc.Read(path);

        Place("divider-basic.cir");

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(layout, LayoutDoc.Read(path));
    }
}
