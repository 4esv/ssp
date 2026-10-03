using System.Globalization;
using System.Text;
using Ssp.Core.Netlist;
using Tomlyn;
using Tomlyn.Model;

namespace Ssp.Core.Layout;

public readonly record struct Point(double X, double Y);

/// <summary>Position, rotation (degrees, a multiple of 90) and flip of one reference.</summary>
public sealed record PartPlacement(string Reference, double X, double Y, int Rotation, bool Flip);

/// <summary>The points of one wire. A wire belongs to a net.</summary>
public sealed record WireRoute(string Net, IReadOnlyList<Point> Points)
{
    public bool Equals(WireRoute? other) =>
        other is not null && Net == other.Net && Points.SequenceEqual(other.Points);

    public override int GetHashCode() => HashCode.Combine(Net, Points.Count);
}

/// <summary>The schematic layout of a netlist. Read from and written to <c>&lt;name&gt;.layout.toml</c>.</summary>
public sealed class Layout : IEquatable<Layout>
{
    public Layout(IReadOnlyList<PartPlacement> parts, IReadOnlyList<WireRoute> wires)
    {
        Parts = parts;
        Wires = wires;
    }

    public IReadOnlyList<PartPlacement> Parts { get; }

    public IReadOnlyList<WireRoute> Wires { get; }

    /// <summary>Reads a layout file. Throws <see cref="InvalidDataException"/> if the file is not a valid layout.</summary>
    public static Layout Read(string path)
    {
        TomlTable doc;
        try
        {
            doc = TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(path)) ?? new TomlTable();
        }
        catch (TomlException ex)
        {
            throw new InvalidDataException($"{path}: invalid TOML: {ex.Message}", ex);
        }

        var parts = new List<PartPlacement>();
        foreach (var t in Tables(path, doc, "part"))
        {
            parts.Add(new PartPlacement(
                Str(path, t, "ref"),
                Num(path, t, "x"),
                Num(path, t, "y"),
                (int)Num(path, t, "rotation", 0),
                t.TryGetValue("flip", out var f) ? f is bool b ? b : throw Bad(path, "flip must be true or false") : false));
        }

        var wires = new List<WireRoute>();
        foreach (var t in Tables(path, doc, "wire"))
        {
            if (!t.TryGetValue("points", out var p) || p is not TomlArray arr) throw Bad(path, "wire needs a points array");
            var points = new List<Point>();
            foreach (var item in arr)
            {
                if (item is not TomlArray xy || xy.Count != 2 || !IsNum(xy[0]) || !IsNum(xy[1]))
                {
                    throw Bad(path, "each wire point must be [x, y]");
                }
                points.Add(new Point(Convert.ToDouble(xy[0], CultureInfo.InvariantCulture), Convert.ToDouble(xy[1], CultureInfo.InvariantCulture)));
            }
            wires.Add(new WireRoute(Str(path, t, "net"), points));
        }
        return new Layout(parts, wires);
    }

    /// <summary>Writes a layout file. The output is the same for the same layout.</summary>
    public static void Write(string path, Layout layout)
    {
        var sb = new StringBuilder();
        foreach (var p in layout.Parts)
        {
            sb.Append("[[part]]\n")
              .Append("ref = ").Append(Quote(p.Reference)).Append('\n')
              .Append("x = ").Append(Fmt(p.X)).Append('\n')
              .Append("y = ").Append(Fmt(p.Y)).Append('\n')
              .Append("rotation = ").Append(p.Rotation.ToString(CultureInfo.InvariantCulture)).Append('\n')
              .Append("flip = ").Append(p.Flip ? "true" : "false").Append("\n\n");
        }
        foreach (var w in layout.Wires)
        {
            sb.Append("[[wire]]\n")
              .Append("net = ").Append(Quote(w.Net)).Append('\n')
              .Append("points = [").Append(string.Join(", ", w.Points.Select(pt => $"[{Fmt(pt.X)}, {Fmt(pt.Y)}]"))).Append("]\n\n");
        }
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>
    /// Checks the layout against the netlist. A reference or net that is not in the netlist gives an error.
    /// So do a repeated reference, a rotation that is not a multiple of 90, and a wire with fewer than two points.
    /// </summary>
    public IReadOnlyList<Diagnostic> Validate(LoadedCircuit circuit)
    {
        var diagnostics = new List<Diagnostic>();
        // NOTE: the parser flattens a subcircuit instance X1 into parts named X1.<part>. The layout places X1.
        var references = circuit.Circuit.Select(e => e.Name.Split('.')[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nets = new HashSet<string>(circuit.NodeNames, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in Parts)
        {
            if (!references.Contains(p.Reference))
            {
                diagnostics.Add(Error($"Layout names reference '{p.Reference}', which is not in the netlist."));
            }
            if (!seen.Add(p.Reference))
            {
                diagnostics.Add(Error($"Layout places reference '{p.Reference}' more than once."));
            }
            if (p.Rotation % 90 != 0)
            {
                diagnostics.Add(Error($"Reference '{p.Reference}' has rotation {p.Rotation}. Use a multiple of 90."));
            }
        }
        foreach (var w in Wires)
        {
            if (!nets.Contains(w.Net))
            {
                diagnostics.Add(Error($"Layout has a wire on net '{w.Net}', which is not in the netlist."));
            }
            if (w.Points.Count < 2)
            {
                diagnostics.Add(Error($"A wire on net '{w.Net}' has fewer than two points."));
            }
        }
        return diagnostics;
    }

    public bool Equals(Layout? other) =>
        other is not null && Parts.SequenceEqual(other.Parts) && Wires.SequenceEqual(other.Wires);

    public override bool Equals(object? obj) => Equals(obj as Layout);

    public override int GetHashCode() => HashCode.Combine(Parts.Count, Wires.Count);

    static Diagnostic Error(string message) => new(Severity.Error, message, null);

    static InvalidDataException Bad(string path, string message) => new($"{path}: {message}");

    static IEnumerable<TomlTable> Tables(string path, TomlTable doc, string key)
    {
        if (!doc.TryGetValue(key, out var node)) return [];
        return node is TomlTableArray tables ? tables : throw Bad(path, $"'{key}' must be an array of tables");
    }

    static string Str(string path, TomlTable t, string key) =>
        t.TryGetValue(key, out var v) && v is string s && s.Length > 0 ? s : throw Bad(path, $"missing or invalid '{key}'");

    static bool IsNum(object? v) => v is long or double;

    static double Num(string path, TomlTable t, string key, double? fallback = null)
    {
        if (!t.TryGetValue(key, out var v)) return fallback ?? throw Bad(path, $"missing '{key}'");
        return IsNum(v) ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : throw Bad(path, $"'{key}' must be a number");
    }

    /// <summary>Formats a number as a TOML float, so that it reads back as a number with a fraction.</summary>
    static string Fmt(double v)
    {
        var s = v.ToString("R", CultureInfo.InvariantCulture);
        return s.Contains('.') || s.Contains('E') ? s : s + ".0";
    }

    static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
