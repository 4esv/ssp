using System.Text;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Components;
using Ssp.Web.Schematic;

namespace Ssp.Web.Tests;

public class PartsListTests
{
    const string Netlist = "* ssp:title Test pedal\nV1 in 0 9\nR1 in a 10k\nR2 a b 10k\nR3 b 0 4.7k\nC1 a 0 100n\nC2 b 0 100n\nD1 a b DGEN\n.MODEL DGEN D\n.END\n";

    static PartsList Build(string netlist)
    {
        var circuit = NetlistLoader.Load(netlist);
        return PartsList.From(circuit, PartMap.Resolve(circuit, SchematicView.Table));
    }

    [Fact]
    public void EqualPartsAreOneRowWithAQuantity()
    {
        var rows = Build(Netlist).Rows;

        var tenK = Assert.Single(rows, r => r.Kind == "resistor" && r.Value == "10k");
        Assert.Equal(2, tenK.Quantity);
        Assert.Equal("R1, R2", tenK.References);
        Assert.Equal(2, Assert.Single(rows, r => r.Kind == "capacitor").Quantity);
        Assert.Equal(1, Assert.Single(rows, r => r.Value == "4.7k").Quantity);
        Assert.Equal(Netlist.Split('\n').Count(l => l.Length > 0 && char.IsLetter(l[0]) && l[0] != 'V') , rows.Sum(r => r.Quantity));
    }

    [Fact]
    public void SourcesAreNotParts()
    {
        Assert.DoesNotContain(Build(Netlist).Rows, r => r.References.Contains("V1"));
    }

    [Fact]
    public void EveryRowHasASupplierLink()
    {
        var rows = Build(Netlist).Rows;

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.StartsWith("https://", r.BuyUrl));
        // NOTE: A known part uses its own buy link. A passive searches by its value and kind.
        Assert.Contains(rows, r => r.Kind == "resistor" && r.BuyUrl.Contains("10k", StringComparison.Ordinal));
    }

    [Fact]
    public void CsvHasOneLinePerRowAndALink()
    {
        var list = Build(Netlist);
        var lines = list.ToCsv().TrimEnd('\n').Split('\n');

        Assert.Equal("qty,refs,kind,value,footprint,buy link", lines[0]);
        Assert.Equal(list.Rows.Count + 1, lines.Length);
        Assert.All(lines.Skip(1), l => Assert.Contains("https://", l));
        Assert.Contains("\"R1, R2\"", list.ToCsv());
    }

    [Fact]
    public void TitleComesFromTheDirective()
    {
        Assert.Equal("Test pedal", Build(Netlist).Title);
    }
}
