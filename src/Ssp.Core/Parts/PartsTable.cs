namespace Ssp.Core.Parts;

/// <summary>The parts table. Ids are unique.</summary>
public sealed class PartsTable
{
    readonly Dictionary<string, PartRow> byId;

    internal PartsTable(IReadOnlyList<PartRow> rows)
    {
        Rows = rows;
        byId = rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<PartRow> Rows { get; }

    public PartRow? Get(string id) => byId.GetValueOrDefault(id);
}
