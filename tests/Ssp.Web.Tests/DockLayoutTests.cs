using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Web.Hosting;
using Ssp.Web.Pages;

namespace Ssp.Web.Tests;

public class DockLayoutTests : BunitContext
{
    const string Key = "ssp.dock.editor.v2";

    const string DefaultTree =
        "row(0.72 column(0.8 tabs(schematic*), 0.2 tabs(results*, plots, dc, monitor, amp)), " +
        "0.28 tabs(text*, knobs, calculators, compare, clip))";

    static readonly string[] Panels = ["amp", "calculators", "clip", "compare", "dc", "knobs", "monitor", "plots", "results", "schematic", "text"];

    static IRenderedComponent<Editor> Open(BunitContext context, string? saved)
    {
        context.Services.AddSingleton<TimeProvider>(new ManualTimeProvider());
        context.Services.AddSingleton<ISimulationHost, InProcessSimulationHost>();
        context.JSInterop.Setup<string?>("localStorage.getItem", Key).SetResult(saved);
        context.JSInterop.SetupVoid("localStorage.setItem", _ => true);
        return context.Render<Editor>();
    }

    static string Tree(IRenderedComponent<Editor> page) => page.Find(".dock-layout").GetAttribute("data-tree")!;

    static IEnumerable<(string Panel, string Style)> Places(IRenderedComponent<Editor> page) =>
        page.FindAll(".dock-panel").Select(p => (p.GetAttribute("data-panel")!, p.GetAttribute("style")!));

    [Fact]
    public void EditorShowsEachPanelOnce()
    {
        var page = Open(this, null);

        Assert.Equal(Panels, page.FindAll(".dock-panel").Select(p => p.GetAttribute("data-panel")!).Order());
        Assert.Equal(Panels, page.FindAll(".dock-tab").Select(t => t.GetAttribute("data-panel")!).Order());
        Assert.Equal(DefaultTree, Tree(page));
    }

    [Fact]
    public void SavedLayoutComesBackAfterReload()
    {
        var page = Open(this, null);

        page.Find(".dock-tab[data-panel=knobs]").KeyDown(new KeyboardEventArgs { Code = "ArrowLeft", AltKey = true, ShiftKey = true });
        page.Find(".dock-close[aria-label='Hide Plots']").Click();

        var tree = Tree(page);
        Assert.StartsWith("row(0.33 tabs(knobs*), ", tree);
        Assert.EndsWith(" closed(plots)", tree);
        var saved = (string)JSInterop.Invocations["localStorage.setItem"].Last().Arguments[1]!;

        using var reload = new BunitContext();
        var reloaded = Open(reload, saved);

        Assert.Equal(tree, Tree(reloaded));
        Assert.Equal(Places(page), Places(reloaded));
        Assert.Empty(reloaded.FindAll(".dock-panel[data-panel=plots]"));
    }

    [Fact]
    public void ClosedPanelOpensAgain()
    {
        var page = Open(this, null);
        page.Find(".dock-close[aria-label='Hide Compare']").Click();
        Assert.Empty(page.FindAll(".dock-panel[data-panel=compare]"));

        page.Find(".dock-open[data-panel=compare]").Click();

        Assert.Single(page.FindAll(".dock-panel[data-panel=compare]"));
        Assert.EndsWith(", 0.33 tabs(compare*))", Tree(page));
    }

    [Theory]
    [InlineData("{bad")]
    [InlineData("""{"version":0,"root":null,"closed":[]}""")]
    [InlineData("""{"version":1,"root":{"tabs":["text","schematic"],"active":0},"closed":[]}""")]
    public void BadSavedLayoutGivesDefault(string saved)
    {
        var page = Open(this, saved);

        Assert.Equal(DefaultTree, Tree(page));
        Assert.Equal(Panels, page.FindAll(".dock-panel").Select(p => p.GetAttribute("data-panel")!).Order());
        Assert.Empty(page.FindAll("ul.diagnostics li"));
    }

    [Fact]
    public void ResetLayoutRestoresDefaultAndClearsSaved()
    {
        JSInterop.SetupVoid("localStorage.removeItem", Key).SetVoidResult();
        var page = Open(this, null);
        page.Find(".dock-tab[data-panel=knobs]").KeyDown(new KeyboardEventArgs { Code = "ArrowLeft", AltKey = true, ShiftKey = true });
        page.Find(".dock-close[aria-label='Hide Plots']").Click();
        Assert.NotEqual(DefaultTree, Tree(page));

        page.Find(".dock-reset").Click();

        Assert.Equal(DefaultTree, Tree(page));
        Assert.Equal(Panels, page.FindAll(".dock-panel").Select(p => p.GetAttribute("data-panel")!).Order());
        Assert.Single(JSInterop.Invocations["localStorage.removeItem"]);
        Assert.Empty(page.FindAll(".dock-open"));
    }

    [Fact]
    public void EachPanelHasOneTitleForStackedLayout()
    {
        var page = Open(this, null);

        var titles = page.FindAll(".dock-panel").Select(p => p.QuerySelectorAll(".dock-panel-title").Length);
        Assert.All(titles, count => Assert.Equal(1, count));
        Assert.Equal(Panels.Length, page.FindAll(".dock-panel-title").Count);
    }

    [Fact]
    public void HideGivesTheRoomToTheNeighbour()
    {
        var page = Open(this, null);

        // NOTE: The panel keeps the room while its group has other tabs. The room goes to the neighbour when the last tab is hidden.
        foreach (var title in new[] { "Netlist", "Knobs", "Calculators", "Compare", "Clip" })
        {
            page.Find($".dock-close[aria-label='Hide {title}']").Click();
        }

        Assert.StartsWith("column(", Tree(page));
        Assert.Contains("width: 100%", page.Find(".dock-panel[data-panel=schematic]").GetAttribute("style"));
    }

    [Fact]
    public void HiddenPanelHasOneRestoreControlWithItsName()
    {
        var page = Open(this, null);
        page.Find(".dock-close[aria-label='Hide Results']").Click();

        var restore = page.Find(".dock-open[data-panel=results]");
        Assert.Contains("Results", restore.TextContent);
        restore.Click();
        Assert.Empty(page.FindAll(".dock-open"));
        Assert.Single(page.FindAll(".dock-panel[data-panel=results]"));
    }

    [Fact]
    public void DoubleClickOnTabHidesThePanel()
    {
        var page = Open(this, null);

        page.Find(".dock-tab[data-panel=plots]").DoubleClick();

        Assert.Empty(page.FindAll(".dock-panel[data-panel=plots]"));
        Assert.Single(page.FindAll(".dock-open[data-panel=plots]"));
    }

    [Fact]
    public void HideButtonIsVisibleText()
    {
        var page = Open(this, null);

        Assert.All(page.FindAll(".dock-close"), b => Assert.Equal("–", b.TextContent));
    }

    [Fact]
    public void KeyHelpIsAButtonOnTheDockBar()
    {
        var page = Open(this, null);
        Assert.Empty(page.FindAll(".dock-help"));

        page.Find(".dock-bar .dock-help-toggle").Click();

        Assert.Contains("Alt+Shift+Arrow", page.Find(".dock-help").TextContent);
        page.Find(".dock-bar .dock-help-toggle").Click();
        Assert.Empty(page.FindAll(".dock-help"));
    }
}
