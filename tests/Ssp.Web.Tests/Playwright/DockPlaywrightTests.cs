using Microsoft.Playwright;

namespace Ssp.Web.Tests.Playwright;

[Collection(PlaywrightCollection.Name)]
public class DockPlaywrightTests
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    [PlaywrightFact]
    public Task HideAndRestoreTakeOneClickEach() => HideAndRestoreCore().WaitAsync(Limit);

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
