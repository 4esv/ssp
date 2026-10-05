using Ssp.Core.Diagnostics;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Core.Layout;
using Ssp.Web.Library;
using Ssp.Web.Schematic;
using LayoutDoc = Ssp.Core.Layout.Layout;

namespace Ssp.Web.Tests;

// NOTE: #271: the crashes that the monkey test (Playwright/MonkeyPlaywrightTests.cs) found, each without the monkey.
public class MonkeyCrashTests
{
    static readonly PartsTable Table = Ssp.Core.Parts.Parts.Parse(
        File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "parts.toml")), "parts.toml");

    static SchematicChange Placed(string kind)
    {
        var empty = new LayoutDoc([], []);
        return SchematicEdits.Place(SchematicEdits.Place("* schematic\n", empty, "resistor").Netlist,
            SchematicEdits.Place("* schematic\n", empty, "resistor").Layout, kind);
    }

    static string[] Elements(string netlist) => NetlistLoader.Load(netlist).Circuit.Select(e => e.Name).Order().ToArray();

    // NOTE: A pot P is the lines P_1 and P_2, a switch S is S_1 to S_n, and the canvas names them P and S. Seeds 33 (delete RV1) and 78 (duplicate RV1).
    [Theory]
    [InlineData("pot", "RV1")]
    [InlineData("switch-2", "RSW1")]
    public void Deleting_a_pot_or_a_switch_by_its_name_removes_every_line_and_its_placement(string kind, string reference)
    {
        var placed = Placed(kind);
        Assert.Contains(Elements(placed.Netlist), e => e.StartsWith(reference + "_"));

        var after = SchematicEdits.Delete(placed.Netlist, placed.Layout, reference);

        Assert.Equal(["R1"], Elements(after.Netlist));
        Assert.DoesNotContain("ssp:", after.Netlist);
        Assert.Equal(["R1"], after.Layout.Parts.Select(p => p.Reference));
    }

    [Theory]
    [InlineData("pot", "RV1", "RV2")]
    [InlineData("switch-2", "RSW1", "RSW2")]
    public void Duplicating_a_pot_or_a_switch_by_its_name_adds_a_whole_copy(string kind, string reference, string copy)
    {
        var placed = Placed(kind);
        var lines = Elements(placed.Netlist).Count(e => e.StartsWith(reference + "_"));

        var after = SchematicEdits.Duplicate(placed.Netlist, placed.Layout, reference);

        var circuit = NetlistLoader.Load(after.Netlist);
        Assert.DoesNotContain(circuit.Diagnostics, d => d.Severity == Severity.Error);
        Assert.Equal(lines, Elements(after.Netlist).Count(e => e.StartsWith(copy + "_")));
        Assert.Contains(after.Netlist.Split('\n'), l => l.Contains("ssp:") && l.Contains(" " + copy + " "));
        Assert.Equal(copy + "_1", after.Layout.Parts[^1].Reference);
        Assert.Empty(after.Layout.Validate(circuit));
        if (kind == "pot")
        {
            // NOTE: The two halves of the copy share their wiper, like the halves of the pot.
            var halves = circuit.Circuit.OfType<SpiceSharp.Components.IComponent>().Where(c => c.Name.StartsWith(copy + "_")).OrderBy(c => c.Name).ToList();
            Assert.Equal(halves[0].Nodes[1], halves[1].Nodes[0]);
        }
    }

    // NOTE: Seed 78: a duplicated .model line (an edit of the netlist text) made Copy throw. The first line is the model.
    [Fact]
    public void Copying_a_part_whose_model_line_is_twice_in_the_netlist_works()
    {
        const string Netlist = ".model DGEN D (IS=1e-14 N=1.9)\n.model DGEN D (IS=1e-14 N=1.9)\nD1 n1 n2 DGEN\n";
        var circuit = NetlistLoader.Load(Netlist);
        var layout = AutoPlacer.Place(circuit, circuit.Directives);

        var clip = SchematicEdits.Copy(Netlist, layout, PartMap.Resolve(circuit, Table), ["D1"]);

        Assert.Single(clip.Parts);
        Assert.Single(clip.Models, m => m.Contains("DGEN"));
    }

    // NOTE: Seed 18: a block dropped on a wire to a pin of the op-amp X1 set the node of X1.Rid, a line inside the
    // .subckt, and threw. Deleting such a wire did the same. The node to change is on the line of X1.
    [Fact]
    public void A_block_dropped_on_or_a_delete_of_a_wire_at_an_op_amp_pin_changes_the_line_of_the_op_amp()
    {
        var netlist = File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "library", "buffer-bootstrap-tl072.cir"));
        var circuit = NetlistLoader.Load(netlist);
        var layout = AutoPlacer.Place(circuit, circuit.Directives);
        var parts = PartMap.Resolve(circuit, Table);
        var block = CircuitLibrary.Blocks.Single(b => b.FileName == "clip-shunt-led.cir");
        var stage = SchematicEdits.Block(block.Netlist, block.Layout is null ? null : LayoutDoc.Parse(block.Layout));
        var dropped = 0;
        for (var i = 0; i < layout.Wires.Count; i++)
        {
            var w = layout.Wires[i];
            var at = new Point((w.Points[0].X + w.Points[1].X) / 2, (w.Points[0].Y + w.Points[1].Y) / 2);
            var drop = SchematicEdits.Drop(netlist, layout, parts, stage, at, i);
            if (drop.Refused is null) dropped++;
            Assert.DoesNotContain(NetlistLoader.Load(drop.Change.Netlist).Diagnostics, d => d.Severity == Severity.Error);
            var deleted = SchematicEdits.DeleteWire(netlist, layout, parts, i);
            Assert.DoesNotContain(NetlistLoader.Load(deleted.Netlist).Diagnostics, d => d.Severity == Severity.Error);
        }
        Assert.True(dropped > 0);
    }
}
