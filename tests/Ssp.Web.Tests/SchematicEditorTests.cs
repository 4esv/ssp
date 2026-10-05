using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Core.Layout;
using Ssp.Core.Netlist;
using Ssp.Web.Components;
using Ssp.Web.Hosting;
using Ssp.Web.Pages;
using Ssp.Web.Schematic;
using SpiceSharp.Components;
using Point = Ssp.Core.Layout.Point;

namespace Ssp.Web.Tests;

public class SchematicEditorTests : BunitContext
{
    public SchematicEditorTests() => Services.AddSingleton<TimeProvider>(new ManualTimeProvider());

    // NOTE: R2 is not on the flow from node a.
    const string TwoResistors = "* two resistors\nV1 in 0 1\nR1 in a 1k\nR2 b 0 1k\n.END\n";

    // The position of a pin of a part in the auto-layout.
    static Point PinAt(string reference, int pin)
    {
        var circuit = NetlistLoader.Load(TwoResistors);
        var placement = AutoPlacer.Place(circuit, circuit.Directives).Parts.Single(p => p.Reference == reference);
        var (x, y) = SchematicRenderer.Place(placement, pin * 60, 0);
        return new Point(Math.Round(x, 2) + 0.0, Math.Round(y, 2) + 0.0);
    }

    readonly List<SchematicChange> changes = [];

    IRenderedComponent<SchematicEditor> Editor(string netlist) => Render<SchematicEditor>(p => p
        .Add(c => c.Netlist, netlist)
        .Add(c => c.Changed, (SchematicChange change) => changes.Add(change)));

    static LoadedCircuit Loads(SchematicChange change)
    {
        var circuit = NetlistLoader.Load(change.Netlist);
        Assert.DoesNotContain(circuit.Diagnostics, d => d.Severity == Severity.Error);
        Assert.Empty(change.Layout.Validate(circuit));
        return circuit;
    }

    static IReadOnlyList<string> Nodes(LoadedCircuit circuit, string name) =>
        circuit.Circuit.OfType<IComponent>().Single(c => c.Name == name).Nodes;

    static void ClickPin(IRenderedComponent<SchematicEditor> editor, string reference, int pin) =>
        editor.Find($"circle.pin[data-ref=\"{reference}\"][data-pin=\"{pin}\"]").Click();

    // NOTE: Tap a pin, tap a + dot, pick Connect to a pin, tap the second pin.
    static void JoinPins(IRenderedComponent<SchematicEditor> editor, string reference, int pin, string other, int otherPin)
    {
        ClickPin(editor, reference, pin);
        editor.Find("g.pin-add").Click();
        editor.Find(".pin-menu button[data-action=connect]").Click();
        ClickPin(editor, other, otherPin);
    }

    [Fact]
    public void PlaceResistorWritesNetlistAndLayout()
    {
        var editor = Editor("");

        editor.Find("button.place[data-kind=resistor]").Click();

        var change = Assert.Single(changes);
        Assert.Contains(change.Netlist.Split('\n'), line => line.StartsWith("R1 ", StringComparison.Ordinal));
        Assert.Contains(change.Layout.Parts, p => p.Reference == "R1");
        Assert.Equal(2, Nodes(Loads(change), "R1").Distinct().Count());
    }

    [Fact]
    public void PlaceGivesFreshNamesAndKeepsOtherParts()
    {
        var editor = Editor(TwoResistors);

        editor.Find("button.place[data-kind=resistor]").Click();
        editor.Find("button.place[data-kind=capacitor]").Click();

        var circuit = Loads(changes[^1]);
        Assert.Equal(["C1", "R1", "R2", "R3", "V1"], circuit.Circuit.OfType<IComponent>().Select(c => c.Name).Order(StringComparer.Ordinal));
        var added = Nodes(circuit, "R3").Concat(Nodes(circuit, "C1")).ToList();
        Assert.Equal(4, added.Distinct().Count());
        Assert.DoesNotContain(added, n => n is "in" or "a" or "b" or "0");
        Assert.Equal(["C1", "R1", "R2", "R3", "V1"], changes[^1].Layout.Parts.Select(p => p.Reference).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void WireBetweenTwoPinsJoinsTheirNodes()
    {
        var editor = Editor(TwoResistors);

        JoinPins(editor, "R1", 1, "R2", 0);

        var change = Assert.Single(changes);
        var circuit = Loads(change);
        Assert.Equal(Nodes(circuit, "R1")[1], Nodes(circuit, "R2")[0]);
        Assert.Equal(3, circuit.NodeNames.Count);
        var wire = change.Layout.Wires[^1];
        Assert.Equal(Nodes(circuit, "R1")[1], wire.Net);
        Assert.Equal(PinAt("R1", 1), wire.Points[0]);
        Assert.Equal(PinAt("R2", 0), wire.Points[^1]);
    }

    [Fact]
    public void WireToGroundKeepsNodeZero()
    {
        var editor = Editor(TwoResistors);

        JoinPins(editor, "R1", 1, "R2", 1);

        Assert.Equal(["in", "0"], Nodes(Loads(changes.Single()), "R1"));
    }

    [Fact]
    public void WireRenamesTheNodeInDirectives()
    {
        var editor = Editor("* ssp:output b\n" + TwoResistors);

        JoinPins(editor, "R1", 1, "R2", 0);

        Assert.Equal("a", Loads(changes.Single()).Directives.Output);
    }

    [Fact]
    public void GroundConnectsTheNodeToZero()
    {
        var editor = Editor(TwoResistors);

        editor.Find("button.tool[data-tool=ground]").Click();
        ClickPin(editor, "R1", 1);

        var change = Assert.Single(changes);
        Assert.Equal(["in", "0"], Nodes(Loads(change), "R1"));
        Assert.Contains(change.Layout.Wires, w => w.Net == "0" && w.Points[0] == PinAt("R1", 1));
    }

    [Fact]
    public void EditorPagePlaceUpdatesTheNetlist()
    {
        Services.AddSingleton<ISimulationHost, InProcessSimulationHost>();
        var page = Render<Editor>();
        // NOTE: The editor opens with the starter circuit. This test places parts on an empty netlist.
        page.Find("textarea").Input("");

        page.Find("button.place[data-kind=resistor]").Click();
        page.Find("button.place[data-kind=resistor]").Click();

        var netlist = page.Find("textarea").GetAttribute("value") ?? "";
        Assert.Contains("\nR1 ", netlist);
        Assert.Contains("\nR2 ", netlist);
        Assert.Equal(["R1", "R2"], page.FindAll(".schematic g[data-ref]").Select(g => g.GetAttribute("data-ref")));
    }
}
