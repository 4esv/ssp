using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Core.Layout;
using Ssp.Core.Netlist;
using Ssp.Web.Components;
using Ssp.Web.Editing;
using Ssp.Web.Hosting;
using Ssp.Web.Pages;
using Ssp.Web.Schematic;
using Ssp.Web.Sharing;
using LayoutDoc = Ssp.Core.Layout.Layout;

namespace Ssp.Web.Tests;

// NOTE: An edit in the netlist box must not block the page (#245). It loads the netlist once, keeps the hand layout
// when the parts stay the same, and redraws the schematic only after the typing stops for 100 ms.
public class EditorEditCostTests : BunitContext
{
    const string Divider = "* divider\nV1 in 0 1\nR1 in out 1k\nR2 out 0 2k\n.END\n";
    static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(100);
    readonly ManualTimeProvider time = new();

    public EditorEditCostTests()
    {
        Services.AddSingleton<TimeProvider>(time);
        Services.AddSingleton<ISimulationHost, InProcessSimulationHost>();
    }

    IRenderedComponent<Editor> Open(string netlist)
    {
        Services.GetRequiredService<BunitNavigationManager>().NavigateTo("editor#" + ShareCodec.Encode(netlist));
        return Render<Editor>();
    }

    // The auto-layout with R1 moved by hand.
    static LayoutDoc HandLayout(string netlist)
    {
        var circuit = NetlistLoader.Load(netlist);
        var auto = AutoPlacer.Place(circuit, circuit.Directives);
        return new LayoutDoc(auto.Parts.Select(p => p.Reference == "R1" ? p with { X = p.X + 200, Y = p.Y + 100 } : p).ToList(), auto.Wires);
    }

    IRenderedComponent<SchematicEditor> HandPlaced(IRenderedComponent<Editor> page, LayoutDoc hand)
    {
        var schematic = page.FindComponent<SchematicEditor>();
        page.InvokeAsync(() => schematic.Instance.Changed.InvokeAsync(new SchematicChange(Divider, hand)));
        Assert.Same(hand, schematic.Instance.Layout);
        return schematic;
    }

    [Fact]
    public void AValueEditKeepsTheHandLayout()
    {
        var page = Open(Divider);
        var hand = HandLayout(Divider);
        var schematic = HandPlaced(page, hand);

        page.Find("textarea").Input(Divider.Replace("R2 out 0 2k", "R2 out 0 3.3k"));
        time.Advance(Wait);

        Assert.Contains("3.3k", schematic.Instance.Netlist);
        Assert.Equal(hand.Parts, schematic.Instance.Layout?.Parts);
        Assert.Equal(hand.Wires, schematic.Instance.Layout?.Wires);
    }

    [Fact]
    public void AValueEditDoesNotPlaceTheSchematicAgain()
    {
        var page = Open(Divider);
        var schematic = page.FindComponent<SchematicEditor>();
        var placed = schematic.Instance.Drawn;

        page.Find("textarea").Input(Divider.Replace("R2 out 0 2k", "R2 out 0 3.3k"));
        time.Advance(Wait);

        Assert.Contains("3.3k", schematic.Instance.Netlist);
        Assert.Same(placed, schematic.Instance.Drawn);
    }

    [Fact]
    public void ACommentEditKeepsTheHandLayout()
    {
        var page = Open(Divider);
        var hand = HandLayout(Divider);
        var schematic = HandPlaced(page, hand);

        page.Find("textarea").Input("* a note\n" + Divider);
        time.Advance(Wait);

        Assert.Equal(hand.Parts, schematic.Instance.Layout?.Parts);
    }

    [Fact]
    public void ANewPartKeepsThePlacesOfTheOtherParts()
    {
        var page = Open(Divider);
        var hand = HandLayout(Divider);
        var schematic = HandPlaced(page, hand);

        page.Find("textarea").Input(Divider.Replace(".END", "R3 out 0 10k\n.END"));
        time.Advance(Wait);

        var placed = schematic.Instance.Drawn;
        Assert.Contains(placed.Parts, p => p.Reference == "R3");
        foreach (var p in hand.Parts)
        {
            Assert.Contains(p, placed.Parts);
        }
    }

    [Fact]
    public void AnEditLoadsTheNetlistOnce()
    {
        var page = Open(Divider);
        var loads = page.FindComponent<SchematicEditor>().Instance.Loads!;
        var before = loads.Loads;

        page.Find("textarea").Input(Divider.Replace("R2 out 0 2k", "R2 out 0 3.3k"));
        time.Advance(Wait);

        Assert.Equal(before + 1, loads.Loads);
    }

    [Fact]
    public void TheSchematicRedrawsWhenTheTypingStopsFor100Ms()
    {
        var page = Open(Divider);
        var schematic = page.FindComponent<SchematicEditor>();

        page.Find("textarea").Input(Divider.Replace("2k", "3k"));
        time.Advance(TimeSpan.FromMilliseconds(60));
        page.Find("textarea").Input(Divider.Replace("2k", "3.3k"));
        time.Advance(TimeSpan.FromMilliseconds(60));
        Assert.Equal(Divider, schematic.Instance.Netlist);

        time.Advance(TimeSpan.FromMilliseconds(40));
        Assert.Contains("3.3k", schematic.Instance.Netlist);
    }

    [Fact]
    public void ThePlacerKeepsTheKeptPartsAndPlacesOnlyTheNewOnes()
    {
        var hand = HandLayout(Divider);
        var grown = NetlistLoader.Load(Divider.Replace(".END", "R3 out 0 10k\n.END"));

        var placed = AutoPlacer.Place(grown, grown.Directives, keep: hand);

        foreach (var p in hand.Parts)
        {
            Assert.Contains(p, placed.Parts);
        }
        Assert.Contains(placed.Parts, p => p.Reference == "R3");
        Assert.Empty(placed.Validate(grown));
    }

    [Fact]
    public void TheShapeIsTheSameForAValueEditAndNotForANodeEdit()
    {
        static string Shape(string text) => AutoPlacer.Shape(NetlistLoader.Load(text));

        Assert.Equal(Shape(Divider), Shape("* note\n" + Divider.Replace("2k", "3.3k")));
        Assert.NotEqual(Shape(Divider), Shape(Divider.Replace("R2 out 0", "R2 mid 0")));
        Assert.NotEqual(Shape(Divider), Shape(Divider.Replace(".END", "R3 out 0 1k\n.END")));
    }

    [Fact]
    public void TheCacheLoadsOnlyANewText()
    {
        var cache = new NetlistCache();

        var first = cache.Load(Divider);
        Assert.Same(first, cache.Load(Divider));
        cache.Load(Divider + "* x\n");

        Assert.Equal(2, cache.Loads);
    }
}
