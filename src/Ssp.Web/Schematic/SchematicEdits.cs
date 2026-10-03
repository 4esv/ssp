using System.Text.RegularExpressions;
using Ssp.Core.Layout;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using SpiceSharp.Components;
using LayoutDoc = Ssp.Core.Layout.Layout;
using Point = Ssp.Core.Layout.Point;

namespace Ssp.Web.Schematic;

/// <summary>A netlist and its layout after an edit.</summary>
public sealed record SchematicChange(string Netlist, LayoutDoc Layout);

/// <summary>One pin of a placed component. The pin index is the node index.</summary>
public sealed record PinRef(string Reference, int Pin);

/// <summary>The edits of the schematic editor. Each edit writes the netlist and the layout.</summary>
public static partial class SchematicEdits
{
    const string Zero = "0";
    const int ColumnStep = 140;
    const int Row = 30;

    /// <summary>The kinds that <see cref="Place"/> can add: the reference prefix and the default value.</summary>
    public static readonly IReadOnlyDictionary<string, (string Prefix, string Value)> Kinds = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
    {
        ["resistor"] = ("R", "10k"),
        ["capacitor"] = ("C", "100n"),
    };

    static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Adds a two-pin part with a new reference and two new nodes. The layout places it one column to the right of
    /// the other parts.
    /// </summary>
    public static SchematicChange Place(string netlist, LayoutDoc layout, string kind)
    {
        var (prefix, value) = Kinds[kind];
        var circuit = NetlistLoader.Load(netlist);
        var references = circuit.Circuit.Select(e => e.Name.Split('.')[0]).ToHashSet(Names);
        var reference = Fresh(prefix, references);
        var nodes = new HashSet<string>(circuit.NodeNames, StringComparer.Ordinal);
        var a = Fresh("n", nodes);
        nodes.Add(a);
        var b = Fresh("n", nodes);

        var lines = Lines(netlist);
        // NOTE: The first line of a netlist is its title. A part on it is not read.
        if (lines.Count == 0) lines.Add("* schematic");
        var end = lines.FindIndex(1, l => l.Trim().Equals(".end", StringComparison.OrdinalIgnoreCase));
        lines.Insert(end < 0 ? lines.Count : end, $"{reference} {a} {b} {value}");

        var x = layout.Parts.Count == 0 ? 0 : layout.Parts.Max(p => p.X) + ColumnStep;
        var parts = layout.Parts.Append(new PartPlacement(reference, x, Row, 0, false)).ToList();
        return new SchematicChange(Join(lines), new LayoutDoc(parts, layout.Wires));
    }

    /// <summary>
    /// Joins the nodes of two pins. The joined node is node 0 if one of the pins is on it, else the node of the first pin.
    /// The layout gets a wire between the two pins.
    /// </summary>
    public static SchematicChange Wire(string netlist, LayoutDoc layout, PartMap parts, PinRef from, PinRef to)
    {
        var circuit = NetlistLoader.Load(netlist);
        var (a, pa) = Pin(circuit, layout, parts, from);
        var (b, pb) = Pin(circuit, layout, parts, to);
        var (keep, drop) = b == Zero ? (b, a) : (a, b);

        var corner = new Point(pb.X, pa.Y);
        var wire = new WireRoute(keep, corner == pa || corner == pb ? [pa, pb] : [pa, corner, pb]);
        return Rename(netlist, circuit, layout, drop, keep, [wire]);
    }

    /// <summary>Connects the node of a pin to node 0. The layout gets a ground symbol below the pin, drawn as wires.</summary>
    public static SchematicChange Ground(string netlist, LayoutDoc layout, PartMap parts, PinRef pin)
    {
        var circuit = NetlistLoader.Load(netlist);
        var (node, p) = Pin(circuit, layout, parts, pin);
        double Y(int dy) => p.Y + dy;
        WireRoute Bar(int dy, int half) => new(Zero, [new Point(p.X - half, Y(dy)), new Point(p.X + half, Y(dy))]);
        return Rename(netlist, circuit, layout, node, Zero,
            [new WireRoute(Zero, [p, new Point(p.X, Y(20))]), Bar(20, 10), Bar(24, 6), Bar(28, 2)]);
    }

