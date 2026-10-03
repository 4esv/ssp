using Ssp.Web.Schematic;
using LayoutDoc = Ssp.Core.Layout.Layout;

namespace Ssp.Web.Editing;

/// <summary>One edit of the netlist and the layout.</summary>
public interface ICommand
{
    /// <summary>Gives the netlist and the layout after the edit. A null layout is the auto-placement.</summary>
    SchematicChange Apply(string netlist, LayoutDoc? layout);
}

/// <summary>An edit that the schematic editor or the calculator panel has already made.</summary>
public sealed record ChangeCommand(SchematicChange After) : ICommand
{
    /// <inheritdoc />
    public SchematicChange Apply(string netlist, LayoutDoc? layout) => After;
}

/// <summary>The undo and redo history of the netlist and the layout.</summary>
public sealed class CommandStack
{
    readonly record struct State(string Netlist, LayoutDoc? Layout);

    readonly Stack<State> undo = new();
    readonly Stack<State> redo = new();
    State current = new("", null);

    /// <summary>The netlist after the last edit, undo or redo.</summary>
    public string Netlist => current.Netlist;

    /// <summary>The layout after the last edit, undo or redo. Null is the auto-placement.</summary>
    public LayoutDoc? Layout => current.Layout;

    public bool CanUndo => undo.Count > 0;

    public bool CanRedo => redo.Count > 0;

    /// <summary>Sets the netlist and the layout from outside the history, for example from a text edit, and clears the history.</summary>
    public void Reset(string netlist, LayoutDoc? layout)
    {
        current = new State(netlist, layout);
        undo.Clear();
        redo.Clear();
    }

    /// <summary>Applies the edit. The edit clears the redo stack.</summary>
    public void Do(ICommand c)
    {
        var after = c.Apply(current.Netlist, current.Layout);
        undo.Push(current);
        redo.Clear();
        current = new State(after.Netlist, after.Layout);
    }

    public void Undo()
    {
        if (undo.Count == 0) return;
        redo.Push(current);
        current = undo.Pop();
    }

    public void Redo()
    {
        if (redo.Count == 0) return;
        undo.Push(current);
        current = redo.Pop();
    }
}
