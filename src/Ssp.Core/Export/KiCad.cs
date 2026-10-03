using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Core.Layout;
using SpiceSharp.Components;
using LayoutDoc = Ssp.Core.Layout.Layout;

namespace Ssp.Core.Export;

/// <summary>Exports a circuit as a KiCad schematic.</summary>
public static class KiCad
{
    const double PinReach = 5.08;
    const double RowStep = 2.54;
    const string Effects = "(effects (font (size 1.27 1.27)))";
    const string HiddenEffects = "(effects (font (size 1.27 1.27)) (hide yes))";
    const string NoFootprint = "Unassigned:Unassigned";

    /// <summary>
    /// A <c>.kicad_sch</c> file, version 8. Each component is one symbol with a footprint reference.
    /// A local label on each pin gives the net, so the file needs no wire to connect. The layout wires are drawn too.
    /// A component that the layout does not place goes in a row below the layout. The output is the same for the same input.
    /// </summary>
    public static string ToSchematic(LoadedCircuit circuit, LayoutDoc layout, PartMap parts)
    {
        var components = circuit.Circuit.OfType<IComponent>().OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var placements = layout.Parts.ToDictionary(p => p.Reference, StringComparer.OrdinalIgnoreCase);
        var rootUuid = Uuid("root");

        var sb = new StringBuilder();
        sb.Append("(kicad_sch\n\t(version 20231120)\n\t(generator \"ssp\")\n\t(generator_version \"8.0\")\n")
          .Append("\t(uuid \"").Append(rootUuid).Append("\")\n\t(paper \"A3\")\n\t(lib_symbols\n");
        foreach (var n in components.Select(c => c.Nodes.Count).Distinct().Order())
        {
            AppendLibSymbol(sb, n);
        }
        sb.Append("\t)\n");

        var fallbackX = 0.0;
        foreach (var c in components)
        {
            var row = parts.Parts.GetValueOrDefault(c.Name);
            var placement = placements.GetValueOrDefault(c.Name);
            if (placement is null)
            {
                placement = new(c.Name, fallbackX, layout.Parts.Select(p => p.Y).DefaultIfEmpty(0).Max() + 60, 0, false);
                fallbackX += 30;
            }
            AppendSymbol(sb, c, row, placement, rootUuid);
            for (var i = 0; i < c.Nodes.Count; i++)
            {
                var (x, y) = PinPosition(placement, i);
                sb.Append("\t(label ").Append(Str(c.Nodes[i])).Append(" (at ").Append(N(x)).Append(' ').Append(N(y))
                  .Append(" 0) (effects (font (size 1.27 1.27)) (justify left bottom)) (uuid \"")
                  .Append(Uuid($"label {c.Name} {i}")).Append("\"))\n");
            }
        }

        var wireIndex = 0;
        foreach (var w in layout.Wires)
        {
            for (var i = 1; i < w.Points.Count; i++)
            {
                sb.Append("\t(wire (pts (xy ").Append(N(w.Points[i - 1].X)).Append(' ').Append(N(w.Points[i - 1].Y))
                  .Append(") (xy ").Append(N(w.Points[i].X)).Append(' ').Append(N(w.Points[i].Y))
                  .Append(")) (stroke (width 0) (type default)) (uuid \"").Append(Uuid($"wire {wireIndex++}")).Append("\"))\n");
            }
        }

        sb.Append("\t(sheet_instances (path \"/\" (page \"1\")))\n)\n");
        return sb.ToString();
    }

    // Library symbol: pins alternate left and right, two to a row. Coordinates are y-up, as in a KiCad library.
    static (double X, double Y, int Angle) LibPin(int index) =>
        (index % 2 == 0 ? -PinReach : PinReach, -RowStep * (index / 2), index % 2 == 0 ? 0 : 180);

