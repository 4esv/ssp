using Ssp.Core.Parts;
using PartsApi = Ssp.Core.Parts.Parts;

namespace Ssp.Core.Tests;

public class PartsTests
{
    const string Row = """
        [[part]]
        id = "{0}"
        kind = "diode"
        model = "d1n4148"
        symbol = "diode"
        footprint = "DO-35"
        buy_url = "https://example.com/search?q={{id}}"
        {1}
        """;

    static string Write(string toml)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".toml");
        File.WriteAllText(path, toml);
        return path;
    }

    static string Prov(string v = "1N4148 datasheet, onsemi") => $"provenance = \"{v}\"";

    [Fact]
    public void ShippedTableResolves()
    {
        var table = PartsApi.Load(Path.Combine(RepoPaths.Root, "models", "parts.toml"));
        Assert.NotEmpty(table.Rows);
        foreach (var r in table.Rows)
        {
            Assert.All(new[] { r.Id, r.Kind, r.Model, r.Symbol, r.Footprint, r.BuyUrl, r.Provenance },
                f => Assert.False(string.IsNullOrWhiteSpace(f), $"row {r.Id} has an empty field"));
            Assert.Same(r, table.Get(r.Id));
        }
    }

    [Fact]
    public void ParseGivesTheSameRowsAsLoad()
    {
        var path = Path.Combine(RepoPaths.Root, "models", "parts.toml");

        var parsed = PartsApi.Parse(File.ReadAllText(path), "parts.toml");

        Assert.Equal(PartsApi.Load(path).Rows, parsed.Rows);
    }

    [Fact]
    public void ParseErrorNamesTheSource()
    {
        var ex = Assert.Throws<InvalidDataException>(() => PartsApi.Parse("x = 1\n", "embedded parts.toml"));
        Assert.StartsWith("embedded parts.toml:", ex.Message);
    }

    [Fact]
    public void DuplicateIdFailsAndNamesTheId()
    {
        var path = Write(string.Format(Row, "D1", Prov()) + "\n" + string.Format(Row, "D1", Prov()));
        var ex = Assert.Throws<InvalidDataException>(() => PartsApi.Load(path));
        Assert.Contains("D1", ex.Message);
    }

    [Fact]
    public void MissingProvenanceFailsAndNamesTheRow()
    {
        var path = Write(string.Format(Row, "D7", ""));
        var ex = Assert.Throws<InvalidDataException>(() => PartsApi.Load(path));
        Assert.Contains("D7", ex.Message);
        Assert.Contains("provenance", ex.Message);
    }

    [Fact]
    public void EmptyProvenanceFails()
    {
        var path = Write(string.Format(Row, "D8", Prov("  ")));
        var ex = Assert.Throws<InvalidDataException>(() => PartsApi.Load(path));
        Assert.Contains("D8", ex.Message);
    }
}
