using Bunit;
using Ssp.Core.Netlist;
using Ssp.Web.Components;
using Ssp.Web.Schematic;
using SpiceSharp.Components;

namespace Ssp.Web.Tests;

public class CalculatorPanelTests : BunitContext
{
    const string TwoResistors = "* two resistors\nV1 in 0 1\nR1 in a 1k\nR2 a 0 1k\n.END\n";

    readonly List<SchematicChange> changes = [];

    IRenderedComponent<CalculatorPanel> Panel(string netlist) => Render<CalculatorPanel>(p => p
        .Add(c => c.Netlist, netlist)
        .Add(c => c.Changed, (SchematicChange change) => changes.Add(change)));

    static LoadedCircuit Loads(SchematicChange change)
    {
        var circuit = NetlistLoader.Load(change.Netlist);
        Assert.DoesNotContain(circuit.Diagnostics, d => d.Severity == Severity.Error);
        Assert.Empty(change.Layout.Validate(circuit));
        return circuit;
    }

    static string Line(SchematicChange change, string reference) =>
        change.Netlist.Split('\n').Single(l => l.StartsWith(reference + " ", StringComparison.Ordinal));

    [Fact]
    public void InsertAddsPartsAndLayoutEntries()
    {
        var panel = Panel(TwoResistors);

        panel.Find("button.write").Click();

        var change = Assert.Single(changes);
        var circuit = Loads(change);
        Assert.Equal(5, circuit.Circuit.OfType<IComponent>().Count());
        Assert.EndsWith(" 10k", Line(change, "R3"), StringComparison.Ordinal);
        Assert.EndsWith(" 100n", Line(change, "C1"), StringComparison.Ordinal);
        Assert.Equal(5, change.Layout.Parts.Count);
        Assert.Contains(change.Layout.Parts, p => p.Reference == "R3");
        Assert.Contains(change.Layout.Parts, p => p.Reference == "C1");
    }

    [Fact]
    public void UpdateChangesOnlyTheSelectedPart()
    {
        var panel = Panel(TwoResistors);
        panel.Find("select.calculator").Change("led");

        panel.Find("select.target[data-part=R]").Change("R2");
        panel.Find("button.write").Click();

        var change = Assert.Single(changes);
        Loads(change);
        Assert.Equal(TwoResistors.Replace("R2 a 0 1k", "R2 a 0 700", StringComparison.Ordinal), change.Netlist);
        Assert.Equal(3, change.Layout.Parts.Count);
    }

    [Fact]
    public void InputsChangeTheWrittenValues()
    {
        var panel = Panel(TwoResistors);

        panel.Find("input[data-input=R]").Change("4700");
        panel.Find("select.target[data-part=R]").Change("R1");
        panel.Find("button.write").Click();

        var change = Assert.Single(changes);
        Assert.Equal("R1 in a 4.7k", Line(change, "R1"));
        Assert.EndsWith(" 100n", Line(change, "C1"), StringComparison.Ordinal);
    }

    [Fact]
    public void UndoRevertsTheWriteBack()
    {
        var panel = Panel(TwoResistors);
        Assert.True(panel.Find("button.undo").HasAttribute("disabled"));

        panel.Find("button.write").Click();
        panel.Find("button.undo").Click();

        Assert.Equal(2, changes.Count);
        Assert.Equal(TwoResistors, changes[1].Netlist);
        Assert.Equal(3, changes[1].Layout.Parts.Count);
        Assert.True(panel.Find("button.undo").HasAttribute("disabled"));
    }

    [Fact]
    public void NetlistWithErrorsCannotBeWritten()
    {
        var panel = Panel("* bad\nR1 a\n.END\n");

        Assert.True(panel.Find("button.write").HasAttribute("disabled"));
    }
}
