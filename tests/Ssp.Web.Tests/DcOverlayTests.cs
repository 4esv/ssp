using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Core;
using Ssp.Core.Analysis;
using Ssp.Web.Components;
using Ssp.Web.Hosting;
using Ssp.Web.Pages;

namespace Ssp.Web.Tests;

public class DcOverlayTests : BunitContext
{
    static string Fixture(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root, "circuits", "fixtures", name));

    // NOTE: divider-basic has nodes in, out, and ground. in = 1 V, out = 667 mV.
    static OpResult Divider() => Runner.Run(Fixture("divider-basic.cir"), new RunOptions()).OperatingPoint!;

    IRenderedComponent<DcOverlay> Overlay() => Render<DcOverlay>(p => p.Add(c => c.OperatingPoint, Divider()));

    [Fact]
    public void ToggleOnGivesOneLabelForEachNode()
    {
        var overlay = Overlay();
        Assert.Empty(overlay.FindAll(".dc-label"));

        overlay.Find("input.dc-toggle").Change(true);

        var labels = overlay.FindAll(".dc-label");
        Assert.Equal(["in", "out"], labels.Select(l => l.GetAttribute("data-node")));
    }

    [Fact]
    public void EachLabelHasAUnit()
    {
        var overlay = Overlay();
        overlay.Find("input.dc-toggle").Change(true);

        Assert.Equal(["1 V", "667 mV"], overlay.FindAll(".dc-label").Select(l => l.TextContent));
    }

    [Fact]
    public void ToggleOffRemovesAllLabels()
    {
        var overlay = Overlay();
        overlay.Find("input.dc-toggle").Change(true);
        Assert.NotEmpty(overlay.FindAll(".dc-label"));

        overlay.Find("input.dc-toggle").Change(false);

        Assert.Empty(overlay.FindAll(".dc-label"));
    }

    [Fact]
    public void ClickOnNodeShowsItsValue()
    {
        var overlay = Overlay();
        Assert.Empty(overlay.FindAll(".probe"));

        overlay.Find(".dc-node[data-node=out]").Click();

        Assert.Equal("out: 667 mV", overlay.Find(".probe").TextContent);
    }

    [Fact]
    public void NoOperatingPointGivesNoOverlay()
    {
        var overlay = Render<DcOverlay>();

        Assert.Empty(overlay.FindAll(".dc-overlay"));
    }

    [Fact]
    public void EditorShowsOverlayAfterRun()
    {
        Services.AddSingleton<ISimulationHost>(new InProcessSimulationHost());
        var page = Render<Editor>();
        page.Find("textarea").Input(Fixture("divider-basic.cir"));
        page.Find("button").Click();

        page.Find(".dock-panel[data-panel=dc] input.dc-toggle").Change(true);

        Assert.Equal(2, page.FindAll(".dock-panel[data-panel=dc] .dc-label").Count);
    }
}
