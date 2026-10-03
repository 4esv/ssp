using System.Globalization;
using System.Text.RegularExpressions;
using Ssp.Core.Import.LtSpice;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Schematic;
using LayoutDoc = Ssp.Core.Layout.Layout;
using PartsApi = Ssp.Core.Parts.Parts;

namespace Ssp.Web.Tests;

// NOTE: The schematic renderer is in Ssp.Web, so the SVG part of the issue #61 test is here. The rest is in Ssp.Core.Tests.
public partial class AscLayoutTests
{
    // NOTE: Same as SchematicRenderer.Margin.
    const double Margin = 20;

    readonly record struct Box(double X, double Y, double W, double H)
    {
        public bool Overlaps(Box o) => X < o.X + o.W && o.X < X + W && Y < o.Y + o.H && o.Y < Y + H;
    }

    [GeneratedRegex("viewBox=\"([^\"]+)\"")]
    private static partial Regex ViewBox();

    [Fact]
    public void FixtureSchematicHasNoOverlappingSymbols()
    {
        var asc = File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", "clipper-ltspice.asc"));
        var circuit = NetlistLoader.Load(AscImporter.ToNetlist(asc));
        var parts = PartMap.Resolve(circuit, PartsApi.Load(Path.Combine(RepoPaths.Root, "models", "parts.toml")));
        var path = Path.Combine(Path.GetTempPath(), $"ssp-{Guid.NewGuid():N}.layout.toml");
        LayoutDoc layout;
        try
        {
            LayoutDoc.Write(path, AscImporter.ToLayout(asc));
            layout = LayoutDoc.Read(path);
        }
        finally
        {
            File.Delete(path);
        }

        Assert.Contains("<g data-ref=", SchematicRenderer.ToSvg(circuit, layout, parts));

        // NOTE: The view box of a schematic with one part is that part and its label, plus the margin.
        var boxes = layout.Parts.ToDictionary(p => p.Reference, p =>
        {
            var svg = SchematicRenderer.ToSvg(circuit, new LayoutDoc([p], []), parts);
            var v = ViewBox().Match(svg).Groups[1].Value.Split(' ').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            return new Box(v[0] + Margin, v[1] + Margin, v[2] - 2 * Margin, v[3] - 2 * Margin);
        });

        Assert.Equal(5, boxes.Count);
        foreach (var (a, i) in boxes.Select((b, i) => (b, i)))
        {
            foreach (var b in boxes.Skip(i + 1))
            {
                Assert.False(a.Value.Overlaps(b.Value), $"{a.Key} {a.Value} overlaps {b.Key} {b.Value}");
            }
        }
    }
}
