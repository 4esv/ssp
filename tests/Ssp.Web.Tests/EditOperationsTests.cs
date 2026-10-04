using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Ssp.Core.Layout;
using Ssp.Core.Netlist;
using Ssp.Web.Components;
using Ssp.Web.Schematic;
using SpiceSharp.Components;

namespace Ssp.Web.Tests;

public class EditOperationsTests : BunitContext
{
    const string TwoResistors = "* two resistors\nV1 in 0 1\nR1 in a 1k\nR2 b 0 1k\n.END\n";

    // The placement that auto-layout gives to a part. The tests start from it, so they do not depend on where the layout puts parts.
    static PartPlacement Start(string reference)
    {
        var circuit = NetlistLoader.Load(TwoResistors);
        return AutoPlacer.Place(circuit, circuit.Directives).Parts.Single(p => p.Reference == reference);
    }

    readonly List<SchematicChange> changes = [];

    IRenderedComponent<SchematicEditor> Editor(string netlist) => Render<SchematicEditor>(p => p
        .Add(c => c.Netlist, netlist)
        .Add(c => c.Changed, (SchematicChange change) => changes.Add(change)));

    static IRenderedComponent<SchematicEditor> Select(IRenderedComponent<SchematicEditor> editor, string reference)
    {
        editor.Find($"rect.part[data-ref=\"{reference}\"]").Click();
        return editor;
    }

    static void Press(IRenderedComponent<SchematicEditor> editor, string key, bool meta = false) =>
        editor.Find("figure.schematic").KeyDown(new KeyboardEventArgs { Key = key, MetaKey = meta });

    static LoadedCircuit Loads(SchematicChange change)
    {
        var circuit = NetlistLoader.Load(change.Netlist);
        Assert.DoesNotContain(circuit.Diagnostics, d => d.Severity == Severity.Error);
        Assert.Empty(change.Layout.Validate(circuit));
        return circuit;
    }

    static PartPlacement Placement(SchematicChange change, string reference) =>
        change.Layout.Parts.Single(p => p.Reference == reference);

    static string Fmt(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    static IReadOnlyList<string> Names(LoadedCircuit circuit) =>
        circuit.Circuit.OfType<IComponent>().Select(c => c.Name).Order(StringComparer.Ordinal).ToList();

    [Fact]
    public void ClickSelectsThePart()
    {
        var editor = Select(Editor(TwoResistors), "R1");

        Assert.Equal(["R1"], editor.FindAll("rect.part.selected").Select(r => r.GetAttribute("data-ref")));
    }

    [Fact]
    public void KeyWithNoSelectionDoesNothing()
    {
        var editor = Editor(TwoResistors);

        Press(editor, "r");
        Press(editor, "Delete");

        Assert.Empty(changes);
    }

    [Fact]
    public void RRotatesThePart()
    {
        var editor = Select(Editor(TwoResistors), "R1");

        Press(editor, "r");

        var change = Assert.Single(changes);
        Loads(change);
        Assert.Equal(Start("R1") with { Rotation = (Start("R1").Rotation + 90) % 360 }, Placement(change, "R1"));
        Assert.Equal(TwoResistors, change.Netlist);
    }

    [Fact]
    public void RFourTimesGivesTheFirstRotation()
    {
        var editor = Select(Editor(TwoResistors), "R1");

        for (var i = 0; i < 4; i++) Press(editor, "R");

        Assert.Equal(Start("R1").Rotation, Placement(changes[^1], "R1").Rotation);
    }

    [Fact]
    public void HFlipsThePartLeftToRight()
    {
        var editor = Select(Editor(TwoResistors), "R1");

        Press(editor, "h");

        var change = Assert.Single(changes);
        Loads(change);
        var expected = Start("R1") with { Rotation = (Start("R1").Rotation + 180) % 360, Flip = !Start("R1").Flip };
        Assert.Equal(expected, Placement(change, "R1"));
        // NOTE: R1 has its pins at x 0 and x 60. A left-right flip mirrors the second pin about the vertical line through the first.
        var (x, y) = SchematicRenderer.Place(Start("R1"), 60, 0);
        var pin = editor.Find("circle.pin[data-ref=R1][data-pin=\"1\"]");
        Assert.Equal((Fmt(2 * Start("R1").X - x), Fmt(y)), (pin.GetAttribute("cx"), pin.GetAttribute("cy")));
    }

    [Fact]
    public void VFlipsThePartTopToBottom()
    {
        var editor = Select(Editor(TwoResistors), "R1");

        Press(editor, "v");

        var change = Assert.Single(changes);
        Loads(change);
        Assert.Equal(Start("R1") with { Flip = !Start("R1").Flip }, Placement(change, "R1"));
    }

    [Theory]
    [InlineData("ArrowLeft", -10, 0)]
    [InlineData("ArrowRight", 10, 0)]
    [InlineData("ArrowUp", 0, -10)]
    [InlineData("ArrowDown", 0, 10)]
    public void ArrowMovesThePartOneGridStep(string key, double dx, double dy)
    {
        var editor = Select(Editor(TwoResistors), "R1");

        Press(editor, key);

        var change = Assert.Single(changes);
        Loads(change);
        Assert.Equal(Start("R1") with { X = Start("R1").X + dx, Y = Start("R1").Y + dy }, Placement(change, "R1"));
        Assert.Equal(TwoResistors, change.Netlist);
    }

    [Fact]
    public void DeleteRemovesThePartFromTheNetlistAndTheLayout()
    {
        var editor = Select(Editor(TwoResistors), "R1");

        Press(editor, "Delete");

        var change = Assert.Single(changes);
        var circuit = Loads(change);
        Assert.Equal(["R2", "V1"], Names(circuit));
        Assert.DoesNotContain(change.Layout.Parts, p => p.Reference == "R1");
        Assert.Empty(editor.FindAll("rect.part[data-ref=R1]"));
        Assert.Empty(editor.FindAll("rect.part.selected"));
    }

    [Fact]
    public void BackspaceAlsoDeletes()
    {
        var editor = Select(Editor(TwoResistors), "R2");

        Press(editor, "Backspace");

        Assert.Equal(["R1", "V1"], Names(Loads(Assert.Single(changes))));
    }

    [Fact]
    public void CmdDDuplicatesWithTheNextFreeReference()
    {
        var editor = Select(Editor(TwoResistors), "R1");

        Press(editor, "d", meta: true);

        var change = Assert.Single(changes);
        var circuit = Loads(change);
        Assert.Equal(["R1", "R2", "R3", "V1"], Names(circuit));
        Assert.Contains(change.Netlist.Split('\n'), line => line.StartsWith("R3 ", StringComparison.Ordinal) && line.EndsWith(" 1k", StringComparison.Ordinal));
        var nodes = circuit.Circuit.OfType<IComponent>().Single(c => c.Name == "R3").Nodes;
        Assert.Equal(2, nodes.Distinct().Count());
        Assert.DoesNotContain(nodes, n => n is "in" or "a" or "b" or "0");
        var right = AutoPlacer.Place(NetlistLoader.Load(TwoResistors), NetlistLoader.Load(TwoResistors).Directives).Parts.Max(p => p.X);
        Assert.Equal(Start("R1") with { Reference = "R3", X = right + 140 }, Placement(change, "R3"));
        Assert.Equal(["R3"], editor.FindAll("rect.part.selected").Select(r => r.GetAttribute("data-ref")));
    }

    [Fact]
    public void DWithoutCmdDuplicates()
    {
        var editor = Select(Editor(TwoResistors), "R1");

        Press(editor, "d");

        Assert.Single(changes);
    }
}
