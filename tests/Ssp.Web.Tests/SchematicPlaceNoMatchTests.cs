using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Schematic;
using LayoutDoc = Ssp.Core.Layout.Layout;
using Point = Ssp.Core.Layout.Point;

namespace Ssp.Web.Tests;

public class SchematicPlaceNoMatchTests
{
    static readonly PartsTable Table = Ssp.Core.Parts.Parts.Parse(
        File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "parts.toml")), "parts.toml");

    // NOTE: #371: the monkey test removed a closing line. The new part went inside the open subcircuit, or after
    // nothing the loader reads, so the circuit had no element for it, and Free threw NoMatch.
    [Theory]
    [InlineData(".ends TL072")]
    [InlineData(".END")]
    public void PlaceAt_never_throws_when_the_netlist_loses_a_closing_line(string removed)
    {
        var text = File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits/library/tone-baxandall-active-tl072.cir"));
        var netlist = string.Join('\n', text.Split('\n').Where(l => !l.Trim().Equals(removed, StringComparison.OrdinalIgnoreCase)));
        var circuit = NetlistLoader.Load(netlist);
        var layout = AutoPlacer.Place(circuit, circuit.Directives, PartMap.Resolve(circuit, Table));
        foreach (var kind in SchematicEdits.Kinds.Keys.Where(k => !k.StartsWith("jack")))
        {
            var change = SchematicEdits.PlaceAt(netlist, layout, kind, new Point(100, 100));
            Assert.NotNull(change);
            if (removed.StartsWith(".ends", StringComparison.OrdinalIgnoreCase)) Assert.Equal(netlist, change.Netlist);
        }
    }
}
