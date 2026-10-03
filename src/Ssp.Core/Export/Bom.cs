using System.Text;
using Ssp.Core.Parts;

namespace Ssp.Core.Export;

/// <summary>Exports a bill of materials.</summary>
public static class Bom
{
    /// <summary>
    /// One CSV row for each reference, sorted by reference. The columns are ref, part, value, footprint and buy link.
    /// A reference with no part has empty cells. The value is the model name of the part.
    /// </summary>
    public static string ToCsv(PartMap parts)
    {
        var sb = new StringBuilder("ref,part,value,footprint,buy link\n");
        foreach (var (reference, row) in parts.Parts.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            string[] cells = row is null
                ? [reference, "", "", "", ""]
                : [reference, row.Id, row.Model, row.Footprint, row.BuyUrl.Replace("{id}", row.Id, StringComparison.Ordinal)];
            sb.Append(string.Join(',', cells.Select(Quote))).Append('\n');
        }
        return sb.ToString();
    }

    static string Quote(string cell) =>
        cell.AsSpan().IndexOfAny(",\"\r\n") < 0 ? cell : "\"" + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
