using System.Globalization;

namespace Ssp.Web.Components;

/// <summary>Plain engineering notation for panels: <c>44.5 pV</c>, <c>4.5 V</c>, <c>10 nF</c>. It never gives an exponent.</summary>
public static class Eng
{
    static readonly (double Factor, string Prefix)[] Prefixes =
        [(1e12, "T"), (1e9, "G"), (1e6, "M"), (1e3, "k"), (1, ""), (1e-3, "m"), (1e-6, "µ"), (1e-9, "n"), (1e-12, "p"), (1e-15, "f")];

    /// <summary>Gives the value with three significant digits, an SI prefix and the unit.</summary>
    public static string Format(double value, string unit = "")
    {
        var suffix = unit.Length == 0 ? "" : " " + unit;
        if (value == 0)
        {
            return "0" + suffix;
        }
        if (!double.IsFinite(value))
        {
            return (double.IsNaN(value) ? "NaN" : value > 0 ? "∞" : "-∞") + suffix;
        }

        var size = Math.Abs(value);
        var index = Array.FindIndex(Prefixes, p => size >= p.Factor * (1 - 1e-9));
        var (factor, prefix) = Prefixes[index < 0 ? ^1 : index];
        var scaled = value / factor;
        // NOTE: 999.96 rounds to 1000 with three digits. Move up one prefix so the number stays under 1000.
        if (Math.Abs(double.Parse(Plain(scaled), CultureInfo.InvariantCulture)) >= 1000 && index > 0)
        {
            (factor, prefix) = Prefixes[index - 1];
            scaled = value / factor;
        }

        // NOTE: Below the smallest prefix the number is tiny and has no name. Print it with fixed digits, not an exponent.
        var text = size < Prefixes[^1].Factor
            ? scaled.ToString("0.###", CultureInfo.InvariantCulture)
            : Plain(scaled);
        return prefix.Length + unit.Length == 0 ? text : text + " " + prefix + unit;
    }

    // NOTE: A fixed format keeps three significant digits and gives no exponent.
    static string Plain(double scaled)
    {
        var size = Math.Abs(scaled);
        var format = size >= 100 ? "0" : size >= 10 ? "0.#" : "0.##";
        return scaled.ToString(format, CultureInfo.InvariantCulture);
    }
}
