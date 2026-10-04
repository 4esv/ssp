using Bunit;
using Ssp.Core.Layout;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Components;
using Ssp.Web.Schematic;
using LayoutDoc = Ssp.Core.Layout.Layout;

namespace Ssp.Web.Tests;

public class SchematicDeleteTests : BunitContext
{
    const string Chain = "* chain\nV1 in 0 1\nR1 in a 1k\nR2 a 0 1k\nR3 a 0 2k\n.END\n";
    static readonly PartsTable Table = Ssp.Core.Parts.Parts.Parse(
        File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "parts.toml")), "parts.toml");

    static (PartMap Parts, LayoutDoc Layout) Start(string netlist)
    {
        var circuit = NetlistLoader.Load(netlist);
        return (PartMap.Resolve(circuit, Table), AutoPlacer.Place(circuit, circuit.Directives));
    }

    static string NodeOf(string netlist, string reference, int pin) =>
        NetlistLoader.Load(netlist).Circuit.OfType<SpiceSharp.Components.IComponent>().Single(c => c.Name == reference).Nodes[pin];

    [Fact]
    public void Deleting_a_wire_splits_the_net_it_joined()
    {
        var (parts, layout) = Start("* two\nR1 a 0 1k\nR2 b 0 1k\n.END\n");
        var wired = SchematicEdits.Wire("* two\nR1 a 0 1k\nR2 b 0 1k\n.END\n", layout, parts, new PinRef("R1", 0), new PinRef("R2", 0));
        Assert.Equal(NodeOf(wired.Netlist, "R1", 0), NodeOf(wired.Netlist, "R2", 0));
        var index = wired.Layout.Wires.ToList().FindIndex(w => w.Net == NodeOf(wired.Netlist, "R1", 0));

        var after = SchematicEdits.DeleteWire(wired.Netlist, wired.Layout, PartMap.Resolve(NetlistLoader.Load(wired.Netlist), Table), index);

        Assert.NotEqual(NodeOf(after.Netlist, "R1", 0), NodeOf(after.Netlist, "R2", 0));
        Assert.Equal(wired.Layout.Wires.Count - 1, after.Layout.Wires.Count);
        Assert.Equal("0", NodeOf(after.Netlist, "R1", 1));
        Assert.Equal("0", NodeOf(after.Netlist, "R2", 1));
        var circuit = NetlistLoader.Load(after.Netlist);
        Assert.DoesNotContain(circuit.Diagnostics, d => d.Severity == Severity.Error);
        Assert.Empty(after.Layout.Validate(circuit));
    }

    [Fact]
    public void Deleting_a_part_removes_its_line_and_its_placement()
    {
        var (_, layout) = Start(Chain);

        var after = SchematicEdits.Delete(Chain, layout, "R3");

        Assert.DoesNotContain("R3 ", after.Netlist);
        Assert.DoesNotContain(after.Layout.Parts, p => p.Reference == "R3");
        Assert.Equal(layout.Parts.Count - 1, after.Layout.Parts.Count);
    }

    IRenderedComponent<SchematicEditor> Editor(List<SchematicChange> seen)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Render<SchematicEditor>(p => p
            .Add(c => c.Netlist, Chain)
            .Add(c => c.Changed, (SchematicChange c) => seen.Add(c)));
    }

    [Fact]
    public void Selecting_a_part_shows_a_bar_and_delete_then_undo_restores_the_part()
    {
        var seen = new List<SchematicChange>();
        var editor = Editor(seen);
        Assert.Empty(editor.FindAll(".selection-bar"));

        editor.Find("rect.part[data-ref=\"R3\"]").Click();
        var bar = editor.Find(".selection-bar");
        Assert.Equal(["Delete", "Rotate", "Flip", "Duplicate"], bar.QuerySelectorAll("button").Select(b => b.TextContent.Trim()));

        bar.QuerySelector("button[data-action=delete]")!.Click();
        Assert.Contains("Deleted R3.", editor.Find(".delete-status").TextContent);
        Assert.Empty(editor.FindAll("rect.part[data-ref=\"R3\"]"));
        Assert.Empty(editor.FindAll(".selection-bar"));
        Assert.DoesNotContain("R3 ", seen[^1].Netlist);

        editor.Find(".delete-status button").Click();
        Assert.Single(editor.FindAll("rect.part[data-ref=\"R3\"]"));
        Assert.Empty(editor.FindAll(".delete-status"));
        Assert.Contains("R3 a 0 2k", seen[^1].Netlist);
        Assert.Contains(seen[^1].Layout.Parts, p => p.Reference == "R3");
    }

    [Fact]
    public void Tapping_a_wire_selects_it_and_delete_removes_it_and_undo_restores_it()
    {
        var seen = new List<SchematicChange>();
        var editor = Editor(seen);
        editor.Find("circle.pin[data-ref=\"R1\"][data-pin=\"1\"]").Click();
        editor.Find("circle.pin[data-ref=\"R3\"][data-pin=\"0\"]").Click();
        var wires = seen[^1].Layout.Wires.Count;
        Assert.Equal(wires, editor.FindAll("polyline.wire-hit").Count);

        editor.FindAll("polyline.wire-hit")[^1].Click();
        Assert.Single(editor.FindAll("polyline.wire-hit.selected"));
        editor.Find(".selection-bar button[data-action=delete]").Click();

        Assert.Equal(wires - 1, seen[^1].Layout.Wires.Count);
        Assert.Contains("Deleted wire.", editor.Find(".delete-status").TextContent);
        editor.Find(".delete-status button").Click();
        Assert.Equal(wires, seen[^1].Layout.Wires.Count);
    }

    [Fact]
    public void Delete_and_backspace_keep_working()
    {
        var seen = new List<SchematicChange>();
        var editor = Editor(seen);
        editor.Find("rect.part[data-ref=\"R3\"]").Click();
        editor.Find("figure.schematic").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Backspace" });

        Assert.DoesNotContain("R3 ", seen[^1].Netlist);
        Assert.Contains("Deleted R3.", editor.Find(".delete-status").TextContent);
    }

    [Fact]
    public void The_undo_survives_the_parent_passing_the_change_back()
    {
        var seen = new List<SchematicChange>();
        var editor = Editor(seen);
        editor.Find("rect.part[data-ref=\"R3\"]").Click();
        editor.Find(".selection-bar button[data-action=delete]").Click();

        editor.Render(p => p.Add(c => c.Netlist, seen[^1].Netlist).Add(c => c.Layout, seen[^1].Layout));

        Assert.Contains("Deleted R3.", editor.Find(".delete-status").TextContent);
        editor.Find(".delete-status button").Click();
        Assert.Contains("R3 a 0 2k", seen[^1].Netlist);
    }
}
