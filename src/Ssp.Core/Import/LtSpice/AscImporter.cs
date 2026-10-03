using System.Globalization;
using System.Text;
using Ssp.Core.Netlist;
using LayoutDoc = Ssp.Core.Layout.Layout;

namespace Ssp.Core.Import.LtSpice;

/// <summary>The netlist made from an LTspice <c>.asc</c> file, and the problems found.</summary>
public sealed record AscImport(string Netlist, IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>Makes a SPICE netlist and a layout from an LTspice <c>.asc</c> schematic.</summary>
public static class AscImporter
{
    // NOTE: The SPICE prefix of each symbol in PinTable.Builtin. LTspice adds the prefix when the InstName does not start with it.
    static readonly Dictionary<string, string> Prefixes = new()
    {
        ["res"] = "R",
        ["cap"] = "C",
        ["ind"] = "L",
        ["diode"] = "D",
        ["npn"] = "Q",
        ["pnp"] = "Q",
        ["njf"] = "J",
        ["opamp"] = "X",
    };

    // NOTE: How the ssp symbol at layout rotation 0 goes on the LTspice symbol at R0. Base is the layout rotation that turns the
    // ssp symbol to the LTspice direction. (X, Y) is the point of the LTspice symbol where the ssp symbol origin goes.
    // The two-pin LTspice symbols are vertical with pin A at top. The two-pin ssp symbols are horizontal with pin A at the origin.
    // The ssp op-amp origin is between its inputs. The others have no ssp symbol; their origin goes on the first pin.
    static readonly Dictionary<string, (int Base, int X, int Y)> Anchors = new()
    {
        ["res"] = (270, 16, 16),
        ["cap"] = (270, 16, 0),
        ["ind"] = (270, 16, 16),
        ["diode"] = (270, 16, 0),
        ["npn"] = (0, 64, 0),
        ["pnp"] = (0, 64, 0),
        ["njf"] = (0, 48, 0),
        ["opamp"] = (0, -32, 64),
    };

    sealed class Symbol(string name, int x, int y, string orientation, int line)
    {
        public string Name { get; } = name;
        public int X { get; } = x;
        public int Y { get; } = y;
        public string Orientation { get; } = orientation;
        public int Line { get; } = line;
        public string? InstName { get; set; }
        public string? Value { get; set; }
    }

    public static string ToNetlist(string asc) => Import(asc).Netlist;

    /// <summary>
    /// A layout with one entry for each symbol that <see cref="Import"/> puts in the netlist, and no wires.
    /// The positions are in LTspice units. The rotation and flip come from the LTspice orientation.
    /// </summary>
    public static LayoutDoc ToLayout(string asc)
    {
        var placements = new List<Layout.PartPlacement>();
        foreach (var (symbol, prefix) in Importable(Parse(asc).Symbols, []))
        {
            var (baseRotation, x, y) = Anchors[symbol.Name];
            var at = Orient(x, y, symbol.Orientation)!.Value;
            var mirror = symbol.Orientation[0] == 'M';
            var turn = Int(symbol.Orientation[1..]);
            // NOTE: LTspice turns clockwise and mirrors x. The layout turns counter-clockwise and flips y. Mirror x is flip y and a half turn.
            var rotation = (((mirror ? 180 - baseRotation : baseRotation) - turn) % 360 + 360) % 360;
            placements.Add(new Layout.PartPlacement(Reference(symbol, prefix), symbol.X + at.X, symbol.Y + at.Y, rotation, mirror));
        }
        return new LayoutDoc(placements, []);
    }

    public static AscImport Import(string asc)
    {
        var diagnostics = new List<Diagnostic>();
        var (wires, flags, symbols, directives) = Parse(asc);

        var nodes = new UnionFind();
        foreach (var (a, b) in wires)
        {
            nodes.Union(a, b);
        }

        var placed = new List<(Symbol Symbol, string Prefix, (int X, int Y)[] Pins)>();
        foreach (var (symbol, prefix) in Importable(symbols, diagnostics))
        {
            var offsets = PinTable.Builtin[symbol.Name];
            var pins = new (int X, int Y)[offsets.Length];
            for (var p = 0; p < offsets.Length; p++)
            {
                var t = Orient(offsets[p].X, offsets[p].Y, symbol.Orientation)!.Value;
                pins[p] = (symbol.X + t.X, symbol.Y + t.Y);
                nodes.Add(pins[p]);
            }
            placed.Add((symbol, prefix, pins));
        }

        foreach (var (at, _) in flags)
        {
            nodes.Add(at);
        }

        // NOTE: A point on a wire, not only at its end, connects to that wire. This gives T-junctions.
        foreach (var point in nodes.Points.ToList())
        {
            foreach (var (a, b) in wires)
            {
                if (OnSegment(point, a, b))
                {
                    nodes.Union(point, a);
                }
            }
        }

        // NOTE: Flags with the same name are one net.
        var byLabel = new Dictionary<string, (int X, int Y)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (at, name) in flags)
        {
            if (byLabel.TryGetValue(name, out var first))
            {
                nodes.Union(at, first);
            }
            else
            {
                byLabel[name] = at;
            }
        }

        var names = new Dictionary<(int X, int Y), string>();
        foreach (var (at, name) in flags)
        {
            var root = nodes.Find(at);
            if (name == "0" || !names.ContainsKey(root))
            {
                names[root] = name;
            }
        }

        var netlist = new StringBuilder("* Imported from LTspice\n");
        var unnamed = 0;
        foreach (var (symbol, prefix, pins) in placed)
        {
            netlist.Append(Reference(symbol, prefix));
            foreach (var pin in pins)
            {
                var root = nodes.Find(pin);
                if (!names.TryGetValue(root, out var net))
                {
                    net = string.Create(CultureInfo.InvariantCulture, $"N{++unnamed:D3}");
                    names[root] = net;
                }
                netlist.Append(' ').Append(net);
            }
            if (!string.IsNullOrEmpty(symbol.Value))
            {
                netlist.Append(' ').Append(symbol.Value);
            }
            netlist.Append('\n');
        }

        foreach (var directive in directives)
        {
            netlist.Append(directive).Append('\n');
        }
        netlist.Append(".end\n");

        return new AscImport(netlist.ToString(), diagnostics);
    }

    static (List<((int X, int Y) A, (int X, int Y) B)> Wires, List<((int X, int Y) At, string Name)> Flags, List<Symbol> Symbols, List<string> Directives) Parse(string asc)
    {
        var wires = new List<((int X, int Y) A, (int X, int Y) B)>();
        var flags = new List<((int X, int Y) At, string Name)>();
        var symbols = new List<Symbol>();
        var directives = new List<string>();

        var lines = asc.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            switch (parts[0])
            {
                case "WIRE" when parts.Length >= 5:
                    wires.Add(((Int(parts[1]), Int(parts[2])), (Int(parts[3]), Int(parts[4]))));
                    break;
                case "FLAG" when parts.Length >= 4:
                    flags.Add(((Int(parts[1]), Int(parts[2])), parts[3]));
                    break;
                case "SYMBOL" when parts.Length >= 5:
                    symbols.Add(new Symbol(parts[1].ToLowerInvariant(), Int(parts[2]), Int(parts[3]), parts[4], i + 1));
                    break;
                case "SYMATTR" when symbols.Count > 0 && parts.Length >= 2:
                    var attr = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                    var value = attr.Length == 3 ? attr[2] : "";
                    if (attr[1] == "InstName")
                    {
                        symbols[^1].InstName = value;
                    }
                    else if (attr[1] == "Value")
                    {
                        symbols[^1].Value = value;
                    }
                    break;
                case "TEXT":
                    // NOTE: TEXT x y align size text. A text that starts with '!' is a SPICE directive; LTspice writes its line breaks as "\n".
                    var text = line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries);
                    if (text.Length == 6 && text[5].StartsWith('!'))
                    {
                        directives.AddRange(text[5][1..].Split("\\n").Select(d => d.Trim()).Where(d => d.Length > 0));
                    }
                    break;
            }
        }

        return (wires, flags, symbols, directives);
    }

    // The symbols that are imported, with their SPICE prefix. Each symbol that is not imported gives a diagnostic.
    static List<(Symbol Symbol, string Prefix)> Importable(List<Symbol> symbols, List<Diagnostic> diagnostics)
    {
        var importable = new List<(Symbol Symbol, string Prefix)>();
        foreach (var symbol in symbols)
        {
            if (!PinTable.Builtin.ContainsKey(symbol.Name) || !Prefixes.TryGetValue(symbol.Name, out var prefix))
            {
                diagnostics.Add(new Diagnostic(Severity.Error,
                    $"Unknown LTspice symbol '{symbol.Name}' ({symbol.InstName ?? "no InstName"}). The symbol is not imported.", symbol.Line));
            }
            else if (string.IsNullOrEmpty(symbol.InstName))
            {
                diagnostics.Add(new Diagnostic(Severity.Error,
                    $"LTspice symbol '{symbol.Name}' has no InstName. The symbol is not imported.", symbol.Line));
            }
            else if (Orient(0, 0, symbol.Orientation) is null)
            {
                diagnostics.Add(new Diagnostic(Severity.Error,
                    $"Unknown orientation '{symbol.Orientation}' of {symbol.InstName}. The symbol is not imported.", symbol.Line));
            }
            else
            {
                importable.Add((symbol, prefix));
            }
        }
        return importable;
    }

    static string Reference(Symbol symbol, string prefix) =>
        symbol.InstName!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? symbol.InstName : prefix + symbol.InstName;

    static int Int(string s) => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);

    // NOTE: LTspice rotates clockwise on screen (y points down). Mn mirrors x first, then rotates by n.
    static (int X, int Y)? Orient(int x, int y, string orientation) => orientation switch
    {
        "R0" => (x, y),
        "R90" => (-y, x),
        "R180" => (-x, -y),
        "R270" => (y, -x),
        "M0" => (-x, y),
        "M90" => (-y, -x),
        "M180" => (x, -y),
        "M270" => (y, x),
        _ => null,
    };

    static bool OnSegment((int X, int Y) p, (int X, int Y) a, (int X, int Y) b)
    {
        var cross = (long)(b.X - a.X) * (p.Y - a.Y) - (long)(b.Y - a.Y) * (p.X - a.X);
        return cross == 0
            && p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X)
            && p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y);
    }

    sealed class UnionFind
    {
        readonly Dictionary<(int X, int Y), (int X, int Y)> parent = [];

        public IEnumerable<(int X, int Y)> Points => parent.Keys;

        public void Add((int X, int Y) p) => parent.TryAdd(p, p);

        public (int X, int Y) Find((int X, int Y) p)
        {
            Add(p);
            while (parent[p] != p)
            {
                parent[p] = parent[parent[p]];
                p = parent[p];
            }
            return p;
        }

        public void Union((int X, int Y) a, (int X, int Y) b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb)
            {
                parent[rb] = ra;
            }
        }
    }
}
