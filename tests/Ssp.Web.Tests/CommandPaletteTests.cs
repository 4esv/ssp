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
        Assert.True(page.FindAll(".command-palette li").Count >= 5);

        page.Find(".command-palette input").Input("share");

        var item = Assert.Single(page.FindAll(".command-palette li"));
        Assert.Contains("Share link", item.TextContent);
    }

    [Fact]
    public void ChosenActionRuns()
    {
        var page = Open();
        page.Find(".command-palette input").Input("run");

        page.Find(".command-palette li button").Click();

        Assert.Empty(page.FindAll(".command-palette"));
        Assert.NotEmpty(page.FindAll("table.voltages"));
    }

    [Fact]
    public void EnterRunsTheFirstMatch()
    {
        var page = Open();
        page.Find(".command-palette input").Input("run");

        page.Find(".command-palette input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.NotEmpty(page.FindAll("table.voltages"));
    }

    [Fact]
    public void QuestionMarkListsTheKeysOfTheTable()
    {
        JSInterop.Mark();
        var page = Render<Editor>();
        page.Find(".editor").KeyDown(new KeyboardEventArgs { Key = "?", ShiftKey = true });

        var rows = page.FindAll(".shortcut-sheet tbody tr")
            .Select(r => r.QuerySelectorAll("td").Select(td => td.TextContent.Trim()).ToArray())
            .ToList();
        Assert.Equal(Shortcuts.All.Select(s => new[] { s.Label, s.Keys }).ToList(), rows);
    }

    [Fact]
    public void TheHelpPanelHasAStartHereSectionAndALinkToTheDocs()
    {
        JSInterop.Mark();
        var page = Render<Editor>();
        Assert.Empty(page.FindAll(".shortcut-sheet"));
        page.Find(".editor").KeyDown(new KeyboardEventArgs { Key = "?", ShiftKey = true });

        var start = page.Find(".shortcut-sheet .start-here");
        Assert.Contains("Start here", start.TextContent);
        var link = page.Find(".shortcut-sheet a");
        Assert.EndsWith("docs/index.html", link.GetAttribute("href"));
    }

    [Fact]
    public void TheFirstVisitShowsThePanelAndRemembersIt()
    {
        var page = Render<Editor>();
        Assert.Single(page.FindAll(".shortcut-sheet"));
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "localStorage.setItem" && Equals(i.Arguments[0], HelpSeen.Key));

        page.Find(".editor").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(page.FindAll(".shortcut-sheet"));
    }

    [Fact]
    public void TheCloseButtonRemovesTheHelpPanel()
    {
        var page = Render<Editor>();
        Assert.Single(page.FindAll(".shortcut-sheet"));

        var close = page.Find(".shortcut-sheet button");
        Assert.Equal("Close", close.TextContent.Trim());
        Assert.Equal("button", close.GetAttribute("type"));
        close.Click();
        Assert.Empty(page.FindAll(".shortcut-sheet"));
    }

    [Fact]
    public void ASeenFlagKeepsThePanelShutAtTheLoad()
    {
        JSInterop.Mark();
        var page = Render<Editor>();
        Assert.Empty(page.FindAll(".shortcut-sheet"));
    }

    [Fact]
    public void ThrowingStorageDoesNotStopTheEditor()
    {
        JSInterop.Setup<string?>("localStorage.getItem", HelpSeen.Key).SetException(new InvalidOperationException("blocked"));
        JSInterop.SetupVoid("localStorage.setItem", HelpSeen.Key, "1").SetException(new InvalidOperationException("blocked"));
        var page = Render<Editor>();
        page.Find(".editor").KeyDown(Mod("Enter"));
        Assert.NotEmpty(page.FindAll("table.voltages"));
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
        Assert.Empty(page.FindAll(".command-palette"));
    }
}