    /// <summary>The node and schematic position of a pin.</summary>
    public static (string Node, Point Position) Pin(LoadedCircuit circuit, LayoutDoc layout, PartMap parts, PinRef pin)
    {
        var component = circuit.Circuit.OfType<IComponent>().Single(c => Names.Equals(c.Name, pin.Reference));
        var placement = layout.Parts.Single(p => Names.Equals(p.Reference, pin.Reference));
        var positions = SchematicRenderer.Pins(component, placement, parts.Parts.GetValueOrDefault(component.Name));
        return (component.Nodes[pin.Pin], positions[pin.Pin]);
    }

    /// <summary>Turns a part 90 degrees counter-clockwise on screen. The netlist does not change.</summary>
    public static SchematicChange Rotate(string netlist, LayoutDoc layout, string reference) =>
        Edit(netlist, layout, reference, p => p with { Rotation = Norm(p.Rotation + 90) });

    /// <summary>
    /// Flips a part about its origin, left to right or top to bottom on screen. The netlist does not change.
    /// </summary>
    // NOTE: The layout flip is top to bottom, before the rotation. A left-to-right flip is that flip and a half turn.
    public static SchematicChange Flip(string netlist, LayoutDoc layout, string reference, bool leftToRight) =>
        Edit(netlist, layout, reference, p => p with
        {
            Rotation = Norm((leftToRight ? 180 : 0) - p.Rotation),
            Flip = !p.Flip,
        });

    /// <summary>Moves a part by a distance in schematic units. The netlist does not change.</summary>
    public static SchematicChange Move(string netlist, LayoutDoc layout, string reference, double dx, double dy) =>
        Edit(netlist, layout, reference, p => p with { X = p.X + dx, Y = p.Y + dy });

    /// <summary>
    /// Removes a part from the netlist and the layout. The layout also loses the wires on nets that the netlist no
    /// longer has.
    /// </summary>
    public static SchematicChange Delete(string netlist, LayoutDoc layout, string reference)
    {
        var lines = Lines(netlist);
        var (at, count) = Element(lines, reference);
        lines.RemoveRange(at, count);
        var result = Join(lines);

        var nets = new HashSet<string>(NetlistLoader.Load(result).NodeNames, StringComparer.Ordinal);
        var parts = layout.Parts.Where(p => !Names.Equals(p.Reference, reference)).ToList();
        var wires = layout.Wires.Where(w => nets.Contains(w.Net)).ToList();
        return new SchematicChange(result, new LayoutDoc(parts, wires));
    }

    /// <summary>
    /// Copies a part with the next free reference of its prefix and a new node for each pin. The layout places the
    /// copy one column to the right of the other parts, with the rotation and flip of the part.
    /// </summary>
    public static SchematicChange Duplicate(string netlist, LayoutDoc layout, string reference)
    {
        var circuit = NetlistLoader.Load(netlist);
        var component = circuit.Circuit.OfType<IComponent>().Single(c => Names.Equals(c.Name, reference));
        var references = circuit.Circuit.Select(e => e.Name.Split('.')[0]).ToHashSet(Names);
        var copy = Fresh(Prefix().Match(component.Name).Value, references);
        var nodes = new HashSet<string>(circuit.NodeNames, StringComparer.Ordinal);

        var lines = Lines(netlist);
        var (at, count) = Element(lines, reference);
        var words = Words().Split(lines[at]);
        var first = words[0].Length == 0 && words.Length > 2 ? 2 : 0;
        words[first] = copy;
        for (var k = 1; k <= component.Nodes.Count; k++)
        {
            var node = Fresh("n", nodes);
            nodes.Add(node);
            words[first + 2 * k] = node;
        }
        lines.InsertRange(at + count, [string.Concat(words), .. lines.GetRange(at + 1, count - 1)]);

        var source = layout.Parts.Single(p => Names.Equals(p.Reference, reference));
        var placement = source with { Reference = copy, X = layout.Parts.Max(p => p.X) + ColumnStep };
        return new SchematicChange(Join(lines), new LayoutDoc([.. layout.Parts, placement], layout.Wires));
    }

