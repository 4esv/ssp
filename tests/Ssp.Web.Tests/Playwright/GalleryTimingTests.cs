using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Ssp.Web.Tests.Playwright;

[Collection(PlaywrightCollection.Name)]
public class GalleryTimingTests(ITestOutputHelper output)
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    [PlaywrightFact]
    public Task GalleryIsInteractiveWithinOneSecond() => Core().WaitAsync(Limit);

    async Task Core()
    {
        var baseUrl = Environment.GetEnvironmentVariable(PlaywrightFactAttribute.BaseUrlVariable)!;
        Assert.Equal(0, Program.Main(["install", "chromium"]));

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });
        page.SetDefaultTimeout(60_000);

        await page.GotoAsync(baseUrl);
        await page.Locator("a[href=editor]").WaitForAsync();
        // NOTE: Wait until the home page has settled, so the click measures only the route change.
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Time from the click to the first turn of the event loop after the gallery is in the DOM.
        var ms = await page.EvaluateAsync<double>(@"async () => {
            const t0 = performance.now();
            document.querySelector('a[href=gallery]').click();
            while (!location.pathname.endsWith('/gallery') || document.querySelectorAll('main ul.gallery li, ul.gallery li').length === 0 || !document.querySelector('h1')) {
                await new Promise(r => setTimeout(r, 0));
            }
            await new Promise(r => requestAnimationFrame(() => setTimeout(r, 0)));
            return performance.now() - t0;
        }");
        output.WriteLine($"Gallery click to first interaction: {ms:F0} ms");

        Assert.True(ms < 1000, $"Gallery took {ms:F0} ms to become interactive.");
    }
}
