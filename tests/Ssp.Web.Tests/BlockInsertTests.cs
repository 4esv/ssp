using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Ssp.Core.Diagnostics;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Web.Components;
using Ssp.Web.Library;
using Ssp.Web.Projects;
using Ssp.Web.Schematic;
using LayoutDoc = Ssp.Core.Layout.Layout;
using Point = Ssp.Core.Layout.Point;

namespace Ssp.Web.Tests;

// NOTE: Ready-made stages and My Blocks (#181): insert one on an empty spot, and save a selection in browser storage.
public class BlockInsertTests : BunitContext
{
    static readonly PartsTable Table = Ssp.Core.Parts.Parts.Parse(
        File.ReadAllText(Path.Combine(RepoPaths.Root, "models", "parts.toml")), "parts.toml");

    static readonly string Fuzz = File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", "fuzz-drawn.cir"));

    static PartMap Map(string netlist) => PartMap.Resolve(NetlistLoader.Load(netlist), Table);

    static LayoutDoc Placed(string netlist)
    {
        var circuit = NetlistLoader.Load(netlist);
        return AutoPlacer.Place(circuit, circuit.Directives);
    }

    // The outline of each placed element, by reference.
    static Dictionary<string, (Point Min, Point Max)> Outlines(string netlist, LayoutDoc layout)
    {
        var circuit = NetlistLoader.Load(netlist);
        var outlines = new Dictionary<string, (Point, Point)>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in SchematicRenderer.Elements(circuit, Map(netlist)))
        {
            var placement = layout.Parts.FirstOrDefault(p => string.Equals(p.Reference, e.Reference, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Reference, e.Members[0], StringComparison.OrdinalIgnoreCase));
            if (placement is not null) outlines[e.Reference] = SchematicRenderer.Outline(e, placement);
        }
        return outlines;
    }

    static bool Overlap((Point Min, Point Max) a, (Point Min, Point Max) b) =>
        a.Min.X < b.Max.X && b.Min.X < a.Max.X && a.Min.Y < b.Max.Y && b.Min.Y < a.Max.Y;

    static void AssertValidAndClear(string before, LayoutDoc beforeLayout, SchematicChange after)
    {
        var circuit = NetlistLoader.Load(after.Netlist);
        Assert.DoesNotContain(circuit.Diagnostics, d => d.Severity == Severity.Error);
        Assert.Empty(after.Layout.Validate(circuit));
        var names = circuit.Circuit.Select(e => e.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var old = Outlines(before, beforeLayout);
        var now = Outlines(after.Netlist, after.Layout);
        foreach (var (reference, outline) in now.Where(n => !old.ContainsKey(n.Key)))
        {
            foreach (var (other, kept) in old) Assert.False(Overlap(outline, kept), $"{reference} overlaps {other}");
        }
        // NOTE: The parts that were there do not move.
        foreach (var (reference, outline) in old) Assert.Equal(outline, now[reference]);
    }

    public static TheoryData<string> Blocks() => [.. CircuitLibrary.Blocks.Select(b => b.FileName)];

    [Fact]
    public void The_library_lists_each_block_with_its_title_and_layout()
    {
        var files = Directory.GetFiles(Path.Combine(RepoPaths.Root, "circuits", "blocks"), "*.cir").Select(Path.GetFileName).Order(StringComparer.Ordinal);
        Assert.Equal(files, CircuitLibrary.Blocks.Select(b => b.FileName));
        var block = CircuitLibrary.Blocks.Single(b => b.FileName == "gain-noninverting-tl072.cir");
        Assert.Equal("Non-inverting gain stage, TL072", block.Title);
        Assert.NotNull(block.Layout);
    }

    [Theory]
    [MemberData(nameof(Blocks))]
    public void A_block_inserted_on_an_empty_spot_gives_a_valid_netlist_with_no_overlap(string file)
    {
        var block = CircuitLibrary.Blocks.Single(b => b.FileName == file);
        var layout = Placed(Fuzz);
        var clip = SchematicEdits.Block(block.Netlist, block.Layout is null ? null : LayoutDoc.Parse(block.Layout));
        var below = new Point(0, layout.Parts.Max(p => p.Y) + 400);

        var (change, fresh) = SchematicEdits.PasteAt(Fuzz, layout, clip, below);

        AssertValidAndClear(Fuzz, layout, change);
        Assert.Equal(clip.Parts.Count, fresh.Count);
        // NOTE: An empty spot keeps the block where the tap was.
        var placed = change.Layout.Parts.Where(p => !layout.Parts.Contains(p)).ToList();
        Assert.Equal(below.Y, placed.Min(p => p.Y));
    }

    [Fact]
    public void A_block_inserted_on_top_of_parts_moves_right_until_it_is_clear()
    {
        var layout = Placed(Fuzz);
        var block = CircuitLibrary.Blocks.Single(b => b.FileName == "gain-noninverting-tl072.cir");
        var clip = SchematicEdits.Block(block.Netlist, LayoutDoc.Parse(block.Layout!));
        var onTop = new Point(layout.Parts.Min(p => p.X), layout.Parts.Min(p => p.Y));

        var (change, _) = SchematicEdits.PasteAt(Fuzz, layout, clip, onTop);

        AssertValidAndClear(Fuzz, layout, change);
    }

    [Fact]
    public void A_block_with_a_subcircuit_inserted_twice_defines_the_subcircuit_once()
    {
        var block = CircuitLibrary.Blocks.Single(b => b.FileName == "gain-noninverting-tl072.cir");
        var clip = SchematicEdits.Block(block.Netlist, LayoutDoc.Parse(block.Layout!));
        var (once, _) = SchematicEdits.PasteAt("", new LayoutDoc([], []), clip, new Point(0, 0));
        var (twice, _) = SchematicEdits.PasteAt(once.Netlist, once.Layout, clip, new Point(0, 0));

        AssertValidAndClear(once.Netlist, once.Layout, twice);
        Assert.Single(twice.Netlist.Split('\n'), l => l.StartsWith(".subckt TL072", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_saved_block_round_trips_through_storage_and_a_corrupt_entry_is_skipped()
    {
        var storage = new MemoryStore();
        var store = new BlockStore(storage);
        var layout = Placed(Fuzz);
        var alone = SchematicEdits.Alone(SchematicEdits.Copy(Fuzz, layout, Map(Fuzz), ["R2", "R3", "R4"]));

        var saved = await store.Save("Bias", alone.Netlist, LayoutDoc.Format(alone.Layout));
        storage.Items[BlockStore.EntryKey("broken")] = "{not json";
        storage.Items[BlockStore.IndexKey] = $"[\"broken\",\"{saved.Id}\"]";

        var listed = Assert.Single(await new BlockStore(storage).List());
        Assert.Equal(("Bias", alone.Netlist, LayoutDoc.Format(alone.Layout)), (listed.Name, listed.Netlist, listed.Layout));
        var (change, fresh) = SchematicEdits.PasteAt(Fuzz, layout, SchematicEdits.Block(listed.Netlist, LayoutDoc.Parse(listed.Layout!)), new Point(0, 2000));
        Assert.Equal(3, fresh.Count);
        AssertValidAndClear(Fuzz, layout, change);
    }

    [Fact]
    public void A_block_from_the_list_lands_where_the_canvas_is_tapped_in_one_step()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var layout = Placed(Fuzz);
        var seen = new List<SchematicChange>();
        var editor = Render<SchematicEditor>(p => p
            .Add(c => c.Netlist, Fuzz)
            .Add(c => c.Layout, layout)
            .Add(c => c.BlockStorage, new MemoryStore())
            .Add(c => c.Changed, (SchematicChange c) => seen.Add(c)));

        editor.Find("button[data-action=blocks]").Click();
        editor.Find("button[data-block='gain-noninverting-tl072.cir']").Click();
        Assert.Empty(seen);
        Assert.Contains("Tap an empty spot", editor.Find(".schematic-hint").TextContent);
        editor.Find("figure.schematic").Click(new MouseEventArgs { ClientX = 0, ClientY = 3000 });

        var change = Assert.Single(seen);
        AssertValidAndClear(Fuzz, layout, change);
        // NOTE: The block has 12 parts: V1, VCC, VEE, CIN, RB, X1, RF, CF, RG, CG, COUT and RL.
        static int Parts(string n) => NetlistLoader.Load(n).Circuit.Select(e => e.Name.Split('.')[0]).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Assert.Equal(Parts(Fuzz) + 12, Parts(change.Netlist));
        // NOTE: One insert per pick. The next tap on the canvas adds nothing.
        editor.Find("figure.schematic").Click(new MouseEventArgs { ClientX = 0, ClientY = 3000 });
        Assert.Single(seen);
    }

    [Fact]
    public async Task A_selection_saved_as_my_block_is_listed_first_and_inserts_the_same_way()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var layout = Placed(Fuzz);
        var storage = new MemoryStore();
        var seen = new List<SchematicChange>();
        var editor = Render<SchematicEditor>(p => p
            .Add(c => c.Netlist, Fuzz)
            .Add(c => c.Layout, layout)
            .Add(c => c.BlockStorage, storage)
            .Add(c => c.Changed, (SchematicChange c) => seen.Add(c)));

        editor.Find(".part[data-ref=R2]").Click();
        editor.Find(".part[data-ref=R3]").Click(new MouseEventArgs { ShiftKey = true });
        editor.Find(".part[data-ref=R4]").Click(new MouseEventArgs { ShiftKey = true });
        editor.Find("button[data-action=save-block]").Click();
        editor.Find("input.block-name").Input("Bias");
        editor.Find("form.block-save").Submit();
        Assert.Empty(seen);

        var saved = Assert.Single(await new BlockStore(storage).List());
        Assert.Equal("Bias", saved.Name);
        editor.Find("button[data-action=blocks]").Click();
        var first = editor.FindAll(".block-list button[data-block]")[0];
        Assert.Equal("Bias", first.TextContent.Trim());
        first.Click();
        editor.Find("figure.schematic").Click(new MouseEventArgs { ClientX = 0, ClientY = 3000 });

        var change = Assert.Single(seen);
        AssertValidAndClear(Fuzz, layout, change);
        Assert.Equal(NetlistLoader.Load(Fuzz).Circuit.Count() + 3, NetlistLoader.Load(change.Netlist).Circuit.Count());
    }
}
