using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Library;
using Ssp.Web.Schematic;
using Ssp.Core.Layout;
using LayoutDoc = Ssp.Core.Layout.Layout;
using Point = Ssp.Core.Layout.Point;

namespace Ssp.Web.Tests;

/// <summary>
/// #417: each circuit of the library and of the landing page draws a readable schematic. No part sits on a part, no
/// label sits on a label or on a part, and the signal goes from the input at the left to the output at the right.
/// </summary>
/// <remarks>
/// The gallery draws a preview with the parts table, and the editor places a circuit without one, so both placements are
/// drawn here. Two placements of the same netlist can differ: the parts table gives a part a better symbol.
/// </remarks>
public class LibraryDiagramTests
{
    // NOTE: The landing page shows the library circuits, and the editor opens with this one.
    const string Starter = "starter.cir";

    public static TheoryData<string, bool> Circuits()
    {
        var data = new TheoryData<string, bool>();
        foreach (var file in CircuitLibrary.All.Select(c => c.FileName).Append(Starter)) data.Add(file, false);
        foreach (var file in CircuitLibrary.All.Select(c => c.FileName).Append(Starter)) data.Add(file, true);
        return data;
    }

    static string Netlist(string file) => file == Starter
        ? File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", "rc-lowpass.cir"))
        : CircuitLibrary.All.Single(c => c.FileName == file).Netlist;

    static readonly PartsTable Table = Ssp.Core.Parts.Parts.Load(Path.Combine(RepoPaths.Root, "models", "parts.toml"));

    static readonly Dictionary<(string, bool), SchematicDrawing> Drawn = [];

    // A circuit as the gallery draws it (with the parts table) or as the editor places it (without one).
    static SchematicDrawing Drawing(string file, bool partsTable)
    {
        lock (Drawn)
        {
            if (Drawn.TryGetValue((file, partsTable), out var known)) return known;
            var circuit = NetlistLoader.Load(Netlist(file));
            var parts = PartMap.Resolve(circuit, Table);
            // NOTE: A library circuit with a hand layout is drawn from it, in the gallery and in the editor.
            var layout = Hand(file) is { } hand ? LayoutDoc.Parse(hand)
                : partsTable ? AutoPlacer.Place(circuit, circuit.Directives, parts) : AutoPlacer.Place(circuit, circuit.Directives);
            return Drawn[(file, partsTable)] = SchematicRenderer.Draw(circuit, layout, parts);
        }
    }

    static string? Hand(string file) => file == Starter ? null : CircuitLibrary.All.Single(c => c.FileName == file).Layout;

    public static TheoryData<string> HandLaidOut() => new(CircuitLibrary.All.Where(c => c.Layout is not null).Select(c => c.FileName));

