using Microsoft.AspNetCore.Components.Web;

namespace Ssp.Web.Editing;

/// <summary>One editor action and its key. Needs Ctrl or Cmd unless <see cref="Bare"/> is set.</summary>
public sealed record Shortcut(string Id, string Label, string Key, bool Shift = false, bool Bare = false)
{
    /// <summary>The key as the sheet shows it.</summary>
    public string Keys => Bare ? Key : $"Ctrl+{(Shift ? "Shift+" : "")}{KeyName} or Cmd+{(Shift ? "Shift+" : "")}{KeyName}";

    string KeyName => Key.Length == 1 ? Key.ToUpperInvariant() : Key;

    /// <summary>Whether a typed key is this shortcut. A bare key keeps its own Shift (the ? key needs it).</summary>
    public bool Matches(KeyboardEventArgs e) =>
        string.Equals(e.Key, Key, StringComparison.OrdinalIgnoreCase)
        && (Bare ? !e.CtrlKey && !e.MetaKey && !e.AltKey : (e.CtrlKey || e.MetaKey) && !e.AltKey && e.ShiftKey == Shift);

    /// <summary>The palette and the sheet and the key handler read this one table.</summary>
    public static readonly IReadOnlyList<Shortcut> All =
    [
        new("run", "Run", "Enter"),
        new("undo", "Undo", "z"),
        new("redo", "Redo", "z", Shift: true),
        new("share", "Share link", "l", Shift: true),
        new("svg", "Download SVG", "e", Shift: true),
        new("palette", "Open the command palette", "k"),
        new("shortcuts", "Show the shortcuts", "?", Bare: true),
    ];
}

/// <summary>The table, as a type the editor and the tests use.</summary>
public static class Shortcuts
{
    public static IReadOnlyList<Shortcut> All => Shortcut.All;

    public static Shortcut? Find(KeyboardEventArgs e) => All.FirstOrDefault(s => s.Matches(e));
}
