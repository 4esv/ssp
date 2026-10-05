using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Ssp.Core.Layout;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Components;
using Ssp.Web.Editing;
using Ssp.Web.Schematic;
using LayoutDoc = Ssp.Core.Layout.Layout;
using Point = Ssp.Core.Layout.Point;

namespace Ssp.Web.Tests;

// NOTE: Several parts at once (#169): select, move, copy, paste and cut, with one undo step for each.
public class SchematicGroupTests : BunitContext
{
    static readonly PartsTable Table = Ssp.Core.Parts.Parts.Parse(
        File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "parts.toml")), "parts.toml");

    static readonly string Fuzz = File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", "fuzz-drawn.cir"));

    static PartMap Map(string netlist) => PartMap.Resolve(NetlistLoader.Load(netlist), Table);

    static LayoutDoc Placed(string netlist)
    {
        var circuit = NetlistLoader.Load(netlist);
        return AutoPlacer.Place(circuit, circuit.Directives);
    }

    // R1 - R2 - R3 - ground, drawn with wires.
    static SchematicChange Chain()
    {
        var c = new SchematicChange("", new LayoutDoc([], []));
        for (var i = 0; i < 3; i++) c = SchematicEdits.Place(c.Netlist, c.Layout, "resistor");
        c = SchematicEdits.Wire(c.Netlist, c.Layout, Map(c.Netlist), new PinRef("R1", 1), new PinRef("R2", 0));
        c = SchematicEdits.Wire(c.Netlist, c.Layout, Map(c.Netlist), new PinRef("R2", 1), new PinRef("R3", 0));
        return SchematicEdits.Ground(c.Netlist, c.Layout, Map(c.Netlist), new PinRef("R3", 1));
    }

    static List<string> References(string netlist) =>
        NetlistLoader.Load(netlist).Circuit.Select(e => e.Name).ToList();

    [Fact]
    public void Paste_gives_fresh_references_and_nodes_and_clashes_with_nothing()
    {
        var layout = Placed(Fuzz);
        var clip = SchematicEdits.Copy(Fuzz, layout, Map(Fuzz), ["R2", "R3", "R4"]);

        var (pasted, fresh) = SchematicEdits.Paste(Fuzz, layout, clip);

        var circuit = NetlistLoader.Load(pasted.Netlist);
        Assert.DoesNotContain(circuit.Diagnostics, d => d.Severity == Severity.Error);
        Assert.Empty(pasted.Layout.Validate(circuit));
        Assert.Equal(["R1", "R7", "R8"], fresh);
        var names = References(pasted.Netlist);
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(References(Fuzz).Count + 3, names.Count);
        // NOTE: The copy joins nothing of the circuit. Only node 0 is shared.
        var old = NetlistLoader.Load(Fuzz).NodeNames.ToHashSet();
        foreach (var r in fresh)
        {
            var nodes = circuit.Circuit.OfType<SpiceSharp.Components.IComponent>().Single(c => c.Name == r).Nodes;
            Assert.All(nodes, n => Assert.True(n == "0" || !old.Contains(n), $"{r} is on old node {n}"));
        }

        // A second paste clashes with the first.
        var (twice, again) = SchematicEdits.Paste(pasted.Netlist, pasted.Layout, clip);
        Assert.Equal(["R9", "R10", "R11"], again);
        Assert.DoesNotContain(NetlistLoader.Load(twice.Netlist).Diagnostics, d => d.Severity == Severity.Error);
    }

    [Fact]
    public void A_pot_pastes_with_its_halves_and_its_knob()
    {
        var layout = Placed(Fuzz);
        var clip = SchematicEdits.Copy(Fuzz, layout, Map(Fuzz), ["RV1"]);

        var (pasted, fresh) = SchematicEdits.Paste(Fuzz, layout, clip);

        Assert.Equal(["RV2"], fresh);
        Assert.Contains("RV2_1", References(pasted.Netlist));
        Assert.Contains("RV2_2", References(pasted.Netlist));
        Assert.Contains("* ssp:knob RV2 linear 0.5", pasted.Netlist);
        Assert.Contains(pasted.Layout.Parts, p => p.Reference == "RV2_1");
        Assert.Empty(pasted.Layout.Validate(NetlistLoader.Load(pasted.Netlist)));
    }

    [Fact]
    public void Cut_then_paste_brings_back_the_wires_between_the_cut_parts()
    {
        var chain = Chain();
        var map = Map(chain.Netlist);
        var between = chain.Layout.Wires.Count(w => w.Net == NetlistLoader.Load(chain.Netlist).Circuit
            .OfType<SpiceSharp.Components.IComponent>().Single(c => c.Name == "R1").Nodes[1]);
        Assert.Equal(1, between);

        var (cut, clip) = SchematicEdits.Cut(chain.Netlist, chain.Layout, map, ["R1", "R2"]);

        Assert.Equal(["R3"], References(cut.Netlist));
        Assert.Single(cut.Layout.Parts);
        Assert.Single(clip.Wires);

        var (pasted, fresh) = SchematicEdits.Paste(cut.Netlist, cut.Layout, clip);

        Assert.Equal(["R1", "R2"], fresh);
        var circuit = NetlistLoader.Load(pasted.Netlist);
        Assert.DoesNotContain(circuit.Diagnostics, d => d.Severity == Severity.Error);
        var pastedMap = Map(pasted.Netlist);
        var r1 = SchematicEdits.Pin(circuit, pasted.Layout, pastedMap, new PinRef("R1", 1));
        var r2 = SchematicEdits.Pin(circuit, pasted.Layout, pastedMap, new PinRef("R2", 0));
        // NOTE: R1 pin 2 and R2 pin 1 share a node again, and one wire joins them.
        Assert.Equal(r1.Node, r2.Node);
        var wire = Assert.Single(pasted.Layout.Wires, w => w.Net == r1.Node);
        Assert.Equal(new HashSet<Point> { r1.Position, r2.Position }, new HashSet<Point> { wire.Points[0], wire.Points[^1] });
    }

    [Fact]
    public void A_drag_of_three_parts_moves_them_together_and_is_one_undo_step()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var layout = Placed(Fuzz);
        var seen = new List<SchematicChange>();
        var history = new CommandStack();
        history.Reset(Fuzz, layout);
        var editor = Render<SchematicEditor>(p => p
            .Add(c => c.Netlist, Fuzz)
            .Add(c => c.Layout, layout)
            .Add(c => c.Changed, (SchematicChange c) => { seen.Add(c); history.Do(new ChangeCommand(c)); }));

        editor.Find(".part[data-ref=R2]").Click();
        editor.Find(".part[data-ref=R3]").Click(new MouseEventArgs { ShiftKey = true });
        editor.Find(".part[data-ref=R4]").Click(new MouseEventArgs { CtrlKey = true });
        Assert.Equal(3, editor.FindAll(".part.selected").Count);

        editor.Find(".part[data-ref=R3]").PointerDown(new PointerEventArgs { ClientX = 100, ClientY = 100, PointerId = 1 });
        editor.Find(".part[data-ref=R3]").PointerMove(new PointerEventArgs { ClientX = 140, ClientY = 180, PointerId = 1 });
        Assert.Equal(3, editor.FindAll(".part.ghost").Count);
        editor.Find(".part[data-ref=R3]").PointerUp(new PointerEventArgs { ClientX = 140, ClientY = 180, PointerId = 1 });

        var change = Assert.Single(seen);
        foreach (var r in new[] { "R2", "R3", "R4" })
        {
            var from = layout.Parts.Single(p => p.Reference == r);
            var to = change.Layout.Parts.Single(p => p.Reference == r);
            Assert.Equal((from.X + 40, from.Y + 80), (to.X, to.Y));
        }
        Assert.Equal(layout.Parts.Single(p => p.Reference == "R5"), change.Layout.Parts.Single(p => p.Reference == "R5"));
        Assert.Equal(3, editor.FindAll(".part.selected").Count);

        history.Undo();
        Assert.Same(layout, history.Layout);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void The_keys_copy_paste_and_cut_the_selection_each_in_one_step()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var layout = Placed(Fuzz);
        var seen = new List<SchematicChange>();
        var editor = Render<SchematicEditor>(p => p
            .Add(c => c.Netlist, Fuzz)
            .Add(c => c.Layout, layout)
            .Add(c => c.Changed, (SchematicChange c) => seen.Add(c)));

        editor.Find(".part[data-ref=R2]").Click();
        editor.Find(".part[data-ref=R3]").Click(new MouseEventArgs { MetaKey = true });
        editor.Find(".part[data-ref=R4]").Click(new MouseEventArgs { MetaKey = true });
        editor.Find("figure.schematic").KeyDown(new KeyboardEventArgs { Key = "c", MetaKey = true });
        Assert.Empty(seen);
        editor.Find("figure.schematic").KeyDown(new KeyboardEventArgs { Key = "v", CtrlKey = true });

        var pasted = Assert.Single(seen);
        Assert.Equal(References(Fuzz).Count + 3, References(pasted.Netlist).Count);
        // NOTE: The paste is the new selection, so a drag or a cut takes it.
        Assert.Equal(["R1", "R7", "R8"], editor.FindAll(".part.selected").Select(e => e.GetAttribute("data-ref")).Order());

        editor.Find("figure.schematic").KeyDown(new KeyboardEventArgs { Key = "x", CtrlKey = true });
        Assert.Equal(2, seen.Count);
        Assert.Equal(References(Fuzz), References(seen[1].Netlist));

        editor.Find("figure.schematic").KeyDown(new KeyboardEventArgs { Key = "a", CtrlKey = true });
        Assert.Equal(editor.FindAll(".part").Count(e => !e.ClassList.Contains("ghost")), editor.FindAll(".part.selected").Count);
    }

    [Fact]
    public void A_box_drawn_with_the_select_tool_selects_the_parts_inside_it()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var layout = Placed(Fuzz);
        var editor = Render<SchematicEditor>(p => p
            .Add(c => c.Netlist, Fuzz)
            .Add(c => c.Layout, layout)
            .Add(c => c.Changed, (SchematicChange _) => { }));
        var all = editor.FindAll(".part").Count;

        editor.Find("button[data-tool=select]").Click();
        Assert.Equal("true", editor.Find("button[data-tool=select]").GetAttribute("aria-pressed"));
        var pins = editor.Find("svg.schematic-pins");
        pins.PointerDown(new PointerEventArgs { ClientX = -10000, ClientY = -10000, PointerId = 1 });
        pins.PointerMove(new PointerEventArgs { ClientX = 10000, ClientY = 10000, PointerId = 1 });
        Assert.Single(editor.FindAll(".select-box"));
        pins.PointerUp(new PointerEventArgs { ClientX = 10000, ClientY = 10000, PointerId = 1 });

        Assert.Empty(editor.FindAll(".select-box"));
        Assert.Equal(all, editor.FindAll(".part.selected").Count);
    }
}
