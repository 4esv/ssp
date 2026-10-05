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
        { "battery", 2 }, { "source", 2 },
        { "electrolytic", 2 }, { "inductor", 2 }, { "zener", 2 }, { "schottky", 2 },
    };

    public static TheoryData<string> GroupOne => new() { "electrolytic", "inductor", "zener", "schottky" };

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
    [InlineData("zener", ".model DZ_5V1")]
    [InlineData("schottky", ".model DSCHOTTKY")]
    public void Place_adds_the_model_once(string kind, string model)
    {
        var once = SchematicEdits.Place(Empty, NoLayout, kind);
        var twice = SchematicEdits.Place(once.Netlist, once.Layout, kind);

        Assert.Equal(1, Count(twice.Netlist, model));
        Assert.DoesNotContain(NetlistLoader.Load(twice.Netlist).Diagnostics, d => d.Severity == Severity.Error);
    }

    [Theory]
    [MemberData(nameof(GroupOne))]
    public void A_new_kind_has_its_own_symbol_and_the_renderer_draws_it(string kind)
    {
        Assert.Equal(kind, Symbols.For(kind).Kind);
        Assert.Equal(1, SchematicEdits.Kinds.Keys.Count(k => k == kind));
        Assert.Equal(1, Symbols.Kinds.Count(k => Symbols.For(k).Svg == Symbols.For(kind).Svg));

        var change = SchematicEdits.Place(Empty, NoLayout, kind);
        var circuit = NetlistLoader.Load(change.Netlist);
        Assert.Equal(kind, SchematicRenderer.Elements(circuit, Parts(change.Netlist)).Single().Kind);
    }

    [Theory]
    [MemberData(nameof(GroupOne))]
    public void A_new_kind_wired_to_a_battery_runs_without_errors(string kind)
    {
        var change = SchematicEdits.Place(Empty, NoLayout, kind);
        change = SchematicEdits.Place(change.Netlist, change.Layout, "battery");
        change = SchematicEdits.Place(change.Netlist, change.Layout, "resistor");
        var parts = Parts(change.Netlist);
        var reference = kind switch { "electrolytic" => "C1", "inductor" => "L1", _ => "D1" };
        // NOTE: A resistor sits between the battery and the part, because an inductor straight across a battery is a loop of voltage sources.
        change = Wire(change, parts, "R1", 0, "V1", 0);
        change = Wire(change, parts, reference, 0, "R1", 1);
        change = SchematicEdits.Ground(change.Netlist, change.Layout, parts, new PinRef(reference, 1));
        change = SchematicEdits.PlaceJack(change.Netlist, change.Layout, parts, "jack-out", new Spot(default, new PinRef(reference, 0)));

        var result = Runner.Run(change.Netlist, new RunOptions());
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Severity.Error);
        Assert.NotNull(result.OperatingPoint);
    }

    [Fact]
    public void The_group_one_fixture_solves_to_its_hand_values()
    {
        var text = File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", "parts-group1.cir"));
        var result = Runner.Run(text, new RunOptions());
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Severity.Error);
        var v = result.OperatingPoint!.NodeVoltages;
        Assert.InRange(v["z"], 5.0, 5.2);
        Assert.InRange(v["s"], 0.25, 0.31);
        Assert.InRange(v["out"], 8.9, 9.0);
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

    [Theory]
    [InlineData("jack-in")]
    [InlineData("jack-out")]
    public void Place_refuses_a_jack_because_it_is_a_marker(string kind) =>
        Assert.Throws<ArgumentException>(() => SchematicEdits.Place(Empty, NoLayout, kind));

    [Fact]
    public void Placed_parts_wired_together_run_without_errors()
    {
        var change = new SchematicChange(Empty, NoLayout);
        foreach (var kind in new[] { "npn", "diode", "pot", "battery", "source" })
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

        // The output jack marks the collector.
        change = SchematicEdits.PlaceJack(change.Netlist, change.Layout, parts, "jack-out", new Spot(default, new PinRef("Q1", 0)));
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
