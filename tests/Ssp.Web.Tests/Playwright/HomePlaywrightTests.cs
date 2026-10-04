using Microsoft.Playwright;

namespace Ssp.Web.Tests.Playwright;

[Collection(PlaywrightCollection.Name)]
public class HomePlaywrightTests
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    [PlaywrightFact]
    public Task EditorLinkOpensDockLayout() => EditorLinkOpensDockLayoutCore().WaitAsync(Limit);

    static async Task EditorLinkOpensDockLayoutCore()
    {
        var baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!;
        var withDeps = Environment.GetEnvironmentVariable("SSP_PLAYWRIGHT_WITH_DEPS") == "1";
        var install = withDeps ? new[] { "install", "--with-deps", "chromium" } : new[] { "install", "chromium" };
        Assert.Equal(0, Program.Main(install));

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });
        page.SetDefaultTimeout(30_000);

        // NOTE: The test server has no fallback to index.html, so the test serves it for the editor route.
        await page.RouteAsync(baseUrl + "editor", async route =>
            await route.FulfillAsync(new() { Response = await route.FetchAsync(new() { Url = baseUrl }) }));
        await page.GotoAsync(baseUrl);

        await page.Locator("a[href=editor]").ClickAsync(new() { Timeout = 60_000 });

        await Assertions.Expect(page.Locator(".dock-layout")).ToBeVisibleAsync(new() { Timeout = 60_000 });
    }
}
