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
    // NOTE: Auto-placement puts R1 at (0, 30), V1 at (140, 30) and R2 at (280, 30).
    const string TwoResistors = "* two resistors\nV1 in 0 1\nR1 in a 1k\nR2 b 0 1k\n.END\n";

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
        Assert.Equal(new PartPlacement("R1", 0, 30, 90, false), Placement(change, "R1"));
        Assert.Equal(TwoResistors, change.Netlist);
    }

    [Fact]
    public void RFourTimesGivesTheFirstRotation()
    {
        var editor = Select(Editor(TwoResistors), "R1");

        for (var i = 0; i < 4; i++) Press(editor, "R");

        Assert.Equal(0, Placement(changes[^1], "R1").Rotation);
    }

    [Fact]
    public void HFlipsThePartLeftToRight()
    {
        var editor = Select(Editor(TwoResistors), "R1");

        Press(editor, "h");

        var change = Assert.Single(changes);
        Loads(change);
        Assert.Equal(new PartPlacement("R1", 0, 30, 180, true), Placement(change, "R1"));
        // NOTE: R1 has its pins at x 0 and x 60. A left-right flip about x 0 puts the second pin at x -60.
        var pin = editor.Find("circle.pin[data-ref=R1][data-pin=\"1\"]");
        Assert.Equal(("-60", "30"), (pin.GetAttribute("cx"), pin.GetAttribute("cy")));
    }

    [Fact]
    public void VFlipsThePartTopToBottom()
    {
        var editor = Select(Editor(TwoResistors), "R1");

        Press(editor, "v");

        var change = Assert.Single(changes);
        Loads(change);
        Assert.Equal(new PartPlacement("R1", 0, 30, 0, true), Placement(change, "R1"));
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
        Assert.Equal(new PartPlacement("R1", dx, 30 + dy, 0, false), Placement(change, "R1"));
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
        Assert.Equal(new PartPlacement("R3", 420, 30, 0, false), Placement(change, "R3"));
        Assert.Equal(["R3"], editor.FindAll("rect.part.selected").Select(r => r.GetAttribute("data-ref")));
    }

    [Fact]
    public void DWithoutCmdDoesNotDuplicate()
    {
        var editor = Select(Editor(TwoResistors), "R1");

        Press(editor, "d");

        Assert.Empty(changes);
    }
}
