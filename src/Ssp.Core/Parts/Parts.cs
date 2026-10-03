using Tomlyn;
using Tomlyn.Model;

namespace Ssp.Core.Parts;

/// <summary>Loads the parts table from a TOML file (<c>models/parts.toml</c>).</summary>
public static class Parts
{
    static readonly string[] Fields = ["id", "kind", "model", "symbol", "footprint", "buy_url", "provenance"];

    public static PartsTable Load(string path)
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

        if (!doc.TryGetValue("part", out var node) || node is not TomlTableArray tables)
        {
            throw new InvalidDataException($"{path}: no [[part]] rows.");
        }

        var rows = new List<PartRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < tables.Count; i++)
        {
            var t = tables[i];
            var label = t.TryGetValue("id", out var idv) && idv is string s && s.Length > 0
                ? $"row \"{s}\""
                : $"row #{i + 1}";
            var v = new string[Fields.Length];
            for (var f = 0; f < Fields.Length; f++)
            {
                if (!t.TryGetValue(Fields[f], out var val) || val is not string str || string.IsNullOrWhiteSpace(str))
                {
                    throw new InvalidDataException($"{path}: {label} is missing {Fields[f]}.");
                }
                v[f] = str;
            }
            if (!seen.Add(v[0]))
            {
                throw new InvalidDataException($"{path}: duplicate id \"{v[0]}\".");
            }
            rows.Add(new PartRow(v[0], v[1], v[2], v[3], v[4], v[5], v[6]));
        }
        return new PartsTable(rows);
    }
}
