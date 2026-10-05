using System.Globalization;
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

/// <summary>A direction on the canvas, one grid step of a move.</summary>
public enum Direction { Up, Down, Left, Right }

/// <summary>A point that a move starts from: a pin, or the open end of a wire. An open end has no pin.</summary>
public readonly record struct Spot(Point At, PinRef? Pin = null);

/// <summary>The edits of the schematic editor. Each edit writes the netlist and the layout.</summary>
public static partial class SchematicEdits
{
    const string Zero = "0";
    const int ColumnStep = 140;
    const int Row = 30;

    /// <summary>
    /// The kinds that <see cref="Place"/> can add: the reference prefix and the default value. The value of a diode, LED
    /// or transistor is its model name. The value of a pot is its total resistance. A jack and a signal source have none. A jack is a marker, not a part: see <see cref="PlaceJack"/>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Prefix, string Value)> Kinds = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
    {
        ["resistor"] = ("R", "10k"),
        ["capacitor"] = ("C", "100n"),
        ["npn"] = ("Q", "QNPN"),
        ["pnp"] = ("Q", "QPNP"),
        ["electrolytic"] = ("C", "10u"),
        ["inductor"] = ("L", "10m"),
        ["diode"] = ("D", "DGEN"),
        ["zener"] = ("D", "DZ_5V1"),
        ["schottky"] = ("D", "DSCHOTTKY"),
        ["led"] = ("D", "LED_RED"),
        ["pot"] = ("RV", "10k"),
        ["switch-1"] = ("RSW", ""),
        ["switch-2"] = ("RSW", ""),
        ["switch-3"] = ("RSW", ""),
        ["battery"] = ("V", "9"),
        ["source"] = ("V", ""),
        ["jack-in"] = ("", ""),
        ["jack-out"] = ("", ""),
    };

    // The .model line that a kind needs, by model name.
    static readonly Dictionary<string, string> Models = new(StringComparer.OrdinalIgnoreCase)
    {
        ["QNPN"] = ".model QNPN NPN (IS=1e-14 BF=200)",
        ["QPNP"] = ".model QPNP PNP (IS=1e-14 BF=100)",
        ["DGEN"] = ".model DGEN D (IS=1e-14 N=1.9)",
        ["LED_RED"] = ".model LED_RED D(Is=4.2555e-19 N=2)",
        ["DZ_5V1"] = ".model DZ_5V1 D(Is=1e-14 N=1.9 BV=4.5 IBV=5m)",
        ["DSCHOTTKY"] = ".model DSCHOTTKY D(Is=3e-7 N=1.05)",
    };

    static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Adds a part with a new reference and a new node for each pin. A battery has its minus pin on node 0 and a
    /// transistor has its substrate on node 0. A pot adds a <c>ssp:knob</c> line, a switch a <c>ssp:switch</c> line, a diode,
    /// LED or transistor adds its <c>.model</c> line if missing. The layout places the part one column to the right of
    /// the other parts. With no value the part gets the default value of its kind.
    /// </summary>
    public static SchematicChange Place(string netlist, LayoutDoc layout, string kind, string? value = null)
    {
        if (kind is "jack-in" or "jack-out") throw new ArgumentException("A jack marks a pin. Use PlaceJack.", nameof(kind));
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
        var model = Models.ContainsKey(value) && kind is "npn" or "pnp" or "diode" or "led" or "zener" or "schottky" ? value : null;
        switch (kind)
        {
            case "npn" or "pnp":
                added.Add($"{reference} {Node()} {Node()} {Node()} {Zero} {value}");
                break;
            case "diode" or "led" or "zener" or "schottky":
                added.Add($"{reference} {Node()} {Node()} {value}");
                break;
            case "pot":
                var (top, wiper, bottom) = (Node(), Node(), Node());
                added.Add($"{reference}_1 {top} {wiper} 5k");
                added.Add($"{reference}_2 {wiper} {bottom} 5k");
                added.Add($"* ssp:knob {reference} linear 0.5");
                break;
            case "switch-1" or "switch-2" or "switch-3":
                // NOTE: A switch S is the resistors S_1 to S_n from the common to each throw. Its reference is S.
                var throws = Throws(kind);
                var (common, position) = (Node(), Switch.Positions(throws)[0]);
                var ohms = Switch.Resistances(throws, position);
                for (var t = 0; t < throws; t++) added.Add($"{reference}_{t + 1} {common} {Node()} {Ohms(ohms[t])}");
                added.Add($"* ssp:switch {reference} {position}");
                break;
            case "battery":
                added.Add($"{reference} {Node()} {Zero} DC {value}");
                break;
            case "source":
                added.Add($"{reference} {Node()} {Node()} DC 0 AC 1 SINE(0 1 1k)");
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
        // NOTE: A text edit can remove a part and leave its placement. The new part takes the freed reference, so the stale placement goes.
        var placed = kind == "pot" || IsSwitch(kind) ? reference + "_1" : reference;
        var parts = layout.Parts.Where(p => !Names.Equals(p.Reference, placed)).Append(new PartPlacement(kind == "pot" || IsSwitch(kind) ? reference + "_1" : reference, x, Row, 0, false)).ToList();
        return new SchematicChange(Join(lines), new LayoutDoc(parts, layout.Wires));
    }

    /// <summary>True for the kinds switch-1, switch-2 and switch-3.</summary>
    public static bool IsSwitch(string? kind) => kind is "switch-1" or "switch-2" or "switch-3";

    /// <summary>The count of throws of a switch kind.</summary>
    public static int Throws(string kind) => kind[^1] - '0';

    // The value of a switch resistor: 1m closed, 1G open.
    static string Ohms(double ohms) => ohms == Switch.Closed ? "1m" : "1G";

    /// <summary>
    /// Moves a switch to a position: its <c>ssp:switch</c> line and the value of each of its resistors. Other lines do
    /// not change. A netlist with no such switch, or a position that the switch does not have, does not change.
    /// </summary>
    public static string SetSwitch(string netlist, string reference, int position)
    {
        var circuit = NetlistLoader.Load(netlist);
        var throws = Switch.Throws(circuit, reference);
        if (throws == 0 || !Switch.Positions(throws).Contains(position)) return netlist;
        var lines = Lines(netlist);
        var at = lines.FindIndex(l => SwitchLine().Match(l) is { Success: true } m && Names.Equals(m.Groups[2].Value, reference));
        if (at < 0) return netlist;
        lines[at] = SwitchLine().Replace(lines[at], m => m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value + position.ToString(CultureInfo.InvariantCulture), 1);
        var ohms = Switch.Resistances(throws, position);
        for (var t = 0; t < throws; t++)
        {
            var (line, _) = Element(lines, $"{reference}_{t + 1}");
            var words = Words().Split(lines[line]);
            var first = words[0].Length == 0 && words.Length > 2 ? 2 : 0;
            // NOTE: Words has the separators at odd indices. The value follows the name and two nodes.
            if (first + 6 < words.Length) words[first + 6] = Ohms(ohms[t]);
            lines[line] = string.Concat(words);
        }
        return Join(lines);
    }

    /// <summary>Moves a switch to its next position, as a tap on it does. See <see cref="Switch.Next"/>.</summary>
    public static string Flip(string netlist, string reference)
    {
        var circuit = NetlistLoader.Load(netlist);
        var line = circuit.Directives.Switches.FirstOrDefault(s => Names.Equals(s.Part, reference));
        var throws = Switch.Throws(circuit, reference);
        return line is null || throws == 0 ? netlist : SetSwitch(netlist, reference, Switch.Next(throws, line.Position));
    }

    /// <summary>
    /// Marks the node of a spot as the input or the output of the circuit: <c>* ssp:input &lt;node&gt;</c> or <c>* ssp:output &lt;node&gt;</c>.
    /// A directive that the netlist has is moved. No component line is added and the layout does not change.
    /// </summary>
    public static SchematicChange PlaceJack(string netlist, LayoutDoc layout, PartMap parts, string kind, Spot spot)
    {
        var directive = kind switch { "jack-in" => "input", "jack-out" => "output", _ => throw new ArgumentException("Not a jack.", nameof(kind)) };
        var node = NodeOf(NetlistLoader.Load(netlist), layout, parts, spot);
        var lines = Lines(netlist);
        var line = $"* ssp:{directive} {node}";
        var at = lines.FindIndex(l => Regex.IsMatch(l, $@"^\s*\*\s*ssp:{directive}\b", RegexOptions.IgnoreCase));
        if (at >= 0) lines[at] = line;
        // NOTE: The first line of a netlist is its title.
        else lines.Insert(Math.Min(1, lines.Count), line);
        return new SchematicChange(Join(lines), layout);
    }

    /// <summary>Rounds a coordinate to the nearest <see cref="Symbols.Grid"/> step.</summary>
    public static double Snap(double value) => Math.Round(value / Symbols.Grid, MidpointRounding.AwayFromZero) * Symbols.Grid + 0.0;

    /// <summary>
    /// <see cref="Place"/> with the part at a point, snapped to the grid. A pin that lands on another pin or on a wire
    /// joins that net.
    /// </summary>
    public static SchematicChange PlaceAt(string netlist, LayoutDoc layout, string kind, Point at)
    {
        var placed = Place(netlist, layout, kind);
        var last = placed.Layout.Parts[^1];
        var parts = placed.Layout.Parts.SkipLast(1).Append(last with { X = Snap(at.X), Y = Snap(at.Y) }).ToList();
        var change = placed with { Layout = new LayoutDoc(parts, placed.Layout.Wires) };
        return Settle(change, last.Reference);
    }

    /// <summary>The distance from a pin to the part or wire that one move adds, in schematic units.</summary>
    public const double Step = 40;

    /// <summary>The point one <see cref="Step"/> from a point in a direction.</summary>
    public static Point Toward(Point at, Direction direction) => direction switch
    {
        Direction.Up => new Point(at.X, at.Y - Step),
        Direction.Down => new Point(at.X, at.Y + Step),
        Direction.Left => new Point(at.X - Step, at.Y),
        _ => new Point(at.X + Step, at.Y),
    };

    /// <summary>
    /// The directions that a move from a spot can go, in the order up, down, left, right. A direction is free if no wire
    /// leaves the spot that way and no part outline holds the point one <see cref="Step"/> away or half way there.
    /// </summary>
    public static IReadOnlyList<Direction> Directions(string netlist, LayoutDoc layout, PartMap parts, Spot spot)
    {
        var circuit = NetlistLoader.Load(netlist);
        var outlines = new List<(Point Min, Point Max)>();
        foreach (var e in SchematicRenderer.Elements(circuit, parts))
        {
            if (layout.Parts.FirstOrDefault(p => Names.Equals(p.Reference, e.Reference) || Names.Equals(p.Reference, e.Members[0])) is { } placement)
            {
                outlines.Add(SchematicRenderer.Outline(e, placement));
            }
        }

        bool Inside(Point p) => outlines.Any(o => p.X >= o.Min.X - 0.01 && p.X <= o.Max.X + 0.01 && p.Y >= o.Min.Y - 0.01 && p.Y <= o.Max.Y + 0.01);
        var taken = layout.Wires.SelectMany(w => Leaves(w, spot.At)).ToHashSet();
        return Enum.GetValues<Direction>().Where(d =>
        {
            var end = Toward(spot.At, d);
            return !taken.Contains(d) && !Inside(end) && !Inside(new Point((spot.At.X + end.X) / 2, (spot.At.Y + end.Y) / 2));
        }).ToList();
    }

    // The directions that a wire leaves a point: the end of a segment, or both ways along a segment that passes through.
    static IEnumerable<Direction> Leaves(WireRoute w, Point p)
    {
        for (var i = 1; i < w.Points.Count; i++)
        {
            var (a, b) = (w.Points[i - 1], w.Points[i]);
            if (a == b || !OnWire(new WireRoute(w.Net, [a, b]), p)) continue;
            if (p != b) yield return Along(a, b);
            if (p != a) yield return Along(b, a);
        }
    }

    static Direction Along(Point from, Point to) =>
        Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y)
            ? to.X > from.X ? Direction.Right : Direction.Left
            : to.Y > from.Y ? Direction.Down : Direction.Up;

