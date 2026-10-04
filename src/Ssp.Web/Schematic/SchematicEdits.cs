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
        "jack-in" => (1, 0),
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

    static bool HasDirective(string netlist, string name) =>
        Lines(netlist).Any(l => Regex.IsMatch(l, $@"^\s*\*\s*ssp:{name}\b", RegexOptions.IgnoreCase));

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
        var (at, _) = Element(lines, reference);
        var words = Words().Split(lines[at]);
        var first = words[0].Length == 0 && words.Length > 2 ? 2 : 0;
        // NOTE: Words has the separators at odd indices.
        words[first + 2 * (pin + 1)] = node;
        lines[at] = string.Concat(words);
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