    [Theory]
    [MemberData(nameof(HandLaidOut))]
    public void AHandLayoutIsValidAndPlacesEachPart(string file)
    {
        var circuit = NetlistLoader.Load(Netlist(file));
        var layout = LayoutDoc.Parse(Hand(file)!);
        Assert.DoesNotContain(layout.Validate(circuit), d => d.Severity == Severity.Error);
        var placed = layout.Parts.Select(p => p.Reference).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var e in SchematicRenderer.Elements(circuit, PartMap.Resolve(circuit, Table)))
        {
            Assert.True(placed.Contains(e.Reference) || e.Members.Any(placed.Contains), $"{file}: the layout does not place {e.Reference}.");
        }
    }

    [Fact]
    public void TheEditorFindsTheHandLayoutOfALibraryCircuitByItsNetlist()
    {
        var laid = CircuitLibrary.All.First(c => c.Layout is not null);
        Assert.Equal(laid.Layout, CircuitLibrary.LayoutOf(laid.Netlist));
        Assert.Equal(laid.Layout, CircuitLibrary.LayoutOf(laid.Netlist.Replace("\n", "\r\n")));
        Assert.Null(CircuitLibrary.LayoutOf(laid.Netlist + "R99 a 0 1k\n"));
        Assert.All(CircuitLibrary.All, c => Assert.EndsWith(".cir", c.FileName));
    }

    static string Name(string file, bool partsTable) => partsTable ? $"{file} with the parts table" : file;

    [Theory]
    [MemberData(nameof(Circuits))]
    public void NoPartSitsOnAnotherPart(string file, bool partsTable)
    {
        var parts = Drawing(file, partsTable).Parts;

        for (var i = 0; i < parts.Count; i++)
        {
            for (var j = i + 1; j < parts.Count; j++)
            {
                Assert.False(parts[i].Box.Overlaps(parts[j].Box), $"{Name(file, partsTable)}: {parts[i].Reference} sits on {parts[j].Reference}.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Circuits))]
    public void NoTextSitsOnAnotherTextOrOnAPart(string file, bool partsTable)
    {
        var drawing = Drawing(file, partsTable);

        for (var i = 0; i < drawing.Text.Count; i++)
        {
            for (var j = i + 1; j < drawing.Text.Count; j++)
            {
                Assert.False(drawing.Text[i].Box.Overlaps(drawing.Text[j].Box), $"{Name(file, partsTable)}: '{drawing.Text[i].Text}' sits on '{drawing.Text[j].Text}'.");
            }
            foreach (var part in drawing.Parts)
            {
                Assert.False(drawing.Text[i].Box.Overlaps(part.Box), $"{Name(file, partsTable)}: '{drawing.Text[i].Text}' sits on {part.Reference}.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Circuits))]
    public void TheInputIsAtTheLeftAndTheOutputAtTheRight(string file, bool partsTable)
    {
        var drawing = Drawing(file, partsTable);
        var directives = NetlistLoader.Load(Netlist(file)).Directives;
        var parts = drawing.Parts.ToDictionary(p => p.Reference, StringComparer.OrdinalIgnoreCase);

        // NOTE: A circuit with no ssp:input is fed by its source, on the node that is not ground.
        var source = drawing.Parts.FirstOrDefault(p => p.Kind == "source")?.Reference;
        var input = directives.Input
            ?? drawing.Pins.Where(p => string.Equals(p.Reference, source, StringComparison.OrdinalIgnoreCase) && p.Net is not ("0" or "gnd")).Select(p => p.Net).FirstOrDefault();
        if (input is not null)
        {
            var fed = drawing.Pins.Where(p => string.Equals(p.Net, input, StringComparison.OrdinalIgnoreCase)).ToList();
            var left = parts.Values.Min(p => p.Box.X0);
            Assert.True(fed.Any(p => parts[p.Reference].Box.X0 <= left + Tolerance), $"{Name(file, partsTable)}: the input is not at the left. {Describe(drawing)}");
        }

        if (directives.Output is not { } output) return;
        var leaves = drawing.Pins.Where(p => string.Equals(p.Net, output, StringComparison.OrdinalIgnoreCase)).ToList();
        var right = parts.Values.Max(p => p.Box.X1);
        Assert.True(leaves.Any(p => parts[p.Reference].Box.X1 >= right - Tolerance), $"{Name(file, partsTable)}: the output is not at the right. {Describe(drawing)}");
    }

    // #417 review: the boxes alone pass a drawing whose wires lie. The checks below read the wires of the drawing.

    [Theory]
    [MemberData(nameof(Circuits))]
    public void NoWireRunsThroughAPartOrASymbol(string file, bool partsTable)
    {
        var drawing = Drawing(file, partsTable);
        foreach (var (net, a, b) in Segments(drawing))
        {
            foreach (var part in drawing.Parts)
            {
                // NOTE: A wire may end on a pin of the part, at the edge of its box. A wire that runs close beside a part looks
                // like a lead of it (#417: the n10 wire two units from R4), so a wire keeps half a grid clear of the box.
                var own = drawing.Pins.Any(p => Same(p.Reference, part.Reference) && (p.At == a || p.At == b));
                var room = new SchematicBox(part.Box.X0 - Clearance, part.Box.Y0 - Clearance, part.Box.X1 + Clearance, part.Box.Y1 + Clearance);
                Assert.False(!own && Crosses(a, b, room), $"{Name(file, partsTable)}: the wire of {net} from {a} to {b} runs through or close beside {part.Reference}.");
            }
            foreach (var mark in drawing.Marks)
            {
                Assert.False(Crosses(a, b, mark), $"{Name(file, partsTable)}: the wire of {net} from {a} to {b} runs through a ground or supply symbol at {mark}.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Circuits))]
    public void NoTextSitsOnAWireOrASymbol(string file, bool partsTable)
    {
        var drawing = Drawing(file, partsTable);
        foreach (var text in drawing.Text)
        {
            foreach (var (net, a, b) in Segments(drawing))
            {
                Assert.False(Crosses(a, b, text.Box), $"{Name(file, partsTable)}: '{text.Text}' sits on the wire of {net} from {a} to {b}.");
            }
            foreach (var mark in drawing.Marks)
            {
                Assert.False(text.Box.Overlaps(mark), $"{Name(file, partsTable)}: '{text.Text}' sits on a ground or supply symbol at {mark}.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Circuits))]
    public void AWireTouchesNoWireOrPinOfAnotherNet(string file, bool partsTable)
    {
        var drawing = Drawing(file, partsTable);
        var segments = Segments(drawing);
        foreach (var (net, a, b) in segments)
        {
            foreach (var (other, c, d) in segments.Where(s => !Same(s.Net, net)))
            {
                Assert.False(Collinear(a, b, c, d), $"{Name(file, partsTable)}: the wire of {net} from {a} to {b} runs along the wire of {other} from {c} to {d}.");
                Assert.False(On(c, d, a) || On(c, d, b), $"{Name(file, partsTable)}: the wire of {net} from {a} to {b} ends on the wire of {other} from {c} to {d}.");
            }
            foreach (var pin in drawing.Pins.Where(p => !Same(p.Net, net)))
            {
                Assert.False(On(a, b, pin.At), $"{Name(file, partsTable)}: the wire of {net} from {a} to {b} touches pin {pin.Reference} of {pin.Net}.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Circuits))]
    public void EachWireEndIsOnAPinOrOnAWireOfItsNet(string file, bool partsTable)
    {
        var drawing = Drawing(file, partsTable);
        var segments = Segments(drawing);
        foreach (var wire in drawing.Wires)
        {
            foreach (var end in new[] { wire.Points[0], wire.Points[^1] })
            {
                var pin = drawing.Pins.Any(p => Same(p.Net, wire.Net) && p.At == end);
                var joined = drawing.Wires.Where(w => !ReferenceEquals(w, wire) && Same(w.Net, wire.Net))
                    .Any(w => w.Points.Zip(w.Points.Skip(1)).Any(s => On(s.First, s.Second, end)));
                Assert.True(pin || joined, $"{Name(file, partsTable)}: the wire of {wire.Net} ends at {end} on no pin and no wire of {wire.Net}.");
            }
        }
    }

    // NOTE: The drawing has no junction dots. A joint where a net leaves a point in four directions reads as two wires that
    // cross and do not join, so each joint is a T at most.
    [Theory]
    [MemberData(nameof(Circuits))]
    public void NoNetJoinsFourWaysAtOnePoint(string file, bool partsTable)
    {
        var drawing = Drawing(file, partsTable);
        var segments = Segments(drawing);
        var points = segments.SelectMany(s => new[] { (s.Net, At: s.A), (s.Net, At: s.B) }).Distinct().ToList();
        foreach (var (net, at) in points)
        {
            var arms = segments.Where(s => Same(s.Net, net) && On(s.A, s.B, at))
                .SelectMany(s => new[] { s.A, s.B }.Where(e => e != at).Select(e => (Math.Sign(Math.Round(e.X - at.X, 2)), Math.Sign(Math.Round(e.Y - at.Y, 2)))))
                .Distinct().Count();
            var pin = drawing.Pins.Any(p => Same(p.Net, net) && p.At == at) ? 1 : 0;
            Assert.True(arms + pin < 4, $"{Name(file, partsTable)}: {net} joins four ways at {at}, which reads as a crossing.");
        }
    }

    [Theory]
    [MemberData(nameof(Circuits))]
    public void TheWiresJoinEachPinOfANet(string file, bool partsTable)
    {
        var drawing = Drawing(file, partsTable);
        var circuit = NetlistLoader.Load(Netlist(file));
        var rails = SchematicRenderer.Elements(circuit, PartMap.Resolve(circuit, Table)).Where(e => e.Kind == "rail").Select(e => e.Nodes[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var net in drawing.Pins.Select(p => p.Net).Distinct(StringComparer.OrdinalIgnoreCase).Where(n => n is not ("0" or "gnd") && !rails.Contains(n)))
        {
            var pins = drawing.Pins.Where(p => Same(p.Net, net)).ToList();
            var wires = drawing.Wires.Where(w => Same(w.Net, net)).ToList();
            // NOTE: Items 0..pins-1 are the pins, the rest the wires. Two items join if they share a point.
            var root = Enumerable.Range(0, pins.Count + wires.Count).ToArray();
            int Find(int i) => root[i] == i ? i : root[i] = Find(root[i]);
            bool PinOn(DrawnPin p, WireRoute w) => w.Points.Zip(w.Points.Skip(1)).Any(s => On(s.First, s.Second, p.At));
            bool Join(WireRoute v, WireRoute w) => v.Points.Any(q => w.Points.Zip(w.Points.Skip(1)).Any(s => On(s.First, s.Second, q)));
            for (var i = 0; i < pins.Count; i++)
            {
                for (var j = i + 1; j < pins.Count; j++) if (pins[i].At == pins[j].At) root[Find(i)] = Find(j);
                for (var k = 0; k < wires.Count; k++) if (PinOn(pins[i], wires[k])) root[Find(i)] = Find(pins.Count + k);
            }
            for (var k = 0; k < wires.Count; k++)
            {
                for (var l = k + 1; l < wires.Count; l++) if (Join(wires[k], wires[l]) || Join(wires[l], wires[k])) root[Find(pins.Count + k)] = Find(pins.Count + l);
            }
            var pieces = Enumerable.Range(0, pins.Count).GroupBy(Find).Select(g => string.Join(" ", g.Select(i => pins[i].Reference))).ToList();
            Assert.True(pieces.Count == 1, $"{Name(file, partsTable)}: the pins of {net} are not joined: {string.Join(" | ", pieces)}.");
        }
    }

    const double Clearance = Symbols.Grid / 2.0;

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static List<(string Net, Point A, Point B)> Segments(SchematicDrawing drawing) =>
        drawing.Wires.SelectMany(w => w.Points.Zip(w.Points.Skip(1)).Where(s => s.First != s.Second).Select(s => (w.Net, s.First, s.Second))).ToList();

    // True if the point is on the segment, ends included.
    static bool On(Point a, Point b, Point p) =>
        Math.Abs((b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X)) < Tolerance
        && p.X >= Math.Min(a.X, b.X) - Tolerance && p.X <= Math.Max(a.X, b.X) + Tolerance
        && p.Y >= Math.Min(a.Y, b.Y) - Tolerance && p.Y <= Math.Max(a.Y, b.Y) + Tolerance;

    // True if two segments lie on one line and share more than a point.
    static bool Collinear(Point a, Point b, Point c, Point d)
    {
        if (!On(a, b, c) && !On(a, b, d) && !On(c, d, a) && !On(c, d, b)) return false;
        var cross = (b.X - a.X) * (d.Y - c.Y) - (b.Y - a.Y) * (d.X - c.X);
        if (Math.Abs(cross) > Tolerance) return false;
        var horizontal = Math.Abs(a.Y - b.Y) < Tolerance;
        var (a0, a1) = horizontal ? (Math.Min(a.X, b.X), Math.Max(a.X, b.X)) : (Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y));
        var (c0, c1) = horizontal ? (Math.Min(c.X, d.X), Math.Max(c.X, d.X)) : (Math.Min(c.Y, d.Y), Math.Max(c.Y, d.Y));
        return Math.Min(a1, c1) - Math.Max(a0, c0) > Tolerance;
    }

    // True if the orthogonal segment goes through the inside of the box. A segment along an edge, or ending on it, does not.
    static bool Crosses(Point a, Point b, SchematicBox box)
    {
        var (x0, x1, y0, y1) = (Math.Min(a.X, b.X), Math.Max(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y));
        var across = x0 == x1 ? box.X0 + Tolerance < x0 && x0 < box.X1 - Tolerance : box.X0 + Tolerance < x1 && x0 < box.X1 - Tolerance;
        var down = y0 == y1 ? box.Y0 + Tolerance < y0 && y0 < box.Y1 - Tolerance : box.Y0 + Tolerance < y1 && y0 < box.Y1 - Tolerance;
        return across && down;
    }

    // NOTE: A drawn coordinate is rounded to two places, so an edge that two parts share is equal up to that.
    const double Tolerance = 0.01;

    static string Describe(SchematicDrawing drawing) =>
        string.Join(", ", drawing.Parts.OrderBy(p => p.Box.X0).Select(p => $"{p.Reference} {p.Box.X0}..{p.Box.X1}"));
}
