using System.Globalization;
using System.Text;
using Ssp.Core.Netlist;

namespace Ssp.Core.Import.LtSpice;

/// <summary>The netlist made from an LTspice <c>.asc</c> file, and the problems found.</summary>
public sealed record AscImport(string Netlist, IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>Makes a SPICE netlist from an LTspice <c>.asc</c> schematic.</summary>
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

    public static AscImport Import(string asc)
    {
        var diagnostics = new List<Diagnostic>();
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

        var nodes = new UnionFind();
        foreach (var (a, b) in wires)
        {
            nodes.Union(a, b);
        }

        var placed = new List<(Symbol Symbol, string Prefix, (int X, int Y)[] Pins)>();
        foreach (var symbol in symbols)
        {
            if (!PinTable.Builtin.TryGetValue(symbol.Name, out var offsets) || !Prefixes.TryGetValue(symbol.Name, out var prefix))
            {
                diagnostics.Add(new Diagnostic(Severity.Error,
                    $"Unknown LTspice symbol '{symbol.Name}' ({symbol.InstName ?? "no InstName"}). The symbol is not imported.", symbol.Line));
                continue;
            }

            if (string.IsNullOrEmpty(symbol.InstName))
            {
                diagnostics.Add(new Diagnostic(Severity.Error,
                    $"LTspice symbol '{symbol.Name}' has no InstName. The symbol is not imported.", symbol.Line));
                continue;
            }

            var pins = new (int X, int Y)[offsets.Length];
            var known = true;
            for (var p = 0; p < offsets.Length && known; p++)
            {
                var turned = Orient(offsets[p].X, offsets[p].Y, symbol.Orientation);
                known = turned is not null;
                if (turned is { } t)
                {
                    pins[p] = (symbol.X + t.X, symbol.Y + t.Y);
                    nodes.Add(pins[p]);
                }
            }

            if (!known)
            {
                diagnostics.Add(new Diagnostic(Severity.Error,
                    $"Unknown orientation '{symbol.Orientation}' of {symbol.InstName}. The symbol is not imported.", symbol.Line));
                continue;
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
            var instName = symbol.InstName!;
            netlist.Append(instName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? instName : prefix + instName);
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
