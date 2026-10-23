namespace Ssp.Core;

/// <summary>Chooses the nodes that a result view shows. The JSON result keeps every node.</summary>
public static class NodeView
{
    /// <summary>The name of the ground node. It is always 0 V, so a view leaves it out.</summary>
    public const string Ground = "0";

    /// <summary>True if the name belongs to a subcircuit, for example <c>X1.lo</c>. A top-level name has no dot.</summary>
    public static bool IsInternal(string name) => name.Contains('.');

    /// <summary>
    /// The names to show. Ground is left out. A subcircuit name is left out unless <paramref name="includeInternal"/> is true.
    /// The <paramref name="output"/> name comes first. The other names follow in ordinal order.
    /// </summary>
    public static List<string> Names(IEnumerable<string> names, string? output, bool includeInternal) => names
        .Where(n => n != Ground && (includeInternal || !IsInternal(n)))
        .OrderBy(n => n != output)
        .ThenBy(n => n, StringComparer.Ordinal)
        .ToList();
}
