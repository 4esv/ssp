using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Web.Hosting;
using Ssp.Web.Pages;

namespace Ssp.Web.Tests;

public class EditorLayoutTests : BunitContext
{
    [Fact]
    public void DefaultLayoutPutsTheSchematicInTheLargestCell()
    {
        var rects = Editor.DefaultLayout().GroupRects().ToList();

        var largest = rects.MaxBy(r => r.Rect.Width * r.Rect.Height);
        Assert.Equal(["schematic"], largest.Group.Panels);
        Assert.True(largest.Rect.Width >= 0.6, $"The schematic is {largest.Rect.Width:0.##} of the width.");
        Assert.True(largest.Rect.Height >= 0.65, $"The schematic is {largest.Rect.Height:0.##} of the height.");
    }

    [Fact]
    public void DefaultLayoutHasOneSideStripAndOneBottomStrip()
    {
        var rects = Editor.DefaultLayout().GroupRects().ToList();

        Assert.Equal(3, rects.Count);
        var schematic = rects.Single(r => r.Group.Panels.Contains("schematic")).Rect;
        var side = rects.Single(r => r.Group.Panels.Contains("text")).Rect;
        var bottom = rects.Single(r => r.Group.Panels.Contains("results")).Rect;
        Assert.True(side.Left >= schematic.Left + schematic.Width - 1e-9, "The side strip is right of the schematic.");
        Assert.True(bottom.Top >= schematic.Top + schematic.Height - 1e-9, "The bottom strip is under the schematic.");
    }

    [Fact]
    public void PointingAtAPartShowsItsValueVoltagesAndRows()
    {
        var time = new ManualTimeProvider();
        Services.AddSingleton<TimeProvider>(time);
        Services.AddSingleton<ISimulationHost, InProcessSimulationHost>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        var page = Render<Editor>();
        page.Find("textarea").Input(File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", "divider-basic.cir")));
        time.Advance(TimeSpan.FromMilliseconds(100));
        page.Find("button.run").Click();

        page.Find(".schematic-pins rect.part[data-ref=R2]").TriggerEvent("onpointerenter", new PointerEventArgs());

        Assert.Equal("R2 2k, node out 667 mV, node 0 0 V", page.Find(".schematic-hint").TextContent);
        Assert.Equal(["out"], page.FindAll("table.voltages tr.pointed").Select(r => r.GetAttribute("data-node")));

        page.Find(".schematic-pins rect.part[data-ref=R2]").TriggerEvent("onpointerleave", new PointerEventArgs());

        Assert.Empty(page.FindAll("table.voltages tr.pointed"));
    }

    [Fact]
    public void SavedLayoutKeyIsVersionTwo()
    {
        Services.AddSingleton<TimeProvider>(new ManualTimeProvider());
        Services.AddSingleton<ISimulationHost, InProcessSimulationHost>();
        JSInterop.Mode = JSRuntimeMode.Loose;

        Render<Editor>();

        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "localStorage.getItem" && Equals(i.Arguments[0], "ssp.dock.editor.v2"));
    }
}
