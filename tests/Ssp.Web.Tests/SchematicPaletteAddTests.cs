using Bunit;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Components;
using Ssp.Web.Schematic;
using LayoutDoc = Ssp.Core.Layout.Layout;
using Point = Ssp.Core.Layout.Point;

namespace Ssp.Web.Tests;

// #338: a palette add puts each part at the centre of the view. Parts on the same spot shared pins, and the place joined
// their nets without a wire.
public class SchematicPaletteAddTests : BunitContext
{
    const string Empty = "* schematic\n.END\n";
    static readonly string[] Repro = ["source", "diode", "battery", "npn"];
    static readonly PartsTable Table = Ssp.Core.Parts.Parts.Parse(
        File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "parts.toml")), "parts.toml");

    static (string Netlist, LayoutDoc Layout) AddAtOnePoint(string netlist, LayoutDoc layout, IEnumerable<string> kinds)
    {
        // NOTE: The view does not move between the taps, so each part asks for the same centre.
        foreach (var kind in kinds)
        {
            var change = SchematicEdits.PlaceAt(netlist, layout, kind, new Point(100, 100));
            (netlist, layout) = (change.Netlist, change.Layout);
        }
        return (netlist, layout);
    }

    [Fact]
    public void Parts_added_at_one_point_share_no_node()
    {
        var (netlist, _) = AddAtOnePoint(Empty, new LayoutDoc([], []), Repro);

        var circuit = NetlistLoader.Load(netlist);
        var elements = SchematicRenderer.Elements(circuit, PartMap.Resolve(circuit, Table));
        Assert.Equal(4, elements.Count);
        // NOTE: Node 0 is ground. The battery minus and the transistor substrate are on it by design.
        var shared = elements.SelectMany(e => e.Nodes.Distinct().Where(n => n != "0").Select(n => (Node: n, e.Reference)))
            .GroupBy(x => x.Node).Where(g => g.Count() > 1).Select(g => $"{g.Key}: {string.Join(" ", g.Select(x => x.Reference))}").ToList();
        Assert.True(shared.Count == 0, $"Shared nodes: {string.Join("; ", shared)}\n{netlist}");
    }

    [Fact]
    public void Parts_added_at_one_point_do_not_overlap()
    {
        var (netlist, layout) = AddAtOnePoint(Empty, new LayoutDoc([], []), Repro);

        var circuit = NetlistLoader.Load(netlist);
        var elements = SchematicRenderer.Elements(circuit, PartMap.Resolve(circuit, Table));
        var boxes = elements.Select(e => (e.Reference, Box: SchematicRenderer.Outline(e, layout.Parts.Single(p => p.Reference == e.Reference)))).ToList();
        var hits = (from a in boxes from b in boxes
                    where string.CompareOrdinal(a.Reference, b.Reference) < 0
                    where a.Box.Min.X < b.Box.Max.X && b.Box.Min.X < a.Box.Max.X && a.Box.Min.Y < b.Box.Max.Y && b.Box.Min.Y < a.Box.Max.Y
                    select $"{a.Reference}/{b.Reference}").ToList();
        Assert.True(hits.Count == 0, $"Overlapping parts: {string.Join(", ", hits)}");
    }

    [Fact]
    public void A_part_added_at_a_wire_does_not_join_its_net()
    {
        var a = SchematicEdits.PlaceAt(Empty, new LayoutDoc([], []), "resistor", new Point(0, 0));
        var b = SchematicEdits.PlaceAt(a.Netlist, a.Layout, "resistor", new Point(0, 200));
        var circuit = NetlistLoader.Load(b.Netlist);
        var wired = SchematicEdits.Wire(b.Netlist, b.Layout, PartMap.Resolve(circuit, Table), new PinRef("R1", 1), new PinRef("R2", 1));

        // NOTE: The wire runs from (60, 0) down to (60, 200). A resistor at (60, 100) has its first pin on it.
        var c = SchematicEdits.PlaceAt(wired.Netlist, wired.Layout, "resistor", new Point(60, 100));

        var r3 = c.Netlist.Split('\n').Single(l => l.StartsWith("R3 ", StringComparison.Ordinal)).Split(' ');
        var r1 = c.Netlist.Split('\n').Single(l => l.StartsWith("R1 ", StringComparison.Ordinal)).Split(' ');
        Assert.DoesNotContain(r1[2], r3[1..3]);
    }

    [Fact]
    public void A_drag_picks_the_part_drawn_last()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        // NOTE: The schematic draws the pot RA before RB. The rects for the pointer put the pots last before the fix.
        var editor = Render<SchematicEditor>(p => p
            .Add(c => c.Netlist, "* pot and resistor\nRA_1 a b 5k\nRA_2 b 0 5k\n* ssp:knob RA linear 0.5\nRB a 0 1k\n.END\n")
            .Add(c => c.Changed, (SchematicChange _) => { }));

        // NOTE: Where two rects overlap, the last rect gets the pointer. It must be the part on top of the drawing.
        var drawn = editor.FindAll("figure.schematic > svg:not(.schematic-pins) g[data-ref]").Select(g => g.GetAttribute("data-ref")).ToList();
        var rects = editor.FindAll("rect.part").Select(r => r.GetAttribute("data-ref")).ToList();
        Assert.True(drawn.SequenceEqual(rects), $"drawn {string.Join(",", drawn)} rects {string.Join(",", rects)}");
    }
}
