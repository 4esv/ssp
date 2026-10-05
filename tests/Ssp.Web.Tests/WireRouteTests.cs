using System.Globalization;
using System.Text.RegularExpressions;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Schematic;
using Xunit.Abstractions;
using PartsApi = Ssp.Core.Parts.Parts;
using Point = Ssp.Core.Layout.Point;

namespace Ssp.Web.Tests;

// NOTE: #175. The counts come from the SVG that the renderer writes: its polylines, and the outline of each placed part.
public partial class WireRouteTests(ITestOutputHelper output)
{
    static readonly PartsTable Table = PartsApi.Load(Path.Combine(RepoPaths.Root, "models", "parts.toml"));

    // The Transistor fuzz starter and the four gallery stages.
    public static TheoryData<string> Gallery()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.GetFiles(Path.Combine(RepoPaths.Root, "circuits", "library"), "*.cir").Order(StringComparer.Ordinal)) data.Add(Path.GetFileName(path));
        return data;
    }

    readonly record struct Segment(string Net, Point A, Point B);

    // The wire segments that go through the inside of a part, and the pairs of segments of two nets that meet.
    static (int Parts, int Wires, List<string> Notes) Count(string netlist)
    {
        var circuit = NetlistLoader.Load(netlist);
        var map = PartMap.Resolve(circuit, Table);
        var layout = AutoPlacer.Place(circuit, circuit.Directives, map);
        var svg = SchematicRenderer.ToSvg(circuit, layout, map);

        var segments = new List<Segment>();
        foreach (Match m in Polyline().Matches(svg))
        {
            var points = m.Groups[2].Value.Split(' ').Select(p => p.Split(',')).Select(p => new Point(double.Parse(p[0], CultureInfo.InvariantCulture), double.Parse(p[1], CultureInfo.InvariantCulture))).ToList();
            segments.AddRange(points.Zip(points.Skip(1)).Select(s => new Segment(m.Groups[1].Value, s.First, s.Second)));
        }

        var placements = layout.Parts.ToDictionary(p => p.Reference, StringComparer.OrdinalIgnoreCase);
        var boxes = SchematicRenderer.Elements(circuit, map).Where(e => e.Kind != "rail")
            .Select(e => (e.Reference, Placement: placements.GetValueOrDefault(e.Reference) ?? e.Members.Select(placements.GetValueOrDefault).First(p => p is not null)!))
            .Select(x => (x.Reference, Box: SchematicRenderer.Outline(SchematicRenderer.Elements(circuit, map).First(e => e.Reference == x.Reference), x.Placement)))
            .ToList();

        var notes = new List<string>();
        var parts = 0;
        foreach (var s in segments)
        {
            foreach (var (reference, (rawMin, rawMax)) in boxes)
            {
                // NOTE: The SVG has two decimals. A box edge at 19.999999999999996 is at 20 on the drawing.
                var (min, max) = (new Point(Math.Round(rawMin.X, 2), Math.Round(rawMin.Y, 2)), new Point(Math.Round(rawMax.X, 2), Math.Round(rawMax.Y, 2)));
                var (x0, x1, y0, y1) = (Math.Min(s.A.X, s.B.X), Math.Max(s.A.X, s.B.X), Math.Min(s.A.Y, s.B.Y), Math.Max(s.A.Y, s.B.Y));
                var crosses = x0 == x1
                    ? min.X < x0 && x0 < max.X && y0 < max.Y && min.Y < y1
                    : min.Y < y0 && y0 < max.Y && x0 < max.X && min.X < x1;
                if (!crosses) continue;
                parts++;
                notes.Add($"net {s.Net} {s.A}-{s.B} crosses {reference} {min}-{max}");
            }
        }

        var wires = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            for (var j = i + 1; j < segments.Count; j++)
            {
                var (p, q) = (segments[i], segments[j]);
                if (string.Equals(p.Net, q.Net, StringComparison.OrdinalIgnoreCase) || !Meet(p, q)) continue;
                wires++;
                notes.Add($"net {p.Net} {p.A}-{p.B} meets net {q.Net} {q.A}-{q.B}");
            }
        }
        return (parts, wires, notes);
    }

    // True if two orthogonal segments share a point.
    static bool Meet(Segment p, Segment q) =>
        Math.Max(Math.Min(p.A.X, p.B.X), Math.Min(q.A.X, q.B.X)) <= Math.Min(Math.Max(p.A.X, p.B.X), Math.Max(q.A.X, q.B.X))
        && Math.Max(Math.Min(p.A.Y, p.B.Y), Math.Min(q.A.Y, q.B.Y)) <= Math.Min(Math.Max(p.A.Y, p.B.Y), Math.Max(q.A.Y, q.B.Y));

    [Theory]
    [MemberData(nameof(Gallery))]
    public void NoRouteCrossesAPartOutline(string file)
    {
        var (parts, wires, notes) = Count(File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "library", file)));
        output.WriteLine($"{file}: {parts} part crossings, {wires} wire crossings");
        foreach (var note in notes) output.WriteLine(note);
        Assert.True(parts == 0, $"{file}: {parts} part crossings. {string.Join("; ", notes)}");
    }

    // NOTE: Before #175 the five circuits had 7 wire crossings. Two remain, each an op-amp feedback that has no way round.
    [Fact]
    public void TheGalleryHasTwoWireCrossingsAtMost()
    {
        var total = 0;
        foreach (var path in Directory.GetFiles(Path.Combine(RepoPaths.Root, "circuits", "library"), "*.cir")) total += Count(File.ReadAllText(path)).Wires;
        Assert.True(total <= 2, $"The gallery has {total} wire crossings.");
    }

    const string Two = """
        * two dividers
        V1 vcc 0 DC 9
        R1 vcc n1 10k
        R2 n1 0 10k
        R3 vcc n2 10k
        R4 n2 0 10k
        .END
        """;

    static Ssp.Core.Layout.Layout Layout(string netlist)
    {
        var circuit = NetlistLoader.Load(netlist);
        return AutoPlacer.Place(circuit, circuit.Directives, PartMap.Resolve(circuit, Table));
    }

    [Fact]
    public void TwoFlagsWithOneNameJoinIntoOneNet()
    {
        var layout = Layout(Two);
        var first = SchematicEdits.NameNet(Two, layout, "n1", "VREF");
        var second = SchematicEdits.NameNet(first.Netlist, first.Layout, "n2", "VREF");

        var circuit = NetlistLoader.Load(second.Netlist);
        var nodes = circuit.Circuit.OfType<SpiceSharp.Components.IComponent>().SelectMany(c => c.Nodes).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("VREF", nodes);
        Assert.DoesNotContain("n1", nodes);
        Assert.DoesNotContain("n2", nodes);
        Assert.Contains("R2 VREF 0 10k", second.Netlist);
        Assert.Contains("R4 VREF 0 10k", second.Netlist);
        // NOTE: The parts and the wires stay where they are. Only the net of a wire changes.
        Assert.Equal(layout.Parts, second.Layout.Parts);
        Assert.Equal(layout.Wires.Select(w => w.Points), second.Layout.Wires.Select(w => w.Points));
        Assert.All(second.Layout.Wires.Where(w => w.Net is "n1" or "n2"), _ => Assert.Fail("A wire keeps an old name."));
    }

    [Fact]
    public void TheSchematicDrawsTheNameOnTheNet()
    {
        var change = SchematicEdits.NameNet(Two, Layout(Two), "n1", "VREF");
        var circuit = NetlistLoader.Load(change.Netlist);
        var svg = SchematicRenderer.ToSvg(circuit, change.Layout, PartMap.Resolve(circuit, Table));

        Assert.Single(Regex.Matches(svg, "<text class=\"net-name\" data-net=\"VREF\"[^>]*>VREF</text>"));
        // NOTE: A node of the netlist, such as n2 or vcc, has no flag.
        Assert.DoesNotContain("data-net=\"n2\"[^>]*>n2<", svg);
        Assert.DoesNotContain(">n2</text>", svg);
    }

    [Theory]
    [InlineData("", "A net name needs a letter or a digit.")]
    [InlineData("a b", "A net name has no spaces.")]
    [InlineData("0", "A net cannot be named 0 or gnd: that is ground.")]
    [InlineData("x(1)", "A net name has no ( ) = , . or quote.")]
    public void ABadNameIsRefused(string name, string error)
    {
        Assert.False(SchematicEdits.TryNetName(name, out var why));
        Assert.Equal(error, why);
    }

    [Fact]
    public void ANameIsInCapitals()
    {
        var change = SchematicEdits.NameNet(Two, Layout(Two), "n1", "vref");
        Assert.Contains("R2 VREF 0 10k", change.Netlist);
    }

    [Fact]
    public void AGoodNameIsTaken()
    {
        Assert.True(SchematicEdits.TryNetName("+9V", out _));
        Assert.True(SchematicEdits.TryNetName("VREF", out _));
    }

    [GeneratedRegex("<polyline data-net=\"([^\"]*)\" points=\"([^\"]*)\"/>")]
    private static partial Regex Polyline();
}