    // The node of a spot: the node of its pin, or the net of the wire that it sits on.
    static string NodeOf(LoadedCircuit circuit, LayoutDoc layout, PartMap parts, Spot spot) =>
        spot.Pin is { } pin
            ? Pin(circuit, layout, parts, pin).Node
            : layout.Wires.FirstOrDefault(w => OnWire(w, spot.At))?.Net
                ?? throw new ArgumentException("The spot is on no pin and no wire.", nameof(spot));

    // The near pin joins the spot, the far pin is the next spot. A transistor is wired at its base and goes on at its collector.
    static (int Near, int Far) Ends(string kind) => kind switch
    {
        "npn" or "pnp" => (1, 0),
        "pot" => (0, 2),
        _ => (0, 1),
    };

    /// <summary>
    /// Adds a part one <see cref="Step"/> from a spot in a direction. The part is turned so that its far pin points away
    /// from the spot. A wire joins the spot to the near pin of the part, so that pin joins the net of the spot. The far pin
    /// is the next spot.
    /// </summary>
    public static (SchematicChange Change, Spot Far) PlaceNext(string netlist, LayoutDoc layout, PartMap parts, string kind, Spot from, Direction direction)
    {
        var node = NodeOf(NetlistLoader.Load(netlist), layout, parts, from);
        var placed = Place(netlist, layout, kind);
        var circuit = NetlistLoader.Load(placed.Netlist);
        var element = ElementOf(circuit, PartMap.Resolve(circuit, Ssp.Web.Components.SchematicView.Table), placed.Layout.Parts[^1].Reference);
        var (near, far) = Ends(kind);

        var target = Toward(from.At, direction);
        var (ux, uy) = (Math.Sign(target.X - from.At.X), Math.Sign(target.Y - from.At.Y));
        var best = new[] { 0, 90, 180, 270 }
            .Select(r => placed.Layout.Parts[^1] with { X = 0, Y = 0, Rotation = r, Flip = false })
            .MaxBy(o =>
            {
                var pins = SchematicRenderer.Pins(element, o);
                return (pins[far].At.X - pins[near].At.X) * ux + (pins[far].At.Y - pins[near].At.Y) * uy;
            })!;
        var origin = SchematicRenderer.Pins(element, best)[near].At;
        var placement = best with { X = Snap(target.X - origin.X), Y = Snap(target.Y - origin.Y) };

        var laid = new LayoutDoc(placed.Layout.Parts.SkipLast(1).Append(placement).ToList(), placed.Layout.Wires);
        var pinsAt = SchematicRenderer.Pins(element, placement);
        var wire = new WireRoute(node, [from.At, pinsAt[near].At]);
        var joined = pinsAt[near].Net == node
            ? new SchematicChange(placed.Netlist, new LayoutDoc(laid.Parts, [.. laid.Wires, wire]))
            : Rename(placed.Netlist, circuit, laid, pinsAt[near].Net, node, [wire]);
        var change = Settle(joined, placement.Reference);

        // NOTE: A pot is placed as P_1 and its pins are those of P.
        return (change, new Spot(pinsAt[far].At, new PinRef(element.Reference, far)));
    }

    /// <summary>Adds a wire one <see cref="Step"/> from a spot in a direction. The wire ends in an open spot. The netlist does not change.</summary>
    public static (SchematicChange Change, Spot Open) ExtendWire(string netlist, LayoutDoc layout, PartMap parts, Spot from, Direction direction)
    {
        var node = NodeOf(NetlistLoader.Load(netlist), layout, parts, from);
        var end = Toward(from.At, direction);
        var wire = new WireRoute(node, [from.At, end]);
        return (new SchematicChange(netlist, new LayoutDoc(layout.Parts, [.. layout.Wires, wire])), new Spot(end));
    }

    /// <summary><see cref="Ground(string, LayoutDoc, PartMap, PinRef)"/> for a pin or an open wire end.</summary>
    public static SchematicChange Ground(string netlist, LayoutDoc layout, PartMap parts, Spot spot)
    {
        var circuit = NetlistLoader.Load(netlist);
        return Ground(netlist, circuit, layout, NodeOf(circuit, layout, parts, spot), spot.At);
    }

    /// <summary>
    /// Moves a part by an offset in schematic units. The new position snaps to the grid. The wires that end on its pins
    /// follow. A pin that lands on another pin or on a wire joins that net. The reference is the one in the layout.
    /// </summary>
    public static SchematicChange Drag(string netlist, LayoutDoc layout, PartMap parts, string reference, double dx, double dy)
    {
        var circuit = NetlistLoader.Load(netlist);
        var element = ElementOf(circuit, parts, reference);
        // NOTE: A pot is placed by its first half, P_1.
        var start = layout.Parts.Single(p => Names.Equals(p.Reference, element.Reference) || Names.Equals(p.Reference, element.Members[0]));
        var to = start with { X = Snap(start.X + dx), Y = Snap(start.Y + dy) };
        var before = SchematicRenderer.Pins(element, start).Select(p => p.At).ToList();
        var after = SchematicRenderer.Pins(element, to).Select(p => p.At).ToList();
        var all = SchematicRenderer.Elements(circuit, parts)
            .SelectMany(e => layout.Parts.Where(p => Names.Equals(p.Reference, e.Reference) || Names.Equals(p.Reference, e.Members[0])).Take(1)
                .SelectMany(p => SchematicRenderer.Pins(e, p).Select(x => x.At)))
            .ToHashSet();

        var wires = layout.Wires.Select(w => Follow(w, before, after, all, (to.X - start.X, to.Y - start.Y))).ToList();
        var moved = new LayoutDoc(layout.Parts.Select(p => p == start ? to : p).ToList(), wires);
        return Settle(new SchematicChange(netlist, moved), element.Reference);
    }

