using Microsoft.Playwright;

namespace Ssp.Web.Tests.Playwright;

// NOTE: #376: a touch user has no Escape key. The help panel opens at the first visit, so a tap on Close must shut it.
// This test does not set the seen flag, because it tests the first visit.
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
            ViewportSize = new() { Width = 390, Height = 844 },
        });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(60_000);
        await page.RouteAsync(baseUrl + "editor", async route =>
            await route.FulfillAsync(new() { Response = await route.FetchAsync(new() { Url = baseUrl }) }));
        // NOTE: The test server sometimes drops a connection on the first load, so a page with no sheet loads again.
        var sheet = page.Locator(".shortcut-sheet");
        for (var attempt = 0; ; attempt++)
        {
            if (attempt == 0) await page.GotoAsync(baseUrl + "editor");
            else await page.ReloadAsync();
            try
            {
                await sheet.WaitForAsync(new() { Timeout = 30_000 });
                break;
            }
            catch (TimeoutException) when (attempt < 2) { }
        }

        await page.Locator(".shortcut-sheet button", new() { HasTextString = "Close" }).TapAsync();
        await sheet.WaitForAsync(new() { State = WaitForSelectorState.Detached });

        var netlist = page.Locator("textarea[aria-label=Netlist]");
        await page.Locator("button[data-action=new]").TapAsync();
        var before = await netlist.InputValueAsync();
        await page.Locator(".palette-item[data-kind=source]").TapAsync();
        await page.WaitForFunctionAsync("b => document.querySelector('textarea[aria-label=Netlist]').value !== b", before,
            new() { Timeout = 10_000, PollingInterval = 50 });
    }
}
