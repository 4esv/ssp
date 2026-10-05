using System.Text;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Components;
using Ssp.Web.Schematic;

namespace Ssp.Web.Tests;

public class SchematicPdfTests
{
    static string Fixture(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", name));

    static string Pdf(string netlist)
    {
        var circuit = NetlistLoader.Load(netlist);
        var list = PartsList.From(circuit, PartMap.Resolve(circuit, SchematicView.Table));
        return Encoding.Latin1.GetString(SchematicPdf.Build(SchematicView.Svg(netlist)!, list));
    }

    [Fact]
    public void PdfHasAHeaderAndATrailer()
    {
        var pdf = Pdf(Fixture("divider-basic.cir"));

        Assert.StartsWith("%PDF-1.4", pdf);
        Assert.EndsWith("%%EOF\n", pdf);
        Assert.Contains("/Type /Page", pdf);
    }

    [Fact]
    public void PdfHasATitleBlock()
    {
        var pdf = Pdf(Fixture("divider-basic.cir"));

        Assert.Contains("(Resistor divider) Tj", pdf);
        Assert.Contains("(Title) Tj", pdf);
        Assert.Contains("(Parts: 2) Tj", pdf);
    }

    [Fact]
    public void PdfDrawsTheSchematicAndListsTheParts()
    {
        var pdf = Pdf(Fixture("divider-basic.cir"));

        // NOTE: Each wire and symbol is a stroke, and each label is text.
        Assert.Contains(" l\n", pdf);
        Assert.Contains("S\n", pdf);
        Assert.Contains("(R1) Tj", pdf);
        Assert.Contains("(1k) Tj", pdf);
    }

    [Fact]
    public void XrefOffsetsPointAtTheirObjects()
    {
        var pdf = Pdf(Fixture("divider-basic.cir"));
        var xref = pdf.LastIndexOf("xref\n", StringComparison.Ordinal);
        var lines = pdf[xref..].Split('\n').Skip(3).TakeWhile(l => l.EndsWith(" n ", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(lines);
        for (var i = 0; i < lines.Count; i++)
        {
            Assert.StartsWith($"{i + 1} 0 obj", pdf[int.Parse(lines[i][..10])..]);
        }
        Assert.Equal(xref, int.Parse(pdf[(pdf.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]));
    }
}
