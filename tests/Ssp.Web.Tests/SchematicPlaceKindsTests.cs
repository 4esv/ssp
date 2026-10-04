using Ssp.Core;
using Ssp.Core.Layout;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Schematic;
using SpiceSharp.Components;
using LayoutDoc = Ssp.Core.Layout.Layout;

namespace Ssp.Web.Tests;

public class SchematicPlaceKindsTests
{
    const string Empty = "* schematic\n.END\n";
    static readonly LayoutDoc NoLayout = new([], []);

    public static TheoryData<string, int> NewKinds => new()
    {
        { "npn", 4 }, { "pnp", 4 }, { "diode", 2 }, { "led", 2 }, { "pot", 3 },
        { "battery", 2 }, { "source", 2 }, { "jack-in", 2 }, { "jack-out", 2 },
    };

    [Theory]
    [MemberData(nameof(NewKinds))]
    public void Place_writes_a_netlist_that_loads(string kind, int pins)
    {
        var change = SchematicEdits.Place(Empty, NoLayout, kind);
        var circuit = NetlistLoader.Load(change.Netlist);

        Assert.DoesNotContain(circuit.Diagnostics, d => d.Severity == Severity.Error);
        Assert.Single(change.Layout.Parts);
        Assert.Empty(change.Layout.Validate(circuit));
        var element = SchematicRenderer.Elements(circuit, Parts(change.Netlist)).Single();
        Assert.Equal(element.Members[0], change.Layout.Parts[0].Reference);
        Assert.Equal(kind is "pot" ? 3 : pins, kind is "pot" ? element.Nodes.Count : circuit.Circuit.OfType<IComponent>().Single().Nodes.Count);
    }

    [Theory]
    [InlineData("npn", ".model QNPN")]
    [InlineData("pnp", ".model QPNP")]
    [InlineData("diode", ".model DGEN")]
    [InlineData("led", ".model LED_RED")]
    public void Place_adds_the_model_once(string kind, string model)
    {
        var once = SchematicEdits.Place(Empty, NoLayout, kind);
        var twice = SchematicEdits.Place(once.Netlist, once.Layout, kind);

        Assert.Equal(1, Count(twice.Netlist, model));
        Assert.DoesNotContain(NetlistLoader.Load(twice.Netlist).Diagnostics, d => d.Severity == Severity.Error);
    }

    [Fact]
    public void Place_pot_writes_a_knob_line_and_battery_goes_to_node_zero()
    {
        var pot = SchematicEdits.Place(Empty, NoLayout, "pot");
        Assert.Contains("* ssp:knob RV1 ", pot.Netlist);
        Assert.Contains("RV1_1 ", pot.Netlist);

        var battery = SchematicEdits.Place(Empty, NoLayout, "battery");
        Assert.Matches(@"V1 n\d+ 0 DC 9", battery.Netlist);
    }

    [Fact]
    public void Place_jacks_label_the_nodes_in_and_out()
    {
        var jacks = SchematicEdits.Place(SchematicEdits.Place(Empty, NoLayout, "jack-in").Netlist, NoLayout, "jack-out");
        var circuit = NetlistLoader.Load(jacks.Netlist);

        Assert.Contains("in", circuit.NodeNames);
        Assert.Contains("out", circuit.NodeNames);
        Assert.Contains("* ssp:input in", jacks.Netlist);
        Assert.Contains("* ssp:output out", jacks.Netlist);
    }

    [Fact]
    public void Placed_parts_wired_together_run_without_errors()
    {
        var change = new SchematicChange(Empty, NoLayout);
        foreach (var kind in new[] { "npn", "diode", "pot", "battery", "source", "jack-out" })
            change = SchematicEdits.Place(change.Netlist, change.Layout, kind);

        // Battery V1 pin 0 is the supply. Source V2 pin 0 is the signal node.
        var parts = Parts(change.Netlist);
        change = Wire(change, parts, "Q1", 0, "V1", 0);   // collector to supply
        change = Wire(change, parts, "Q1", 1, "V2", 0);   // base to the signal source
        change = Wire(change, parts, "Q1", 2, "RV1", 2);  // emitter to the pot bottom
        change = SchematicEdits.Ground(change.Netlist, change.Layout, parts, new PinRef("RV1", 2));
        change = SchematicEdits.Ground(change.Netlist, change.Layout, parts, new PinRef("V2", 1));
        change = Wire(change, parts, "D1", 0, "Q1", 0);
        change = SchematicEdits.Ground(change.Netlist, change.Layout, parts, new PinRef("D1", 1));
        change = Wire(change, parts, "RV1", 0, "V1", 0);
        change = SchematicEdits.Ground(change.Netlist, change.Layout, parts, new PinRef("RV1", 1));

        change = Wire(change, parts, "R1", 1, "Q1", 0);   // output jack to the collector
        var result = Runner.Run(change.Netlist, new RunOptions());
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Severity.Error);
    }

    static readonly PartsTable Table = Ssp.Core.Parts.Parts.Parse(
        File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "parts.toml")), "parts.toml");

    static PartMap Parts(string netlist) => PartMap.Resolve(NetlistLoader.Load(netlist), Table);

    static SchematicChange Wire(SchematicChange change, PartMap parts, string a, int pa, string b, int pb) =>
        SchematicEdits.Wire(change.Netlist, change.Layout, parts, new PinRef(a, pa), new PinRef(b, pb));

    static int Count(string text, string needle) =>
        text.Split('\n').Count(l => l.StartsWith(needle, StringComparison.OrdinalIgnoreCase));
}
