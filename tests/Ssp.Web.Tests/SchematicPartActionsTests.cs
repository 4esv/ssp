using System.Globalization;
using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Ssp.Web.Components;
using Ssp.Web.Schematic;

namespace Ssp.Web.Tests;

public class SchematicPartActionsTests : BunitContext
{
    const string Chain = "* chain\nV1 in 0 1\nR1 in a 1k\nR2 a 0 1k\nR3 a 0 2k\n.END\n";
    const double PaneWidth = 390, PaneHeight = 500, Button = 44;

    IRenderedComponent<SchematicEditor> Editor(List<SchematicChange> seen)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./js/schematic-canvas.js").Setup<double[]>("paneBox", _ => true).SetResult([0, 0, PaneWidth, PaneHeight]);
        var editor = Render<SchematicEditor>(p => p
            .Add(c => c.Netlist, Chain)
            .Add(c => c.Changed, (SchematicChange c) => seen.Add(c)));
        editor.Find("button[data-zoom=fit]").Click();
        return editor;
    }

    static (double Left, double Top) At(AngleSharp.Dom.IElement button)
    {
        var style = button.GetAttribute("style") ?? "";
        double Px(string name) => double.Parse(Regex.Match(style, name + @":\s*(-?[\d.]+)px").Groups[1].Value, CultureInfo.InvariantCulture);
        return (Px("left"), Px("top"));
    }

    [Fact]
    public void Selecting_a_part_shows_four_round_buttons_in_one_row_inside_the_view()
    {
        var editor = Editor([]);
        Assert.Empty(editor.FindAll(".selection-bar"));
        Assert.Empty(editor.FindAll(".part-actions button"));

        editor.Find("rect.part[data-ref=\"R3\"]").Click();

        var buttons = editor.FindAll(".part-actions button");
        Assert.Equal(["delete", "rotate", "flip", "duplicate"], buttons.Select(b => b.GetAttribute("data-action")));
        var at = buttons.ToDictionary(b => b.GetAttribute("data-action")!, At);
        Assert.Single(at.Values.Select(v => v.Top).Distinct());
        Assert.Equal(4, at.Values.Select(v => v.Left).Distinct().Count());
        foreach (var (left, top) in at.Values)
        {
            Assert.InRange(left, Button / 2, PaneWidth - Button / 2);
            Assert.InRange(top, Button / 2, PaneHeight - Button / 2);
        }
    }

    [Fact]
    public void Delete_removes_the_line_and_the_placement_and_undo_restores_both()
    {
        var seen = new List<SchematicChange>();
        var editor = Editor(seen);
        editor.Find("rect.part[data-ref=\"R3\"]").Click();

        editor.Find(".part-actions button[data-action=delete]").Click();

        Assert.DoesNotContain("R3 ", seen[^1].Netlist);
        Assert.DoesNotContain(seen[^1].Layout.Parts, p => p.Reference == "R3");
        Assert.Contains("Deleted R3.", editor.Find(".delete-status").TextContent);
        Assert.Empty(editor.FindAll(".part-actions button"));

        editor.Find(".delete-status button").Click();

        Assert.Contains("R3 a 0 2k", seen[^1].Netlist);
        Assert.Contains(seen[^1].Layout.Parts, p => p.Reference == "R3");
    }

    [Fact]
    public void Selecting_a_wire_shows_one_delete()
    {
        var seen = new List<SchematicChange>();
        var editor = Editor(seen);

        editor.FindAll("polyline.wire-hit")[0].Click();

        var buttons = editor.FindAll(".part-actions button");
        Assert.Equal("delete", Assert.Single(buttons).GetAttribute("data-action"));
        buttons[0].Click();
        Assert.Contains("Deleted wire.", editor.Find(".delete-status").TextContent);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("Delete")]
    public void Delete_keys_remove_the_part(string key)
    {
        var seen = new List<SchematicChange>();
        var editor = Editor(seen);
        editor.Find("rect.part[data-ref=\"R3\"]").Click();
        editor.Find("figure.schematic").KeyDown(new KeyboardEventArgs { Key = key });
        Assert.DoesNotContain("R3 ", seen[^1].Netlist);
    }

    [Theory]
    [InlineData("r")]
    [InlineData("f")]
    public void Rotate_and_flip_keys_edit_the_layout(string key)
    {
        var seen = new List<SchematicChange>();
        var editor = Editor(seen);
        editor.Find("rect.part[data-ref=\"R3\"]").Click();
        editor.Find("figure.schematic").KeyDown(new KeyboardEventArgs { Key = key });
        Assert.Single(seen);
    }

    [Fact]
    public void The_d_key_duplicates_the_part()
    {
        var seen = new List<SchematicChange>();
        var editor = Editor(seen);
        editor.Find("rect.part[data-ref=\"R3\"]").Click();
        editor.Find("figure.schematic").KeyDown(new KeyboardEventArgs { Key = "d" });
        Assert.Equal(Chain.Split('\n').Count(l => l.StartsWith('R')) + 1, seen[^1].Netlist.Split('\n').Count(l => l.StartsWith('R')));
    }

    // NOTE: #300: the netlist text can lose the selected part before the render pass clears the selection. A duplicate of a
    // part that the netlist no longer has returns the layout unchanged, and an empty layout made the last-part index throw.
    [Fact]
    public void The_d_key_on_a_stale_selection_does_not_throw_or_show_an_error()
    {
        var seen = new List<SchematicChange>();
        var editor = Editor(seen);
        editor.Find("rect.part[data-ref=\"R3\"]").Click();

        editor.Render(p => p.Add(c => c.Netlist, "* schematic\n.END\n"));

        var thrown = Record.Exception(() => editor.Find("figure.schematic").KeyDown(new KeyboardEventArgs { Key = "d" }));

        Assert.Null(thrown);
        Assert.Empty(editor.FindAll("ul.problems"));
        Assert.Empty(editor.FindAll(".value-error"));
        Assert.Contains("Tap a part in the palette", editor.Find("p.schematic-empty").TextContent);
    }
}
