using Microsoft.Playwright;

namespace Ssp.Web.Tests.Playwright;

public class SmokeTests
{
    [PlaywrightFact]
    public async Task PublishedSiteShowsEngineVersions()
    {
        var baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!;
        var withDeps = Environment.GetEnvironmentVariable("SSP_PLAYWRIGHT_WITH_DEPS") == "1";
        var install = withDeps ? new[] { "install", "--with-deps", "chromium" } : new[] { "install", "chromium" };
        Assert.Equal(0, Program.Main(install));

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();
        await page.GotoAsync(baseUrl);

        await Assertions.Expect(page.Locator("pre")).ToContainTextAsync("SpiceSharp 3.2.3", new() { Timeout = 60_000 });
    }
}
