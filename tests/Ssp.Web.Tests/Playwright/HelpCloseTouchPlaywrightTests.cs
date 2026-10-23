using Microsoft.Playwright;

namespace Ssp.Web.Tests.Playwright;

// NOTE: #376, #380: a touch user has no Escape key. The first visit shows a Start here button, not the panel.
// A tap on it opens the panel, and a tap on Close must shut it. This test does not set the seen flag, because it tests the first visit.
[Collection(PlaywrightCollection.Name)]
public class HelpCloseTouchPlaywrightTests
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    [PlaywrightFact]
    public Task A_tap_on_Close_hides_the_help_panel_and_a_palette_part_tap_works() => Core().WaitAsync(Limit);

    async Task Core()
    {
        var withDeps = Environment.GetEnvironmentVariable("SSP_PLAYWRIGHT_WITH_DEPS") == "1";
        Assert.Equal(0, Microsoft.Playwright.Program.Main(withDeps ? ["install", "--with-deps", "chromium"] : ["install", "chromium"]));
        var baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!;
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var context = await browser.NewContextAsync(new()
        {
            HasTouch = true,
            IsMobile = true,
            ViewportSize = new() { Width = 375, Height = 812 },
        });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(60_000);
        await page.RouteAsync(baseUrl + "editor", async route =>
            await route.FulfillAsync(new() { Response = await route.FetchAsync(new() { Url = baseUrl }) }));
        // NOTE: The test server sometimes drops a connection on the first load, so a page with no button loads again.
        var sheet = page.Locator(".shortcut-sheet");
        var start = page.Locator("button.start-here-button");
        for (var attempt = 0; ; attempt++)
        {
            if (attempt == 0) await page.GotoAsync(baseUrl + "editor");
            else await page.ReloadAsync();
            try
            {
                await start.WaitForAsync(new() { Timeout = 30_000 });
                break;
            }
            catch (TimeoutException) when (attempt < 2) { }
        }

        Assert.Equal(0, await sheet.CountAsync());
        await start.TapAsync();
        await sheet.WaitForAsync();
        var close = page.Locator(".shortcut-sheet button", new() { HasTextString = "Close" });
        var box = (await close.BoundingBoxAsync())!;
        Assert.True(box.Height >= 44, $"Close is {box.Height} px tall");
        var sheetBox = (await sheet.BoundingBoxAsync())!;
        Assert.True(sheetBox.Y >= 0 && sheetBox.Y + sheetBox.Height <= 812, "The sheet fits the viewport");
        Assert.Equal(0, await start.CountAsync());
        await close.TapAsync();
        await sheet.WaitForAsync(new() { State = WaitForSelectorState.Detached });

        var netlist = page.Locator("textarea[aria-label=Netlist]");
        await page.Locator("button[data-action=new]").TapAsync();
        var before = await netlist.InputValueAsync();
        await page.Locator(".palette-item[data-kind=source]").TapAsync();
        await page.WaitForFunctionAsync("b => document.querySelector('textarea[aria-label=Netlist]').value !== b", before,
            new() { Timeout = 10_000, PollingInterval = 50 });
    }
}
