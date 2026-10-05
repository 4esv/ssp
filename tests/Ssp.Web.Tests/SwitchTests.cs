using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Core;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Components;
using Ssp.Web.Schematic;
using SpiceSharp.Components;
using LayoutDoc = Ssp.Core.Layout.Layout;
using PartsApi = Ssp.Core.Parts.Parts;

namespace Ssp.Web.Tests;

public class SwitchTests : BunitContext
{
    const string Empty = "* schematic\n.END\n";
    static readonly LayoutDoc NoLayout = new([], []);
    static readonly PartsTable Table = PartsApi.Load(Path.Combine(RepoPaths.Root, "models", "parts.toml"));

    // NOTE: An SPDT picks the top resistor of a divider: 10k over 10k gives 4.5 V, 30k over 10k gives 2.25 V.
    const string Divider = "* spdt divider\nV1 in 0 DC 9\nR1 in t1 10k\nR2 in t2 30k\nRSW1_1 out t1 1m\nRSW1_2 out t2 1G\nR3 out 0 10k\n* ssp:switch RSW1 1\n.END\n";

    // The resistance of each throw, common to throw 1 first, at each position. Closed is 1 mohm, open is 1 Gohm.
    public static TheoryData<int, int, double[]> Table_() => new()
    {
        { 1, 0, [1e9] }, { 1, 1, [1e-3] },
        { 2, 1, [1e-3, 1e9] }, { 2, 2, [1e9, 1e-3] },
        { 3, 1, [1e-3, 1e9, 1e9] }, { 3, 2, [1e9, 1e-3, 1e9] }, { 3, 3, [1e9, 1e9, 1e-3] },
    };

    [Theory]
    [MemberData(nameof(Table_))]
    public void Position_gives_the_resistance_of_each_throw(int throws, int position, double[] ohms) =>
        Assert.Equal(ohms, Switch.Resistances(throws, position));

    [Theory]
    [InlineData(1, new[] { 0, 1 })]
    [InlineData(2, new[] { 1, 2 })]
    [InlineData(3, new[] { 1, 2, 3 })]
    public void Each_kind_has_its_positions(int throws, int[] positions) => Assert.Equal(positions, Switch.Positions(throws));

    [Theory]
    [InlineData("switch-1", 2)]
    [InlineData("switch-2", 3)]
    [InlineData("switch-3", 4)]
    public void The_pin_count_follows_the_kind(string kind, int pins)
    {
        var change = SchematicEdits.Place(Empty, NoLayout, kind);
        var circuit = NetlistLoader.Load(change.Netlist);

        Assert.DoesNotContain(circuit.Diagnostics, d => d.Severity == Severity.Error);
        var element = SchematicRenderer.Elements(circuit, null).Single();
        Assert.Equal(kind, element.Kind);
        Assert.Equal(pins, element.Nodes.Count);
        Assert.Equal(pins, Symbols.For(kind).Pins.Count);
        Assert.Equal(element.Members[0], change.Layout.Parts.Single().Reference);
        Assert.Contains("* ssp:switch RSW1 ", change.Netlist);
    }