    /// <summary>
    /// <see cref="Drag"/> for several parts by one offset. The offset snaps once, so the parts keep their places to each
    /// other. A wire between two of the parts moves with them. A wire to a part that stays bends to follow.
    /// </summary>
    public static SchematicChange DragMany(string netlist, LayoutDoc layout, PartMap parts, IEnumerable<string> references, double dx, double dy)
    {
        var circuit = NetlistLoader.Load(netlist);
        var group = Group(circuit, layout, parts, references);
        if (group.Count == 0) return new SchematicChange(netlist, layout);
        var first = group[0].Placement;
        var d = (X: Snap(first.X + dx) - first.X, Y: Snap(first.Y + dy) - first.Y);
        var moved = group.ToDictionary(g => g.Placement, g => g.Placement with { X = g.Placement.X + d.X, Y = g.Placement.Y + d.Y });
        var before = group.SelectMany(g => SchematicRenderer.Pins(g.Element, g.Placement).Select(p => p.At)).ToList();
        var after = group.SelectMany(g => SchematicRenderer.Pins(g.Element, moved[g.Placement]).Select(p => p.At)).ToList();
        var all = SchematicRenderer.Elements(circuit, parts)
            .SelectMany(e => layout.Parts.Where(p => Names.Equals(p.Reference, e.Reference) || Names.Equals(p.Reference, e.Members[0])).Take(1)
                .SelectMany(p => SchematicRenderer.Pins(e, p).Select(x => x.At)))
            .ToHashSet();

        var wires = layout.Wires.Select(w => Follow(w, before, after, all, d)).ToList();
        var change = new SchematicChange(netlist, new LayoutDoc(layout.Parts.Select(p => moved.GetValueOrDefault(p, p)).ToList(), wires));
        foreach (var g in group) change = Settle(change, g.Element.Reference);
        return change;
    }

    /// <summary>
    /// Copies parts with their lines, their places, and the wires between them. A wire to a part outside the copy is not
    /// copied. A ground symbol on a copied pin is.
    /// </summary>
    public static Clip Copy(string netlist, LayoutDoc layout, PartMap parts, IEnumerable<string> references)
    {
        var circuit = NetlistLoader.Load(netlist);
        var group = Group(circuit, layout, parts, references);
        var lines = Lines(netlist);
        var nodes = circuit.Circuit.OfType<IComponent>().ToDictionary(c => c.Name, c => c.Nodes.Count, Names);
        // NOTE: A model can be on two lines after an edit of the text. The first one goes with the copy.
        var models = lines.Where(l => ModelName().IsMatch(l)).DistinctBy(l => ModelName().Match(l).Groups[1].Value, Names)
            .ToDictionary(l => ModelName().Match(l).Groups[1].Value, l => l, Names);
        // NOTE: A subcircuit goes with the copy like a model, so a block with an op-amp pastes into any netlist.
        foreach (var (name, text) in Subcircuits(lines)) models.TryAdd(name, text);

        var copied = new List<ClipPart>();
        var used = new List<string>();
        var instances = circuit.Subcircuits.ToDictionary(x => x.Name, x => x.Pins.Count, Names);
        foreach (var (element, placement) in group)
        {
            // NOTE: The loader flattens a subcircuit instance into X1.Rid and the like. The netlist line is X1.
            IEnumerable<string> names = instances.ContainsKey(element.Reference) ? [element.Reference] : element.Members;
            var members = names.Select(m =>
            {
                var (at, count) = Element(lines, m);
                var text = string.Join('\n', lines.GetRange(at, count));
                used.AddRange(Words().Split(lines[at]).Where(w => models.ContainsKey(w)));
                return new ClipLine(m, text, instances.TryGetValue(m, out var pins) ? pins : nodes.GetValueOrDefault(m));
            }).ToList();
            var knobs = lines.Where(l => KnobOf(l) is { } k && Names.Equals(k, element.Reference)).ToList();
            copied.Add(new ClipPart(element.Reference, members, knobs, placement));
        }

        var pins = group.SelectMany(g => SchematicRenderer.Pins(g.Element, g.Placement).Where(p => !p.Hidden).Select(p => p.At)).ToHashSet();
        bool UnderGround(Point q) => pins.Any(p => Math.Abs(q.X - p.X) <= 10 && q.Y >= p.Y && q.Y <= p.Y + 28);
        var wires = layout.Wires.Where(w =>
            new[] { w.Points[0], w.Points[^1] }.All(q => pins.Contains(q) || w.Net == Zero && UnderGround(q))
            && (w.Net != Zero || w.Points.Any(pins.Contains) || w.Points.All(UnderGround))).ToList();
        // NOTE: A model that only a copied subcircuit uses goes with the copy too.
        foreach (var m in used.ToList()) used.AddRange(models[m].Split('\n').Skip(1).SelectMany(l => Words().Split(l)).Where(w => models.ContainsKey(w)));
        return new Clip(copied, used.Distinct(Names).Select(m => models[m]).ToList(), wires);
    }

    /// <summary>All the parts of a netlist as a clip, to insert as a block. With no layout the parts get the auto-placement.</summary>
    public static Clip Block(string netlist, LayoutDoc? layout)
    {
        var circuit = NetlistLoader.Load(netlist);
        layout ??= AutoPlacer.Place(circuit, circuit.Directives);
        var references = circuit.Circuit.Select(e => e.Name).ToList();
        var clip = Copy(netlist, layout, PartMap.Resolve(circuit, Components.SchematicView.Table), references);
        return clip with { Input = circuit.Directives.Input, Output = circuit.Directives.Output };
    }

    /// <summary>
    /// The points where three or more wires of one net meet. A ground symbol is not a junction, so node 0 has none.
    /// </summary>
    public static IReadOnlyList<Point> Junctions(LayoutDoc layout) =>
        layout.Wires.Where(w => w.Net != Zero).SelectMany(w => w.Points.Select(p => (w.Net, At: p))).Distinct()
            .Where(x => layout.Wires.Where(w => w.Net == x.Net).SelectMany(w => Leaves(w, x.At)).Distinct().Count() >= 3)
            .Select(x => x.At).Distinct().ToList();

    /// <summary>
    /// Inserts a block where it is dropped. On a junction the block's <c>ssp:input</c> node joins that net and its
    /// output stays free. On a wire the wire is cut: the input joins the net of one cut end and the output the net of
    /// the other, and wires go from the cut ends to the pins. A block with no input and output (a loose group) joins
    /// the net of the wire or the junction by its first pin. On a wire or a junction the stage's own test source (a
    /// voltage source from its input to node 0) stays out. Elsewhere the block goes on the empty spot, as
    /// <see cref="PasteAt"/>. With a wire index the drop is on that wire. A drop that would short two nets is refused:
    /// the change is the netlist and the layout as they were, and <see cref="BlockDrop.Refused"/> says why.
    /// </summary>
    public static BlockDrop Drop(string netlist, LayoutDoc layout, PartMap parts, Clip clip, Point at, int? wire = null)
    {
        var none = new SchematicChange(netlist, layout);
        BlockDrop Refuse(string why) => new(none, [], "", why);
        if (clip.Parts.Count == 0) return new(none, [], "");
        var junction = Junctions(layout).Where(j => Distance(j, at) <= Symbols.Grid).OrderBy(j => Distance(j, at)).Select(j => (Point?)j).FirstOrDefault();
        wire ??= Enumerable.Range(0, layout.Wires.Count).Where(i => Distance(Project(layout.Wires[i], at), at) <= Symbols.Grid / 2.0)
            .OrderBy(i => Distance(Project(layout.Wires[i], at), at)).Select(i => (int?)i).FirstOrDefault();
        if (junction is null && wire is null)
        {
            var (placed, fresh) = PasteAt(netlist, layout, clip, at);
            return new(placed, fresh, "");
        }

        var stage = clip.Input is not null && clip.Output is not null;
        if (junction is not null || !stage)
        {
            var tap = junction ?? Project(layout.Wires[wire!.Value], at);
            var net = layout.Wires.First(w => junction is null ? w == layout.Wires[wire!.Value] : w.Points.Contains(tap) && w.Net != Zero).Net;
            var (pasted, fresh, nodes) = PasteClear(netlist, layout, clip, tap);
            var pins = PinsOf(pasted.Change, fresh);
            var first = clip.Input is { } input ? pins.FirstOrDefault(p => p.Net == nodes.GetValueOrDefault(input)) : pins.FirstOrDefault();
            if (first.Net is null) return Refuse("its input is on no part.");
            if (first.Net == Zero && net != Zero) return Refuse($"its {(stage ? "input" : "first pin")} is on ground, so net {net} would short to ground.");
            var joined = Rename(pasted.Change.Netlist, NetlistLoader.Load(pasted.Change.Netlist), pasted.Change.Layout, first.Net, net, [Route(net, tap, first.At)]);
            return new(joined, fresh, stage ? $"Its input joins net {net}. Its output is free: wire it." : $"Its first pin joins net {net}.");
        }

        var cutWire = layout.Wires[wire!.Value];
        if (cutWire.Net == Zero) return Refuse("it is a ground wire. Drop it on a signal wire.");
        var tapped = Project(cutWire, at);
        var (cut, a, na, b, nb, why) = Cut(netlist, layout, parts, wire.Value, tapped);
        if (why is not null) return Refuse(why);

        var (stagePasted, stageFresh, stageNodes) = PasteClear(cut.Netlist, cut.Layout, clip, tapped);
        var stagePins = PinsOf(stagePasted.Change, stageFresh);
        var inPin = stagePins.FirstOrDefault(p => p.Net == stageNodes.GetValueOrDefault(clip.Input!));
        var outPin = stagePins.FirstOrDefault(p => p.Net == stageNodes.GetValueOrDefault(clip.Output!));
        if (inPin.Net is null || outPin.Net is null) return Refuse("its input or output is on no part.");
        if (inPin.Net == outPin.Net) return Refuse("its input and output are one net, so the cut ends would short.");
        // NOTE: The nearer end goes to each pin, so the wires do not cross.
        if (Distance(b, inPin.At) + Distance(a, outPin.At) < Distance(a, inPin.At) + Distance(b, outPin.At)) (a, b, na, nb) = (b, a, nb, na);
        if (inPin.Net == Zero && na != Zero || outPin.Net == Zero && nb != Zero) return Refuse("its input or output is on ground, so a net would short to ground.");
        var change = stagePasted.Change;
        if (inPin.Net != Zero) change = Rename(change.Netlist, NetlistLoader.Load(change.Netlist), change.Layout, inPin.Net, na, [Route(na, a, inPin.At)]);
        if (outPin.Net != Zero) change = Rename(change.Netlist, NetlistLoader.Load(change.Netlist), change.Layout, outPin.Net, nb, [Route(nb, b, outPin.At)]);
        return new(change, stageFresh, $"It sits in series between nets {na} and {nb}.");
    }

