using Microsoft.Playwright;

namespace Ssp.Web.Tests.Playwright;

// NOTE: #381: at 375x812 every toolbar control, the Start here button and the Saved label are inside the viewport. The page has no sideways scroll.
[Collection(PlaywrightCollection.Name)]
public class PhoneToolbarPlaywrightTests
{
    static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    [PlaywrightFact]
    public Task Every_toolbar_control_is_inside_a_phone_viewport() => Core().WaitAsync(Limit);

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

        var outside = new List<string>();
        async Task Check(string name, ILocator locator)
        {
            var box = await locator.BoundingBoxAsync();
            if (box is null) return;
            if (box.X < 0 || box.X + box.Width > 375.5) outside.Add($"{name} x={box.X:F0} w={box.Width:F0}");
        }

        await Check("Start here", start);
        await Check("Saved", page.Locator(".project-state"));
        var buttons = page.Locator(".schematic-tools button");
        for (var i = 0; i < await buttons.CountAsync(); i++)
        {
            var button = buttons.Nth(i);
            await Check((await button.InnerTextAsync()).Trim(), button);
        }

        Assert.True(outside.Count == 0, "Outside 0..375: " + string.Join("; ", outside));
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > 375"), "The page scrolls sideways");
        Assert.False(await page.EvaluateAsync<bool>("(() => { const t = document.querySelector('.schematic-tools'); return t.scrollWidth > t.clientWidth; })()"), "The toolbar scrolls sideways");
        // NOTE: The palette shows that it scrolls: one item is cut by its right edge.
        Assert.True(await page.EvaluateAsync<bool>("(() => { const p = document.querySelector('.palette'); const r = p.getBoundingClientRect(); return p.scrollWidth > p.clientWidth && [...p.querySelectorAll('.palette-item')].some(e => { const b = e.getBoundingClientRect(); return b.left < r.right - 1 && b.right > r.right + 1; }); })()"), "The palette shows no cut item");
    }
}
