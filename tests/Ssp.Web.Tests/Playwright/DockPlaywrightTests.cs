using Microsoft.Playwright;

namespace Ssp.Web.Tests.Playwright;

[Collection(PlaywrightCollection.Name)]
public class DockPlaywrightTests
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    [PlaywrightFact]
    public Task HideAndRestoreTakeOneClickEach() => HideAndRestoreCore().WaitAsync(Limit);

    [PlaywrightFact]
    public Task FloatDragAndDockAsTab() => FloatDragAndDockCore().WaitAsync(Limit);

    static async Task FloatDragAndDockCore()
    {
        var withDeps = Environment.GetEnvironmentVariable("SSP_PLAYWRIGHT_WITH_DEPS") == "1";
        var install = withDeps ? new[] { "install", "--with-deps", "chromium" } : new[] { "install", "chromium" };
        Assert.Equal(0, Program.Main(install));

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });
        page.SetDefaultTimeout(30_000);

        await ClipPlayerTests.Session.OpenEditor(page);
        await page.Locator(".dock-tab[data-panel=text]").WaitForAsync(new() { Timeout = 60_000 });
        var tabs = page.Locator(".dock-tab");
        var before = await tabs.CountAsync();

        await page.Locator(".dock-float-btn[aria-label='Float Netlist']").ClickAsync();
        var bar = page.Locator(".dock-float-bar[data-panel=text]");
        await bar.WaitForAsync();
        Assert.Equal(before - 1, await tabs.CountAsync());
        var schematicBox = (await page.Locator(".dock-panel[data-panel=schematic]").BoundingBoxAsync())!;
        var started = (await bar.BoundingBoxAsync())!;
        Assert.True(started.X >= schematicBox.X + schematicBox.Width - 1 || started.Y >= schematicBox.Y + schematicBox.Height - 1,
            "The floating window starts over the schematic.");

        // NOTE: Drag the bar by the title to the middle of the schematic group, then release.
        await page.Mouse.MoveAsync((float)(started.X + 20), (float)(started.Y + started.Height / 2));
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(schematicBox.X + schematicBox.Width / 2), (float)(schematicBox.Y + schematicBox.Height / 2), new() { Steps = 8 });
        await page.ScreenshotAsync(new() { Path = Path.Combine(Path.GetTempPath(), "dock-float-1280.png") });
        await page.Mouse.UpAsync();

        await Assertions.Expect(page.Locator(".dock-float-bar")).ToHaveCountAsync(0);
        await Assertions.Expect(tabs).ToHaveCountAsync(before);
        var group = page.Locator(".dock-tabs", new() { Has = page.Locator(".dock-tab[data-panel=schematic]") });
        await Assertions.Expect(group.Locator(".dock-tab")).ToHaveCountAsync(2);
    }

    static async Task HideAndRestoreCore()
    {
        var withDeps = Environment.GetEnvironmentVariable("SSP_PLAYWRIGHT_WITH_DEPS") == "1";
        var install = withDeps ? new[] { "install", "--with-deps", "chromium" } : new[] { "install", "chromium" };
        Assert.Equal(0, Program.Main(install));

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });
        page.SetDefaultTimeout(30_000);

        await ClipPlayerTests.Session.OpenEditor(page);
        await page.Locator(".dock-tab[data-panel=text]").WaitForAsync(new() { Timeout = 60_000 });
        var schematic = page.Locator(".dock-panel[data-panel=schematic]");
        var before = (await schematic.BoundingBoxAsync())!.Width;

        // NOTE: The group of the netlist has five tabs, so the room stays with it until the last tab is hidden.
        await page.Locator(".dock-close[aria-label='Hide Netlist']").ClickAsync();
        await Assertions.Expect(page.Locator(".dock-panel[data-panel=text]")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator(".dock-open[data-panel=text]")).ToHaveTextAsync("+ Netlist");

        await page.Locator(".dock-open[data-panel=text]").ClickAsync();
        await Assertions.Expect(page.Locator(".dock-panel[data-panel=text]")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".dock-open")).ToHaveCountAsync(0);

        foreach (var panel in new[] { "text", "knobs", "calculators", "compare", "clip" })
        {
            await page.Locator($".dock-close[aria-label$='{(panel == "text" ? "Netlist" : char.ToUpper(panel[0]) + panel[1..])}']").ClickAsync();
        }

        var after = (await schematic.BoundingBoxAsync())!.Width;
        Assert.True(after > before, $"The schematic was {before} px wide and is {after} px wide.");
        await page.ScreenshotAsync(new() { Path = Path.Combine(Path.GetTempPath(), "dock-hidden-1280.png") });

        await page.Locator(".dock-open[data-panel=text]").ClickAsync();
        Assert.True((await schematic.BoundingBoxAsync())!.Width < after);

        await page.SetViewportSizeAsync(390, 800);
        await page.ScreenshotAsync(new() { Path = Path.Combine(Path.GetTempPath(), "dock-390.png") });
    }
}
