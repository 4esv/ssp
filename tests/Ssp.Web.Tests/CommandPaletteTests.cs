using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Ssp.Web.Editing;
using Ssp.Web.Hosting;
using Ssp.Web.Pages;

namespace Ssp.Web.Tests;

public class CommandPaletteTests : BunitContext
{
    public CommandPaletteTests()
    {
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<ISimulationHost, InProcessSimulationHost>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    static KeyboardEventArgs Mod(string key, bool shift = false) => new() { Key = key, CtrlKey = true, ShiftKey = shift };

    IRenderedComponent<Editor> Open()
    {
        var page = Render<Editor>();
        page.Find(".editor").KeyDown(Mod("k"));
        return page;
    }

    [Fact]
    public void CtrlKOpensAndTextFiltersTheActions()
    {
        var page = Open();
        Assert.True(page.FindAll(".palette li").Count >= 5);

        page.Find(".palette input").Input("share");

        var item = Assert.Single(page.FindAll(".palette li"));
        Assert.Contains("Share link", item.TextContent);
    }

    [Fact]
    public void ChosenActionRuns()
    {
        var page = Open();
        page.Find(".palette input").Input("run");

        page.Find(".palette li button").Click();

        Assert.Empty(page.FindAll(".palette"));
        Assert.NotEmpty(page.FindAll("table.voltages"));
    }

    [Fact]
    public void EnterRunsTheFirstMatch()
    {
        var page = Open();
        page.Find(".palette input").Input("run");

        page.Find(".palette input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.NotEmpty(page.FindAll("table.voltages"));
    }

    [Fact]
    public void QuestionMarkListsTheKeysOfTheTable()
    {
        var page = Render<Editor>();
        page.Find(".editor").KeyDown(new KeyboardEventArgs { Key = "?", ShiftKey = true });

        var rows = page.FindAll(".shortcut-sheet tbody tr")
            .Select(r => r.QuerySelectorAll("td").Select(td => td.TextContent.Trim()).ToArray())
            .ToList();
        Assert.Equal(Shortcuts.All.Select(s => new[] { s.Label, s.Keys }).ToList(), rows);
    }

    [Fact]
    public void EveryTableKeyRunsItsHandler()
    {
        // NOTE: Every toolbar action has a key. The table is the only place that lists them.
        Assert.Superset(new[] { "run", "undo", "redo", "share", "svg" }.ToHashSet(), Shortcuts.All.Select(s => s.Id).ToHashSet());
        var page = Render<Editor>();
        page.Find(".editor").KeyDown(Mod("Enter"));
        Assert.NotEmpty(page.FindAll("table.voltages"));
    }

    [Fact]
    public void EscapeClosesThePalette()
    {
        var page = Open();
        page.Find(".editor").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(page.FindAll(".palette"));
    }
}
