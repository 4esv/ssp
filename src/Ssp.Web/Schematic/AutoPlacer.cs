using Ssp.Core.Layout;
using Ssp.Core.Netlist;
using SpiceSharp.Components;
using LayoutDoc = Ssp.Core.Layout.Layout;
using Point = Ssp.Core.Layout.Point;

namespace Ssp.Web.Schematic;

/// <summary>Makes a layout for a circuit that has none.</summary>
public static class AutoPlacer
{
    // NOTE: Each part gets the space of the largest symbol: 80 wide, from 30 above to 70 below its origin.
    // The placer does not know the part kinds, so the space also holds the fallback box for the pin count.
    const int Width = 80;
    const int Top = -30;
    const int Bottom = 70;
    const int Gap = 4 * Symbols.Grid;
    const int ColumnStep = Width + 6 * Symbols.Grid;
    const int PinStep = 2 * Symbols.Grid;
    const int FallbackWidth = 60;

    static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Places the parts in columns from left to right by signal flow. The first column has the parts on the
    /// <c>* ssp:input</c> node. The parts after the <c>* ssp:output</c> node come last. Parts that the flow does not
    /// reach go in one more column. Parts in a column are in name order. Each net is a set of wires with orthogonal
    /// segments. The layout is the same for the same netlist.
    /// </summary>
    public static LayoutDoc Place(LoadedCircuit circuit, Directives directives)
    {
        var parts = circuit.Circuit.OfType<IComponent>()
            .OrderBy(c => c.Name, Names).ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();
        var columns = Columns(parts, directives);

        var placements = new List<PartPlacement>();
        var pins = new Dictionary<string, List<Point>>(Names);
        foreach (var column in parts.GroupBy(c => columns[c.Name]).OrderBy(g => g.Key))
        {
            double y = 0;
            foreach (var c in column)
            {
                var x = column.Key * ColumnStep;
                y -= Top;
                placements.Add(new PartPlacement(c.Name, x, y, 0, false));
                for (var i = 0; i < c.Nodes.Count; i++)
                {
                    // NOTE: Same pin positions as the fallback box in SchematicRenderer.
                    var pin = new Point(x + (i % 2 == 0 ? 0 : FallbackWidth), y + PinStep * (i / 2));
                    if (!pins.TryGetValue(c.Nodes[i], out var list)) pins[c.Nodes[i]] = list = [];
                    list.Add(pin);
                }
                y += Math.Max(Bottom, PinStep * ((c.Nodes.Count + 1) / 2 - 1) + Symbols.Grid) + Gap;
            }
        }

        var wires = new List<WireRoute>();
        foreach (var (net, points) in pins.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            foreach (var (a, b) in sorted.Zip(sorted.Skip(1)))
            {
                var corner = new Point(b.X, a.Y);
                wires.Add(new WireRoute(net, corner == a || corner == b ? [a, b] : [a, corner, b]));
            }
        }

        return new LayoutDoc(placements.OrderBy(p => p.Reference, Names).ThenBy(p => p.Reference, StringComparer.Ordinal).ToList(), wires);
    }

    // The column of each part. A breadth-first search over nets from the input node. Ground is not crossed.
    // The search stops at the output node, then goes on from it in the next column.
    static Dictionary<string, int> Columns(IReadOnlyList<IComponent> parts, Directives directives)
    {
        var onNet = new Dictionary<string, List<IComponent>>(Names);
        foreach (var c in parts)
        {
            foreach (var node in c.Nodes.Distinct(Names))
            {
                if (!onNet.TryGetValue(node, out var list)) onNet[node] = list = [];
                list.Add(c);
            }
        }

        var columns = new Dictionary<string, int>(Names);
        var seen = new HashSet<string>(Names);
        var input = directives.Input ?? onNet.Keys.Where(n => !IsGround(n)).Order(StringComparer.Ordinal).FirstOrDefault();
        var output = directives.Output;

        void Walk(string start, int first, string? stop)
        {
            if (!onNet.ContainsKey(start) || !seen.Add(start)) return;
            var queue = new Queue<(string Net, int Column)>([(start, first)]);
            while (queue.TryDequeue(out var item))
            {
                if (stop is not null && Names.Equals(item.Net, stop) && !Names.Equals(item.Net, start)) continue;
                foreach (var c in onNet[item.Net])
                {
                    if (!columns.TryAdd(c.Name, item.Column)) continue;
                    foreach (var node in c.Nodes)
                    {
                        if (!IsGround(node) && seen.Add(node)) queue.Enqueue((node, item.Column + 1));
                    }
                }
            }
        }

        int Next() => columns.Count == 0 ? 0 : columns.Values.Max() + 1;

        if (input is not null) Walk(input, 0, output);
        if (output is not null)
        {
            seen.Remove(output);
            Walk(output, Next(), null);
        }
        var rest = Next();
        foreach (var c in parts) columns.TryAdd(c.Name, rest);
        return columns;
    }

    static bool IsGround(string node) => node == "0" || Names.Equals(node, "gnd");
}
