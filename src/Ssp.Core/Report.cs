using System.Globalization;
using System.Numerics;
using System.Text;
using Ssp.Core.Analysis;
using Ssp.Core.Netlist;

namespace Ssp.Core;

/// <summary>Writes a run result as a plain-English text report.</summary>
public static class Report
{
    const string Indent = "  ";

    // NOTE: Node 0 is the ground reference. It is always 0 V and has no signal, so the report leaves it out.
    const string Ground = "0";

    /// <summary>
    /// Writes the report. The sections are summary, voltages, response, impedances and diagnostics, in that order.
    /// Each number has a unit. The response and the impedances show each decade frequency of the sweep.
    /// </summary>
    public static string Render(RunResult result)
    {
        var sb = new StringBuilder();
        Section(sb, "Summary", Summary(result));
        Section(sb, "Voltages", Voltages(result.OperatingPoint));
        Section(sb, "Response", Response(result.FrequencyResponse));
        Section(sb, "Impedances", Impedances(result.Impedance));
        Section(sb, "Diagnostics", Diagnostics(result.Diagnostics));
        return sb.ToString().TrimEnd('\n') + "\n";
    }

    static void Section(StringBuilder sb, string heading, IEnumerable<string> lines)
    {
        sb.Append(heading).Append('\n');
        foreach (var line in lines)
        {
            sb.Append(Indent).Append(line).Append('\n');
        }
        sb.Append('\n');
    }

    static IEnumerable<string> Summary(RunResult result)
    {
        yield return result.OperatingPoint is null
            ? "The run stopped before the analyses."
            : "The run completed all analyses.";

        var errors = result.Diagnostics.Count(d => d.Severity == Severity.Error);
        var warnings = result.Diagnostics.Count(d => d.Severity == Severity.Warning);
        yield return (errors, warnings) switch
        {
            (0, 0) => "There are no errors and no warnings.",
            _ => $"There are {Count(errors, "error")} and {Count(warnings, "warning")}. See the diagnostics.",
        };

        if (result.FrequencyResponse is { } ac)
        {
            var points = CornerPoints(ac).ToList();
            if (points.Count == 0)
            {
                yield return "No node falls 3 dB below its level at the start of the sweep.";
            }
            foreach (var (node, hz) in points)
            {
                yield return $"Node {node} falls 3 dB below its {Si(ac.Frequencies[0], "Hz")} level at {Si(hz, "Hz")}.";
            }
        }
    }