    // Cuts a wire at a point: two pieces stay, one grid step shy of the point on each side. The pins and wires that the
    // first piece reaches keep the node or get a new one, and those that the second reaches get the other: the group
    // with fewer pins gets the new node. Gives the open end and the node of each piece, or why the cut cannot be made.
    static (SchematicChange Change, Point A, string NetA, Point B, string NetB, string? Refused) Cut(
        string netlist, LayoutDoc layout, PartMap parts, int index, Point at)
    {
        var wire = layout.Wires[index];
        var none = (new SchematicChange(netlist, layout), at, wire.Net, at, wire.Net);
        var points = wire.Points;
        var s = Enumerable.Range(1, points.Count - 1).First(i => OnWire(new WireRoute(wire.Net, [points[i - 1], points[i]]), at));
        Point Back(Point toward)
        {
            var d = Distance(at, toward);
            return d <= Symbols.Grid ? toward : new Point(at.X + (toward.X - at.X) * Symbols.Grid / d, at.Y + (toward.Y - at.Y) * Symbols.Grid / d);
        }
        var (ea, eb) = (Back(points[s - 1]), Back(points[s]));
        if (ea == eb) return (none.Item1, at, wire.Net, at, wire.Net, "the wire is too short to cut there.");
        var pieceA = new WireRoute(wire.Net, [.. points.Take(s).Where(p => p != ea), ea]);
        var pieceB = new WireRoute(wire.Net, [eb, .. points.Skip(s).Where(p => p != eb)]);

        var circuit = NetlistLoader.Load(netlist);
        var pins = new List<(SchematicElement Element, int Pin, Point At)>();
        foreach (var e in SchematicRenderer.Elements(circuit, parts))
        {
            if (layout.Parts.FirstOrDefault(p => Names.Equals(p.Reference, e.Reference) || Names.Equals(p.Reference, e.Members[0])) is not { } placement) continue;
            var all = SchematicRenderer.Pins(e, placement);
            for (var i = 0; i < all.Count; i++)
            {
                if (!all[i].Hidden && all[i].Net == wire.Net) pins.Add((e, i, all[i].At));
            }
        }
        // NOTE: Union-find over the pins, the other wires of the net, and the two pieces. A piece of one point is a probe at that point.
        var rest = Enumerable.Range(0, layout.Wires.Count).Where(i => i != index && layout.Wires[i].Net == wire.Net).ToList();
        var routes = rest.Select(i => layout.Wires[i]).Append(pieceA).Append(pieceB).ToList();
        var parent = Enumerable.Range(0, pins.Count + routes.Count).ToArray();
        int Find(int x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
        void Union(int x, int y) => parent[Find(x)] = Find(y);
        for (var w = 0; w < routes.Count; w++)
        {
            for (var k = 0; k < pins.Count; k++)
            {
                if (OnWire(routes[w], pins[k].At)) Union(k, pins.Count + w);
            }
            for (var v = 0; v < w; v++)
            {
                if (routes[w].Points.Any(q => OnWire(routes[v], q)) || routes[v].Points.Any(q => OnWire(routes[w], q))) Union(pins.Count + v, pins.Count + w);
            }
        }
        var (rootA, rootB) = (Find(pins.Count + routes.Count - 2), Find(pins.Count + routes.Count - 1));
        if (rootA == rootB) return (none.Item1, at, wire.Net, at, wire.Net, $"the two sides of the wire stay on net {wire.Net} by another path, so its input and output would short.");
        int[] In(int root) => Enumerable.Range(0, pins.Count).Where(k => Find(k) == root).ToArray();
        var (inA, inB) = (In(rootA), In(rootB));
        if (inA.Length == 0 || inB.Length == 0) return (none.Item1, at, wire.Net, at, wire.Net, "one side of the wire reaches no pin. Drop it on a wire between two pins.");
        if (pins.Where((p, k) => OnWire(wire, p.At) && Find(k) != rootA && Find(k) != rootB).Any())
        {
            return (none.Item1, at, wire.Net, at, wire.Net, "a pin sits where the wire would be cut.");
        }

        var moved = inA.Length < inB.Length ? rootA : rootB;
        var node = Fresh("n", new HashSet<string>(circuit.NodeNames, StringComparer.Ordinal));
        var lines = Lines(netlist);
        foreach (var (element, pin, _) in (moved == rootA ? inA : inB).Select(k => pins[k]))
        {
            // NOTE: A pot P is P_1 and P_2. The wiper is the second node of P_1 and the first node of P_2.
            if (element.Kind == "pot" && element.Members.Count == 2)
            {
                if (pin < 2) SetNode(lines, element.Members[0], pin, node);
                if (pin > 0) SetNode(lines, element.Members[1], pin - 1, node);
            }
            else SetNode(lines, element.Members[0], pin, node);
        }
        var wires = new List<WireRoute>();
        for (var i = 0; i < layout.Wires.Count; i++)
        {
            var w = rest.IndexOf(i);
            if (i == index)
            {
                foreach (var (piece, root) in new[] { (pieceA, rootA), (pieceB, rootB) })
                {
                    if (piece.Points.Distinct().Count() > 1) wires.Add(root == moved ? piece with { Net = node } : piece);
                }
            }
            else wires.Add(w >= 0 && Find(pins.Count + w) == moved ? layout.Wires[i] with { Net = node } : layout.Wires[i]);
        }
        var (netA, netB) = moved == rootA ? (node, wire.Net) : (wire.Net, node);
        return (new SchematicChange(Join(lines), new LayoutDoc(layout.Parts, wires)), ea, netA, eb, netB, null);
    }

    static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // A wire from a point to a pin, with one corner at the x of the pin and the y of the point, as Wire draws it.
    static WireRoute Route(string net, Point from, Point to)
    {
        var corner = new Point(to.X, from.Y);
        return new WireRoute(net, corner == from || corner == to ? [from, to] : [from, corner, to]);
    }

    // The point of a wire nearest to a point. On a straight segment the point snaps to the grid along it.
    static Point Project(WireRoute w, Point p)
    {
        var best = w.Points[0];
        for (var i = 1; i < w.Points.Count; i++)
        {
            var (a, b) = (w.Points[i - 1], w.Points[i]);
            var (dx, dy) = (b.X - a.X, b.Y - a.Y);
            var length = dx * dx + dy * dy;
            var t = length == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, 0, 1);
            var q = new Point(a.X + t * dx, a.Y + t * dy);
            if (dy == 0) q = new Point(Math.Clamp(Snap(q.X), Math.Min(a.X, b.X), Math.Max(a.X, b.X)), a.Y);
            else if (dx == 0) q = new Point(a.X, Math.Clamp(Snap(q.Y), Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y)));
            if (Distance(q, p) < Distance(best, p)) best = q;
        }
        return best;
    }

    // The shown pins of some parts, in the order of the layout.
    static List<SchematicRenderer.ElementPin> PinsOf(SchematicChange change, IEnumerable<string> references)
    {
        var circuit = NetlistLoader.Load(change.Netlist);
        return Group(circuit, change.Layout, PartMap.Resolve(circuit, Components.SchematicView.Table), references)
            .SelectMany(g => SchematicRenderer.Pins(g.Element, g.Placement).Where(p => !p.Hidden)).ToList();
    }

    // PasteAt for a drop on a wire or a junction: the stage's test source on its input stays out, with its wires.
    static ((SchematicChange Change, IReadOnlyList<string> References) Pasted, IReadOnlyList<string> Fresh, IReadOnlyDictionary<string, string> Nodes) PasteClear(
        string netlist, LayoutDoc layout, Clip clip, Point at)
    {
        var (change, fresh, nodes) = PasteAtNodes(netlist, layout, clip, at);
        if (clip.Input is not { } input) return ((change, fresh), fresh, nodes);
        var kept = fresh.ToList();
        for (var i = 0; i < clip.Parts.Count; i++)
        {
            var line = clip.Parts[i].Members[0].Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (clip.Parts[i].Members.Count != 1 || line.Length < 3 || !line[0].StartsWith('V') && !line[0].StartsWith('v')) continue;
            if (!(line[1] == input && line[2] == Zero || line[1] == Zero && line[2] == input)) continue;

            var circuit = NetlistLoader.Load(change.Netlist);
            var element = ElementOf(circuit, PartMap.Resolve(circuit, Components.SchematicView.Table), fresh[i]);
            var placement = change.Layout.Parts.Single(p => Names.Equals(p.Reference, fresh[i]));
            var pins = SchematicRenderer.Pins(element, placement).Select(p => p.At).ToList();
            bool Under(Point q) => pins.Any(p => Math.Abs(q.X - p.X) <= 10 && q.Y >= p.Y && q.Y <= p.Y + 28);
            // NOTE: Only the pasted wires can go. They come after the wires that were there.
            var wires = change.Layout.Wires.Where((w, k) => k < layout.Wires.Count
                || !(pins.Contains(w.Points[0]) || pins.Contains(w.Points[^1]) || w.Net == Zero && w.Points.All(Under))).ToList();
            var lines = Lines(change.Netlist);
            var (row, count) = Element(lines, fresh[i]);
            lines.RemoveRange(row, count);
            change = new SchematicChange(Join(lines), new LayoutDoc(change.Layout.Parts.Where(p => p != placement).ToList(), wires));
            kept.Remove(fresh[i]);
        }
        return ((change, kept), kept, nodes);
    }

    /// <summary>The clip alone: a netlist and a layout with only its parts, as My Blocks keeps it.</summary>
    public static SchematicChange Alone(Clip clip) => Paste("", new LayoutDoc([], []), clip).Change;

    /// <summary>
    /// Pastes the clip with its top-left part at the point. If the clip would cover a part, it moves one column to the
    /// right until it covers none. The parts that are there do not move.
    /// </summary>
    public static (SchematicChange Change, IReadOnlyList<string> References) PasteAt(string netlist, LayoutDoc layout, Clip clip, Point at)
    {
        var (change, references, _) = PasteAtNodes(netlist, layout, clip, at);
        return (change, references);
    }

    // PasteAt with the new name of each node of the clip.
    static (SchematicChange Change, IReadOnlyList<string> References, IReadOnlyDictionary<string, string> Nodes) PasteAtNodes(string netlist, LayoutDoc layout, Clip clip, Point at)
    {
        if (clip.Parts.Count == 0) return (new SchematicChange(netlist, layout), [], new Dictionary<string, string>());
        var old = Outlines(NetlistLoader.Load(netlist), layout);
        var dy = Snap(at.Y - clip.Parts.Min(p => p.Placement.Y));
        var dx = Snap(at.X - clip.Parts.Min(p => p.Placement.X));
        // NOTE: Past the right edge of the old parts the clip covers none, so the loop ends.
        var right = old.Count == 0 ? dx : old.Max(o => o.Max.X) - clip.Parts.Min(p => p.Placement.X) + ColumnStep;
        while (true)
        {
            var pasted = PasteBy(netlist, layout, clip, dx, dy);
            var added = Outlines(NetlistLoader.Load(pasted.Change.Netlist), pasted.Change.Layout).Where(n => !old.Any(o => Names.Equals(o.Reference, n.Reference)));
            if (dx >= right || !added.Any(n => old.Any(o => Covers(n, o)))) return pasted;
            dx += ColumnStep;
        }
    }

    // NOTE: Two outlines closer than one grid step cover each other, so the labels of a block do not touch the parts beside it.
    static bool Covers((string, Point Min, Point Max) a, (string, Point Min, Point Max) b) =>
        a.Min.X < b.Max.X + Symbols.Grid && b.Min.X < a.Max.X + Symbols.Grid && a.Min.Y < b.Max.Y + Symbols.Grid && b.Min.Y < a.Max.Y + Symbols.Grid;

    // The outline of each placed element.
    static List<(string Reference, Point Min, Point Max)> Outlines(LoadedCircuit circuit, LayoutDoc layout)
    {
        var outlines = new List<(string, Point, Point)>();
        foreach (var e in SchematicRenderer.Elements(circuit, null))
        {
            if (layout.Parts.FirstOrDefault(p => Names.Equals(p.Reference, e.Reference) || Names.Equals(p.Reference, e.Members[0])) is not { } placement) continue;
            var (min, max) = SchematicRenderer.Outline(e, placement);
            outlines.Add((e.Reference, min, max));
        }
        return outlines;
    }

    // The .subckt blocks of a netlist by name, each with its lines up to and with .ends.
    static Dictionary<string, string> Subcircuits(List<string> lines)
    {
        var found = new Dictionary<string, string>(Names);
        for (var i = 0; i < lines.Count; i++)
        {
            if (SubcircuitName().Match(lines[i]) is not { Success: true } m) continue;
            var end = lines.FindIndex(i, l => l.TrimStart().StartsWith(".ends", StringComparison.OrdinalIgnoreCase));
            if (end < 0) break;
            found.TryAdd(m.Groups[1].Value, string.Join('\n', lines.GetRange(i, end - i + 1)));
            i = end;
        }
        return found;
    }

    /// <summary>
    /// Adds a copy. Each part gets the next free reference of its prefix and each node a new name, so the copy joins
    /// nothing. Node 0 stays node 0. The copy goes one column to the right of the other parts. Gives the new references.
    /// </summary>
    public static (SchematicChange Change, IReadOnlyList<string> References) Paste(string netlist, LayoutDoc layout, Clip clip)
    {
        if (clip.Parts.Count == 0) return (new SchematicChange(netlist, layout), []);
        var dx = Snap((layout.Parts.Count == 0 ? 0 : layout.Parts.Max(p => p.X) + ColumnStep) - clip.Parts.Min(p => p.Placement.X));
        var (change, references, _) = PasteBy(netlist, layout, clip, dx, 0);
        return (change, references);
    }

    static (SchematicChange Change, IReadOnlyList<string> References, IReadOnlyDictionary<string, string> Nodes) PasteBy(string netlist, LayoutDoc layout, Clip clip, double dx, double dy)
    {
        var circuit = NetlistLoader.Load(netlist);
        var references = circuit.Circuit.Select(e => e.Name.Split('.')[0]).SelectMany(n => new[] { n, Pair().Replace(n, "") }).ToHashSet(Names);
        var nodes = new HashSet<string>(circuit.NodeNames, StringComparer.Ordinal);
        var renamed = new Dictionary<string, string>(StringComparer.Ordinal) { [Zero] = Zero };
        string Node(string node)
        {
            if (renamed.TryGetValue(node, out var to)) return to;
            to = Fresh("n", nodes);
            nodes.Add(to);
            return renamed[node] = to;
        }

        var lines = Lines(netlist);
        if (lines.Count == 0) lines.Add("* schematic");
        var defined = Subcircuits(lines);
        var added = clip.Models.Where(m => !lines.Any(l => Names.Equals(l.Trim(), m.Trim()) || ModelName().Match(m) is { Success: true } n && ModelLine(n.Groups[1].Value).IsMatch(l))
            && !(SubcircuitName().Match(m) is { Success: true } s && defined.ContainsKey(s.Groups[1].Value))).ToList();
        var placements = new List<PartPlacement>();
        var fresh = new List<string>();
        foreach (var part in clip.Parts)
        {
            var reference = Fresh(Prefix().Match(part.Reference).Value, references);
            references.Add(reference);
            fresh.Add(reference);
            string Name(string member) => reference + member[part.Reference.Length..];
            foreach (var member in part.Members)
            {
                references.Add(Name(member.Name));
                var text = member.Text.Split('\n');
                var words = Words().Split(text[0]);
                var first = words[0].Length == 0 && words.Length > 2 ? 2 : 0;
                words[first] = Name(member.Name);
                // NOTE: Words has the separators at odd indices.
                for (var k = 1; k <= member.Nodes && first + 2 * k < words.Length; k++) words[first + 2 * k] = Node(words[first + 2 * k]);
                added.Add(string.Join('\n', [string.Concat(words), .. text[1..]]));
            }
            added.AddRange(part.Knobs.Select(k => KnobWord().Replace(k, m => m.Groups[1].Value + reference, 1)));
            var at = part.Placement;
            placements.Add(at with { Reference = Name(at.Reference), X = at.X + dx, Y = at.Y + dy });
        }
        var end = lines.FindIndex(1, l => l.Trim().Equals(".end", StringComparison.OrdinalIgnoreCase));
        lines.InsertRange(end < 0 ? lines.Count : end, added);

        var wires = clip.Wires.Select(w => new WireRoute(Node(w.Net), w.Points.Select(p => new Point(p.X + dx, p.Y + dy)).ToList()));
        var drawn = wires.ToList();
        return (new SchematicChange(Join(lines), new LayoutDoc([.. layout.Parts, .. placements], [.. layout.Wires, .. drawn])), fresh, renamed);
    }

    /// <summary>
    /// <see cref="Copy"/>, then removes the parts, their knob lines, the copied wires, and the wires on nets that the
    /// netlist no longer has.
    /// </summary>
    public static (SchematicChange Change, Clip Clip) Cut(string netlist, LayoutDoc layout, PartMap parts, IEnumerable<string> references)
    {
        var clip = Copy(netlist, layout, parts, references);
        var lines = Lines(netlist);
        foreach (var part in clip.Parts)
        {
            foreach (var member in part.Members)
            {
                var (at, count) = Element(lines, member.Name);
                lines.RemoveRange(at, count);
            }
            lines.RemoveAll(l => KnobOf(l) is { } k && Names.Equals(k, part.Reference));
        }
        var result = Join(lines);
        var nets = new HashSet<string>(NetlistLoader.Load(result).NodeNames, StringComparer.Ordinal);
        var gone = clip.Parts.Select(p => p.Placement).ToHashSet();
        var kept = layout.Parts.Where(p => !gone.Contains(p)).ToList();
        var wires = layout.Wires.Where(w => nets.Contains(w.Net) && !clip.Wires.Contains(w)).ToList();
        return (new SchematicChange(result, new LayoutDoc(kept, wires)), clip);
    }

    // The elements of some references and their places, once each, in the order of the layout.
    static List<(SchematicElement Element, PartPlacement Placement)> Group(LoadedCircuit circuit, LayoutDoc layout, PartMap parts, IEnumerable<string> references)
    {
        var wanted = references.ToHashSet(Names);
        var group = new List<(SchematicElement, PartPlacement)>();
        foreach (var e in SchematicRenderer.Elements(circuit, parts))
        {
            if (!wanted.Contains(e.Reference) && !e.Members.Any(wanted.Contains)) continue;
            if (layout.Parts.FirstOrDefault(p => Names.Equals(p.Reference, e.Reference) || Names.Equals(p.Reference, e.Members[0])) is { } placement) group.Add((e, placement));
        }
        var order = layout.Parts.ToList();
        return group.OrderBy(g => order.IndexOf(g.Item2)).ToList();
    }

    // The part of a ssp:knob line, or null.
    static string? KnobOf(string line) => KnobWord().Match(line) is { Success: true } m ? m.Groups[2].Value : null;

    static SchematicElement ElementOf(LoadedCircuit circuit, PartMap parts, string reference) =>
        SchematicRenderer.Elements(circuit, parts).Single(e => Names.Equals(e.Reference, reference) || Names.Equals(e.Members[0], reference));

    // NOTE: A wire with a free end is a ground symbol stem. Its bars are the net 0 wires under it, like Ground draws them.
    static WireRoute Follow(WireRoute w, List<Point> before, List<Point> after, HashSet<Point> pins, (double X, double Y) d)
    {
        var points = w.Points.ToList();
        var first = before.IndexOf(points[0]);
        var last = before.IndexOf(points[^1]);
        if (first >= 0 && last >= 0) return w with { Points = points.Select(p => new Point(p.X + d.X, p.Y + d.Y)).ToList() };
        if (first < 0 && last < 0)
        {
            foreach (var pin in before)
            {
                if (w.Net == Zero && points.All(p => Math.Abs(p.X - pin.X) <= 10 && p.Y >= pin.Y + 20 && p.Y <= pin.Y + 28))
                {
                    return w with { Points = points.Select(p => new Point(p.X + d.X, p.Y + d.Y)).ToList() };
                }
            }
            return w;
        }

        var free = first >= 0 ? points[^1] : points[0];
        if (!pins.Contains(free)) return w with { Points = points.Select(p => new Point(p.X + d.X, p.Y + d.Y)).ToList() };
        if (first >= 0) points[0] = after[first];
        else points[^1] = after[last];
        // NOTE: A wire from Wire has at most one corner, at the x of the last point and the y of the first. Keep it that way.
        if (points.Count > 3) return w with { Points = points };
        var (a, b) = (points[0], points[^1]);
        var corner = new Point(b.X, a.Y);
        return w with { Points = corner == a || corner == b ? [a, b] : [a, corner, b] };
    }

    // Joins the nets of the pins of one part to the pins and the wires that they sit on.
    static SchematicChange Settle(SchematicChange change, string reference)
    {
        var (netlist, layout) = (change.Netlist, change.Layout);
        for (var i = 0; ; i++)
        {
            var circuit = NetlistLoader.Load(netlist);
            var parts = PartMap.Resolve(circuit, Ssp.Web.Components.SchematicView.Table);
            var elements = SchematicRenderer.Elements(circuit, parts);
            var element = elements.Single(e => Names.Equals(e.Reference, reference) || Names.Equals(e.Members[0], reference));
            var pins = SchematicRenderer.Pins(element, layout.Parts.Single(p => Names.Equals(p.Reference, reference) || Names.Equals(p.Reference, element.Members[0])));
            if (i >= pins.Count) return new SchematicChange(netlist, layout);
            if (pins[i].Hidden) continue;

            var (node, at) = (pins[i].Net, pins[i].At);
            string? other = null;
            foreach (var e in elements.Where(e => e != element))
            {
                if (layout.Parts.FirstOrDefault(p => Names.Equals(p.Reference, e.Reference) || Names.Equals(p.Reference, e.Members[0])) is not { } placement) continue;
                var hit = SchematicRenderer.Pins(e, placement).FirstOrDefault(p => !p.Hidden && p.At == at);
                if (hit.Net is not null) { other = hit.Net; break; }
            }
            other ??= layout.Wires.FirstOrDefault(w => w.Net != node && OnWire(w, at))?.Net;
            if (other is null || other == node) continue;

            var (keep, drop) = node == Zero ? (node, other) : (other, node);
            var merged = Rename(netlist, circuit, layout, drop, keep, []);
            (netlist, layout) = (merged.Netlist, merged.Layout);
        }
    }

    static bool OnWire(WireRoute w, Point p)
    {
        for (var i = 1; i < w.Points.Count; i++)
        {
            var (a, b) = (w.Points[i - 1], w.Points[i]);
            var cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            if (Math.Abs(cross) > 0.01) continue;
            if (p.X >= Math.Min(a.X, b.X) - 0.01 && p.X <= Math.Max(a.X, b.X) + 0.01 &&
                p.Y >= Math.Min(a.Y, b.Y) - 0.01 && p.Y <= Math.Max(a.Y, b.Y) + 0.01) return true;
        }
        return false;
    }

    static Regex ModelLine(string name) => new($@"^\s*\.model\s+{Regex.Escape(name)}\b", RegexOptions.IgnoreCase);

    /// <summary>
    /// Joins the nodes of two pins. The joined node is node 0 if one of the pins is on it, else the node of the first pin.
    /// The layout gets a wire between the two pins.
    /// </summary>
    public static SchematicChange Wire(string netlist, LayoutDoc layout, PartMap parts, PinRef from, PinRef to) =>
        Wire(netlist, layout, parts, new Spot(Pin(NetlistLoader.Load(netlist), layout, parts, from).Position, from), to);

    /// <summary><see cref="Wire(string, LayoutDoc, PartMap, PinRef, PinRef)"/> from a pin or an open wire end.</summary>
    public static SchematicChange Wire(string netlist, LayoutDoc layout, PartMap parts, Spot from, PinRef to)
    {
        var circuit = NetlistLoader.Load(netlist);
        var a = NodeOf(circuit, layout, parts, from);
        var (b, pb) = Pin(circuit, layout, parts, to);
        var pa = from.At;
        var (keep, drop) = b == Zero ? (b, a) : (a, b);

        var corner = new Point(pb.X, pa.Y);
        var wire = new WireRoute(keep, corner == pa || corner == pb ? [pa, pb] : [pa, corner, pb]);
        return Rename(netlist, circuit, layout, drop, keep, [wire]);
    }

    /// <summary>
    /// Names a net: the node gets the name, in capitals, in the element lines, the <c>ssp:input</c> and <c>ssp:output</c> lines and the
    /// layout wires. A net that already has the name joins it, so two flags with one name are one net. The parts and the
    /// wire points do not change. The name is checked with <see cref="TryNetName"/>.
    /// </summary>
    /// <remarks>
    /// NOTE: The name is the node, not a <c>ssp:net</c> line. A shared link and the chain composer carry the netlist, and
    /// every SPICE tool joins two nodes of one name.
    /// </remarks>
    public static SchematicChange NameNet(string netlist, LayoutDoc layout, string node, string name)
    {
        name = name.Trim().ToUpperInvariant();
        if (!TryNetName(name, out var error)) throw new ArgumentException(error, nameof(name));
        if (node == Zero) throw new ArgumentException("Ground has no other name.", nameof(node));
        var change = new SchematicChange(netlist, layout);
        // NOTE: SPICE reads vref and VREF as one node, but Rename matches the case. Each spelling of the name and the node gets the name.
        var circuit = NetlistLoader.Load(netlist);
        foreach (var from in circuit.NodeNames.Where(n => Names.Equals(n, name) || Names.Equals(n, node)).Append(node).Distinct(StringComparer.Ordinal).Where(n => n != name).ToList())
        {
            change = Rename(change.Netlist, NetlistLoader.Load(change.Netlist), change.Layout, from, name, []);
        }
        return change;
    }

    /// <summary>True if the text can name a net: one word, not ground, with no character that SPICE reads as syntax.</summary>
    public static bool TryNetName(string text, out string error)
    {
        text = text.Trim();
        error = text.Length == 0 || !text.Any(char.IsLetterOrDigit) ? "A net name needs a letter or a digit."
            : text.Any(char.IsWhiteSpace) ? "A net name has no spaces."
            : text == Zero || Names.Equals(text, "gnd") ? "A net cannot be named 0 or gnd: that is ground."
            : text.IndexOfAny(['(', ')', '=', ',', '.', '\'', '"']) >= 0 ? "A net name has no ( ) = , . or quote."
            : "";
        return error.Length == 0;
    }

    /// <summary>
    /// The open ends of the wires: ends that no pin and no other wire touches. A wire on node 0 has none, as its free
    /// ends are the ground symbol.
    /// </summary>
    public static IReadOnlyList<Point> OpenEnds(LayoutDoc layout, IEnumerable<Point> pins)
    {
        var at = pins.ToHashSet();
        var ends = new List<Point>();
        for (var i = 0; i < layout.Wires.Count; i++)
        {
            var w = layout.Wires[i];
            if (w.Net == Zero) continue;
            foreach (var end in new[] { w.Points[0], w.Points[^1] })
            {
                if (at.Contains(end) || ends.Contains(end)) continue;
                if (layout.Wires.Where((o, j) => j != i && OnWire(o, end)).Any()) continue;
                ends.Add(end);
            }
        }
        return ends;
    }

    /// <summary>Connects the node of a pin to node 0. The layout gets a ground symbol below the pin, drawn as wires.</summary>
    public static SchematicChange Ground(string netlist, LayoutDoc layout, PartMap parts, PinRef pin)
    {
        var circuit = NetlistLoader.Load(netlist);
        var (node, p) = Pin(circuit, layout, parts, pin);
        return Ground(netlist, circuit, layout, node, p);
    }

    static SchematicChange Ground(string netlist, LoadedCircuit circuit, LayoutDoc layout, string node, Point p)
    {
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

    static readonly (string Suffix, double Scale)[] Scales =
        [("meg", 1e6), ("k", 1e3), ("", 1), ("m", 1e-3), ("u", 1e-6), ("n", 1e-9), ("p", 1e-12)];

    static readonly double[] E12 = [1.0, 1.2, 1.5, 1.8, 2.2, 2.7, 3.3, 3.9, 4.7, 5.6, 6.8, 8.2];

    /// <summary>
    /// Reads a value such as <c>47k</c>, <c>4u7</c>, <c>10n</c> or <c>1M</c>. A capital M or <c>meg</c> is mega and a small m is milli.
    /// A unit after the scale (Ω, F, H) is ignored. On failure the error says why.
    /// </summary>
    public static bool TryParseValue(string? text, out double value, out string error)
    {
        value = 0;
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0)
        {
            error = "Type a value, like 47k, 4u7 or 10n.";
            return false;
        }

        if (trimmed.StartsWith('-') && ValueText().IsMatch(trimmed[1..]))
        {
            error = "A value must be above zero.";
            return false;
        }

        var m = ValueText().Match(trimmed);
        if (!m.Success)
        {
            error = $"\"{trimmed}\" is not a value. Use a number and a scale, like 47k, 4u7 or 10n.";
            return false;
        }

        // NOTE: 4u7 means 4.7 u: the scale letter stands for the decimal point.
        var number = m.Groups["whole"].Success ? $"{m.Groups["whole"].Value}.{m.Groups["frac"].Value}" : m.Groups["num"].Value;
        var scale = m.Groups["scale"].Value;
        var factor = scale.Length == 0 ? 1 : scale == "M" || scale.Equals("meg", StringComparison.OrdinalIgnoreCase) ? 1e6
            : Scales.First(s => s.Suffix == scale.ToLowerInvariant().Replace("µ", "u")).Scale;
        value = double.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture) * factor;
        if (!double.IsFinite(value) || value <= 0)
        {
            error = double.IsFinite(value) ? "A value must be above zero." : $"\"{trimmed}\" is too large.";
            return false;
        }
        error = "";
        return true;
    }

    /// <summary>Writes a value for the netlist with three significant digits and a SPICE scale: <c>4.7u</c>, <c>1meg</c>.</summary>
    public static string FormatValue(double value)
    {
        var (suffix, scale) = Scales.FirstOrDefault(s => value >= s.Scale * (1 - 1e-9), Scales[^1]);
        var scaled = double.Parse((value / scale).ToString("G3", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return scaled.ToString("0.###", CultureInfo.InvariantCulture) + suffix;
    }

    /// <summary>The next E12 value up (+1) or down (-1) from a value. A value off the series goes to its nearest neighbour in that direction.</summary>
    public static double StepE12(double value, int direction)
    {
        var decade = Math.Floor(Math.Log10(value) + 1e-9);
        var mantissa = value / Math.Pow(10, decade);
        const double Same = 1e-6;
        // NOTE: Past the ends of the list the step goes to the next decade.
        var index = direction > 0
            ? Array.FindIndex(E12, m => m > mantissa * (1 + Same))
            : Array.FindLastIndex(E12, m => m < mantissa * (1 - Same));
        if (index < 0) (index, decade) = direction > 0 ? (0, decade + 1) : (E12.Length - 1, decade - 1);
        var next = E12[index];
        return double.Parse((next * Math.Pow(10, decade)).ToString("G3", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>The value word of a two-pin part, or null when the part has none.</summary>
    public static string? ValueOf(string netlist, string reference)
    {
        var inSubcircuit = false;
        foreach (var line in Lines(netlist).Skip(1))
        {
            var words = Words().Split(line);
            var first = words[0].Length == 0 && words.Length > 2 ? 2 : 0;
            var word = words.Length > first ? words[first] : "";
            if (word.StartsWith(".subckt", StringComparison.OrdinalIgnoreCase)) inSubcircuit = true;
            else if (word.StartsWith(".ends", StringComparison.OrdinalIgnoreCase)) inSubcircuit = false;
            if (inSubcircuit || !Names.Equals(word, reference)) continue;
            return first + 6 < words.Length ? words[first + 6] : null;
        }
        return null;
    }

    [GeneratedRegex(@"^(?:(?<whole>\d+)(?<scale>meg|[pnuµmkM])(?<frac>\d+)|(?<num>(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)\s*(?<scale>meg|[pnuµmkM])?)\s*(?:Ω|ohms?|[FH])?$", RegexOptions.IgnoreCase)]
    private static partial Regex ValueText();

    /// <summary>The node and schematic position of a pin.</summary>
    public static (string Node, Point Position) Pin(LoadedCircuit circuit, LayoutDoc layout, PartMap parts, PinRef pin)
    {
        // NOTE: A pot P is the resistors P_1 and P_2. The layout places it as P_1. Its pins are the top, the wiper and the bottom.
        // NOTE: A switch S is S_1 to S_n and is placed as S_1 in the same way.
        if (SchematicRenderer.Elements(circuit, parts).SingleOrDefault(e => (e.Kind == "pot" || IsSwitch(e.Kind)) && Names.Equals(e.Reference, pin.Reference)) is { } pot)
        {
            var at = layout.Parts.Single(p => Names.Equals(p.Reference, pin.Reference) || Names.Equals(p.Reference, pot.Members[0]));
            return (pot.Nodes[pin.Pin], SchematicRenderer.Pins(pot, at)[pin.Pin].At);
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
        // NOTE: A reference that the netlist no longer has (the text was edited) has no lines to remove. Its placement still goes.
        var members = Found(lines, reference);
        foreach (var member in members)
        {
            var (at, count) = Element(lines, member);
            lines.RemoveRange(at, count);
        }
        lines.RemoveAll(l => KnobOf(l) is { } k && Names.Equals(k, reference));
        var result = members.Count > 0 ? Join(lines) : netlist;

        var nets = new HashSet<string>(NetlistLoader.Load(result).NodeNames, StringComparer.Ordinal);
        var parts = layout.Parts.Where(p => !Names.Equals(p.Reference, reference) && !members.Contains(p.Reference, Names)).ToList();
        var wires = layout.Wires.Where(w => nets.Contains(w.Net)).ToList();
        return new SchematicChange(result, new LayoutDoc(parts, wires));
    }

    /// <summary>
    /// Removes one wire, by its index in the layout. The wire is the only link between the pins that it joined, so the
    /// netlist splits the net: the largest group of pins that the wire touched keeps the node, and each other group moves
    /// to a new node. A ground stem (a wire on node 0 with one free end) moves its pin off node 0. A piece of wire that no
    /// pin reaches any more goes with it, and so do the bars of a ground symbol.
    /// </summary>
    public static SchematicChange DeleteWire(string netlist, LayoutDoc layout, PartMap parts, int index)
    {
        var wire = layout.Wires[index];
        var circuit = NetlistLoader.Load(netlist);
        var pins = new List<(SchematicElement Element, int Pin, Point At)>();
        foreach (var e in SchematicRenderer.Elements(circuit, parts))
        {
            if (layout.Parts.FirstOrDefault(p => Names.Equals(p.Reference, e.Reference) || Names.Equals(p.Reference, e.Members[0])) is not { } placement) continue;
            var all = SchematicRenderer.Pins(e, placement);
            for (var i = 0; i < all.Count; i++)
            {
                if (!all[i].Hidden && all[i].Net == wire.Net) pins.Add((e, i, all[i].At));
            }
        }

        // The other wires of the net, by index in the layout.
        var rest = Enumerable.Range(0, layout.Wires.Count).Where(i => i != index && layout.Wires[i].Net == wire.Net).ToList();
        // Union-find over the pins, then the rest wires.
        var parent = Enumerable.Range(0, pins.Count + rest.Count).ToArray();
        int Find(int x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
        void Union(int a, int b) => parent[Find(a)] = Find(b);
        for (var w = 0; w < rest.Count; w++)
        {
            var route = layout.Wires[rest[w]];
            for (var k = 0; k < pins.Count; k++)
            {
                if (OnWire(route, pins[k].At)) Union(k, pins.Count + w);
            }
            for (var v = 0; v < w; v++)
            {
                var other = layout.Wires[rest[v]];
                if (route.Points.Any(q => OnWire(other, q)) || other.Points.Any(q => OnWire(route, q))) Union(pins.Count + v, pins.Count + w);
            }
        }

        var touched = Enumerable.Range(0, pins.Count).Where(k => OnWire(wire, pins[k].At)).Select(Find).Distinct().ToList();
        var ends = new[] { wire.Points[0], wire.Points[^1] };
        var stem = wire.Net == Zero && ends.Count(q => pins.Any(p => p.At == q)) < 2;
        int PinsIn(int root) => Enumerable.Range(0, pins.Count).Count(k => Find(k) == root);
        var keeper = stem || touched.Count == 0 ? -1 : touched.MaxBy(PinsIn);
        var moved = touched.Where(r => r != keeper).ToList();

        var lines = Lines(netlist);
        var used = new HashSet<string>(circuit.NodeNames, StringComparer.Ordinal);
        var renamed = new Dictionary<int, string>();
        foreach (var root in moved)
        {
            var node = Fresh("n", used);
            used.Add(node);
            renamed[root] = node;
            foreach (var (element, pin, _) in Enumerable.Range(0, pins.Count).Where(k => Find(k) == root).Select(k => pins[k]))
            {
                // NOTE: A pot P is P_1 and P_2. The wiper is the second node of P_1 and the first node of P_2.
                if (element.Kind == "pot" && element.Members.Count == 2)
                {
                    if (pin < 2) SetNode(lines, element.Members[0], pin, node);
                    if (pin > 0) SetNode(lines, element.Members[1], pin - 1, node);
                }
                // NOTE: A switch S is S_1 to S_n. The common is the first node of each, throw n the second node of S_n.
                else if (IsSwitch(element.Kind))
                {
                    if (pin == 0) foreach (var member in element.Members) SetNode(lines, member, 0, node);
                    else SetNode(lines, element.Members[pin - 1], 1, node);
                }
                else SetNode(lines, element.Members[0], pin, node);
            }
        }

        var drop = new HashSet<int> { index };
        if (stem)
        {
            var free = ends.FirstOrDefault(q => pins.All(p => p.At != q), ends[^1]);
            foreach (var i in rest)
            {
                var points = layout.Wires[i].Points;
                if (points.All(q => Math.Abs(q.X - free.X) <= 10 && q.Y >= free.Y && q.Y <= free.Y + 8)) drop.Add(i);
            }
        }
        var wires = new List<WireRoute>();
        for (var i = 0; i < layout.Wires.Count; i++)
        {
            var w = rest.IndexOf(i);
            if (w >= 0)
            {
                var root = Find(pins.Count + w);
                var reaches = Enumerable.Range(0, pins.Count).Any(k => Find(k) == root);
                // NOTE: A piece with no pin that touched the deleted wire only drew that link.
                if (!reaches && layout.Wires[i].Points.Any(q => OnWire(wire, q)) || (!reaches && wire.Points.Any(q => OnWire(layout.Wires[i], q)))) drop.Add(i);
                if (!drop.Contains(i) && renamed.TryGetValue(root, out var node)) { wires.Add(layout.Wires[i] with { Net = node }); continue; }
            }
            if (!drop.Contains(i)) wires.Add(layout.Wires[i]);
        }
        return new SchematicChange(Join(lines), new LayoutDoc(layout.Parts, wires));
    }

    // Sets the node of one pin of an element line. The pin index is the node index.
    static void SetNode(List<string> lines, string reference, int pin, string node)
    {
        // NOTE: The members of an op-amp X1 are the lines of its .subckt, named X1.Rid and so on. Its pins are on the line of X1.
        var (at, _) = Element(lines, reference.Split('.')[0]);
        var words = Words().Split(lines[at]);
        var first = words[0].Length == 0 && words.Length > 2 ? 2 : 0;
        // NOTE: Words has the separators at odd indices.
        words[first + 2 * (pin + 1)] = node;
        lines[at] = string.Concat(words);
    }

    /// <summary>
    /// Copies a part with the next free reference of its prefix and a new node for each pin. A pot or a switch copies
    /// all its lines and its knob or switch line, and its lines keep the nodes they share. The layout places the
    /// copy one column to the right of the other parts, with the rotation and flip of the part.
    /// </summary>
    public static SchematicChange Duplicate(string netlist, LayoutDoc layout, string reference)
    {
        var circuit = NetlistLoader.Load(netlist);
        var lines = Lines(netlist);
        var members = Found(lines, reference);
        // NOTE: A part that the netlist has no line for, or that the loader did not read, has nothing to copy (the text was edited, #278).
        if (members.Count == 0 || members.Any(m => !circuit.Circuit.OfType<IComponent>().Any(c => Names.Equals(c.Name, m)))) return new SchematicChange(netlist, layout);
        var references = circuit.Circuit.Select(e => e.Name.Split('.')[0]).SelectMany(n => new[] { n, Pair().Replace(n, "") }).ToHashSet(Names);
        var copy = Fresh(Prefix().Match(reference).Value, references);
        var nodes = new HashSet<string>(circuit.NodeNames, StringComparer.Ordinal);
        // NOTE: The members of a pot or a switch share nodes, so a node gets one new name for all of them.
        var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
        string Node(string node)
        {
            if (renamed.TryGetValue(node, out var to)) return to;
            to = Fresh("n", nodes);
            nodes.Add(to);
            return renamed[node] = to;
        }

        var added = new List<string>();
        var last = 0;
        foreach (var member in members)
        {
            var component = circuit.Circuit.OfType<IComponent>().Single(c => Names.Equals(c.Name, member));
            var (at, count) = Element(lines, member);
            var words = Words().Split(lines[at]);
            var first = words[0].Length == 0 && words.Length > 2 ? 2 : 0;
            words[first] = copy + member[reference.Length..];
            // NOTE: Each pin of a single part gets its own new node, also two pins on one node.
            for (var k = 1; k <= component.Nodes.Count; k++)
            {
                words[first + 2 * k] = members.Count == 1 ? Node(words[first + 2 * k] + "\n" + k) : Node(words[first + 2 * k]);
            }
            added.AddRange([string.Concat(words), .. lines.GetRange(at + 1, count - 1)]);
            last = Math.Max(last, at + count);
        }
        added.AddRange(lines.Where(l => KnobOf(l) is { } knob && Names.Equals(knob, reference)).Select(l => KnobWord().Replace(l, m => m.Groups[1].Value + copy, 1)));
        lines.InsertRange(last, added);

        var source = layout.Parts.Single(p => Names.Equals(p.Reference, reference) || Names.Equals(p.Reference, members[0]));
        var placement = source with { Reference = copy + source.Reference[reference.Length..], X = layout.Parts.Max(p => p.X) + ColumnStep };
        return new SchematicChange(Join(lines), new LayoutDoc([.. layout.Parts, placement], layout.Wires));
    }

    static SchematicChange Edit(string netlist, LayoutDoc layout, string reference, Func<PartPlacement, PartPlacement> edit)
    {
        var parts = layout.Parts.Select(p => Names.Equals(p.Reference, reference) ? edit(p) : p).ToList();
        return new SchematicChange(netlist, new LayoutDoc(parts, layout.Wires));
    }

    // The first line and the count of lines of an element: its line and the continuation lines after it.
    // NOTE: Lines in a .subckt block are not elements of the circuit. The first line is the title.
    // The element lines of a part: the part itself, or the lines P_1 to P_3 of a pot or a switch P.
    static List<string> Members(List<string> lines, string reference)
    {
        var members = Found(lines, reference);
        return members.Count > 0 ? members : throw new KeyNotFoundException($"The netlist has no element '{reference}'.");
    }

    // The same as Members, but empty for a reference that the netlist does not have.
    static List<string> Found(List<string> lines, string reference)
    {
        if (Has(lines, reference)) return [reference];
        return Enumerable.Range(1, 3).Select(i => $"{reference}_{i}").Where(m => Has(lines, m)).ToList();
    }

    static bool Has(List<string> lines, string reference)
    {
        try { Element(lines, reference); return true; }
        catch (KeyNotFoundException) { return false; }
    }

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

    [GeneratedRegex(@"_[123]$")]
    private static partial Regex Pair();

    [GeneratedRegex(@"^[A-Za-z]+")]
    private static partial Regex Prefix();

    [GeneratedRegex(@"^\s*\.model\s+(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex ModelName();

    [GeneratedRegex(@"^\s*\.subckt\s+(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex SubcircuitName();

    // NOTE: A switch line goes with its part like a knob line.
    [GeneratedRegex(@"^(\s*\*\s*ssp:(?:knob|switch)\s+)(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex KnobWord();

    [GeneratedRegex(@"^(\s*\*\s*ssp:switch\s+)(\S+)(\s+)(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex SwitchLine();
}

/// <summary>One netlist line of a copied part: the component name, the text with its continuation lines, and the count of nodes.</summary>
public sealed record ClipLine(string Name, string Text, int Nodes);

/// <summary>A copied part: its reference, its lines, its knob lines and its place.</summary>
public sealed record ClipPart(string Reference, IReadOnlyList<ClipLine> Members, IReadOnlyList<string> Knobs, PartPlacement Placement);

/// <summary>What <see cref="SchematicEdits.Copy"/> keeps: the parts, the .model lines they use, and the wires between them.</summary>
/// <summary>
/// What <see cref="SchematicEdits.Copy"/> keeps: the parts, the .model lines they use, and the wires between them. A block
/// has the nodes of its <c>ssp:input</c> and <c>ssp:output</c> lines.
/// </summary>
public sealed record Clip(IReadOnlyList<ClipPart> Parts, IReadOnlyList<string> Models, IReadOnlyList<WireRoute> Wires, string? Input = null, string? Output = null);

/// <summary>What <see cref="SchematicEdits.Drop"/> gives: the change, the new references, a note for the status line, and why a drop was refused.</summary>
public sealed record BlockDrop(SchematicChange Change, IReadOnlyList<string> References, string Message, string? Refused = null);