    static void AppendLibSymbol(StringBuilder sb, int pins)
    {
        var rows = (pins + 1) / 2;
        var name = $"Part{pins}";
        sb.Append("\t\t(symbol \"ssp:").Append(name).Append("\"\n")
          .Append("\t\t\t(pin_names (offset 0))\n\t\t\t(exclude_from_sim no)\n\t\t\t(in_bom yes)\n\t\t\t(on_board yes)\n");
        string[] props = ["Reference", "Value", "Footprint", "Datasheet", "Description"];
        for (var i = 0; i < props.Length; i++)
        {
            sb.Append("\t\t\t(property \"").Append(props[i]).Append("\" \"").Append(i == 0 ? "U" : "")
              .Append("\" (at 0 ").Append(N(RowStep * (i + 1))).Append(" 0) ").Append(i < 2 ? Effects : HiddenEffects).Append(")\n");
        }
        sb.Append("\t\t\t(symbol \"").Append(name).Append("_0_1\"\n\t\t\t\t(rectangle (start -2.54 1.27) (end 2.54 ")
          .Append(N(-RowStep * (rows - 1) - 1.27)).Append(") (stroke (width 0.254) (type default)) (fill (type background)))\n\t\t\t)\n");
        sb.Append("\t\t\t(symbol \"").Append(name).Append("_1_1\"\n");
        for (var i = 0; i < pins; i++)
        {
            var (x, y, angle) = LibPin(i);
            sb.Append("\t\t\t\t(pin passive line (at ").Append(N(x)).Append(' ').Append(N(y)).Append(' ').Append(angle.ToString(CultureInfo.InvariantCulture))
              .Append(") (length 2.54) (name \"~\" ").Append(Effects).Append(") (number \"").Append(i + 1).Append("\" ").Append(Effects).Append("))\n");
        }
        sb.Append("\t\t\t)\n\t\t)\n");
    }

    static void AppendSymbol(StringBuilder sb, IComponent c, PartRow? row, PartPlacement p, string rootUuid)
    {
        var at = $"{N(p.X)} {N(p.Y)}";
        var value = row?.Id ?? c.Name;
        sb.Append("\t(symbol (lib_id \"ssp:Part").Append(c.Nodes.Count).Append("\") (at ").Append(at).Append(' ')
          .Append(Norm(p.Rotation).ToString(CultureInfo.InvariantCulture)).Append(')');
        if (p.Flip) sb.Append(" (mirror x)");
        sb.Append(" (unit 1)\n\t\t(exclude_from_sim no) (in_bom yes) (on_board yes) (dnp no)\n\t\t(uuid \"").Append(Uuid($"symbol {c.Name}")).Append("\")\n");
        sb.Append("\t\t(property \"Reference\" ").Append(Str(c.Name)).Append(" (at ").Append(at).Append(" 0) ").Append(Effects).Append(")\n");
        sb.Append("\t\t(property \"Value\" ").Append(Str(value)).Append(" (at ").Append(at).Append(" 0) ").Append(HiddenEffects).Append(")\n");
        sb.Append("\t\t(property \"Footprint\" ").Append(Str(row?.Footprint is { Length: > 0 } f ? f : NoFootprint)).Append(" (at ").Append(at).Append(" 0) ").Append(HiddenEffects).Append(")\n");
        sb.Append("\t\t(property \"Datasheet\" \"\" (at ").Append(at).Append(" 0) ").Append(HiddenEffects).Append(")\n");
        for (var i = 0; i < c.Nodes.Count; i++)
        {
            sb.Append("\t\t(pin \"").Append(i + 1).Append("\" (uuid \"").Append(Uuid($"pin {c.Name} {i}")).Append("\"))\n");
        }
        sb.Append("\t\t(instances (project \"ssp\" (path \"/").Append(rootUuid).Append("\" (reference ").Append(Str(c.Name)).Append(") (unit 1))))\n\t)\n");
    }

    // NOTE: Mirror applies before rotation. Rotation is counter-clockwise on screen. The schematic y axis points down.
    static (double X, double Y) PinPosition(PartPlacement p, int index)
    {
        var (lx, ly, _) = LibPin(index);
        if (p.Flip) ly = -ly;
        var r = Norm(p.Rotation) * Math.PI / 180;
        var rx = lx * Math.Cos(r) - ly * Math.Sin(r);
        var ry = lx * Math.Sin(r) + ly * Math.Cos(r);
        return (Round(p.X + rx), Round(p.Y - ry));
    }

    static int Norm(int degrees) => (degrees % 360 + 360) % 360;

    static double Round(double v) => Math.Round(v, 2) + 0.0;

    static string N(double v) => Round(v).ToString("0.##", CultureInfo.InvariantCulture);

    static string Str(string s) => "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    static string Uuid(string key)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(bytes).ToString();
    }
}