    static IEnumerable<string> Voltages(OpResult? op)
    {
        if (op is null)
        {
            yield return "Not computed.";
            yield break;
        }

        yield return "Node voltages:";
        foreach (var line in Table(op.NodeVoltages.Where(kv => kv.Key != Ground).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new[] { kv.Key, Si(kv.Value, "V") })))
        {
            yield return Indent + line;
        }
        yield return "Source currents:";
        foreach (var line in Table(op.SourceCurrents.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new[] { kv.Key, Si(kv.Value, "A") })))
        {
            yield return Indent + line;
        }
        yield return "Device powers:";
        foreach (var line in Table(op.DevicePowers.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new[] { kv.Key, Si(kv.Value, "W") })))
        {
            yield return Indent + line;
        }
    }

    static IEnumerable<string> Response(AcResult? ac)
    {
        if (ac is null)
        {
            return ["Not computed."];
        }

        var nodes = Nodes(ac).ToList();
        var rows = new List<string[]> { (string[])["Frequency", .. nodes] };
        foreach (var i in DecadeIndexes(ac.Frequencies))
        {
            rows.Add([Si(ac.Frequencies[i], "Hz"), .. nodes.Select(n => $"{Db(ac.MagnitudeDb[n][i])}, {Degrees(ac.PhaseDegrees[n][i])}")]);
        }
        return Table(rows);
    }

    static IEnumerable<string> Impedances(ZResult? z)
    {
        if (z is null)
        {
            return ["Not computed."];
        }

        var rows = new List<string[]> { new[] { "Frequency", "Input", "Output" } };
        foreach (var i in DecadeIndexes(z.Frequencies))
        {
            rows.Add([Si(z.Frequencies[i], "Hz"), Impedance(z.Input[i]), Impedance(z.Output[i])]);
        }
        return Table(rows);
    }

    static IEnumerable<string> Diagnostics(IReadOnlyList<Diagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return ["None."];
        }

        return diagnostics.Select(d =>
            d.Severity.ToString() + (d.Line is { } line ? $", line {line}" : "") + ": " + d.Message);
    }

    /// <summary>For each node, the frequency where the magnitude first falls 3 dB below the first point. Log interpolation between points.</summary>
    static IEnumerable<(string Node, double Hz)> CornerPoints(AcResult ac)
    {
        foreach (var node in Nodes(ac))
        {
            var db = ac.MagnitudeDb[node];
            if (db.Count == 0 || !double.IsFinite(db[0]))
            {
                continue;
            }

            var target = db[0] - 3.0;
            for (var i = 1; i < db.Count; i++)
            {
                if (db[i] <= target)
                {
                    var t = (db[i - 1] - target) / (db[i - 1] - db[i]);
                    var logHz = Math.Log10(ac.Frequencies[i - 1]) + t * (Math.Log10(ac.Frequencies[i]) - Math.Log10(ac.Frequencies[i - 1]));
                    yield return (node, Math.Pow(10, logHz));
                    break;
                }
            }
        }
    }

    /// <summary>The indexes of the frequencies that are a power of ten. If there are none, the first and the last index.</summary>
    static IEnumerable<int> DecadeIndexes(IReadOnlyList<double> frequencies)
    {
        var decades = Enumerable.Range(0, frequencies.Count)
            .Where(i => Math.Abs(Math.Log10(frequencies[i]) - Math.Round(Math.Log10(frequencies[i]))) < 1e-9)
            .ToList();
        return decades.Count > 0 || frequencies.Count == 0 ? decades : new[] { 0, frequencies.Count - 1 }.Distinct();
    }

    static IEnumerable<string> Nodes(AcResult ac) => ac.MagnitudeDb.Keys.Where(n => n != Ground).OrderBy(n => n, StringComparer.Ordinal);

    static IEnumerable<string> Table(IEnumerable<string[]> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0)
        {
            return ["None."];
        }

        var widths = Enumerable.Range(0, list.Max(r => r.Length)).Select(c => list.Max(r => c < r.Length ? r[c].Length : 0)).ToArray();
        return list.Select(r => string.Join("   ", r.Select((cell, c) => cell.PadRight(widths[c]))).TrimEnd());
    }

    static string Count(int n, string noun) => n == 0 ? $"no {noun}s" : $"{n} {noun}{(n == 1 ? "" : "s")}";

    static string Impedance(Complex z) => $"{Si(z.Magnitude, "Ω")} at {Degrees(z.Phase * 180.0 / Math.PI)}";

    static string Db(double value) => double.IsFinite(value) ? Fixed(value, 2) + " dB" : "no signal";

    static string Degrees(double value) => double.IsFinite(value) ? Fixed(value, 1) + "°" : "no phase";

    static readonly string[] Prefixes = ["f", "p", "n", "µ", "m", "", "k", "M", "G"];

    /// <summary>Three significant digits with an SI prefix from f to G. A value smaller than 1 f is zero.</summary>
    static string Si(double value, string unit)
    {
        if (!double.IsFinite(value))
        {
            return "not finite";
        }
        if (Math.Abs(value) < 1e-15)
        {
            return $"0 {unit}";
        }

        var exponent = Math.Clamp((int)Math.Floor(Math.Log10(Math.Abs(value)) / 3) * 3, -15, 9);
        var scaled = value / Math.Pow(10, exponent);
        var decimals = Math.Abs(scaled) >= 100 ? 0 : Math.Abs(scaled) >= 10 ? 1 : 2;
        // NOTE: Rounding can give 1000, for example 999.7 k. Move to the next prefix.
        if (Math.Abs(Math.Round(scaled, decimals)) >= 1000 && exponent < 9)
        {
            exponent += 3;
            scaled /= 1000;
            decimals = 2;
        }
        return $"{Fixed(scaled, decimals)} {Prefixes[(exponent + 15) / 3]}{unit}";
    }

    static string Fixed(double value, int decimals)
    {
        var text = Math.Round(value, decimals).ToString("F" + decimals, CultureInfo.InvariantCulture);
        return text.StartsWith('-') && text.Trim('-', '0', '.').Length == 0 ? text[1..] : text;
    }
}
