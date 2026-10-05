using Ssp.Core.Layout;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using LayoutDoc = Ssp.Core.Layout.Layout;
using Point = Ssp.Core.Layout.Point;

namespace Ssp.Web.Schematic;

/// <summary>Makes a layout for a circuit that has none.</summary>
public static class AutoPlacer
{
    const int Grid = Symbols.Grid;

    // NOTE: A ground or rail symbol is 20 wide and 20 long. The renderer draws one at each pin on a ground or rail net.
    const int SymbolHalf = 10;
    const int SymbolLength = 20;

    // NOTE: A label needs room beside a part. The renderer puts the label of a vertical part to its right, and the labels of other parts above and below.
    const int LabelGap = 4;
    const double CharWidth = 6;
    const int RowLabel = 14;

    const int Margin = 2 * Grid;

    static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    static readonly (int Rotation, bool Flip)[] Orientations =
        [(0, false), (0, true), (180, false), (180, true), (90, false), (90, true), (270, false), (270, true)];

    /// <summary>
    /// Places the elements of the schematic like a drawing of a pedal: the signal goes from left to right, a supply is
    /// at the top of its part and ground is at the bottom. A subcircuit instance is one element. A part is placed in
    /// the orientation that puts its supply pins up, its ground pins down and its input side left. The first part of
    /// the signal path is at the origin. Each next part has a pin on the net of a part that is already placed, and is
    /// moved only as far as it needs to be clear of the parts and symbols around it. A ground net or a supply net has
    /// no wires: the renderer draws a symbol at each of its pins. The other nets are wires with orthogonal segments
    /// that go around the parts. The layout is the same for the same netlist.
    /// </summary>
    /// <remarks>
    /// With a layout to keep, each part that has a placement in it keeps that placement, and only the other parts are
    /// placed. The wires are routed again. A netlist edit that adds a part keeps the hand layout of the parts before it.
    /// </remarks>
    public static LayoutDoc Place(LoadedCircuit circuit, Directives directives, PartMap? partMap = null, LayoutDoc? keep = null)
    {
        var all = SchematicRenderer.Elements(circuit, partMap);
        var rails = all.Where(e => e.Kind == "rail").ToList();
        var railNets = rails.ToDictionary(e => e.Nodes[0], e => !e.Value.StartsWith('-'), Names);
        var parts = Order(all.Where(e => e.Kind != "rail").ToList(), directives, railNets);
        var references = circuit.Circuit.Select(c => c.Name.Split('.')[0]).ToHashSet(Names);

        var onNet = new Dictionary<string, List<int>>(Names);
        for (var i = 0; i < parts.Count; i++)
        {
            foreach (var net in parts[i].Nodes.Distinct(Names))
            {
                if (!onNet.TryGetValue(net, out var list)) onNet[net] = list = [];
                list.Add(i);
            }
        }

        var sheet = new Sheet();
        var placements = new List<PartPlacement>();
        var railAt = new Dictionary<string, Point>(Names);
        var kept = (keep?.Parts ?? []).GroupBy(p => p.Reference, Names).ToDictionary(g => g.Key, g => g.First(), Names);

        void Put(SchematicElement e, PartPlacement placement)
        {
            placements.Add(placement);
            sheet.Add(e, placement, railNets);
            foreach (var pin in SchematicRenderer.Pins(e, placement).Where(p => !p.Hidden && railNets.ContainsKey(p.Net)))
            {
                railAt.TryAdd(pin.Net, pin.At);
            }
        }

        // NOTE: The kept parts go on the sheet first, so each new part keeps clear of all of them.
        var fresh = new List<int>();
        for (var index = 0; index < parts.Count; index++)
        {
            if (kept.TryGetValue(Reference(parts[index], references), out var placement)) Put(parts[index], placement);
            else fresh.Add(index);
        }
        var moved = fresh.Count == parts.Count;

        foreach (var index in fresh)
        {
            var e = parts[index];
            var reference = Reference(e, references);
            var (rotation, flip) = Orientation(e, reference, index, onNet, railNets);
            Put(e, Fit(sheet, e, new PartPlacement(reference, 0, 0, rotation, flip), railNets));
        }

        // A rail element sits on the first pin of its net, so the pin has no second symbol.
        var spare = sheet.Right + 2 * Grid;
        foreach (var rail in rails)
        {
            var reference = Reference(rail, references);
            if (kept.TryGetValue(reference, out var placement))
            {
                placements.Add(placement);
                continue;
            }
            var at = railAt.TryGetValue(rail.Nodes[0], out var found) ? found : new Point(spare += 4 * Grid, 0);
            placements.Add(new PartPlacement(reference, at.X, at.Y, 0, !railNets[rail.Nodes[0]]));
        }

        var wires = Wires(sheet, railNets);
        return Shift(placements, wires, moved ? sheet : null);
    }

