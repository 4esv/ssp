using System.Globalization;
using System.Text.RegularExpressions;
using SpiceSharp.Components;

namespace Ssp.Core.Netlist;

public static partial class Overrides
{
    // Longest suffix first: "meg" and "mil" must win over "m".
    private static readonly (string Suffix, double Scale)[] Suffixes =
    {
        ("meg", 1e6), ("mil", 25.4e-6), ("t", 1e12), ("g", 1e9), ("k", 1e3),
        ("m", 1e-3), ("u", 1e-6), ("n", 1e-9), ("p", 1e-12), ("f", 1e-15),
    };

    [GeneratedRegex(@"^(?<int>\d+)(?<suffix>meg|mil|[tgkmunpf])(?<frac>\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex PositionalForm();

    /// <summary>Parses text such as <c>R1=4k7</c>. Throws <see cref="FormatException"/> on bad text.</summary>
    public static Override Parse(string text)
    {
        var parts = (text ?? string.Empty).Split('=');
        if (parts.Length != 2 || parts[0].Trim().Length == 0)
        {
            throw new FormatException($"Override '{text}' must have the form REF=VALUE.");
        }

        return new Override(parts[0].Trim(), ParseValue(parts[1].Trim(), text!));
    }

    /// <summary>Sets part values in the loaded circuit. An unknown or unsupported reference gives a diagnostic.</summary>
    public static IReadOnlyList<Diagnostic> Apply(LoadedCircuit circuit, IEnumerable<Override> overrides)
    {
        var diagnostics = new List<Diagnostic>();
        foreach (var item in overrides)
        {
            if (!circuit.Circuit.TryGetEntity(item.Reference, out var entity))
            {
                diagnostics.Add(new Diagnostic(Severity.Error, $"Unknown part reference {item.Reference}.", null));
                continue;
            }

            switch (entity)
            {
                case Resistor r: r.Parameters.Resistance = item.Value; break;
                case Capacitor c: c.Parameters.Capacitance = item.Value; break;
                case Inductor l: l.Parameters.Inductance = item.Value; break;
                case VoltageSource v: v.Parameters.DcValue = item.Value; break;
                case CurrentSource i: i.Parameters.DcValue = item.Value; break;
                default:
                    diagnostics.Add(new Diagnostic(Severity.Error, $"Part {item.Reference} does not support a value override.", null));
                    break;
            }
        }

        return diagnostics;
    }

    private static double ParseValue(string value, string text)
    {
        var positional = PositionalForm().Match(value);
        if (positional.Success)
        {
            var scale = Scale(positional.Groups["suffix"].Value);
            var number = double.Parse(
                $"{positional.Groups["int"].Value}.{positional.Groups["frac"].Value}", CultureInfo.InvariantCulture);
            return number * scale;
        }

        foreach (var (suffix, scale) in Suffixes)
        {
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(value[..^suffix.Length], NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            {
                return n * scale;
            }
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain))
        {
            return plain;
        }

        throw new FormatException($"Override '{text}' has a value that is not a SPICE number.");
    }

    private static double Scale(string suffix) =>
        Suffixes.First(s => s.Suffix.Equals(suffix, StringComparison.OrdinalIgnoreCase)).Scale;
}
