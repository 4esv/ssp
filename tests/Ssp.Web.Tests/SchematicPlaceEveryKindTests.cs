using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Schematic;
using LayoutDoc = Ssp.Core.Layout.Layout;
using Point = Ssp.Core.Layout.Point;

namespace Ssp.Web.Tests;

public class SchematicPlaceEveryKindTests
{
    static readonly PartsTable Table = Ssp.Core.Parts.Parts.Parse(
        File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "parts.toml")), "parts.toml");

    // NOTE: #281: a text edit can remove a part from the netlist while the layout still has its placement. The next
    // palette add took the same reference and the layout had two placements for it: MoreThanOneMatch in Settle.
    // The monkey test hit it on seed 27. It is not specific to the new kinds: diode, led and the others have it too.
    [Theory]
    [InlineData("circuits/library/tone-baxandall-active-tl072.cir")]
    [InlineData("circuits/library/clip-feedback-led-tl072.cir")]
    public void PlaceAt_after_the_netlist_lost_its_parts_gives_each_reference_one_placement(string path)
    {
        var full = NetlistLoader.Load(File.ReadAllText(Path.Combine(RepoPaths.Root, path)));
        var stale = AutoPlacer.Place(full, full.Directives, PartMap.Resolve(full, Table));
        foreach (var kind in SchematicEdits.Kinds.Keys.Where(k => !k.StartsWith("jack")))
        {
            // The layout has a placement for every part of the starter. The netlist has none of them.
            var netlist = "* schematic\n.END\n";
            var layout = stale;
            for (var i = 0; i < 3; i++)
            {
                var change = SchematicEdits.PlaceAt(netlist, layout, kind, new Point(100 + 10 * i, 100));
                (netlist, layout) = (change.Netlist, change.Layout);
                var duplicates = layout.Parts.GroupBy(p => p.Reference, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                Assert.True(duplicates.Count == 0, $"{kind} #{i + 1}: more than one placement for {string.Join(", ", duplicates)}");
            }
        }
    }
}