    static SchematicChange Edit(string netlist, LayoutDoc layout, string reference, Func<PartPlacement, PartPlacement> edit)
    {
        var parts = layout.Parts.Select(p => Names.Equals(p.Reference, reference) ? edit(p) : p).ToList();
        return new SchematicChange(netlist, new LayoutDoc(parts, layout.Wires));
    }

    // The first line and the count of lines of an element: its line and the continuation lines after it.
    // NOTE: Lines in a .subckt block are not elements of the circuit. The first line is the title.
    static (int At, int Count) Element(List<string> lines, string reference)
    {
        var inSubcircuit = false;
        for (var i = 1; i < lines.Count; i++)
        {
            var word = lines[i].TrimStart().Split((char[]?)null, 2)[0];
            if (word.StartsWith(".subckt", StringComparison.OrdinalIgnoreCase)) inSubcircuit = true;
            else if (word.StartsWith(".ends", StringComparison.OrdinalIgnoreCase)) inSubcircuit = false;
            if (inSubcircuit || !Names.Equals(word, reference)) continue;

            var end = i + 1;
            while (end < lines.Count && lines[end].TrimStart().StartsWith('+')) end++;
            return (i, end - i);
        }
        throw new KeyNotFoundException($"The netlist has no element '{reference}'.");
    }

    static int Norm(int degrees) => (degrees % 360 + 360) % 360;

    // Renames a node in the element lines, the ssp:input and ssp:output directives, and the layout wires.
    // NOTE: Lines in a .subckt block have their own nodes. They are not changed.
    static SchematicChange Rename(string netlist, LoadedCircuit circuit, LayoutDoc layout, string from, string to, IEnumerable<WireRoute> added)
    {
        var pins = circuit.Circuit.OfType<IComponent>().ToDictionary(c => c.Name, c => c.Nodes.Count, Names);
        foreach (var x in circuit.Subcircuits) pins[x.Name] = x.Pins.Count;

        var lines = Lines(netlist);
        var inSubcircuit = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var words = Words().Split(lines[i]);
            var first = words[0].Length == 0 && words.Length > 2 ? 2 : 0;
            var word = words.Length > first ? words[first] : "";
            if (word.StartsWith(".subckt", StringComparison.OrdinalIgnoreCase)) inSubcircuit = true;
            else if (word.StartsWith(".ends", StringComparison.OrdinalIgnoreCase)) inSubcircuit = false;
            if (inSubcircuit) continue;

            var count = 0;
            if (word == "*" && words.Length > first + 4 && words[first + 2] is "ssp:input" or "ssp:output") count = 1;
            // NOTE: The title line can hold a directive, but not a part.
            else if (i > 0 && !word.StartsWith('*') && pins.TryGetValue(word, out var n)) count = n;
            // NOTE: Words has the separators at odd indices.
            for (var k = 1; k <= count; k++)
            {
                var at = first + 2 * k + (word == "*" ? 2 : 0);
                if (at < words.Length && words[at] == from) words[at] = to;
            }
            lines[i] = string.Concat(words);
        }

        var wires = layout.Wires.Select(w => w.Net == from ? w with { Net = to } : w).Concat(added).ToList();
        return new SchematicChange(Join(lines), new LayoutDoc(layout.Parts, wires));
    }

    static string Fresh(string prefix, IReadOnlySet<string> used)
    {
        var n = 1;
        while (used.Contains(prefix + n)) n++;
        return prefix + n;
    }

    static List<string> Lines(string netlist)
    {
        var lines = netlist.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    static string Join(List<string> lines) => string.Join('\n', lines) + "\n";

    [GeneratedRegex(@"(\s+)")]
    private static partial Regex Words();

    [GeneratedRegex(@"^[A-Za-z]+")]
    private static partial Regex Prefix();
}
