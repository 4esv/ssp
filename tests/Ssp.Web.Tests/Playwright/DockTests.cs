using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Ssp.Web.Tests.Playwright;

public class DockTests
{
    [PlaywrightFact]
    public async Task AltShiftArrowDocksPanelAtLeftEdge()
    {
        var baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!;
        var withDeps = Environment.GetEnvironmentVariable("SSP_PLAYWRIGHT_WITH_DEPS") == "1";
        var install = withDeps ? new[] { "install", "--with-deps", "chromium" } : new[] { "install", "chromium" };
        Assert.Equal(0, Program.Main(install));

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });

        // NOTE: The test server has no fallback to index.html, so the test serves it for the editor route.
        await page.RouteAsync(baseUrl + "editor", async route =>
            await route.FulfillAsync(new() { Response = await route.FetchAsync(new() { Url = baseUrl }) }));
        await page.GotoAsync(baseUrl + "editor");

        var tab = page.Locator(".dock-tab[data-panel=knobs]");
        await tab.WaitForAsync(new() { Timeout = 60_000 });
        var text = page.Locator(".dock-panel[data-panel=text]");
        var before = (await text.BoundingBoxAsync())!;

        await tab.FocusAsync();
        await page.Keyboard.PressAsync("Alt+Shift+ArrowLeft");

        var layout = page.Locator(".dock-layout");
        await Assertions.Expect(layout).ToHaveAttributeAsync("data-tree", new Regex(@"^row\(0\.33 tabs\(knobs\*\), "));
        await Assertions.Expect(tab).ToBeFocusedAsync();
        var knobs = (await page.Locator(".dock-panel[data-panel=knobs]").BoundingBoxAsync())!;
        var after = (await text.BoundingBoxAsync())!;
        Assert.True(knobs.X < after.X, $"Knobs is at x = {knobs.X}, the netlist is at x = {after.X}.");
        Assert.True(after.X > before.X, $"The netlist was at x = {before.X} and is at x = {after.X}.");

        var tree = await layout.GetAttributeAsync("data-tree");
        await page.ReloadAsync();
        await Assertions.Expect(layout).ToHaveAttributeAsync("data-tree", tree!, new() { Timeout = 60_000 });
    }
}
