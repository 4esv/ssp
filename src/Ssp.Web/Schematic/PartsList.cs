using System.Text;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;

namespace Ssp.Web.Schematic;

/// <summary>One line of a parts list: equal parts of a kind and value, with the references that use them.</summary>
public sealed record PartsListRow(int Quantity, string References, string Kind, string Value, string Footprint, string BuyUrl);

/// <summary>
/// The parts of a schematic. Equal parts are one row with a quantity. Sources and supply rails are not parts.
/// Every row has a link to a supplier search.
/// </summary>
public sealed class PartsList
{
    const string SearchUrl = "https://www.mouser.com/c/?q=";

    static readonly HashSet<string?> NotParts = ["source", "battery", "isource", "rail"];

    PartsList(string title, IReadOnlyList<PartsListRow> rows)
    {
        Title = title;
        Rows = rows;
    }

    /// <summary>The <c>ssp:title</c> of the netlist, or an empty string.</summary>
    public string Title { get; }

    public IReadOnlyList<PartsListRow> Rows { get; }

    /// <summary>The number of parts to buy.</summary>
    public int Total => Rows.Sum(r => r.Quantity);

    public static PartsList From(LoadedCircuit circuit, PartMap parts)
    {
        var rows = SchematicRenderer.Elements(circuit, parts)
            .Where(e => !NotParts.Contains(e.Kind))
            .Select(e => (Element: e, Row: parts.Parts.GetValueOrDefault(e.Reference)))
            .GroupBy(x => (Kind: x.Element.Kind ?? "part", x.Element.Value, Id: x.Row?.Id), x => x, new KeyComparer())
            .Select(g =>
            {
                var (kind, value, _) = g.Key;
                var row = g.First().Row;
                var refs = string.Join(", ", g.Select(x => x.Element.Reference));
                return new PartsListRow(g.Count(), refs, kind, value, row?.Footprint ?? "", BuyUrl(row, kind, value));
            })
            .ToList();
        return new PartsList(circuit.Directives.Title ?? "", rows);
    }

    /// <summary>One CSV line for each row. The columns are qty, refs, kind, value, footprint and buy link.</summary>
    public string ToCsv()
    {
        var sb = new StringBuilder("qty,refs,kind,value,footprint,buy link\n");
        foreach (var r in Rows)
        {
            string[] cells = [r.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture), r.References, r.Kind, r.Value, r.Footprint, r.BuyUrl];
            sb.Append(string.Join(',', cells.Select(Quote))).Append('\n');
        }
        return sb.ToString();
    }

    // NOTE: A part from the parts table has its own link. Any other part searches by its value, and by its kind when the value is a bare number.
    static string BuyUrl(PartRow? row, string kind, string value)
    {
        if (row is not null) return row.BuyUrl.Replace("{id}", Uri.EscapeDataString(row.Id), StringComparison.Ordinal);
        var term = value.Length > 0 && !char.IsDigit(value[0]) ? value : $"{value} {kind}".Trim();
        return SearchUrl + Uri.EscapeDataString(term);
    }

    static string Quote(string cell) =>
        cell.AsSpan().IndexOfAny(",\"\r\n") < 0 ? cell : "\"" + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    sealed class KeyComparer : IEqualityComparer<(string Kind, string Value, string? Id)>
    {
        public bool Equals((string Kind, string Value, string? Id) a, (string Kind, string Value, string? Id) b) =>
            a.Kind == b.Kind && string.Equals(a.Value, b.Value, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Kind, string Value, string? Id) k) =>
            HashCode.Combine(k.Kind, k.Value.ToUpperInvariant(), k.Id?.ToUpperInvariant());
    }
}
