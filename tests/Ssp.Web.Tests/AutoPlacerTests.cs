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

    internal readonly record struct Box(double X0, double Y0, double X1, double Y1)
    {
        public bool Overlaps(Box o) => X0 < o.X1 && o.X0 < X1 && Y0 < o.Y1 && o.Y0 < Y1;
    }

    // NOTE: A subcircuit instance is one element, and its internals are not parts. A pot is placed by its first half.
    [Theory]
    [MemberData(nameof(Netlists))]
    public void EveryElementIsPlacedOnce(string fixture)
    {
        var (circuit, layout) = Place(fixture);

        var elements = SchematicRenderer.Elements(circuit, PartMap.Resolve(circuit, Table));
        var expected = elements.Select(e => e.Members.Count == 0 ? e.Reference : circuit.Circuit.Any(c => c.Name.Equals(e.Reference, StringComparison.OrdinalIgnoreCase)) ? e.Reference : e.Members.Order(StringComparer.OrdinalIgnoreCase).First());
        Assert.Equal(expected.Order(StringComparer.OrdinalIgnoreCase), layout.Parts.Select(p => p.Reference).Order(StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain(layout.Parts, p => p.Reference.Contains('.'));
        // NOTE: opamp-buffer.cir has no .subckt for OPAMP, so the layout names X1 and the netlist has no part X1.
        Assert.DoesNotContain(layout.Validate(circuit), d => !circuit.Subcircuits.Any(x => d.Message.Contains($"'{x.Name}'", StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(Netlists))]
    public void SymbolsDoNotOverlap(string fixture)
    {
        var (circuit, layout) = Place(fixture);
        var map = PartMap.Resolve(circuit, Table);
        var sheet = new Sheet(circuit, layout, SchematicRenderer.Elements(circuit, map), map);

        var boxes = sheet.Boxes().ToList();
        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                Assert.False(boxes[i].Value.Overlaps(boxes[j].Value), $"{boxes[i].Key.Reference} overlaps {boxes[j].Key.Reference}.");
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
    public void EveryNetWithTwoPinsHasAWireExceptGround()
    {
        var (_, layout) = Place("divider-basic.cir");

        // NOTE: Ground is a symbol at each pin, not a wire.
        Assert.Equal(["in", "out"], layout.Wires.Select(w => w.Net).Distinct().Order(StringComparer.Ordinal));
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

    // The five circuits of #151, as paths from circuits/.
    public static TheoryData<string> Pedal() => new()
    {
        "fixtures/rc-lowpass.cir", "fixtures/divider-basic.cir", "fixtures/bjt-ce-bias.cir", "fixtures/opamp-buffer.cir", "blocks/buffer-bootstrap-tl072.cir",
    };

    // NOTE: A wire is at most this long in schematic units. A rail or ground is a symbol at each pin, so no wire crosses the sheet.
    // The longest wire is the feedback of the op-amp, from its output round the symbol to its inverting input.
    const double MaxWire = 250;

    sealed record Sheet(LoadedCircuit Circuit, LayoutDoc Layout, IReadOnlyList<SchematicElement> Elements, PartMap Map)
    {
        public PartPlacement PlacementOf(SchematicElement e) =>
            Layout.Parts.FirstOrDefault(p => p.Reference.Equals(e.Reference, StringComparison.OrdinalIgnoreCase))
            ?? Layout.Parts.First(p => e.Members.Contains(p.Reference, StringComparer.OrdinalIgnoreCase));

        public IEnumerable<(SchematicElement Element, SchematicRenderer.ElementPin Pin, Box Box)> Pins() =>
            Elements.SelectMany(e =>
            {
                var placement = PlacementOf(e);
                var (min, max) = SchematicRenderer.Outline(e, placement);
                var box = new Box(min.X, min.Y, max.X, max.Y);
                return SchematicRenderer.Pins(e, placement).Select(pin => (e, pin, box));
            });

        public Dictionary<SchematicElement, Box> Boxes() => Pins().GroupBy(p => p.Element).ToDictionary(g => g.Key, g => g.First().Box);

        public IEnumerable<string> RailNets(bool positive) =>
            Elements.Where(e => e.Kind == "rail" && e.Value.StartsWith('-') != positive).Select(e => e.Nodes[0]);
    }

    static Sheet Sheet5(string path)
    {
        var circuit = NetlistLoader.Load(File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", path)));
        var map = PartMap.Resolve(circuit, Table);
        var layout = AutoPlacer.Place(circuit, circuit.Directives, map);
        return new Sheet(circuit, layout, SchematicRenderer.Elements(circuit, map), map);
    }

    static bool IsGround(string net) => net == "0" || net.Equals("gnd", StringComparison.OrdinalIgnoreCase);

    // The input and output nets: the directives, else the nets named in and out, else the base and the collector of the transistor.
    static (string Input, string Output) Ends(Sheet sheet)
    {
        var d = sheet.Circuit.Directives;
        if (d.Input is not null && d.Output is not null) return (d.Input, d.Output);
        var nets = sheet.Elements.SelectMany(e => e.Nodes).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (nets.Contains("in") && nets.Contains("out")) return ("in", "out");
        var q = sheet.Elements.Single(e => e.Kind == "npn");
        return (q.Nodes[1], q.Nodes[0]);
    }

    [Theory]
    [MemberData(nameof(Pedal))]
    public void InputIsLeftOfOutput(string path)
    {
        var sheet = Sheet5(path);
        var (input, output) = Ends(sheet);

        double LeftOf(string net) => sheet.Pins().Where(p => p.Pin.Net.Equals(net, StringComparison.OrdinalIgnoreCase)).Min(p => p.Pin.At.X);
        Assert.True(LeftOf(input) < LeftOf(output), $"{input} at x {LeftOf(input)} is not left of {output} at x {LeftOf(output)}.");
    }

    [Theory]
    [MemberData(nameof(Pedal))]
    public void SupplyIsAboveTheMiddleOfItsPartAndGroundIsBelow(string path)
    {
        var sheet = Sheet5(path);
        var boxes = sheet.Boxes();
        var supply = sheet.RailNets(positive: true).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var supplyY = new List<double>();
        var groundY = new List<double>();

        foreach (var (e, pin, box) in sheet.Pins().Where(p => p.Element.Kind != "rail" && !p.Pin.Hidden))
        {
            var middle = (box.Y0 + box.Y1) / 2;
            if (supply.Contains(pin.Net))
            {
                Assert.True(pin.At.Y < middle, $"{e.Reference}: supply pin {pin.Net} at y {pin.At.Y} is not above the middle y {middle}.");
                supplyY.Add(pin.At.Y);
            }
            if (IsGround(pin.Net))
            {
                Assert.True(pin.At.Y > middle, $"{e.Reference}: ground pin at y {pin.At.Y} is not below the middle y {middle}.");
                groundY.Add(pin.At.Y);
            }
        }
        Assert.NotEmpty(groundY);
        if (supplyY.Count > 0) Assert.True(supplyY.Max() < groundY.Min(), "A supply pin is not above every ground pin.");
        Assert.All(sheet.Elements.Where(e => e.Kind == "rail" && supply.Contains(e.Nodes[0])), e => Assert.False(sheet.PlacementOf(e).Flip));
    }

    [Theory]
    [MemberData(nameof(Pedal))]
    public void NoPartOverlapsAnother(string path)
    {
        var boxes = Sheet5(path).Boxes().ToList();

        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                Assert.False(boxes[i].Value.Overlaps(boxes[j].Value), $"{boxes[i].Key.Reference} overlaps {boxes[j].Key.Reference}.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Pedal))]
    public void NoWireIsLongerThanTheLimit(string path)
    {
        foreach (var w in Sheet5(path).Layout.Wires)
        {
            var length = w.Points.Zip(w.Points.Skip(1)).Sum(s => Math.Abs(s.First.X - s.Second.X) + Math.Abs(s.First.Y - s.Second.Y));
            Assert.True(length <= MaxWire, $"Wire on net '{w.Net}' is {length} long. The limit is {MaxWire}.");
        }
    }

    [Theory]
    [MemberData(nameof(Pedal))]
    public void WiresStartAndEndOnPinsOfTheirNet(string path)
    {
        var sheet = Sheet5(path);
        var pins = sheet.Pins().Where(p => !p.Pin.Hidden).Select(p => (Net: p.Pin.Net.ToLowerInvariant(), p.Pin.At)).ToHashSet();

        foreach (var w in sheet.Layout.Wires)
        {
            Assert.Contains((w.Net.ToLowerInvariant(), w.Points[0]), pins);
            Assert.Contains((w.Net.ToLowerInvariant(), w.Points[^1]), pins);
        }
    }

    [Theory]
    [MemberData(nameof(Pedal))]
    public void NoWireCrossesAPart(string path)
    {
        var sheet = Sheet5(path);
        var boxes = sheet.Boxes().Where(b => b.Key.Kind != "rail").ToList();

        foreach (var w in sheet.Layout.Wires)
        {
            foreach (var (a, b) in w.Points.Zip(w.Points.Skip(1)))
            {
                foreach (var (e, box) in boxes)
                {
                    var (x0, x1, y0, y1) = (Math.Min(a.X, b.X), Math.Max(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y));
                    var crosses = x0 < box.X1 && box.X0 < x1 && y0 < box.Y1 && box.Y0 < y1
                                  || (x0 == x1 && box.X0 < x0 && x0 < box.X1 && y0 < box.Y1 && box.Y0 < y1)
                                  || (y0 == y1 && box.Y0 < y0 && y0 < box.Y1 && x0 < box.X1 && box.X0 < x1);
                    Assert.False(crosses, $"A wire on net '{w.Net}' from {a} to {b} crosses {e.Reference}.");
                }
            }
        }
    }

    [Fact]
    public void BootstrapBufferShowsAnOpampAndNoInternals()
    {
        var sheet = Sheet5("blocks/buffer-bootstrap-tl072.cir");
        var svg = SchematicRenderer.ToSvg(sheet.Circuit, sheet.Layout, sheet.Map);

        Assert.Contains("M20 -30V30L60 0Z", svg);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(svg, "data-ref=\"X1\""));
        Assert.DoesNotContain("Bgm", svg);
        Assert.DoesNotContain("Bclamp", svg);
        Assert.DoesNotContain(sheet.Layout.Parts, p => p.Reference.Contains('.'));
        Golden.Assert("schematic-buffer-bootstrap-tl072.svg", svg);
    }
}
