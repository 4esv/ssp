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

    /// <summary>
    /// The kinds that <see cref="Place"/> can add: the reference prefix and the default value. The value of a diode, LED
    /// or transistor is its model name. The value of a pot is its total resistance. A jack and a signal source have none.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Prefix, string Value)> Kinds = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
    {
        ["resistor"] = ("R", "10k"),
        ["capacitor"] = ("C", "100n"),
        ["npn"] = ("Q", "QNPN"),
        ["pnp"] = ("Q", "QPNP"),
        ["diode"] = ("D", "DGEN"),
        ["led"] = ("D", "LED_RED"),
        ["pot"] = ("RV", "10k"),
        ["battery"] = ("V", "9"),
        ["source"] = ("V", ""),
        ["jack-in"] = ("R", "1m"),
        ["jack-out"] = ("R", "1m"),
    };

    // The .model line that a kind needs, by model name.
    static readonly Dictionary<string, string> Models = new(StringComparer.OrdinalIgnoreCase)
    {
        ["QNPN"] = ".model QNPN NPN (IS=1e-14 BF=200)",
        ["QPNP"] = ".model QPNP PNP (IS=1e-14 BF=100)",
        ["DGEN"] = ".model DGEN D (IS=1e-14 N=1.9)",
        ["LED_RED"] = ".model LED_RED D(Is=4.2555e-19 N=2)",
    };

    static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Adds a part with a new reference and a new node for each pin. A battery has its minus pin on node 0 and a
    /// transistor has its substrate on node 0. A jack has its outer pin on the node <c>in</c> or <c>out</c>, and adds the
    /// <c>ssp:input</c> or <c>ssp:output</c> directive if the netlist has none. A pot adds a <c>ssp:knob</c> line, a diode,
    /// LED or transistor adds its <c>.model</c> line if missing. The layout places the part one column to the right of
    /// the other parts. With no value the part gets the default value of its kind.
    /// </summary>
    public static SchematicChange Place(string netlist, LayoutDoc layout, string kind, string? value = null)
    {
        var (prefix, fallback) = Kinds[kind];
        if (kind == "pot" && value is not null) throw new ArgumentException("A pot has the default value.", nameof(value));
        value ??= fallback;
        var circuit = NetlistLoader.Load(netlist);
        // NOTE: A pot P is the two resistors P_1 and P_2. Its reference is P.
        var references = circuit.Circuit.Select(e => Pair().Replace(e.Name.Split('.')[0], "")).ToHashSet(Names);
        var reference = Fresh(prefix, references);
        var nodes = new HashSet<string>(circuit.NodeNames, StringComparer.Ordinal);
        string Node()
        {
            var node = Fresh("n", nodes);
            nodes.Add(node);
            return node;
        }

        var added = new List<string>();
        var model = Models.ContainsKey(value) && kind is "npn" or "pnp" or "diode" or "led" ? value : null;
        switch (kind)
        {
            case "npn" or "pnp":
                added.Add($"{reference} {Node()} {Node()} {Node()} {Zero} {value}");
                break;
            case "diode" or "led":
                added.Add($"{reference} {Node()} {Node()} {value}");
                break;
            case "pot":
                var (top, wiper, bottom) = (Node(), Node(), Node());
                added.Add($"{reference}_1 {top} {wiper} 5k");
                added.Add($"{reference}_2 {wiper} {bottom} 5k");
                added.Add($"* ssp:knob {reference} linear 0.5");
                break;
            case "battery":
                added.Add($"{reference} {Node()} {Zero} DC {value}");
                break;
            case "source":
                added.Add($"{reference} {Node()} {Node()} DC 0 AC 1 SINE(0 1 1k)");
                break;
            case "jack-in" or "jack-out":
                var (label, directive) = kind == "jack-in" ? ("in", "input") : ("out", "output");
                added.Add($"{reference} {(kind == "jack-in" ? $"{label} {Node()}" : $"{Node()} {label}")} {value}");
                if (!HasDirective(netlist, directive)) added.Add($"* ssp:{directive} {label}");
                break;
            default:
                added.Add($"{reference} {Node()} {Node()} {value}");
                break;
        }

        var lines = Lines(netlist);
        // NOTE: The first line of a netlist is its title. A part on it is not read.
        if (lines.Count == 0) lines.Add("* schematic");
        var end = lines.FindIndex(1, l => l.Trim().Equals(".end", StringComparison.OrdinalIgnoreCase));
        if (model is not null && !lines.Any(l => ModelLine(model).IsMatch(l))) added.Insert(0, Models[model]);
        lines.InsertRange(end < 0 ? lines.Count : end, added);

        var x = layout.Parts.Count == 0 ? 0 : layout.Parts.Max(p => p.X) + ColumnStep;
        var parts = layout.Parts.Append(new PartPlacement(kind == "pot" ? reference + "_1" : reference, x, Row, 0, false)).ToList();
        return new SchematicChange(Join(lines), new LayoutDoc(parts, layout.Wires));
    }

    static bool HasDirective(string netlist, string name) =>
        Lines(netlist).Any(l => Regex.IsMatch(l, $@"^\s*\*\s*ssp:{name}\b", RegexOptions.IgnoreCase));

    static Regex ModelLine(string name) => new($@"^\s*\.model\s+{Regex.Escape(name)}\b", RegexOptions.IgnoreCase);

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

    /// <summary>Sets the value of a two-pin part: the fourth word of its line. Other lines and the layout do not change.</summary>
    public static SchematicChange SetValue(string netlist, LayoutDoc layout, string reference, string value)
    {
        var lines = Lines(netlist);
        var inSubcircuit = false;
        // NOTE: The title line can hold a directive, but not a part.
        for (var i = 1; i < lines.Count; i++)
        {
            var words = Words().Split(lines[i]);
            var first = words[0].Length == 0 && words.Length > 2 ? 2 : 0;
            var word = words.Length > first ? words[first] : "";
            if (word.StartsWith(".subckt", StringComparison.OrdinalIgnoreCase)) inSubcircuit = true;
            else if (word.StartsWith(".ends", StringComparison.OrdinalIgnoreCase)) inSubcircuit = false;
            if (inSubcircuit || !Names.Equals(word, reference)) continue;

            // NOTE: Words has the separators at odd indices.
            var at = first + 6;
            if (at >= words.Length) break;
            words[at] = value;
            lines[i] = string.Concat(words);
            return new SchematicChange(Join(lines), layout);
        }
        throw new ArgumentException($"No two-pin part {reference} with a value.", nameof(reference));
    }

    /// <summary>The node and schematic position of a pin.</summary>
    public static (string Node, Point Position) Pin(LoadedCircuit circuit, LayoutDoc layout, PartMap parts, PinRef pin)
    {
        // NOTE: A pot P is the resistors P_1 and P_2. The layout places it as P_1. Its pins are the top, the wiper and the bottom.
        if (SchematicRenderer.Elements(circuit, parts).SingleOrDefault(e => e.Kind == "pot" && Names.Equals(e.Reference, pin.Reference)) is { } pot)
        {
            var at = layout.Parts.Single(p => Names.Equals(p.Reference, pin.Reference) || Names.Equals(p.Reference, pot.Members[0]));
            return (pot.Nodes[pin.Pin], SchematicRenderer.PotPins(at)[pin.Pin]);
        }
        var placement = layout.Parts.Single(p => Names.Equals(p.Reference, pin.Reference));
        var component = circuit.Circuit.OfType<IComponent>().Single(c => Names.Equals(c.Name, pin.Reference));
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

    [GeneratedRegex(@"_[12]$")]
    private static partial Regex Pair();

    [GeneratedRegex(@"^[A-Za-z]+")]
    private static partial Regex Prefix();
}