    /// <summary>
    /// The parts of the schematic and the nets of their pins. Two netlists with the same shape have the same drawing
    /// but for the labels: an edit of a value or a comment keeps the shape, and so it keeps the layout.
    /// </summary>
    public static string Shape(LoadedCircuit circuit) => string.Join('\n', SchematicRenderer.Elements(circuit, null)
        .Select(e => $"{e.Kind} {e.Reference} {string.Join(' ', e.Members)} : {string.Join(' ', e.Nodes)}{(e.Kind == "rail" && e.Value.StartsWith('-') ? " -" : "")}")
        .Order(StringComparer.Ordinal));

    // The reference of the placement of an element. A pot is placed by its first half, like the netlist names it.
    // NOTE: A subcircuit that the netlist does not define has no members. It is placed by its own reference.
    static string Reference(SchematicElement e, HashSet<string> references) =>
        references.Contains(e.Reference) || e.Members.Count == 0 ? e.Reference : e.Members.Order(Names).First();

    // The parts in the order of the signal path. A part with two pins goes before a part with more, so the divider is at the origin and the transistor is to its right.
    static List<SchematicElement> Order(List<SchematicElement> parts, Directives directives, Dictionary<string, bool> railNets)
    {
        var onNet = new Dictionary<string, List<SchematicElement>>(Names);
        foreach (var e in parts)
        {
            foreach (var net in e.Nodes.Distinct(Names))
            {
                if (!onNet.TryGetValue(net, out var list)) onNet[net] = list = [];
                list.Add(e);
            }
        }

        bool Crossed(string net) => IsGround(net) || railNets.ContainsKey(net);

        var columns = new Dictionary<string, int>(Names);
        var seen = new HashSet<string>(Names);
        var source = parts.FirstOrDefault(e => e.Kind == "vsource" && e.Nodes.Count == 2 && IsGround(e.Nodes[1]) != IsGround(e.Nodes[0]));
        var input = directives.Input
                    ?? (source is null ? null : IsGround(source.Nodes[0]) ? source.Nodes[1] : source.Nodes[0])
                    ?? onNet.Keys.Where(n => !Crossed(n)).Order(StringComparer.Ordinal).FirstOrDefault();
        var output = directives.Output;

        void Walk(string start, int first, string? stop)
        {
            if (!onNet.ContainsKey(start) || !seen.Add(start)) return;
            var queue = new Queue<(string Net, int Column)>([(start, first)]);
            while (queue.TryDequeue(out var item))
            {
                if (stop is not null && Names.Equals(item.Net, stop) && !Names.Equals(item.Net, start)) continue;
                foreach (var e in onNet[item.Net])
                {
                    if (!columns.TryAdd(e.Reference, item.Column)) continue;
                    foreach (var net in e.Nodes)
                    {
                        if (!Crossed(net) && seen.Add(net)) queue.Enqueue((net, item.Column + 1));
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
        foreach (var e in parts) columns.TryAdd(e.Reference, rest);

        return parts
            .OrderBy(e => columns[e.Reference])
            .ThenBy(e => e.Nodes.Count > 2 ? 1 : 0)
            .ThenBy(e => e.Reference, Names)
            .ThenBy(e => e.Reference, StringComparer.Ordinal)
            .ToList();
    }

    // The orientation that puts supply pins up, ground pins down, the pins on the nets of earlier parts left and the others right.
    // The first orientation with the best score wins, so a symmetric part keeps its drawing.
    static (int Rotation, bool Flip) Orientation(SchematicElement e, string reference, int index, Dictionary<string, List<int>> onNet, Dictionary<string, bool> railNets)
    {
        var best = (Rotation: 0, Flip: false);
        var bestScore = double.NegativeInfinity;
        foreach (var (rotation, flip) in Orientations)
        {
            var pins = SchematicRenderer.Pins(e, new PartPlacement(reference, 0, 0, rotation, flip)).Where(p => !p.Hidden).ToList();
            var (cx, cy) = (pins.Average(p => p.At.X), pins.Average(p => p.At.Y));
            var score = 0.0;
            foreach (var pin in pins)
            {
                var (dx, dy) = (pin.At.X - cx, pin.At.Y - cy);
                if (IsGround(pin.Net)) score += 3 * dy;
                else if (railNets.TryGetValue(pin.Net, out var positive)) score += positive ? -3 * dy : 3 * dy;
                else
                {
                    var others = onNet[pin.Net];
                    score += (others.Any(o => o > index) ? dx : 0) - (others.Any(o => o < index) ? dx : 0);
                }
            }
            if (score > bestScore)
            {
                bestScore = score;
                best = (rotation, flip);
            }
        }
        return best;
    }

    // The position of the part that is near a pin of a placed net, with no overlap and a clear route from each placed net.
    // It tries each pin that is on a placed net as the anchor, and keeps the cheapest: the move from the anchor plus the routes.
    // The first try wants every route to have one bend at most. The second try takes any route.
    static PartPlacement Fit(Sheet sheet, SchematicElement e, PartPlacement oriented, Dictionary<string, bool> railNets)
    {
        var pins = SchematicRenderer.Pins(e, oriented).Where(p => !p.Hidden).ToList();
        var wired = pins.Where(p => !IsGround(p.Net) && !railNets.ContainsKey(p.Net) && sheet.Anchor.ContainsKey(p.Net))
            .OrderBy(p => p.At.X).ThenBy(p => p.At.Y).ToList();
        if (!sheet.Placed || wired.Count == 0) return oriented with { X = sheet.Placed ? sheet.Right + 4 * Grid : 0, Y = 0 };

        var vertical = oriented.Rotation is 90 or 270;
        var offsets = Offsets.OrderBy(o => vertical ? 3 * Math.Abs(o.X) + Math.Abs(o.Y) : Math.Abs(o.X) + 3 * Math.Abs(o.Y))
            .ThenByDescending(o => o.Y).ThenByDescending(o => o.X).ToList();

        PartPlacement? clear = null;
        foreach (var simple in new[] { true, false })
        {
            PartPlacement? best = null;
            var bestCost = double.PositiveInfinity;
            foreach (var pin in wired)
            {
                var at = sheet.Anchor[pin.Net];
                foreach (var offset in offsets)
                {
                    var placement = oriented with { X = at.X - pin.At.X + offset.X, Y = at.Y - pin.At.Y + offset.Y };
                    if (sheet.Overlaps(e, placement, railNets)) continue;
                    clear ??= placement;
                    if (sheet.RouteCost(e, placement, railNets, simple) is not { } routes) continue;
                    var cost = Math.Abs(offset.X) + Math.Abs(offset.Y) + routes;
                    if (cost < bestCost) (best, bestCost) = (placement, cost);
                    break;
                }
            }
            if (best is not null) return best;
        }
        return clear ?? oriented with { X = sheet.Right + 4 * Grid, Y = 0 };
    }

    static readonly List<Point> Offsets = Enumerable.Range(-10, 41).SelectMany(x => Enumerable.Range(-20, 41).Select(y => new Point(x * Grid, y * Grid))).ToList();

    // Each net is a tree: the next pin joins by the cheapest route to a pin that is already joined.
    static List<WireRoute> Wires(Sheet sheet, Dictionary<string, bool> railNets)
    {
        var wires = new List<WireRoute>();
        foreach (var (net, all) in sheet.Pins.Where(p => !IsGround(p.Key) && !railNets.ContainsKey(p.Key)).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var avoid = sheet.Foreign(net);
            var joined = new List<Point> { all[0] };
            var rest = all.Skip(1).Distinct().Where(p => p != all[0]).ToList();
            while (rest.Count > 0)
            {
                var (from, to, route, cost) = (default(Point), default(Point), (IReadOnlyList<Point>?)null, double.PositiveInfinity);
                foreach (var a in joined)
                {
                    foreach (var b in rest)
                    {
                        var r = Router.Route(a, b, sheet.Solid, avoid);
                        var c = r is null ? 1e6 + Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) : Router.Cost(r);
                        if (c >= cost) continue;
                        (from, to, route, cost) = (a, b, r, c);
                    }
                }
                wires.Add(new WireRoute(net, route ?? Router.Corner(from, to)));
                joined.Add(to);
                rest.Remove(to);
            }
        }
        return wires;
    }

    // Moves everything so that the top left of the drawing is at (2 grid, 2 grid) and every coordinate is on the grid.
    // NOTE: With no sheet nothing moves: a kept part stays where the hand put it.
    static LayoutDoc Shift(List<PartPlacement> placements, List<WireRoute> wires, Sheet? sheet)
    {
        var dx = sheet is null ? 0 : Margin - Math.Floor(sheet.Left / Grid) * Grid;
        var dy = sheet is null ? 0 : Margin - Math.Floor(sheet.Top / Grid) * Grid;
        return new LayoutDoc(
            placements.Select(p => p with { X = p.X + dx, Y = p.Y + dy }).OrderBy(p => p.Reference, Names).ThenBy(p => p.Reference, StringComparer.Ordinal).ToList(),
            wires.Select(w => new WireRoute(w.Net, w.Points.Select(pt => new Point(pt.X + dx, pt.Y + dy)).ToList())).ToList());
    }

    static bool IsGround(string node) => node == "0" || Names.Equals(node, "gnd");

    /// <summary>A rectangle in schematic units. Boxes that only touch do not overlap.</summary>
    internal readonly record struct Box(double X0, double Y0, double X1, double Y1)
    {
        public bool Overlaps(Box o) => X0 < o.X1 && o.X0 < X1 && Y0 < o.Y1 && o.Y0 < Y1;
    }

    // What is on the sheet so far: boxes for the placements, and the pins of each net in the order they were placed.
    sealed class Sheet
    {
        // The boxes that the parts keep clear, with room for the labels.
        readonly List<Box> room = [];

        public List<Box> Solid { get; } = [];

        public Dictionary<string, List<Point>> Pins { get; } = new(Names);

        public Dictionary<string, Point> Anchor { get; } = new(Names);

        public bool Placed { get; private set; }

        readonly List<(string Net, Point At)> everyPin = [];

        /// <summary>The pins of the other nets.</summary>
        public List<Point> Foreign(string net, IEnumerable<SchematicRenderer.ElementPin>? more = null) =>
            everyPin.Select(p => (p.Net, p.At)).Concat((more ?? []).Select(p => (p.Net, p.At))).Where(p => !Names.Equals(p.Net, net)).Select(p => p.At).ToList();

        public double Left { get; private set; } = double.PositiveInfinity;

        public double Top { get; private set; } = double.PositiveInfinity;

        public double Right { get; private set; } = double.NegativeInfinity;

        public bool Overlaps(SchematicElement e, PartPlacement p, Dictionary<string, bool> railNets)
        {
            var (body, symbols) = Boxes(e, p, railNets);
            // NOTE: Two pins of different nets at one point look like one node.
            var touching = SchematicRenderer.Pins(e, p).Any(pin => !pin.Hidden && everyPin.Any(q => q.At == pin.At && !Names.Equals(q.Net, pin.Net)));
            return touching || room.Any(r => r.Overlaps(body) || symbols.Any(r.Overlaps));
        }

        /// <summary>The total cost of the routes from the placed nets to the pins of the part, or null if a route is blocked. A simple route has one bend at most.</summary>
        public double? RouteCost(SchematicElement e, PartPlacement p, Dictionary<string, bool> railNets, bool simple)
        {
            var (_, symbols) = Boxes(e, p, railNets);
            var solid = Solid.Concat(symbols.Prepend(Plain(e, p))).Concat(TextBoxes(e, p)).ToList();
            var pins = SchematicRenderer.Pins(e, p).Where(x => !x.Hidden).ToList();
            var total = 0.0;
            foreach (var pin in pins.Where(x => !IsGround(x.Net) && !railNets.ContainsKey(x.Net)))
            {
                if (!Anchor.TryGetValue(pin.Net, out var at) || at == pin.At) continue;
                var route = Router.Route(at, pin.At, solid, Foreign(pin.Net, pins), search: false);
                if (route is null || simple && route.Count > 3) return null;
                total += Router.Cost(route);
            }
            return total;
        }

        public void Add(SchematicElement e, PartPlacement p, Dictionary<string, bool> railNets)
        {
            var (body, symbols) = Boxes(e, p, railNets);
            room.Add(body);
            room.AddRange(symbols);
            Solid.Add(Plain(e, p));
            Solid.AddRange(symbols);
            Solid.AddRange(TextBoxes(e, p));
            Placed = true;
            foreach (var box in symbols.Prepend(body))
            {
                Left = Math.Min(Left, box.X0);
                Top = Math.Min(Top, box.Y0);
                Right = Math.Max(Right, box.X1);
            }
            everyPin.AddRange(SchematicRenderer.Pins(e, p).Where(x => !x.Hidden).Select(x => (x.Net, x.At)));
            // NOTE: A net with two pins of one part, such as the feedback of a follower, is anchored on the rightmost pin: the output.
            foreach (var pin in SchematicRenderer.Pins(e, p).Where(x => !x.Hidden && !IsGround(x.Net) && !railNets.ContainsKey(x.Net)).OrderByDescending(x => x.At.X))
            {
                if (!Pins.TryGetValue(pin.Net, out var list)) Pins[pin.Net] = list = [];
                list.Add(pin.At);
                Anchor.TryAdd(pin.Net, pin.At);
            }
        }

        // The rectangles of the text of the part. A wire does not cross them.
        static IEnumerable<Box> TextBoxes(SchematicElement e, PartPlacement p) =>
            SchematicRenderer.Labels(e, p).Select(l => l.Extent).Select(x => new Box(x.X0, x.Y0, x.X1, x.Y1));

        static Box Plain(SchematicElement e, PartPlacement p)
        {
            var (min, max) = SchematicRenderer.Outline(e, p);
            return new Box(min.X, min.Y, max.X, max.Y);
        }

        // The body with room for its labels, and the symbols that the renderer draws at its ground and rail pins.
        static (Box Body, List<Box> Symbols) Boxes(SchematicElement e, PartPlacement p, Dictionary<string, bool> railNets)
        {
            var box = Plain(e, p);
            var width = CharWidth * Math.Max(e.Reference.Length, e.Value.Length);
            var middle = (box.X0 + box.X1) / 2;
            var body = p.Rotation is 90 or 270
                ? box with { X1 = box.X1 + LabelGap + width }
                : new Box(Math.Min(box.X0, middle - width / 2), box.Y0 - RowLabel, Math.Max(box.X1, middle + width / 2), box.Y1 + RowLabel);
            var symbols = new List<Box>();
            foreach (var pin in SchematicRenderer.Pins(e, p).Where(x => !x.Hidden))
            {
                var (x, y) = (pin.At.X, pin.At.Y);
                if (IsGround(pin.Net)) symbols.Add(new Box(x - SymbolHalf, y, x + SymbolHalf, y + SymbolLength));
                else if (railNets.TryGetValue(pin.Net, out var positive))
                {
                    symbols.Add(positive ? new Box(x - SymbolHalf, y - SymbolLength - RowLabel, x + SymbolHalf, y) : new Box(x - SymbolHalf, y, x + SymbolHalf, y + SymbolLength + RowLabel));
                }
            }
            return (body, symbols);
        }
    }

    // Orthogonal routes between two points that keep clear of the boxes.
    static class Router
    {
        const int Reach = 12 * Grid;
        const int BendCost = 2 * Grid;

        public static IReadOnlyList<Point> Corner(Point a, Point b) =>
            a.X == b.X || a.Y == b.Y ? [a, b] : [a, new Point(b.X, a.Y), b];

        /// <summary>The shortest route with the fewest bends, or null if every route crosses a box. A route may touch the edge of a box.</summary>
        public static IReadOnlyList<Point>? Route(Point a, Point b, IReadOnlyList<Box> solid, IReadOnlyList<Point> avoid, bool search = true)
        {
            IReadOnlyList<Point>? best = null;
            var bestCost = double.PositiveInfinity;
            foreach (var path in Candidates(a, b))
            {
                var cost = Cost(path);
                if (cost >= bestCost || path.Zip(path.Skip(1)).Any(s => solid.Any(box => Crosses(s.First, s.Second, box)) || avoid.Any(pin => On(s.First, s.Second, pin)))) continue;
                (best, bestCost) = (path, cost);
            }
            return best ?? (search ? Search(a, b, solid, avoid) : null);
        }

        // The cheapest route on the grid when no simple route is clear. The cost of a move is its length, and a bend costs more.
        static IReadOnlyList<Point>? Search(Point a, Point b, IReadOnlyList<Box> solid, IReadOnlyList<Point> avoid)
        {
            var x0 = Math.Floor(Math.Min(solid.Select(s => s.X0).Append(a.X).Min(), b.X) / Grid) * Grid - 4 * Grid;
            var y0 = Math.Floor(Math.Min(solid.Select(s => s.Y0).Append(a.Y).Min(), b.Y) / Grid) * Grid - 4 * Grid;
            var x1 = Math.Ceiling(Math.Max(solid.Select(s => s.X1).Append(a.X).Max(), b.X) / Grid) * Grid + 4 * Grid;
            var y1 = Math.Ceiling(Math.Max(solid.Select(s => s.Y1).Append(a.Y).Max(), b.Y) / Grid) * Grid + 4 * Grid;
            (int X, int Y)[] steps = [(1, 0), (0, 1), (-1, 0), (0, -1)];
            var start = ((int)Math.Round((a.X - x0) / Grid), (int)Math.Round((a.Y - y0) / Grid));
            var goal = ((int)Math.Round((b.X - x0) / Grid), (int)Math.Round((b.Y - y0) / Grid));
            var (w, h) = ((int)((x1 - x0) / Grid) + 1, (int)((y1 - y0) / Grid) + 1);
            Point At((int X, int Y) c) => new(x0 + c.X * Grid, y0 + c.Y * Grid);

            var cost = new Dictionary<(int, int, int), double>();
            var from = new Dictionary<(int, int, int), (int, int, int)>();
            var queue = new PriorityQueue<(int X, int Y, int Dir), double>();
            for (var d = 0; d < 4; d++)
            {
                cost[(start.Item1, start.Item2, d)] = 0;
                queue.Enqueue((start.Item1, start.Item2, d), 0);
            }
            while (queue.TryDequeue(out var cur, out var spent))
            {
                if (spent > cost[cur]) continue;
                if ((cur.X, cur.Y) == goal)
                {
                    var cells = new List<Point> { At((cur.X, cur.Y)) };
                    var key = cur;
                    while (from.TryGetValue(key, out var prev))
                    {
                        key = prev;
                        cells.Add(At((key.Item1, key.Item2)));
                    }
                    cells.Reverse();
                    return cells.Where((c, i) => i == 0 || i == cells.Count - 1 || (c.X - cells[i - 1].X) * (cells[i + 1].Y - c.Y) != (c.Y - cells[i - 1].Y) * (cells[i + 1].X - c.X)).ToList();
                }
                for (var d = 0; d < 4; d++)
                {
                    var next = (X: cur.X + steps[d].X, Y: cur.Y + steps[d].Y);
                    if (next.X < 0 || next.Y < 0 || next.X >= w || next.Y >= h) continue;
                    var (p, q) = (At((cur.X, cur.Y)), At(next));
                    var target = (next.X, next.Y) == goal;
                    if (!target && avoid.Any(pin => pin == q) || solid.Any(box => Crosses(p, q, box))) continue;
                    var key = (next.X, next.Y, d);
                    var total = spent + Grid + (d == cur.Dir ? 0 : BendCost);
                    if (cost.TryGetValue(key, out var known) && known <= total) continue;
                    cost[key] = total;
                    from[key] = cur;
                    queue.Enqueue(key, total);
                }
            }
            return null;
        }

        static IEnumerable<Point[]> Candidates(Point a, Point b)
        {
            if (a.X == b.X || a.Y == b.Y) yield return [a, b];
            yield return [a, new Point(b.X, a.Y), b];
            yield return [a, new Point(a.X, b.Y), b];
            var (x0, x1) = (Math.Min(a.X, b.X) - Reach, Math.Max(a.X, b.X) + Reach);
            var (y0, y1) = (Math.Min(a.Y, b.Y) - Reach, Math.Max(a.Y, b.Y) + Reach);
            for (var x = Math.Ceiling(x0 / Grid) * Grid; x <= x1; x += Grid) yield return Clean([a, new Point(x, a.Y), new Point(x, b.Y), b]);
            for (var y = Math.Ceiling(y0 / Grid) * Grid; y <= y1; y += Grid) yield return Clean([a, new Point(a.X, y), new Point(b.X, y), b]);
        }

        // Removes a point that repeats the one before it.
        static Point[] Clean(Point[] path) => path.Where((p, i) => i == 0 || p != path[i - 1]).ToArray();

        public static double Cost(IReadOnlyList<Point> path) =>
            path.Zip(path.Skip(1)).Sum(s => Math.Abs(s.First.X - s.Second.X) + Math.Abs(s.First.Y - s.Second.Y)) + BendCost * (path.Count - 2);

        // True if the pin is on the segment, ends included. A wire must not touch the pin of another net.
        static bool On(Point p, Point q, Point pin) =>
            Math.Min(p.X, q.X) <= pin.X && pin.X <= Math.Max(p.X, q.X) && Math.Min(p.Y, q.Y) <= pin.Y && pin.Y <= Math.Max(p.Y, q.Y);

        // True if the segment goes through the inside of the box. A segment along the edge, or ending on it, does not.
        static bool Crosses(Point p, Point q, Box box)
        {
            var (x0, x1, y0, y1) = (Math.Min(p.X, q.X), Math.Max(p.X, q.X), Math.Min(p.Y, q.Y), Math.Max(p.Y, q.Y));
            return p.Y == q.Y
                ? box.Y0 < p.Y && p.Y < box.Y1 && Math.Max(x0, box.X0) < Math.Min(x1, box.X1)
                : box.X0 < p.X && p.X < box.X1 && Math.Max(y0, box.Y0) < Math.Min(y1, box.Y1);
        }
    }
}
