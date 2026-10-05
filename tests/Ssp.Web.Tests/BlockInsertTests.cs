using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Ssp.Core.Diagnostics;
using Ssp.Core.Netlist;
using Ssp.Core.Parts;
using Ssp.Core.Layout;
using Ssp.Web.Components;
using Ssp.Web.Editing;
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

    static Clip Stage(string file)
    {
        var block = CircuitLibrary.Blocks.Single(b => b.FileName == file);
        return SchematicEdits.Block(block.Netlist, block.Layout is null ? null : LayoutDoc.Parse(block.Layout));
    }

    // The wire from pin 1 of R2 to the supply V2, on net n10.
    static (WireRoute Wire, Point Middle) SupplyWire(LayoutDoc layout)
    {
        Point At(string reference, int pin) => SchematicEdits.Pin(NetlistLoader.Load(Fuzz), layout, Map(Fuzz), new PinRef(reference, pin)).Position;
        var ends = new[] { At("R2", 1), At("V2", 0) };
        var wire = layout.Wires.Single(w => w.Net == "n10" && ends.Contains(w.Points[0]) && ends.Contains(w.Points[^1]));
        var (a, b) = (wire.Points[0], wire.Points[1]);
        return (wire, new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2));
    }

    // The node of each pin of each part, by "reference.pin".
    static Dictionary<string, string> PinNodes(string netlist) =>
        NetlistLoader.Load(netlist).Circuit.OfType<SpiceSharp.Components.IComponent>()
            .SelectMany(c => c.Nodes.Select((n, i) => (Key: $"{c.Name}.{i}", Node: n)))
            .ToDictionary(p => p.Key, p => p.Node, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void A_stage_dropped_on_the_wire_between_R2_and_the_supply_lands_in_series()
    {
        var layout = Placed(Fuzz);
        var (wire, middle) = SupplyWire(layout);

        var drop = SchematicEdits.Drop(Fuzz, layout, Map(Fuzz), Stage("clip-shunt-led.cir"), middle);

        Assert.Null(drop.Refused);
        AssertValidAndClear(Fuzz, layout, drop.Change);
        Assert.DoesNotContain(wire, drop.Change.Layout.Wires);
        // NOTE: The stage's own test source V1 stays out. C1, R1, D1, D2 and R2 of the stage land.
        Assert.Equal(5, drop.References.Count);
        var old = PinNodes(Fuzz);
        var now = PinNodes(drop.Change.Netlist);
        // NOTE: The cut gives one end of the wire, R2 or V2, a new net. Every other pin that was there keeps its net.
        var moved = Assert.Single(old, p => now[p.Key] != p.Value);
        Assert.Contains(moved.Key, new[] { "R2.1", "V2.0" });
        var cut = now[moved.Key];
        Assert.DoesNotContain(cut, old.Values);
        // NOTE: The stage sits in series: one end of it on the new net, the other on n10.
        var c1 = drop.References.Single(r => r.StartsWith('C'));
        var r = drop.References.Where(x => x.StartsWith('R')).ToList();
        var ends = new[] { now[c1 + ".0"] }.Concat(r.SelectMany(x => new[] { now[x + ".0"], now[x + ".1"] })).ToHashSet();
        Assert.Contains(cut, ends);
        Assert.Contains("n10", ends);
        Assert.Equal(now[c1 + ".0"] == cut ? "n10" : cut, now[r.Single(x => now[x + ".1"] != "0") + ".1"]);
        // NOTE: The nets: those of the fuzz, the cut, and the stage net a between C1 and R1.
        Assert.Equal(NetlistLoader.Load(Fuzz).NodeNames.Count() + 2, NetlistLoader.Load(drop.Change.Netlist).NodeNames.Count());
        // NOTE: Wires go from the cut ends, on the old wire, to the stage pins.
        static bool On(WireRoute w, Point p) => w.Points.Zip(w.Points.Skip(1)).Any(s =>
            (s.First.X - p.X) * (s.Second.Y - p.Y) == (s.Second.X - p.X) * (s.First.Y - p.Y)
            && p.X >= Math.Min(s.First.X, s.Second.X) && p.X <= Math.Max(s.First.X, s.Second.X)
            && p.Y >= Math.Min(s.First.Y, s.Second.Y) && p.Y <= Math.Max(s.First.Y, s.Second.Y));
        var stagePins = drop.Change.Layout.Wires.Where(w => !layout.Wires.Contains(w) && On(wire, w.Points[0]) && !On(wire, w.Points[^1])).ToList();
        Assert.Contains(stagePins, w => w.Net == cut);
        Assert.Contains(stagePins, w => w.Net == "n10");
    }

    [Theory]
    [InlineData("fixtures/fuzz-drawn.cir")]
    [InlineData("library/fuzz-transistor-diode.cir")]
    public void A_stage_dropped_on_any_wire_leaves_no_pin_cut_off(string file)
    {
        var netlist = File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", file));
        var layout = Placed(netlist);
        var before = PinNodes(netlist);
        static Dictionary<string, int> Counts(Dictionary<string, string> pins) => pins.Values.GroupBy(n => n).ToDictionary(g => g.Key, g => g.Count());
        var inserted = 0;
        for (var i = 0; i < layout.Wires.Count; i++)
        {
            var w = layout.Wires[i];
            var at = new Point((w.Points[0].X + w.Points[1].X) / 2, (w.Points[0].Y + w.Points[1].Y) / 2);
            var drop = SchematicEdits.Drop(netlist, layout, Map(netlist), Stage("clip-shunt-led.cir"), at, i);
            if (drop.Refused is not null) continue;
            inserted++;
            AssertValidAndClear(netlist, layout, drop.Change);
            var now = PinNodes(drop.Change.Netlist);
            var (was, count) = (Counts(before), Counts(now));
            // NOTE: A pin that shared its net still shares it after the cut.
            Assert.All(before, p => Assert.True(count[now[p.Key]] >= Math.Min(2, was[p.Value]), $"wire {i}: {p.Key} is cut off on {now[p.Key]}"));
        }
        Assert.True(inserted > 0);
    }

    [Fact]
    public void A_stage_dropped_on_a_junction_joins_that_net_with_its_output_free()
    {
        var layout = Placed(Fuzz);
        var junction = SchematicEdits.Junctions(layout).First();
        var net = layout.Wires.First(w => w.Points.Contains(junction)).Net;

        var drop = SchematicEdits.Drop(Fuzz, layout, Map(Fuzz), Stage("clip-shunt-led.cir"), junction);

        Assert.Null(drop.Refused);
        AssertValidAndClear(Fuzz, layout, drop.Change);
        Assert.Contains("output is free", drop.Message);
        var old = PinNodes(Fuzz);
        var now = PinNodes(drop.Change.Netlist);
        Assert.All(old, p => Assert.Equal(p.Value, now[p.Key]));
        var c1 = drop.References.Single(r => r.StartsWith('C'));
        Assert.Equal(net, now[c1 + ".0"]);
        // NOTE: No old net joins another: only the stage's own nets a and out are new.
        Assert.Equal(NetlistLoader.Load(Fuzz).NodeNames.Count() + 2, NetlistLoader.Load(drop.Change.Netlist).NodeNames.Count());
    }

    [Fact]
    public void A_loose_group_dropped_on_a_wire_joins_by_its_first_pin()
    {
        var layout = Placed(Fuzz);
        var (_, middle) = SupplyWire(layout);
        var loose = SchematicEdits.Copy(Fuzz, layout, Map(Fuzz), ["R5"]);

        var drop = SchematicEdits.Drop(Fuzz, layout, Map(Fuzz), loose, middle);

        Assert.Null(drop.Refused);
        AssertValidAndClear(Fuzz, layout, drop.Change);
        var now = PinNodes(drop.Change.Netlist);
        Assert.Equal("n10", now[Assert.Single(drop.References) + ".0"]);
        Assert.All(PinNodes(Fuzz), p => Assert.Equal(p.Value, now[p.Key]));
    }

    [Fact]
    public void A_drop_that_would_short_two_nets_is_refused_with_a_reason()
    {
        var layout = Placed(Fuzz);
        var (_, middle) = SupplyWire(layout);
        // NOTE: The first pin of D1 is on n8 and the second is on ground. Turned round, the first pin is on ground.
        var flipped = Fuzz.Replace("D1 n8 0 DGEN", "D1 0 n8 DGEN");
        var grounded = SchematicEdits.Copy(flipped, layout, Map(flipped), ["D1"]);

        var drop = SchematicEdits.Drop(Fuzz, layout, Map(Fuzz), grounded, middle);

        Assert.NotNull(drop.Refused);
        Assert.Contains("short", drop.Refused);
        Assert.Equal((Fuzz, layout), (drop.Change.Netlist, drop.Change.Layout));
    }

    [Fact]
    public void A_stage_dropped_on_a_wire_from_the_list_is_one_step_and_undo_restores_it_exactly()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var layout = Placed(Fuzz);
        var history = new CommandStack();
        history.Reset(Fuzz, layout);
        var seen = new List<SchematicChange>();
        var editor = Render<SchematicEditor>(p => p
            .Add(c => c.Netlist, Fuzz)
            .Add(c => c.Layout, layout)
            .Add(c => c.BlockStorage, new MemoryStore())
            .Add(c => c.Changed, (SchematicChange c) => { seen.Add(c); history.Do(new ChangeCommand(c)); }));
        var (wire, _) = SupplyWire(layout);

        editor.Find("button[data-action=blocks]").Click();
        editor.Find("button[data-block='clip-shunt-led.cir']").Click();
        Assert.Contains("a wire", editor.Find(".schematic-hint").TextContent);
        editor.Find($"polyline.wire-hit[data-wire='{layout.Wires.ToList().IndexOf(wire)}']").Click();

        var change = Assert.Single(seen);
        AssertValidAndClear(Fuzz, layout, change);
        Assert.DoesNotContain(wire, change.Layout.Wires);
        Assert.Contains("in series", editor.Find(".schematic-hint").TextContent);
        history.Undo();
        Assert.Equal(Fuzz, history.Netlist);
        Assert.Equal(LayoutDoc.Format(layout), LayoutDoc.Format(history.Layout!));
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
