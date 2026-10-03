using Ssp.Core.Netlist;
using Ssp.Web.Editing;
using Ssp.Web.Schematic;
using LayoutDoc = Ssp.Core.Layout.Layout;

namespace Ssp.Web.Tests;

public class CommandStackTests
{
    const string Divider = "* divider\nV1 in 0 1\nR1 in out 1k\nR2 out 0 2k\n.END\n";

    static LayoutDoc Placed(string netlist)
    {
        var circuit = NetlistLoader.Load(netlist);
        return AutoPlacer.Place(circuit, circuit.Directives);
    }

    static CommandStack Stack()
    {
        var stack = new CommandStack();
        stack.Reset(Divider, Placed(Divider));
        return stack;
    }

    static ChangeCommand PlaceResistor(CommandStack stack) =>
        new(SchematicEdits.Place(stack.Netlist, stack.Layout!, "resistor"));

    [Fact]
    public void UndoRestoresThePriorText()
    {
        var stack = Stack();

        stack.Do(PlaceResistor(stack));
        Assert.NotEqual(Divider, stack.Netlist);
        stack.Undo();

        Assert.Equal(Divider, stack.Netlist);
    }

    [Fact]
    public void UndoRestoresThePriorLayout()
    {
        var stack = Stack();
        var before = stack.Layout!;

        stack.Do(new ChangeCommand(SchematicEdits.Move(stack.Netlist, before, "R1", 20, 0)));
        Assert.NotEqual(before, stack.Layout);
        stack.Undo();

        Assert.Equal(before, stack.Layout);
    }

    [Fact]
    public void RedoAfterUndoGivesTheTextAfterTheEdit()
    {
        var stack = Stack();
        var edit = PlaceResistor(stack);

        stack.Do(edit);
        stack.Undo();
        stack.Redo();

        Assert.Equal(edit.After.Netlist, stack.Netlist);
        Assert.Equal(edit.After.Layout, stack.Layout);
    }

    [Fact]
    public void NewEditAfterUndoClearsTheRedoStack()
    {
        var stack = Stack();
        stack.Do(PlaceResistor(stack));
        stack.Undo();
        Assert.True(stack.CanRedo);

        var other = new ChangeCommand(SchematicEdits.Rotate(stack.Netlist, stack.Layout!, "R2"));
        stack.Do(other);
        stack.Redo();

        Assert.False(stack.CanRedo);
        Assert.Equal(other.After.Netlist, stack.Netlist);
        Assert.Equal(other.After.Layout, stack.Layout);
    }

    [Fact]
    public void UndoGoesBackOneEditAtATime()
    {
        var stack = Stack();
        stack.Do(PlaceResistor(stack));
        var middle = stack.Netlist;
        stack.Do(PlaceResistor(stack));

        stack.Undo();
        Assert.Equal(middle, stack.Netlist);
        stack.Undo();
        Assert.Equal(Divider, stack.Netlist);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void UndoAndRedoWithEmptyStacksDoNothing()
    {
        var stack = Stack();
        var layout = stack.Layout;

        stack.Undo();
        stack.Redo();

        Assert.Equal(Divider, stack.Netlist);
        Assert.Same(layout, stack.Layout);
    }

    [Fact]
    public void ResetClearsTheHistory()
    {
        var stack = Stack();
        stack.Do(PlaceResistor(stack));
        stack.Do(PlaceResistor(stack));
        stack.Undo();

        stack.Reset("* typed\n.END\n", null);

        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
        Assert.Equal("* typed\n.END\n", stack.Netlist);
        Assert.Null(stack.Layout);
    }
}
