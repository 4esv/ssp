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
        { "njf", 3 }, { "pjf", 3 }, { "opamp5", 5 },
    };

    public static TheoryData<string> GroupOne => new() { "electrolytic", "inductor", "zener", "schottky" };

    public static TheoryData<string> GroupTwo => new() { "njf", "pjf" };

    public static TheoryData<string> GroupThree => new() { "opamp5" };

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
        // NOTE: An op-amp is a subcircuit instance: the layout places it by its own reference, and its members are the parts inside.
        Assert.Equal(kind is "opamp5" ? element.Reference : element.Members[0], change.Layout.Parts[0].Reference);
        Assert.Equal(kind is "pot" ? 3 : pins, kind is "pot" or "opamp5" ? element.Nodes.Count : circuit.Circuit.OfType<IComponent>().Single().Nodes.Count);
    }

    [Theory]
    [InlineData("npn", ".model QNPN")]
    [InlineData("pnp", ".model QPNP")]
    [InlineData("diode", ".model DGEN")]
    [InlineData("led", ".model LED_RED")]
    [InlineData("zener", ".model DZ_5V1")]
    [InlineData("schottky", ".model DSCHOTTKY")]
    [InlineData("njf", ".model JN")]
    [InlineData("pjf", ".model JP")]
    [InlineData("opamp5", ".subckt JRC4558")]
    [InlineData("opamp5", ".ends JRC4558")]
    public void Place_adds_the_model_once(string kind, string model)
    {
        var once = SchematicEdits.Place(Empty, NoLayout, kind);
        var twice = SchematicEdits.Place(once.Netlist, once.Layout, kind);

        Assert.Equal(1, Count(twice.Netlist, model));
        Assert.DoesNotContain(NetlistLoader.Load(twice.Netlist).Diagnostics, d => d.Severity == Severity.Error);
    }

    [Theory]
    [MemberData(nameof(GroupOne))]
    [MemberData(nameof(GroupTwo))]
    [MemberData(nameof(GroupThree))]
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
    [MemberData(nameof(GroupTwo))]
    public void A_new_kind_wired_to_a_battery_runs_without_errors(string kind)
    {
        var change = SchematicEdits.Place(Empty, NoLayout, kind);
        change = SchematicEdits.Place(change.Netlist, change.Layout, "battery");
        change = SchematicEdits.Place(change.Netlist, change.Layout, "resistor");
        var parts = Parts(change.Netlist);
        var reference = kind switch { "electrolytic" => "C1", "inductor" => "L1", "njf" or "pjf" => "J1", _ => "D1" };
        // NOTE: A resistor sits between the battery and the part, because an inductor straight across a battery is a loop of voltage sources.
        change = Wire(change, parts, "R1", 0, "V1", 0);
        change = Wire(change, parts, reference, 0, "R1", 1);
        change = SchematicEdits.Ground(change.Netlist, change.Layout, parts, new PinRef(reference, kind is "njf" or "pjf" ? 2 : 1));
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
    public void The_group_two_fixture_solves_to_its_hand_values()
    {
        var text = File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", "parts-group2.cir"));
        var result = Runner.Run(text, new RunOptions());
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Severity.Error);
        var v = result.OperatingPoint!.NodeVoltages;
        Assert.InRange(v["dn"], 3.8, 4.1);
        Assert.InRange(v["dp"], 4.9, 5.2);
    }

    [Fact]
    public void Place_opamp_writes_an_X_line_with_five_new_nodes_and_the_JRC4558_subcircuit()
    {
        var change = SchematicEdits.Place(Empty, NoLayout, "opamp5");

        // NOTE: The pin order of the op-amp subcircuits in models/: inp inn out vcc vee.
        Assert.Matches(@"(?m)^X1 (n\d+) (n\d+) (n\d+) (n\d+) (n\d+) JRC4558\r?$", change.Netlist);
        var nodes = change.Netlist.Split('\n').Single(l => l.StartsWith("X1 ", StringComparison.Ordinal)).Split(' ')[1..6];
        Assert.Equal(5, nodes.Distinct().Count());
        Assert.Equal(1, Count(change.Netlist, ".subckt JRC4558 inp inn out vcc vee"));
        Assert.Equal("X1", Assert.Single(change.Layout.Parts).Reference);
    }

    // NOTE: The editor draws a tap target at each overlay pin. An op-amp has five.
    [Fact]
    public void A_placed_opamp_has_five_pins_to_tap_on_their_own_nodes()
    {
        var change = SchematicEdits.Place(Empty, NoLayout, "opamp5");
        var pins = SchematicRenderer.OverlayPins(NetlistLoader.Load(change.Netlist), change.Layout, Parts(change.Netlist));

        Assert.Equal([0, 1, 2, 3, 4], pins.Where(p => p.Reference == "X1").Select(p => p.Pin));
        Assert.Equal(5, pins.Select(p => p.Net).Distinct().Count());
    }

    [Fact]
    public void Place_opamp_in_a_circuit_that_has_the_subcircuit_adds_no_second_one()
    {
        var overdrive = File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "library", "overdrive-808.cir"));
        var change = SchematicEdits.Place(overdrive, NoLayout, "opamp5");

        Assert.Equal(1, Count(change.Netlist, ".subckt JRC4558"));
        Assert.Contains("\nX3 ", change.Netlist);
        Assert.DoesNotContain(NetlistLoader.Load(change.Netlist).Diagnostics, d => d.Severity == Severity.Error);
    }

    // NOTE: A delete leaves the .model line of a diode, and so it leaves the .subckt of an op-amp. The netlist still loads.
    [Fact]
    public void Delete_opamp_removes_its_X_line_and_keeps_the_subcircuit_as_a_model_line_stays()
    {
        var two = SchematicEdits.Place(Empty, NoLayout, "opamp5");
        two = SchematicEdits.Place(two.Netlist, two.Layout, "opamp5");
        var one = SchematicEdits.Delete(two.Netlist, two.Layout, "X1");
        var none = SchematicEdits.Delete(one.Netlist, one.Layout, "X2");

        Assert.Equal(1, Count(two.Netlist, ".subckt JRC4558"));
        Assert.DoesNotContain("\nX1 ", one.Netlist);
        Assert.Contains("\nX2 ", one.Netlist);
        Assert.Equal(1, Count(one.Netlist, ".subckt JRC4558"));
        Assert.DoesNotContain("\nX2 ", none.Netlist);
        Assert.Empty(none.Layout.Parts);
        Assert.DoesNotContain(NetlistLoader.Load(none.Netlist).Diagnostics, d => d.Severity == Severity.Error);

        var diode = SchematicEdits.Place(Empty, NoLayout, "diode");
        Assert.Equal(1, Count(SchematicEdits.Delete(diode.Netlist, diode.Layout, "D1").Netlist, ".model DGEN"));
    }

    // NOTE: #456: a non-inverting stage on one 9 V battery, built with the edits of the editor. The battery feeds V+, V- is
    // ground, two 10k resistors bias the + input at 4.5 V through the source, Rg and a 10u capacitor go from the - input
    // to ground, and Rf goes from the output to the - input. The gain at 1 kHz is 1 + Rf / Rg.
    [Theory]
    [InlineData("100k", 20.83)]
    [InlineData("47k", 15.04)]
    public void A_placed_opamp_wired_as_a_non_inverting_stage_has_a_gain_of_one_plus_rf_over_rg(string rf, double db)
    {
        var change = new SchematicChange(Empty, NoLayout);
        foreach (var kind in new[] { "opamp5", "battery", "source", "resistor", "resistor", "resistor", "resistor", "electrolytic" })
            change = SchematicEdits.Place(change.Netlist, change.Layout, kind);
        change = SchematicEdits.SetValue(change.Netlist, change.Layout, "R3", rf);
        var parts = Parts(change.Netlist);
        change = Wire(change, parts, "X1", 3, "V1", 0);    // V+ to the battery
        change = SchematicEdits.Ground(change.Netlist, change.Layout, parts, new PinRef("X1", 4));
        change = Wire(change, parts, "X1", 0, "V2", 0);    // + input to the source
        change = Wire(change, parts, "R1", 0, "V1", 0);    // bias: R1 from the battery
        change = Wire(change, parts, "R1", 1, "V2", 1);    // to the minus of the source
        change = Wire(change, parts, "R2", 0, "V2", 1);    // and R2 to ground
        change = SchematicEdits.Ground(change.Netlist, change.Layout, parts, new PinRef("R2", 1));
        change = Wire(change, parts, "R3", 0, "X1", 2);    // Rf from the output
        change = Wire(change, parts, "R3", 1, "X1", 1);    // to the - input
        change = Wire(change, parts, "R4", 0, "X1", 1);    // Rg from the - input
        change = Wire(change, parts, "R4", 1, "C1", 0);    // to the capacitor
        change = SchematicEdits.Ground(change.Netlist, change.Layout, parts, new PinRef("C1", 1));
        change = SchematicEdits.PlaceJack(change.Netlist, change.Layout, parts, "jack-in", new Spot(default, new PinRef("X1", 0)));
        change = SchematicEdits.PlaceJack(change.Netlist, change.Layout, parts, "jack-out", new Spot(default, new PinRef("X1", 2)));

        var result = Runner.Run(change.Netlist, new RunOptions());
        Assert.True(!result.Diagnostics.Any(d => d.Severity == Severity.Error), string.Join("\n", result.Diagnostics.Select(d => d.Message)) + "\n" + change.Netlist);
        var circuit = NetlistLoader.Load(change.Netlist);
        var (input, output) = (circuit.Directives.Input!, circuit.Directives.Output!);
        var v = result.OperatingPoint!.NodeVoltages;
        Assert.InRange(v[output], 4.4, 4.6);
        var ac = result.FrequencyResponse!;
        var at = Enumerable.Range(0, ac.Frequencies.Count).MinBy(i => Math.Abs(Math.Log10(ac.Frequencies[i] / 1000)));
        Assert.InRange(ac.MagnitudeDb[output][at] - ac.MagnitudeDb[input][at], db - 0.1, db + 0.1);
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