    [Fact]
    public void An_spdt_connects_the_common_to_exactly_one_throw_at_each_position()
    {
        var netlist = SchematicEdits.Place(Empty, NoLayout, "switch-2").Netlist;
        var seen = new HashSet<string>();
        foreach (var _ in Switch.Positions(2))
        {
            var circuit = NetlistLoader.Load(netlist);
            var element = SchematicRenderer.Elements(circuit, null).Single();
            var throws = circuit.Circuit.OfType<Resistor>().OrderBy(r => r.Name).ToList();
            Assert.All(throws, r => Assert.Equal(element.Nodes[0], r.Nodes[0]));
            var closed = Assert.Single(throws, r => r.Parameters.Resistance.Value < 1);
            seen.Add(closed.Nodes[1]);
            netlist = SchematicEdits.Flip(netlist, "RSW1");
        }
        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public void Flip_moves_to_the_next_position_and_writes_the_line_and_the_resistors()
    {
        var flipped = SchematicEdits.Flip(Divider, "RSW1");

        Assert.Contains("* ssp:switch RSW1 2\n", flipped);
        Assert.Contains("RSW1_1 out t1 1G\n", flipped);
        Assert.Contains("RSW1_2 out t2 1m\n", flipped);
        Assert.Equal(Divider, SchematicEdits.Flip(flipped, "RSW1"));
    }

    [Fact]
    public void An_spdt_between_two_resistors_changes_the_node_voltage_in_a_run()
    {
        var one = Runner.Run(Divider, new RunOptions()).OperatingPoint!.NodeVoltages["out"];
        var two = Runner.Run(SchematicEdits.Flip(Divider, "RSW1"), new RunOptions()).OperatingPoint!.NodeVoltages["out"];

        Assert.Equal(4.5, one, 3);
        Assert.Equal(2.25, two, 3);
    }

    [Fact]
    public void The_switch_line_wins_over_the_resistor_values_in_a_run()
    {
        var run = Runner.Run(Divider.Replace("* ssp:switch RSW1 1", "* ssp:switch RSW1 2", StringComparison.Ordinal), new RunOptions());

        Assert.Equal(2.25, run.OperatingPoint!.NodeVoltages["out"], 3);
    }

    [Fact]
    public void A_bad_position_is_an_error()
    {
        var run = Runner.Run(Divider.Replace("* ssp:switch RSW1 1", "* ssp:switch RSW1 5", StringComparison.Ordinal), new RunOptions());

        Assert.Contains(run.Diagnostics, d => d.Severity == Severity.Error && d.Message.Contains("RSW1", StringComparison.Ordinal));
    }

    [Fact]
    public void A_tap_on_the_switch_changes_its_position_and_writes_one_change()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var seen = new List<SchematicChange>();
        var editor = Render<SchematicEditor>(p => p
            .Add(c => c.Netlist, Divider)
            .Add(c => c.Changed, (SchematicChange c) => seen.Add(c)));
        var before = editor.Instance.Drawn;

        var lever = editor.Find(".switch[data-ref=RSW1]");
        Assert.Equal("1", lever.GetAttribute("data-position"));
        lever.Click();

        var change = Assert.Single(seen);
        Assert.Contains("* ssp:switch RSW1 2\n", change.Netlist);
        Assert.Same(before, change.Layout);
        Assert.Equal("2", editor.Find(".switch[data-ref=RSW1]").GetAttribute("data-position"));
    }

    [Fact]
    public void The_symbol_shows_the_position()
    {
        var circuit = NetlistLoader.Load(Divider);
        var layout = AutoPlacer.Place(circuit, circuit.Directives);
        string Lever(string netlist) =>
            System.Text.RegularExpressions.Regex.Match(SchematicRenderer.ToSvg(NetlistLoader.Load(netlist), layout, PartMap.Resolve(NetlistLoader.Load(netlist), Table)),
                "<path class=\"lever\" d=\"([^\"]+)\"").Groups[1].Value;

        Assert.NotEqual("", Lever(Divider));
        Assert.NotEqual(Lever(Divider), Lever(SchematicEdits.Flip(Divider, "RSW1")));
    }

    [Fact]
    public void The_knobs_panel_has_a_row_of_buttons_for_a_switch()
    {
        Services.AddSingleton(TimeProvider.System);
        var changed = new List<string>();
        var panel = Render<Knobs>(p => p
            .Add(c => c.Netlist, Divider)
            .Add(c => c.NetlistChanged, (string n) => changed.Add(n)));

        var buttons = panel.FindAll(".switch[data-part=RSW1] button");
        Assert.Equal(2, buttons.Count);
        Assert.Equal("true", buttons[0].GetAttribute("aria-pressed"));
        buttons[1].Click();

        Assert.Contains("* ssp:switch RSW1 2\n", Assert.Single(changed));
    }

    [Fact]
    public void The_parts_list_shows_a_switch_with_its_pole_count()
    {
        var circuit = NetlistLoader.Load(Divider);
        var row = Assert.Single(PartsList.From(circuit, PartMap.Resolve(circuit, Table)).Rows, r => r.References == "RSW1");

        Assert.Equal("switch-2", row.Kind);
        Assert.Equal("SPDT", row.Value);
    }
}
